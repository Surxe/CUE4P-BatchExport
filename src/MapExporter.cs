using CUE4Parse.FileProvider;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Actor;
using CUE4Parse.UE4.Assets.Exports.Component;
using CUE4Parse.UE4.Assets.Exports.Component.Landscape;
using CUE4Parse.UE4.Assets.Exports.Component.SplineMesh;
using CUE4Parse.UE4.Assets.Exports.Component.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.NavigationSystem;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Objects.Engine;
using CUE4Parse.UE4.Objects.PhysicsEngine;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Formats.Meshes;
using CUE4Parse_Conversion.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConversionExportOptions = CUE4Parse_Conversion.Options.ExportOptions;
using ConversionMeshFormat = CUE4Parse_Conversion.Options.EMeshFormat;
using ConversionNaniteMeshFormat = CUE4Parse_Conversion.Options.ENaniteMeshFormat;
using ConversionSocketFormat = CUE4Parse_Conversion.Options.ESocketFormat;

namespace BatchExport
{
    public sealed class MapExportOptions
    {
        /// <summary>Export the full-detail Nanite geometry instead of the (much lighter) fallback LOD0.</summary>
        public bool UseNaniteMeshes { get; set; }

        /// <summary>Also dump every visited level package as plain CUE4Parse JSON under maps/&lt;World&gt;/levels/.</summary>
        public bool ExportRawLevels { get; set; }

        public bool IsLoggingEnabled { get; set; }
    }

    /// <summary>
    /// Exports a UWorld as a reconstructable scene: one scene.json per root world listing every
    /// actor and component of the persistent level, its streaming sublevels and (recursively) its
    /// level instances with resolved world transforms, plus the geometry they reference
    /// (static meshes, landscapes, spline meshes) as UEFormat .uemodel files.
    ///
    /// Cooked levels only serialize what differs from the class/Blueprint defaults, so every lookup
    /// that matters for reconstruction (transforms, mesh pointers, shape sizes, collision) is
    /// resolved through the object's template (archetype) chain.
    /// </summary>
    public sealed class MapExporter
    {
        public const int SceneFormatVersion = 1;

        // Per-object instance properties larger than this are replaced by a marker so one
        // pathological actor cannot bloat the scene; the raw level dump still has them.
        private const int MaxInstancePropsChars = 262144;

        private readonly DefaultFileProvider _provider;
        private readonly string _outputPath;
        private readonly MapExportOptions _options;
        private readonly JsonSerializer _serializer = new();
        private readonly ConversionExportOptions _meshOptions;

        // Caches shared by every world exported in this run.
        // Swapped per level visit: its keys are that level's objects, and holding them past the
        // visit would keep every visited package (and all it imported) alive for the whole run.
        private Dictionary<UObject, UObject?> _templateCache = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, JObject> _effectivePropsCache = new();
        private readonly Dictionary<string, JArray> _classChainCache = new();
        private readonly Dictionary<string, JObject?> _meshCache = new();
        // Built on the first lookup that needs it, see LoadStaticMesh.
        private Dictionary<string, GameFile>? _packagesIgnoringCase;

        // Instance properties left out of "props" because the scene already carries them as
        // resolved fields (transform, mesh, shape, collision), or because they are render or
        // bookkeeping data that plays no part in reconstructing the level.
        private static readonly HashSet<string> ActorNoiseProps =
        [
            "RootComponent", "BlueprintCreatedComponents", "InstanceComponents", "bHidden", "Tags"
        ];

        private static readonly HashSet<string> ComponentNoiseProps =
        [
            "AttachParent", "AttachSocketName", "RelativeLocation", "RelativeRotation", "RelativeScale3D",
            "bAbsoluteLocation", "bAbsoluteRotation", "bAbsoluteScale",
            "StaticMesh", "OverrideMaterials", "BodyInstance", "bUseDefaultCollision", "bVisible", "bHiddenInGame",
            "BoxExtent", "SphereRadius", "CapsuleHalfHeight", "CapsuleRadius", "Brush", "BrushBodySetup",
            "BodySetup", "CachedMeshBodySetupGuid", "CreationMethod", "bNetAddressable", "UCSSerializationIndex",
            "CachedMaxDrawDistance", "LDMaxDrawDistance", "CacheMeshExtendedBounds", "BuiltInstanceBounds"
        ];

        public MapExporter(DefaultFileProvider provider, string outputPath, MapExportOptions options)
        {
            _provider = provider;
            _outputPath = outputPath;
            _options = options;
            _meshOptions = new ConversionExportOptions(
                meshFormat: ConversionMeshFormat.UEFormat,
                naniteMeshFormat: options.UseNaniteMeshes ? ConversionNaniteMeshFormat.NaniteOnly : ConversionNaniteMeshFormat.NoNanite,
                meshQuality: EMeshQuality.Highest,
                exportMaterials: false,
                exportMorphTargets: false,
                socketFormat: ConversionSocketFormat.None,
                compressionFormat: CUE4Parse_Conversion.Writers.UEFormat.Enums.EFileCompressionFormat.ZSTD
            );
        }

        /// <summary>
        /// State of one root-world export. Levels can be visited more than once (a level instance
        /// placed twice), so everything positional is keyed per visit, never per package.
        /// </summary>
        private sealed class Scene
        {
            public required string WorldName;
            public required string RelativeDir; // maps/<WorldName>, relative to the output root
            public required StreamWriter ActorsWriter;
            public int ActorCount;
            public readonly JArray Levels = new();
            public readonly JArray ResponseSets = new();
            public readonly Dictionary<string, int> ResponseSetIndex = new();
            public readonly JObject Templates = new();
            public readonly JObject Meshes = new();
            public readonly JArray Warnings = new();
            public readonly HashSet<string> DumpedLevels = new();
            public readonly HashSet<string> UsedFileNames = new();
            public int ComponentCount;
            public int InstanceCount;
            public int TruncatedProps;

            public void Warn(string message)
            {
                if (Warnings.Count < 2000) Warnings.Add(message);
            }
        }

        /// <summary>Everything needed to resolve transforms inside one visit of one level.</summary>
        private sealed class LevelVisit
        {
            public required int Index;
            public required FTransform Transform;
            public required IPackage Package;
            public readonly Dictionary<UObject, FTransform> World = new(ReferenceEqualityComparer.Instance);
            public readonly Dictionary<UObject, UObject> OwnerOf = new(ReferenceEqualityComparer.Instance);
            public readonly Dictionary<UObject, List<UObject>> ComponentsOf = new(ReferenceEqualityComparer.Instance);
        }

        public bool ExportWorld(string worldPackagePath)
        {
            UWorld? world;
            try
            {
                world = LoadWorld(_provider.LoadPackage(worldPackagePath));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load map {worldPackagePath}: {ex.Message}");
                return false;
            }

            if (world == null)
            {
                Console.WriteLine($"No UWorld export in {worldPackagePath} - skipping");
                return false;
            }

            ULevel? rootLevel = null;
            try { rootLevel = world.PersistentLevel.Load<ULevel>(); }
            catch (Exception ex) { Console.WriteLine($"Failed to load the persistent level of {worldPackagePath}: {ex.Message}"); }
            if (rootLevel == null)
            {
                Console.WriteLine($"Map {worldPackagePath} has no loadable persistent level - skipping");
                return false;
            }

            var relativeDir = $"maps/{world.Name}";
            var sceneDir = Path.Combine(_outputPath, relativeDir);
            Directory.CreateDirectory(sceneDir);

            // One actor per line, written as it is built: a full map is too large to hold as one
            // JSON tree, and a consumer can filter it line by line the same way.
            using var actorsWriter = new StreamWriter(Path.Combine(sceneDir, "actors.jsonl")) { NewLine = "\n" };
            var scene = new Scene { WorldName = world.Name, RelativeDir = relativeDir, ActorsWriter = actorsWriter };
            var started = DateTime.UtcNow;
            Console.WriteLine($"Exporting map: {world.GetPathName()}");

            var rootIndex = VisitLevel(scene, world, rootLevel, FTransform.Identity, -1, "persistent", null, [world.GetPathName()]);

            // Only the root world's streaming list matters: the engine ignores the
            // StreamingLevels of a world that is itself loaded as a sublevel.
            foreach (var ptr in world.StreamingLevels ?? [])
            {
                ULevelStreaming? streaming;
                try { streaming = ptr.Load<ULevelStreaming>(); }
                catch (Exception ex) { scene.Warn($"streaming level object failed to load: {ex.Message}"); continue; }
                if (streaming == null) continue;

                var assetName = TryEff<FSoftObjectPath>(streaming, "WorldAsset", out var assetPath) ? assetPath.AssetPathName.Text : null;
                if (string.IsNullOrEmpty(assetName) || assetName == "None")
                {
                    scene.Warn($"streaming level {streaming.Name} has no WorldAsset");
                    continue;
                }

                var via = new JObject
                {
                    ["name"] = streaming.Name,
                    ["class"] = streaming.ExportType,
                    ["props"] = InstanceProps(scene, streaming)
                };
                var levelTransform = EffTransform(streaming, "LevelTransform");
                var kind = streaming is ULevelStreamingAlwaysLoaded or ULevelStreamingPersistent ? "streaming_always_loaded" : "streaming_dynamic";

                if (!TryLoadLevel(assetPath, out var subWorld, out var subLevel, out var loadError))
                {
                    // Keep the reference so a consumer can see the level existed but was not cooked.
                    scene.Warn($"streaming level {assetName}: {loadError}");
                    scene.Levels.Add(new JObject
                    {
                        ["index"] = scene.Levels.Count,
                        ["world"] = assetName,
                        ["name"] = assetName.Split('.').Last(),
                        ["kind"] = kind,
                        ["parent"] = rootIndex,
                        ["missing"] = true,
                        ["streaming"] = via,
                        ["transform"] = Xf(levelTransform),
                        ["actor_count"] = 0
                    });
                    continue;
                }

                VisitLevel(scene, subWorld, subLevel, levelTransform, rootIndex, kind, via, [world.GetPathName(), subWorld.GetPathName()]);
            }

            actorsWriter.Flush();

            var root = new JObject
            {
                ["format"] = "ue-map-scene",
                ["version"] = SceneFormatVersion,
                ["world"] = world.GetPathName(),
                ["name"] = world.Name,
                ["exported_utc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["coordinates"] = new JObject
                {
                    ["units"] = "centimeters",
                    ["handedness"] = "left",
                    ["up"] = "+Z",
                    ["forward"] = "+X",
                    ["transform"] = "t=[x,y,z] translation, q=[x,y,z,w] rotation quaternion, s=[x,y,z] scale; world_point = t + rotate(q, s * local_point)",
                    ["instances"] = "each instance is [tx,ty,tz, qx,qy,qz,qw, sx,sy,sz] in world space"
                },
                ["mesh_geometry"] = _options.UseNaniteMeshes ? "nanite" : "fallback_lod0",
                ["stats"] = new JObject
                {
                    ["levels"] = scene.Levels.Count,
                    ["actors"] = scene.ActorCount,
                    ["components"] = scene.ComponentCount,
                    ["mesh_instances"] = scene.InstanceCount,
                    ["unique_meshes"] = scene.Meshes.Count,
                    ["templates"] = scene.Templates.Count,
                    ["truncated_props"] = scene.TruncatedProps,
                    ["warnings"] = scene.Warnings.Count
                },
                ["actors_file"] = "actors.jsonl",
                ["levels"] = scene.Levels,
                ["collision_response_sets"] = scene.ResponseSets,
                ["templates"] = scene.Templates,
                ["meshes"] = scene.Meshes,
                ["warnings"] = scene.Warnings
            };

            var scenePath = Path.Combine(sceneDir, "scene.json");
            using (var sw = new StreamWriter(scenePath))
            using (var jw = new JsonTextWriter(sw) { Formatting = Formatting.None })
            {
                root.WriteTo(jw);
            }

            Console.WriteLine($"Exported map {scene.WorldName}: {scene.Levels.Count} levels, {scene.ActorCount} actors, " +
                              $"{scene.ComponentCount} components, {scene.Meshes.Count} meshes, {scene.Warnings.Count} warnings " +
                              $"in {(DateTime.UtcNow - started).TotalSeconds:F1}s -> {scenePath}");
            return true;
        }

        private static UWorld? LoadWorld(IPackage package)
        {
            foreach (var lazy in package.ExportsLazy)
            {
                try
                {
                    if (lazy.Value is UWorld world) return world;
                }
                catch
                {
                    // an unreadable export is not the world; keep looking
                }
            }
            return null;
        }

        /// <summary>Loads a world and its persistent level; on failure <paramref name="error"/> says why.</summary>
        private bool TryLoadLevel(FSoftObjectPath asset, out UWorld world, out ULevel level, out string error)
        {
            world = null!;
            level = null!;
            error = "not in the game files";
            try
            {
                if (!asset.TryLoad<UWorld>(_provider, out var loaded) || loaded.Owner == null) return false;
                if (loaded.PersistentLevel.Load<ULevel>() is not { } loadedLevel)
                {
                    error = "has no loadable persistent level";
                    return false;
                }
                world = loaded;
                level = loadedLevel;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Emits one level and, depth first, the level instances placed in it. Always appends
        /// exactly one entry to the scene's levels and returns its index.
        /// </summary>
        private int VisitLevel(Scene scene, UWorld world, ULevel level, FTransform levelTransform, int parentIndex, string kind, JObject? via, List<string> stack)
        {
            var outerTemplateCache = _templateCache;
            _templateCache = new Dictionary<UObject, UObject?>(ReferenceEqualityComparer.Instance);
            try
            {
                return VisitLevelCore(scene, world, level, levelTransform, parentIndex, kind, via, stack);
            }
            finally
            {
                _templateCache = outerTemplateCache;
            }
        }

        private int VisitLevelCore(Scene scene, UWorld world, ULevel level, FTransform levelTransform, int parentIndex, string kind, JObject? via, List<string> stack)
        {
            var visit = new LevelVisit { Index = scene.Levels.Count, Transform = levelTransform, Package = world.Owner! };
            var levelJson = new JObject
            {
                ["index"] = visit.Index,
                ["world"] = world.GetPathName(),
                ["name"] = world.Name,
                ["kind"] = kind,
                ["parent"] = IntOrNull(parentIndex),
                ["transform"] = Xf(levelTransform)
            };
            if (via != null) levelJson[kind == "level_instance" ? "instance_actor" : "streaming"] = via;
            scene.Levels.Add(levelJson);

            if (_options.ExportRawLevels && scene.DumpedLevels.Add(world.GetPathName()))
                DumpRawLevel(scene, world);

            var actors = new List<UObject>();
            var actorSet = new HashSet<UObject>(ReferenceEqualityComparer.Instance);
            foreach (var ptr in level.Actors)
            {
                if (ptr == null || ptr.IsNull) continue;
                try
                {
                    if (ptr.TryLoad<UObject>(out var actor) && actorSet.Add(actor)) actors.Add(actor);
                }
                catch (Exception ex)
                {
                    scene.Warn($"{world.Name}: actor failed to load: {ex.Message}");
                }
            }

            // Components are found by ownership (outer chain) rather than through the actor's
            // RootComponent/InstanceComponents/BlueprintCreatedComponents properties, which miss
            // natively created subobjects.
            foreach (var lazy in visit.Package.ExportsLazy)
            {
                UObject export;
                try { export = lazy.Value; }
                catch { continue; }
                if (export is not UActorComponent) continue;

                var owner = FindOwningActor(export, actorSet);
                if (owner == null) continue;
                visit.OwnerOf[export] = owner;
                if (!visit.ComponentsOf.TryGetValue(owner, out var list)) visit.ComponentsOf[owner] = list = [];
                list.Add(export);
            }

            var worldSettingsEmitted = false;
            foreach (var actor in actors)
            {
                JObject actorJson;
                FTransform actorWorld;
                try
                {
                    actorJson = BuildActor(scene, visit, actor, out actorWorld);
                }
                catch (Exception ex)
                {
                    scene.Warn($"{world.Name}:{actor.Name}: actor export failed: {ex.Message}");
                    continue;
                }
                scene.ActorCount++;

                if (!worldSettingsEmitted && actor is AWorldSettings)
                {
                    levelJson["world_settings_actor"] = actorJson["id"];
                    worldSettingsEmitted = true;
                }

                // Level instances stream their world in at the actor's transform.
                string? instanceName = null;
                if (TryEff<FSoftObjectPath>(actor, "CookedWorldAsset", out var instanceAsset) || TryEff(actor, "WorldAsset", out instanceAsset))
                    instanceName = instanceAsset.AssetPathName.Text;
                if (instanceName == "None") instanceName = null;

                UWorld? instanceWorld = null;
                ULevel? instanceLevel = null;
                if (actor is ALevelInstance || instanceName != null)
                {
                    if (instanceName == null)
                        scene.Warn($"{world.Name}:{actor.Name}: level instance without a world asset");
                    else if (stack.Contains(instanceName))
                        scene.Warn($"{world.Name}:{actor.Name}: recursive level instance {instanceName} skipped");
                    else if (stack.Count > 16)
                        scene.Warn($"{world.Name}:{actor.Name}: level instance nesting too deep, {instanceName} skipped");
                    else if (!TryLoadLevel(instanceAsset, out instanceWorld, out instanceLevel, out var loadError))
                        scene.Warn($"{world.Name}:{actor.Name}: level instance {instanceName}: {loadError}");

                    // The sublevel is visited right after this actor is written, so it takes the next index.
                    actorJson["level_instance"] = new JObject
                    {
                        ["world"] = instanceName,
                        ["level"] = instanceLevel != null ? scene.Levels.Count : JValue.CreateNull()
                    };
                }

                scene.ActorsWriter.WriteLine(actorJson.ToString(Formatting.None));

                if (instanceWorld != null && instanceLevel != null)
                {
                    var instanceVia = new JObject { ["actor"] = actorJson["id"], ["name"] = actor.Name };
                    VisitLevel(scene, instanceWorld, instanceLevel, actorWorld, visit.Index, "level_instance", instanceVia, [.. stack, instanceName!]);
                }
            }

            levelJson["actor_count"] = actors.Count;
            return visit.Index;
        }

        private static UObject? FindOwningActor(UObject export, HashSet<UObject> actors)
        {
            var current = export;
            for (var depth = 0; depth < 16; depth++)
            {
                UObject? outer;
                try { outer = current.Outer?.Object?.Value; }
                catch { return null; }
                if (outer == null) return null;
                if (actors.Contains(outer)) return outer;
                current = outer;
            }
            return null;
        }

        private JObject BuildActor(Scene scene, LevelVisit visit, UObject actor, out FTransform actorWorld)
        {
            visit.ComponentsOf.TryGetValue(actor, out var components);
            components ??= [];

            var root = ResolveRootComponent(visit, actor, components);
            actorWorld = root != null ? WorldTransform(visit, root) : visit.Transform;

            var json = new JObject
            {
                ["id"] = scene.ActorCount, // claimed by the caller once the actor is built
                ["level"] = visit.Index,
                ["name"] = actor.Name,
                ["label"] = (actor as AActor)?.ActorLabel,
                ["class"] = actor.ExportType,
                ["class_path"] = SafePath(actor.Class),
                ["class_chain"] = ClassChain(actor),
                ["native"] = actor.GetType().Name,
                ["path"] = actor.GetPathName(),
                ["world"] = Xf(actorWorld),
                ["root"] = IntOrNull(root != null ? components.IndexOf(root) : -1)
            };
            if (Eff(actor, "bHidden", false)) json["hidden"] = true;
            AddTemplateAndProps(scene, json, actor, ActorNoiseProps, components);

            var tags = Eff<FName[]?>(actor, "Tags", null);
            if (tags is { Length: > 0 }) json["tags"] = new JArray(tags.Select(t => t.Text));

            if (actor is ALandscapeProxy landscape)
            {
                var landscapeJson = ExportLandscape(scene, visit, landscape);
                if (landscapeJson != null) json["landscape"] = landscapeJson;
            }

            if (actor is ARecastNavMesh navMesh)
            {
                var navMeshJson = ExportNavMesh(scene, visit, navMesh);
                if (navMeshJson != null) json["navmesh"] = navMeshJson;
            }

            var componentArray = new JArray();
            foreach (var component in components)
            {
                try
                {
                    componentArray.Add(BuildComponent(scene, visit, actor, component, components));
                }
                catch (Exception ex)
                {
                    scene.Warn($"{actor.Name}.{component.Name}: component export failed: {ex.Message}");
                    componentArray.Add(new JObject { ["name"] = component.Name, ["class"] = component.ExportType, ["kind"] = "error", ["error"] = ex.Message });
                }
            }
            scene.ComponentCount += componentArray.Count;
            json["components"] = componentArray;
            return json;
        }

        private USceneComponent? ResolveRootComponent(LevelVisit visit, UObject actor, List<UObject> components)
        {
            // Own value first, then the template's root matched by name: a reference that only
            // points at "my instance of the archetype's subobject" is not re-serialized.
            UObject? current = actor;
            for (var guard = 0; current != null && guard < 16; guard++)
            {
                if (current.TryGetValue(out FPackageIndex index, "RootComponent"))
                {
                    if (index.IsNull) return null;
                    if (ReferenceEquals(current, actor))
                    {
                        try
                        {
                            if (index.Load<USceneComponent>() is { } own) return own;
                        }
                        catch
                        {
                            // fall through to the name match below
                        }
                    }
                    var name = index.Name;
                    return components.OfType<USceneComponent>().FirstOrDefault(c => c.Name == name);
                }
                current = LoadTemplate(current);
            }

            // No RootComponent anywhere in the chain (pure native actor): the only scene
            // component without a parent is the root.
            var roots = components.OfType<USceneComponent>().Where(c => ResolveAttachParent(visit, c) == null).ToList();
            return roots.Count == 1 ? roots[0] : null;
        }

        private USceneComponent? ResolveAttachParent(LevelVisit visit, USceneComponent component)
        {
            UObject? current = component;
            for (var guard = 0; current != null && guard < 16; guard++)
            {
                if (current.TryGetValue(out FPackageIndex index, "AttachParent"))
                {
                    if (index.IsNull) return null;
                    if (ReferenceEquals(current, component))
                    {
                        try
                        {
                            if (index.Load<USceneComponent>() is { } own) return own;
                        }
                        catch
                        {
                            return null;
                        }
                        return null;
                    }

                    // Inherited from the template: the parent is the sibling with the same name.
                    if (!visit.OwnerOf.TryGetValue(component, out var owner)) return null;
                    var name = index.Name;
                    return visit.ComponentsOf[owner].OfType<USceneComponent>().FirstOrDefault(c => c.Name == name && !ReferenceEquals(c, component));
                }
                current = LoadTemplate(current);
            }
            return null;
        }

        private FTransform RelativeTransform(USceneComponent component)
        {
            // Location, rotation and scale are separate properties and are each inherited
            // from the template independently.
            var location = Eff(component, "RelativeLocation", FVector.ZeroVector);
            var rotation = Eff(component, "RelativeRotation", FRotator.ZeroRotator);
            var scale = Eff(component, "RelativeScale3D", FVector.OneVector);
            return new FTransform(rotation, location, scale);
        }

        private FTransform WorldTransform(LevelVisit visit, USceneComponent component)
        {
            if (visit.World.TryGetValue(component, out var cached)) return cached;

            var relative = RelativeTransform(component);
            visit.World[component] = Mul(relative, visit.Transform); // breaks attachment cycles

            var parent = ResolveAttachParent(visit, component);
            var parentWorld = parent != null ? WorldTransform(visit, parent) : visit.Transform;
            if (parent != null)
            {
                var socket = SocketTransform(component, parent);
                if (socket != null) parentWorld = Mul(socket.Value, parentWorld);
            }

            var world = Mul(relative, parentWorld);
            if (Eff(component, "bAbsoluteLocation", false)) world.Translation = relative.Translation;
            if (Eff(component, "bAbsoluteRotation", false)) world.Rotation = relative.Rotation;
            if (Eff(component, "bAbsoluteScale", false)) world.Scale3D = relative.Scale3D;

            visit.World[component] = world;
            return world;
        }

        private FTransform? SocketTransform(USceneComponent component, USceneComponent parent)
        {
            var socketName = TryEff<FName>(component, "AttachSocketName", out var socketFName) ? socketFName.Text : null;
            if (string.IsNullOrEmpty(socketName) || socketName == "None") return null;
            if (parent is not UStaticMeshComponent meshComponent) return null;

            try
            {
                var mesh = meshComponent.GetStaticMesh().Load<UStaticMesh>();
                foreach (var ptr in mesh?.Sockets ?? [])
                {
                    if (ptr.Load<UStaticMeshSocket>() is { } socket && socket.SocketName.Text == socketName)
                        return new FTransform(socket.RelativeRotation, socket.RelativeLocation, socket.RelativeScale);
                }
            }
            catch
            {
                // an unresolvable socket leaves the component at its parent's origin
            }
            return null;
        }

        private JObject BuildComponent(Scene scene, LevelVisit visit, UObject actor, UObject component, List<UObject> siblings)
        {
            var json = new JObject
            {
                ["name"] = component.Name,
                ["class"] = component.ExportType,
                ["native"] = component.GetType().Name,
                ["kind"] = "logic"
            };
            AddTemplateAndProps(scene, json, component, ComponentNoiseProps, null);

            if (component is not USceneComponent sceneComponent) return json;

            var parent = ResolveAttachParent(visit, sceneComponent);
            var world = WorldTransform(visit, sceneComponent);
            json["kind"] = "scene";
            if (parent != null)
            {
                var parentIndex = siblings.IndexOf(parent);
                if (parentIndex >= 0) json["parent"] = parentIndex;
                else json["parent_path"] = parent.GetPathName(); // attached to another actor's component
            }
            else
            {
                json["parent"] = JValue.CreateNull();
            }
            json["rel"] = Xf(RelativeTransform(sceneComponent));
            json["world"] = Xf(world);

            var socketName = TryEff<FName>(sceneComponent, "AttachSocketName", out var socketFName) ? socketFName.Text : null;
            if (!string.IsNullOrEmpty(socketName) && socketName != "None") json["attach_socket"] = socketName;

            if (component is not UPrimitiveComponent)
            {
                return json;
            }

            // Only non-default flags are written: visible defaults to true, hidden_in_game to false.
            if (!Eff(component, "bVisible", true)) json["visible"] = false;
            if (Eff(component, "bHiddenInGame", false)) json["hidden_in_game"] = true;

            // A missing key was not serialized anywhere in the template chain, i.e. it is the
            // native class default.
            var collision = new JObject();
            AddIfSet(collision, "profile", EffStructFieldToken(component, "BodyInstance", "CollisionProfileName"));
            AddIfSet(collision, "enabled", EffStructFieldToken(component, "BodyInstance", "CollisionEnabled"));
            AddIfSet(collision, "object_type", EffStructFieldToken(component, "BodyInstance", "ObjectType"));
            AddIfSet(collision, "use_default", EffToken(component, "bUseDefaultCollision"));
            var responses = ResponseSet(scene, EffStructFieldToken(component, "BodyInstance", "CollisionResponses"));
            if (responses >= 0) collision["responses"] = responses;
            if (collision.Count > 0) json["collision"] = collision;

            switch (component)
            {
                case USplineMeshComponent spline:
                {
                    json["kind"] = "spline_mesh";
                    var meshPath = RegisterMesh(scene, spline.GetStaticMesh());
                    json["mesh"] = meshPath;
                    var file = ExportSplineMesh(scene, visit, actor, spline);
                    if (file != null) json["model"] = file;
                    break;
                }
                case UInstancedStaticMeshComponent instanced:
                {
                    json["kind"] = "instanced_static_mesh";
                    json["mesh"] = RegisterMesh(scene, instanced.GetStaticMesh());
                    var instances = instanced.GetInstances();
                    var array = new JArray();
                    foreach (var instance in instances)
                    {
                        var instanceWorld = Mul(instance.TransformData, world);
                        array.Add(new JArray(
                            instanceWorld.Translation.X, instanceWorld.Translation.Y, instanceWorld.Translation.Z,
                            instanceWorld.Rotation.X, instanceWorld.Rotation.Y, instanceWorld.Rotation.Z, instanceWorld.Rotation.W,
                            instanceWorld.Scale3D.X, instanceWorld.Scale3D.Y, instanceWorld.Scale3D.Z));
                    }
                    json["instances"] = array;
                    scene.InstanceCount += array.Count;
                    break;
                }
                case UStaticMeshComponent staticMesh:
                {
                    json["kind"] = "static_mesh";
                    json["mesh"] = RegisterMesh(scene, staticMesh.GetStaticMesh());
                    if (json["mesh"]!.Type != JTokenType.Null) scene.InstanceCount++;
                    break;
                }
                case ULandscapeComponent:
                    json["kind"] = "landscape_component";
                    break;
                case UBrushComponent brush:
                {
                    json["kind"] = "brush";
                    json["convex"] = BrushConvexHulls(scene, actor, brush);
                    break;
                }
                case UBoxComponent:
                {
                    json["kind"] = "box";
                    var extent = Eff(component, "BoxExtent", new FVector(32f, 32f, 32f));
                    json["shape"] = new JObject { ["box_extent"] = Vec(extent) };
                    break;
                }
                case USphereComponent:
                {
                    json["kind"] = "sphere";
                    json["shape"] = new JObject { ["sphere_radius"] = Eff(component, "SphereRadius", 32f) };
                    break;
                }
                case UCapsuleComponent:
                {
                    json["kind"] = "capsule";
                    json["shape"] = new JObject
                    {
                        ["capsule_half_height"] = Eff(component, "CapsuleHalfHeight", 44f),
                        ["capsule_radius"] = Eff(component, "CapsuleRadius", 22f)
                    };
                    break;
                }
                default:
                    json["kind"] = "primitive";
                    break;
            }

            return json;
        }

        private JArray BrushConvexHulls(Scene scene, UObject actor, UBrushComponent brush)
        {
            var hulls = new JArray();
            var bodySetupIndex = Eff<FPackageIndex?>(brush, "BrushBodySetup", null);
            if (bodySetupIndex == null || bodySetupIndex.IsNull) return hulls;

            UBodySetup? bodySetup;
            try { bodySetup = bodySetupIndex.Load<UBodySetup>(); }
            catch (Exception ex) { scene.Warn($"{actor.Name}: brush body setup failed to load: {ex.Message}"); return hulls; }

            foreach (var elem in bodySetup?.AggGeom?.ConvexElems ?? [])
                hulls.Add(ConvexJson(elem));
            return hulls;
        }

        private static JObject ConvexJson(FKConvexElem elem)
        {
            // The element transform is baked in so the hull is directly in component/mesh space.
            var transform = Sanitized(elem.Transform);

            var vertices = new JArray();
            foreach (var vertex in elem.VertexData)
                vertices.Add(Vec(transform.TransformPosition(vertex)));

            return new JObject
            {
                ["vertices"] = vertices,
                ["indices"] = new JArray(elem.IndexData)
            };
        }

        // ---------------------------------------------------------------- geometry

        private JToken RegisterMesh(Scene scene, FPackageIndex? index)
        {
            if (index == null || index.IsNull) return JValue.CreateNull();

            string key;
            try { key = index.ResolvedObject?.GetPathName() ?? index.Name; }
            catch { key = index.Name; }

            if (!_meshCache.TryGetValue(key, out var summary))
            {
                summary = ExportStaticMesh(scene, index, key);
                _meshCache[key] = summary;
            }

            if (scene.Meshes[key] == null)
            {
                scene.Meshes[key] = summary != null ? summary.DeepClone() : new JObject { ["missing"] = true };
                // The export itself only warns in the first scene to use the mesh.
                if (summary == null && !scene.Warnings.Any(w => w.ToString().Contains(key)))
                    scene.Warn($"mesh {key} is not in the game files");
            }
            return key;
        }

        // Unreal treats package paths as case-insensitive, and a level may refer to a mesh in
        // another letter case than the path it was cooked under (".../Spaceships/SM_X" for
        // ".../SpaceShips/SM_X"). The provider looks paths up exactly, so a miss is retried
        // against the game files with the case ignored.
        private UStaticMesh? LoadStaticMesh(string objectPath)
        {
            try { return _provider.LoadPackageObject<UStaticMesh>(objectPath); }
            catch (KeyNotFoundException)
            {
                _packagesIgnoringCase ??= _provider.Files
                    .Where(file => file.Key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(file => file.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);

                var separator = objectPath.LastIndexOf('.');
                var packagePath = separator < 0 ? objectPath : objectPath[..separator];
                if (!_packagesIgnoringCase.TryGetValue(_provider.FixPath(packagePath), out var file)) throw;
                var objectName = separator < 0 ? packagePath[(packagePath.LastIndexOf('/') + 1)..] : objectPath[(separator + 1)..];
                return _provider.LoadPackage(file).GetExport<UStaticMesh>(objectName, StringComparison.OrdinalIgnoreCase);
            }
        }

        private JObject? ExportStaticMesh(Scene scene, FPackageIndex index, string key)
        {
            // Loaded through its own package rather than through the level's import table, which
            // would pin the mesh's vertex data in memory until the level itself is released.
            UStaticMesh? mesh;
            try { mesh = LoadStaticMesh(key); }
            catch (Exception ex) { scene.Warn($"mesh {key} is not loadable from the game files: {ex.Message}"); return null; }
            if (mesh == null) { scene.Warn($"mesh {key} is not in the game files"); return null; }

            var packagePath = (mesh.Owner?.Name ?? key.Split('.')[0]).TrimStart('/');
            var relativeBase = $"assets/{packagePath}";
            var summary = new JObject { ["object"] = key };

            try
            {
                using var dto = new StaticMeshDto(mesh, _meshOptions.MeshQuality, _meshOptions.NaniteMeshFormat);
                if (dto.LODs.Count > 0)
                {
                    var lod = dto.LODs[0];
                    var files = new UEFormatMeshFormat().BuildStaticMesh(mesh.Name, mesh.GetPathName(), _meshOptions, dto);
                    if (files.Count > 0)
                    {
                        WriteFile($"{relativeBase}.uemodel", files[0].Data);
                        summary["model"] = $"{relativeBase}.uemodel";
                        summary["vertices"] = lod.Vertices.Length;
                        summary["triangles"] = lod.Indices.Length / 3;
                        summary["nanite"] = lod.IsNanite;
                    }
                }
                else
                {
                    scene.Warn($"mesh {key} has no render LODs");
                }
            }
            catch (Exception ex)
            {
                scene.Warn($"mesh {key} geometry export failed: {ex.Message}");
            }

            if (mesh.RenderData?.Bounds is { } bounds)
            {
                summary["bounds"] = new JObject
                {
                    ["origin"] = Vec(bounds.Origin),
                    ["box_extent"] = Vec(bounds.BoxExtent),
                    ["sphere_radius"] = bounds.SphereRadius
                };
            }

            var info = (JObject) summary.DeepClone();
            info["package"] = packagePath;
            info["materials"] = new JArray(mesh.StaticMaterials.Select(m => new JObject
            {
                ["slot"] = m.MaterialSlotName.Text,
                ["material"] = SafeIndexPath(m.MaterialInterface)
            }));

            var sockets = new JArray();
            foreach (var ptr in mesh.Sockets ?? [])
            {
                try
                {
                    if (ptr.Load<UStaticMeshSocket>() is { } socket)
                        sockets.Add(new JObject
                        {
                            ["name"] = socket.SocketName.Text,
                            ["transform"] = Xf(new FTransform(socket.RelativeRotation, socket.RelativeLocation, socket.RelativeScale))
                        });
                }
                catch
                {
                    // a socket that cannot be read is simply not listed
                }
            }
            info["sockets"] = sockets;

            try
            {
                if (mesh.BodySetup is { IsNull: false } bodySetupIndex && bodySetupIndex.Load<UBodySetup>() is { } bodySetup)
                {
                    var collision = CollisionJson(bodySetup);
                    info["collision"] = collision;
                    summary["collision"] = new JObject
                    {
                        ["trace_flag"] = collision["trace_flag"],
                        ["default_profile"] = collision["default_profile"],
                        ["boxes"] = ((JArray) collision["boxes"]!).Count,
                        ["spheres"] = ((JArray) collision["spheres"]!).Count,
                        ["capsules"] = ((JArray) collision["capsules"]!).Count,
                        ["convex"] = ((JArray) collision["convex"]!).Count
                    };
                }
            }
            catch (Exception ex)
            {
                scene.Warn($"mesh {key} collision export failed: {ex.Message}");
            }

            WriteFile($"{relativeBase}.meshinfo.json", System.Text.Encoding.UTF8.GetBytes(info.ToString(Formatting.None)));
            summary["info"] = $"{relativeBase}.meshinfo.json";
            return summary;
        }

        private JObject CollisionJson(UBodySetup bodySetup)
        {
            var geom = bodySetup.AggGeom;
            var boxes = new JArray();
            var spheres = new JArray();
            var capsules = new JArray();
            var convex = new JArray();

            foreach (var box in geom?.BoxElems ?? [])
                boxes.Add(new JObject
                {
                    ["center"] = Vec(box.Center),
                    ["q"] = Quat(box.Rotation.Quaternion()),
                    ["size"] = new JArray(box.X, box.Y, box.Z) // full edge lengths
                });
            foreach (var sphere in geom?.SphereElems ?? [])
                spheres.Add(new JObject { ["center"] = Vec(sphere.Center), ["radius"] = sphere.Radius });
            foreach (var capsule in geom?.SphylElems ?? [])
                capsules.Add(new JObject
                {
                    ["center"] = Vec(capsule.Center),
                    ["q"] = Quat(capsule.Rotation.Quaternion()),
                    ["radius"] = capsule.Radius,
                    ["length"] = capsule.Length // cylinder part along local Z, excluding the caps
                });
            foreach (var elem in geom?.ConvexElems ?? [])
                convex.Add(ConvexJson(elem));

            var defaultInstance = bodySetup.GetOrDefault<FStructFallback?>("DefaultInstance");
            return new JObject
            {
                // absent = engine default (CTF_UseDefault: simple shapes for simple queries, render triangles for complex ones)
                ["trace_flag"] = PropToken(bodySetup, "CollisionTraceFlag"),
                ["default_profile"] = defaultInstance != null && defaultInstance.TryGetValue(out FName profile, "CollisionProfileName") ? profile.Text : null,
                ["default_instance"] = PropToken(bodySetup, "DefaultInstance"),
                ["boxes"] = boxes,
                ["spheres"] = spheres,
                ["capsules"] = capsules,
                ["convex"] = convex
            };
        }

        /// <summary>
        /// Writes a landscape as what it is, a regular height grid, rather than as triangles: one
        /// raw little-endian uint16 per grid vertex, row-major with +Y going down the rows.
        /// A triangulated full-resolution landscape is hundreds of MB; this is two bytes a vertex
        /// and lets the consumer pick its own resolution.
        /// </summary>
        private JObject? ExportLandscape(Scene scene, LevelVisit visit, ALandscapeProxy landscape)
        {
            if (landscape.LandscapeComponents.Length == 0) return null;
            try
            {
                var components = new List<(ULandscapeComponent Component, int BaseX, int BaseY)>();
                var sizeQuads = -1;
                foreach (var ptr in landscape.LandscapeComponents)
                {
                    if (ptr.Load<ULandscapeComponent>() is not { } component) continue;
                    if (sizeQuads < 0) sizeQuads = component.ComponentSizeQuads;
                    if (component.ComponentSizeQuads != sizeQuads)
                    {
                        scene.Warn($"{landscape.GetPathName()}: component {component.Name} has a different size, skipped");
                        continue;
                    }

                    // A component sits at an integer grid offset from the landscape actor.
                    var location = RelativeTransform(component).Translation;
                    if (MathF.Abs(location.Z) > 0.01f)
                        scene.Warn($"{landscape.GetPathName()}: component {component.Name} has a Z offset of {location.Z}, not applied");
                    components.Add((component, (int) MathF.Round(location.X), (int) MathF.Round(location.Y)));
                }
                if (components.Count == 0 || sizeQuads <= 0) return null;

                var minX = components.Min(c => c.BaseX);
                var minY = components.Min(c => c.BaseY);
                var width = components.Max(c => c.BaseX) + sizeQuads - minX + 1;
                var height = components.Max(c => c.BaseY) + sizeQuads - minY + 1;

                var grid = new byte[width * height * 2];
                var covered = new JArray();
                foreach (var (component, baseX, baseY) in components)
                {
                    var texture = component.GetHeightmap();
                    var mip = texture?.GetMip(0);
                    var data = mip?.BulkData?.Data;
                    if (texture == null || mip == null || data == null || texture.Format != CUE4Parse.UE4.Assets.Exports.Texture.EPixelFormat.PF_B8G8R8A8)
                    {
                        scene.Warn($"{landscape.GetPathName()}: component {component.Name} has no readable BGRA8 heightmap, left empty");
                        continue;
                    }

                    // Several components share one heightmap texture; the scale/bias locates this one.
                    var offsetX = (int) (mip.SizeX * component.HeightmapScaleBias.Z);
                    var offsetY = (int) (mip.SizeY * component.HeightmapScaleBias.W);
                    var subsectionQuads = component.SubsectionSizeQuads;
                    for (var y = 0; y <= sizeQuads; y++)
                    {
                        var texelY = SubsectionTexel(y, subsectionQuads) + offsetY;
                        var row = (baseY - minY + y) * width + (baseX - minX);
                        for (var x = 0; x <= sizeQuads; x++)
                        {
                            var texel = (SubsectionTexel(x, subsectionQuads) + offsetX + texelY * mip.SizeX) * 4;
                            // BGRA: the height is R (high byte) and G (low byte), stored little-endian here.
                            grid[(row + x) * 2] = data[texel + 1];
                            grid[(row + x) * 2 + 1] = data[texel + 2];
                        }
                    }
                    covered.Add(new JArray(baseX - minX, baseY - minY));
                }
                if (covered.Count == 0) return null;

                var relative = $"{scene.RelativeDir}/landscape/{UniqueFileName(scene, $"{visit.Package.Name.Split('/').Last()}.{landscape.Name}")}.r16";
                WriteFile(relative, grid);
                return new JObject
                {
                    ["heightfield"] = relative,
                    ["encoding"] = "uint16 little-endian, row-major, rows along +Y",
                    ["width"] = width,
                    ["height"] = height,
                    ["origin"] = new JArray(minX, minY),
                    ["z_offset"] = 32768,
                    ["z_scale"] = 1.0 / 128.0,
                    // local to the landscape actor; apply the actor "world" transform to place it
                    ["local_position"] = "x = origin[0] + column, y = origin[1] + row, z = (value - z_offset) * z_scale",
                    ["component_size_quads"] = sizeQuads,
                    // grid cells not under a listed component hold no terrain (their value is 0)
                    ["components"] = covered
                };
            }
            catch (Exception ex)
            {
                scene.Warn($"{landscape.GetPathName()}: landscape export failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Heightmap texel of a component vertex along one axis. Subsections each store their own
        /// copy of the shared edge vertex, so the texel index skips one per subsection crossed.
        /// </summary>
        private static int SubsectionTexel(int vertex, int subsectionQuads)
        {
            if (vertex <= 0) return 0;
            var subsection = (vertex - 1) / subsectionQuads;
            var within = (vertex - 1) % subsectionQuads + 1;
            return subsection * (subsectionQuads + 1) + within;
        }

        private string? ExportSplineMesh(Scene scene, LevelVisit visit, UObject actor, USplineMeshComponent spline)
        {
            if (spline.GetStaticMesh().IsNull) return null; // an unassigned spline segment draws nothing

            try
            {
                using var dto = new StaticMeshDto(spline, EMeshQuality.Highest);
                if (dto.LODs.Count == 0) return null;
                var files = new UEFormatMeshFormat().BuildStaticMesh(spline.Name, spline.GetPathName(), _meshOptions, dto);
                if (files.Count == 0) return null;

                // Deformed along the spline, in the component's local space.
                var name = UniqueFileName(scene, $"{visit.Package.Name.Split('/').Last()}.{actor.Name}.{spline.Name}");
                var relative = $"{scene.RelativeDir}/splines/{name}.uemodel";
                WriteFile(relative, files[0].Data);
                return relative;
            }
            catch (Exception ex)
            {
                scene.Warn($"{actor.Name}.{spline.Name}: spline mesh export failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes the cooked Recast navigation mesh (the walkable surface the bots path on) as
        /// JSON: per tile, vertices converted to Unreal world space and convex polygons over them.
        /// </summary>
        private JObject? ExportNavMesh(Scene scene, LevelVisit visit, ARecastNavMesh navMesh)
        {
            var impl = navMesh.RecastNavMeshImpl;
            if (impl?.DetourMeshTiles is not { Length: > 0 } tiles) return null;

            try
            {
                var tileArray = new JArray();
                int polyCount = 0, vertexCount = 0;
                foreach (var tileData in tiles)
                {
                    if (tileData?.Tile is not { } tile) continue;

                    // Recast is Y-up: unreal = (-x, -z, y). The level transform then places the tile.
                    var vertices = new JArray();
                    foreach (var v in tile.Vertices)
                        vertices.Add(Vec(visit.Transform.TransformPosition(new FVector(-v.X, -v.Z, v.Y))));

                    var polys = new JArray();
                    foreach (var poly in tile.Polys)
                    {
                        polys.Add(new JObject
                        {
                            ["v"] = new JArray(poly.Verts.Take(poly.VertCount).Select(i => (int) i)),
                            ["area"] = poly.AreaAndType & 0x3f,
                            ["type"] = poly.AreaAndType >> 6, // 0 = ground, 1 = off-mesh point link, 2 = off-mesh segment link
                            ["flags"] = poly.Flags
                        });
                    }

                    polyCount += polys.Count;
                    vertexCount += vertices.Count;
                    tileArray.Add(new JObject
                    {
                        ["x"] = tile.Header.X,
                        ["y"] = tile.Header.Y,
                        ["layer"] = tile.Header.Layer,
                        ["vertices"] = vertices,
                        ["polys"] = polys
                    });
                }
                if (tileArray.Count == 0) return null;

                var parameters = impl.DetourNavMeshParams;
                var root = new JObject
                {
                    ["actor"] = navMesh.GetPathName(),
                    ["agent_height"] = navMesh.AgentHeight,
                    ["agent_radius"] = navMesh.AgentRadius,
                    ["walkable_height"] = parameters.WalkableHeight,
                    ["walkable_radius"] = parameters.WalkableRadius,
                    ["walkable_climb"] = parameters.WalkableClimb,
                    ["tile_size"] = parameters.TileWidth,
                    ["tiles"] = tileArray
                };

                var name = UniqueFileName(scene, $"{visit.Package.Name.Split('/').Last()}.{navMesh.Name}");
                var relative = $"{scene.RelativeDir}/navmesh/{name}.json";
                WriteFile(relative, System.Text.Encoding.UTF8.GetBytes(root.ToString(Formatting.None)));
                return new JObject
                {
                    ["file"] = relative,
                    ["tiles"] = tileArray.Count,
                    ["polys"] = polyCount,
                    ["vertices"] = vertexCount,
                    ["agent_height"] = navMesh.AgentHeight,
                    ["agent_radius"] = navMesh.AgentRadius
                };
            }
            catch (Exception ex)
            {
                scene.Warn($"{navMesh.GetPathName()}: navmesh export failed: {ex.Message}");
                return null;
            }
        }

        private static string UniqueFileName(Scene scene, string name)
        {
            var safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_'));
            var candidate = safe;
            for (var i = 2; !scene.UsedFileNames.Add(candidate); i++)
                candidate = $"{safe}.{i}";
            return candidate;
        }

        private void DumpRawLevel(Scene scene, UWorld world)
        {
            try
            {
                var exports = new List<UObject>();
                foreach (var lazy in world.Owner!.ExportsLazy)
                {
                    try { exports.Add(lazy.Value); }
                    catch { /* unreadable exports are left out of the raw dump */ }
                }

                var path = Path.Combine(_outputPath, scene.RelativeDir, "levels", $"{world.Name}.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var sw = new StreamWriter(path);
                using var jw = new JsonTextWriter(sw) { Formatting = Formatting.Indented };
                _serializer.Serialize(jw, exports);
            }
            catch (Exception ex)
            {
                scene.Warn($"{world.Name}: raw level dump failed: {ex.Message}");
            }
        }

        private void WriteFile(string relativePath, byte[] data)
        {
            var path = Path.Combine(_outputPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
            Utils.LogInfo($"Exported: {path}", _options.IsLoggingEnabled);
        }

        // ---------------------------------------------------------------- transforms

        /// <summary>
        /// A transform property through the template chain. Struct properties are delta-serialized
        /// field by field, so a level offset may store nothing but its translation: each missing
        /// field takes the identity value rather than zero.
        /// </summary>
        private FTransform EffTransform(UObject obj, string name)
        {
            if (TryEff<FStructFallback>(obj, name, out var data))
            {
                return Sanitized(new FTransform(
                    data.TryGetValue(out FQuat rotation, "Rotation") ? rotation : FQuat.Identity,
                    data.TryGetValue(out FVector translation, "Translation") ? translation : FVector.ZeroVector,
                    data.TryGetValue(out FVector scale, "Scale3D") ? scale : FVector.OneVector));
            }
            return TryEff<FTransform>(obj, name, out var transform) ? Sanitized(transform) : FTransform.Identity;
        }

        /// <summary>
        /// Repairs the two ways a parsed transform can be unusable: a rotation that is zero, NaN
        /// or not unit length, and an all-zero scale left by a struct read without its defaults.
        /// </summary>
        private static FTransform Sanitized(FTransform transform)
        {
            var rotation = transform.Rotation;
            if (rotation.ContainsNaN() || rotation.SizeSquared < 1e-12f) rotation = FQuat.Identity;
            else if (!rotation.IsNormalized) rotation.Normalize();
            transform.Rotation = rotation;

            if (transform.Scale3D.X == 0f && transform.Scale3D.Y == 0f && transform.Scale3D.Z == 0f)
                transform.Scale3D = FVector.OneVector;
            if (transform.Translation.ContainsNaN()) transform.Translation = FVector.ZeroVector;
            return transform;
        }

        /// <summary>Child-then-parent composition, as the engine composes component transforms.</summary>
        private static FTransform Mul(FTransform child, FTransform parent)
        {
            if (!child.IsRotationNormalized) child.Rotation = SanitizedRotation(child.Rotation);
            if (!parent.IsRotationNormalized) parent.Rotation = SanitizedRotation(parent.Rotation);
            return child * parent;
        }

        private static FQuat SanitizedRotation(FQuat rotation)
        {
            if (rotation.ContainsNaN() || rotation.SizeSquared < 1e-12f) return FQuat.Identity;
            rotation.Normalize();
            return rotation;
        }

        // ---------------------------------------------------------------- templates

        private UObject? LoadTemplate(UObject obj)
        {
            if (_templateCache.TryGetValue(obj, out var cached)) return cached;
            UObject? template = null;
            try { template = obj.Template?.Load<UObject>(); }
            catch { /* native class defaults are not in the game files */ }
            if (ReferenceEquals(template, obj)) template = null;
            _templateCache[obj] = template;
            return template;
        }

        /// <summary>First value found walking the object and then its template chain.</summary>
        private T Eff<T>(UObject obj, string name, T fallback)
        {
            var current = obj;
            for (var guard = 0; current != null && guard < 16; guard++)
            {
                if (current.TryGetValue(out T value, name)) return value;
                current = LoadTemplate(current);
            }
            return fallback;
        }

        private bool TryEff<T>(UObject obj, string name, out T value)
        {
            var current = obj;
            for (var guard = 0; current != null && guard < 16; guard++)
            {
                if (current.TryGetValue(out value, name)) return true;
                current = LoadTemplate(current);
            }
            value = default!;
            return false;
        }

        /// <summary>
        /// A field of a struct property. Struct fields are delta-serialized too, so the instance
        /// may hold the struct without this particular field.
        /// </summary>
        private JToken EffStructFieldToken(UObject obj, string structName, string field)
        {
            var current = obj;
            for (var guard = 0; current != null && guard < 16; guard++)
            {
                if (current.TryGetValue(out FStructFallback data, structName))
                {
                    var tag = data.Properties.FirstOrDefault(p => p.Name.Text == field)?.Tag;
                    if (tag != null) return ToToken(tag);
                }
                current = LoadTemplate(current);
            }
            return JValue.CreateNull();
        }

        private JToken EffToken(UObject obj, string name)
        {
            var current = obj;
            for (var guard = 0; current != null && guard < 16; guard++)
            {
                var token = PropToken(current, name);
                if (token.Type != JTokenType.Null) return token;
                current = LoadTemplate(current);
            }
            return JValue.CreateNull();
        }

        private JToken PropToken(UObject obj, string name)
        {
            var tag = obj.Properties.FirstOrDefault(p => p.Name.Text == name)?.Tag;
            return tag != null ? ToToken(tag) : JValue.CreateNull();
        }

        private JToken ToToken(object value)
        {
            try { return JToken.FromObject(value, _serializer); }
            catch (Exception ex) { return $"<unserializable: {ex.Message}>"; }
        }

        private JObject PropsOf(UObject obj)
        {
            var json = new JObject();
            foreach (var property in obj.Properties)
            {
                var name = property.ArrayIndex > 0 ? $"{property.Name.Text}[{property.ArrayIndex}]" : property.Name.Text;
                json[name] = property.Tag != null ? ToToken(property.Tag) : JValue.CreateNull();
            }
            return json;
        }

        /// <summary>
        /// The object's own serialized properties, without the noise keys. Kept whole unless
        /// they are unreasonably large, in which case only the key names survive.
        /// </summary>
        private JToken InstanceProps(Scene scene, UObject obj, HashSet<string>? noise = null)
        {
            var props = PropsOf(obj);
            if (noise != null)
            {
                foreach (var name in props.Properties().Select(p => p.Name).Where(noise.Contains).ToList())
                    props.Remove(name);
            }
            return Truncated(scene, props);
        }

        private JObject Truncated(Scene scene, JObject props)
        {
            if (props.Count == 0) return props;
            var size = props.ToString(Formatting.None).Length;
            if (size <= MaxInstancePropsChars) return props;

            scene.TruncatedProps++;
            return new JObject { ["__truncated"] = true, ["__chars"] = size, ["__keys"] = new JArray(props.Properties().Select(p => p.Name)) };
        }

        /// <summary>
        /// Adds "template" (key into the scene's templates) and "props" (own properties) when
        /// present. For actors, properties that merely point at one of the actor's own components
        /// are moved to "component_refs" as property name -> component index.
        /// </summary>
        private void AddTemplateAndProps(Scene scene, JObject json, UObject obj, HashSet<string> noise, List<UObject>? ownComponents)
        {
            var template = LoadTemplate(obj);
            if (template != null)
            {
                var key = template.GetPathName();
                if (scene.Templates[key] == null)
                    scene.Templates[key] = EffectiveProps(template, 0).DeepClone();
                json["template"] = key;
            }

            var props = new JObject();
            JObject? componentRefs = null;
            foreach (var property in obj.Properties)
            {
                var name = property.ArrayIndex > 0 ? $"{property.Name.Text}[{property.ArrayIndex}]" : property.Name.Text;
                if (noise.Contains(property.Name.Text)) continue;

                if (ownComponents != null && property.Tag?.GenericValue is FPackageIndex { IsExport: true } index)
                {
                    var target = obj.Owner != null && index.Index - 1 < obj.Owner.ExportsLazy.Length ? obj.Owner.ExportsLazy[index.Index - 1] : null;
                    var componentIndex = target is { IsValueCreated: true } ? ownComponents.IndexOf(target.Value) : -1;
                    if (componentIndex >= 0)
                    {
                        (componentRefs ??= new JObject())[name] = componentIndex;
                        continue;
                    }
                }

                props[name] = property.Tag != null ? ToToken(property.Tag) : JValue.CreateNull();
            }

            if (componentRefs != null) json["component_refs"] = componentRefs;
            if (props.Count > 0) json["props"] = Truncated(scene, props);
        }

        private static void AddIfSet(JObject target, string name, JToken value)
        {
            if (value.Type != JTokenType.Null) target[name] = value;
        }

        /// <summary>
        /// Interns a CollisionResponses struct as {channel: Block|Overlap|Ignore} in the scene's
        /// collision_response_sets and returns its index, or -1 when there is none.
        /// </summary>
        private static int ResponseSet(Scene scene, JToken responses)
        {
            if (responses is not JObject obj || obj["ResponseArray"] is not JArray array || array.Count == 0) return -1;

            var set = new JObject();
            foreach (var entry in array.OfType<JObject>())
            {
                var channel = entry["Channel"]?.ToString();
                var response = entry["Response"]?.ToString();
                if (channel == null || response == null) continue;
                set[channel] = response.Replace("ECollisionResponse::ECR_", "");
            }

            var key = set.ToString(Formatting.None);
            if (!scene.ResponseSetIndex.TryGetValue(key, out var index))
            {
                index = scene.ResponseSets.Count;
                scene.ResponseSets.Add(set);
                scene.ResponseSetIndex[key] = index;
            }
            return index;
        }

        private JObject EffectiveProps(UObject obj, int depth)
        {
            var key = obj.GetPathName();
            if (_effectivePropsCache.TryGetValue(key, out var cached)) return cached;

            var parent = depth < 16 ? LoadTemplate(obj) : null;
            var result = parent != null ? (JObject) EffectiveProps(parent, depth + 1).DeepClone() : new JObject();
            MergeProps(result, PropsOf(obj));
            _effectivePropsCache[key] = result;
            return result;
        }

        /// <summary>Objects (structs) merge field by field; everything else is replaced wholesale.</summary>
        private static void MergeProps(JObject target, JObject source)
        {
            foreach (var property in source.Properties())
            {
                if (property.Value is JObject sourceObject && target[property.Name] is JObject targetObject)
                    MergeProps(targetObject, sourceObject);
                else
                    target[property.Name] = property.Value.DeepClone();
            }
        }

        /// <summary>
        /// Class name followed by its ancestors, Blueprint parents first and then the native
        /// hierarchy from the mappings file, so a consumer can test "is a PlayerStart" by name.
        /// </summary>
        private JArray ClassChain(UObject obj)
        {
            var cls = obj.Class;
            if (cls == null) return new JArray(obj.ExportType);

            var key = SafePath(cls) ?? cls.Name.Text;
            if (_classChainCache.TryGetValue(key, out var cached)) return (JArray) cached.DeepClone();

            var chain = new List<string>();
            ResolvedObject? current = cls;
            for (var guard = 0; current != null && guard < 32; guard++)
            {
                chain.Add(current.Name.Text);
                ResolvedObject? next = null;
                try
                {
                    next = current.Super;
                    if (next == null && current.Object?.Value is UStruct { SuperStruct: { IsNull: false } superStruct })
                        next = superStruct.ResolvedObject;
                }
                catch
                {
                    // script classes have no loadable super; the mappings take over below
                }
                current = next;
            }

            var types = _provider.MappingsForGame?.Types;
            var last = chain[^1];
            for (var guard = 0; types != null && guard < 64; guard++)
            {
                if (!types.TryGetValue(last, out var type) || string.IsNullOrEmpty(type.SuperType)) break;
                chain.Add(type.SuperType);
                last = type.SuperType;
            }

            var result = new JArray(chain.Distinct());
            _classChainCache[key] = result;
            return (JArray) result.DeepClone();
        }

        // ---------------------------------------------------------------- json helpers

        private static string? SafePath(ResolvedObject? obj)
        {
            try { return obj?.GetPathName(); }
            catch { return obj?.Name.Text; }
        }

        private static string? SafeIndexPath(FPackageIndex? index)
        {
            if (index == null || index.IsNull) return null;
            try { return index.ResolvedObject?.GetPathName() ?? index.Name; }
            catch { return index.Name; }
        }

        private static JToken IntOrNull(int value) => value < 0 ? JValue.CreateNull() : new JValue(value);

        private static JArray Vec(FVector v) => new(v.X, v.Y, v.Z);

        private static JArray Quat(FQuat q) => new(q.X, q.Y, q.Z, q.W);

        private static JObject Xf(FTransform transform) => new()
        {
            ["t"] = Vec(transform.Translation),
            ["q"] = Quat(transform.Rotation),
            ["s"] = Vec(transform.Scale3D)
        };
    }
}
