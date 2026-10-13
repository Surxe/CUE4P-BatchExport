# Map Export

`--export-mode Maps` exports levels instead of individual assets: for each selected `.umap` it
writes a **scene** describing every actor and component of the level with resolved world
transforms, plus the geometry the scene refers to. The output is enough to rebuild the level
outside the engine: where every mesh sits, the terrain, the invisible blocking volumes, the
navigation mesh, and the gameplay actors with their properties.

```bash
BatchExport --preset WarRobotsFrontiers \
  --export-mode Maps \
  --map-filters "Sparrow/Maps/Mont/Mont,Sparrow/Maps/EM1/EM1" \
  --pak-files-directory "C:\Game" \
  --export-output-path "C:\MapExport" \
  --mapping-file-path "C:\mappings.usmap"
```

| Argument | Description |
|----------|-------------|
| `--export-mode <Assets\|Maps>` | `Assets` (default) is the regular per-asset export; `Maps` exports levels as scenes |
| `--map-filters <list>` | Comma-separated `.umap` package paths (without extension) to export as root worlds. An entry matches the full path or a trailing part of it (`Mont/Mont`). Unset = every `.umap` under the needed directories |
| `--map-use-nanite-meshes <true\|false>` | Export full Nanite geometry instead of the fallback LOD0 (default `false`) |
| `--map-export-raw-levels <true\|false>` | Also dump each visited level package as plain JSON under `maps/<World>/levels/` (default `false`) |

Pass the persistent level of each map as the filter. Its streaming sublevels and level instances
are followed from there; exporting a sublevel on its own gives that sublevel only.

Peak memory is roughly 1-1.5 GB for a large map. The worker count setting does not apply: maps
are exported one after another on a single file provider.

## What a map is made of

A cooked map is a tree of levels:

- the **persistent level** of the root world;
- its **streaming sublevels**, either always loaded (the map's static geometry is usually split
  across these) or loaded dynamically by game code (game-mode objects, lighting variants, debug);
- **level instances**: prefab levels placed by a `LevelInstance` actor, at that actor's
  transform, inside any of the above and nested to any depth.

The exporter walks the whole tree. It cannot know when game code loads a dynamic sublevel, so
those are exported too and marked as such; choosing which ones apply is up to the consumer.

Only the root world's streaming list is followed. The engine ignores the streaming list of a
world that is itself loaded as a sublevel, and so does the export.

### Template resolution

Cooked levels store only what differs from the class or Blueprint defaults. A Blueprint actor
placed in a level typically serializes its location and nothing else; its mesh, the relative
transforms of its child components and its collision settings live in the Blueprint's default
objects. Every field the scene resolves (transforms, attachment, mesh, shape sizes, collision) is
therefore looked up through the object's whole template (archetype) chain, and struct properties
are merged field by field. A value that appears nowhere in the chain is a native C++ class
default, which the game files do not contain.

## Output layout

```
<output>/
  maps/<World>/
    scene.json          levels, templates, mesh index, stats, warnings
    actors.jsonl        one actor per line, with its components
    landscape/*.r16     terrain height grids
    splines/*.uemodel   spline meshes, already deformed along their spline
    navmesh/*.json      navigation mesh polygons
    levels/*.json       raw level dumps (only with --map-export-raw-levels)
  assets/<package path>.uemodel        static mesh geometry, shared by all maps
  assets/<package path>.meshinfo.json  bounds, materials, sockets, collision of that mesh
  config/**/*.ini       the game's config files (collision profiles and channels)
```

Every file path inside the JSON is relative to `<output>`.

## Coordinates

Unreal's own: **centimeters, left-handed, +Z up, +X forward**. Nothing is converted.

A transform is `{"t": [x, y, z], "q": [x, y, z, w], "s": [x, y, z]}`: translation, rotation
quaternion, scale. It maps a local point to

```
world = t + rotate(q, s * local)
```

i.e. scale, then rotate, then translate. World transforms are composed the way the engine
composes component transforms (rotations multiply, scales multiply per axis, no shear), so they
match what the game renders even under non-uniform scale.

## scene.json

| Key | Content |
|-----|---------|
| `format`, `version` | `"ue-map-scene"`, `1` |
| `world`, `name` | Object path and name of the root world |
| `mesh_geometry` | `"fallback_lod0"` or `"nanite"`: which geometry the `.uemodel` files hold |
| `stats` | Counts of levels, actors, components, mesh instances, unique meshes, warnings |
| `actors_file` | `"actors.jsonl"` |
| `levels` | The level tree, see below |
| `collision_response_sets` | Interned custom collision responses, referenced by index from components |
| `templates` | Template path to its effective properties (own values merged over its archetype chain) |
| `meshes` | Mesh object path to a summary of the exported mesh |
| `warnings` | Anything that could not be exported, with the reason |

### levels

| Key | Content |
|-----|---------|
| `index` | Position in the array; actors refer to it as `level` |
| `world`, `name` | Object path and name of the level's world |
| `kind` | `persistent`, `streaming_always_loaded`, `streaming_dynamic` or `level_instance` |
| `parent` | Index of the level it was loaded from, `null` for the persistent level |
| `transform` | Offset applied to everything in the level (already included in all world transforms) |
| `streaming` | For streaming sublevels: name, class and properties of the `LevelStreaming` object |
| `instance_actor` | For level instances: id and name of the `LevelInstance` actor that placed it |
| `world_settings_actor` | Id of the level's world settings actor |
| `actor_count` | Actors in this level |
| `missing` | `true` when the level is referenced but not in the game files |

A level is part of the base map when its outermost non-persistent ancestor is
`streaming_always_loaded` (or it is the persistent level itself). The same prefab world can
appear many times as separate `level_instance` entries, each with its own transform and actors.

### meshes

`model` is the `.uemodel` file, `info` the `.meshinfo.json`, `vertices`/`triangles` the size of
the exported LOD, `bounds` the mesh's local box (`origin`, `box_extent` as half sizes,
`sphere_radius`), and `collision` a count of its simple collision shapes with its `trace_flag`
and `default_profile`. A mesh that is referenced but absent from the game files is
`{"missing": true}`.

## actors.jsonl

One JSON object per line, in `id` order, ids starting at 0.

| Key | Content |
|-----|---------|
| `id` | Unique within the scene |
| `level` | Index into `levels` |
| `name`, `label` | Object name and editor label |
| `class`, `class_path` | Class name and its object path |
| `class_chain` | The class and all its ancestors, Blueprint parents first, then the native hierarchy, e.g. `["BP_TeamPlayerStart_C", "STeamPlayerStart", "PlayerStart", "NavigationObjectBase", "Actor", "Object"]` |
| `native` | The CUE4Parse type the actor was parsed as |
| `path` | Full object path |
| `world` | World transform of the root component |
| `root` | Index of the root component in `components`, or `null` |
| `hidden` | `true` when the actor is hidden; absent otherwise |
| `tags` | Actor tags, when any |
| `template` | Key into `templates` |
| `props` | The actor's own serialized properties (see below) |
| `component_refs` | Property name to component index, for properties that point at the actor's own components |
| `components` | The actor's components |
| `level_instance` | For level instance actors: the prefab `world` and the `level` index it was expanded into |
| `landscape` | For landscapes: the height grid, see below |
| `navmesh` | For navigation meshes: the polygon file, see below |

### components

Components are found by ownership, so natively created ones are included, not only those the
actor lists in `RootComponent` or `BlueprintCreatedComponents`.

| Key | Content |
|-----|---------|
| `name`, `class`, `native` | As for actors |
| `kind` | `static_mesh`, `instanced_static_mesh`, `spline_mesh`, `brush`, `box`, `sphere`, `capsule`, `landscape_component`, `primitive` (other primitive), `scene` (transform only), `logic` (no transform), `error` (could not be exported; `error` holds the reason) |
| `template`, `props` | As for actors |
| `parent` | Index of the attach parent in the same actor, `null` for an unattached component |
| `parent_path` | Instead of `parent`, when attached to a component of another actor |
| `rel`, `world` | Relative and world transform (scene components only) |
| `attach_socket` | Socket of the parent mesh the component is attached to, when any |
| `visible`, `hidden_in_game` | Only when not the default (`true`, `false`) |
| `collision` | Collision settings, see below |
| `mesh` | Mesh object path, a key into `meshes` (`null` when unassigned) |
| `instances` | `instanced_static_mesh`: one `[tx,ty,tz, qx,qy,qz,qw, sx,sy,sz]` per instance, **in world space** |
| `model` | `spline_mesh`: the deformed mesh file, in the component's local space |
| `convex` | `brush`: convex hulls as `{"vertices": [[x,y,z], ...], "indices": [...]}` (triangle list), in the component's local space |
| `shape` | `box`: `box_extent` (half sizes); `sphere`: `sphere_radius`; `capsule`: `capsule_half_height`, `capsule_radius`. Unscaled: multiply by the world scale |

### props

`props` holds what the object itself serializes, as CUE4Parse writes it. For the complete
picture merge it over `templates[template]`, field by field for objects. Properties the scene
already carries as resolved fields are left out of `props`: transforms and attachment, the mesh
pointer, shape sizes, visibility, the collision body, and render-only data such as material
overrides. Properties above 256 KB are replaced by `{"__truncated": true, "__chars": n,
"__keys": [...]}`; `--map-export-raw-levels` keeps them.

An object reference is `{"ObjectName": "Class'Outer:PersistentLevel.Name'", "ObjectPath": "..."}`.
To resolve one to an actor, match the name against the actors of the same `level`.

### collision

Each key is present only when it is set somewhere in the component's template chain; a missing
key means the native default of the component's class.

| Key | Content |
|-----|---------|
| `profile` | Collision profile name, defined in `config/**/DefaultEngine.ini` |
| `enabled` | `ECollisionEnabled::...` |
| `object_type` | `ECollisionChannel::...` |
| `use_default` | `bUseDefaultCollision`: take the mesh's own default collision instead |
| `responses` | Index into `collision_response_sets`: `{channel: "Block" \| "Overlap" \| "Ignore"}`. Only meaningful with profile `Custom`; unlisted channels keep their default response |

A static mesh actor's component uses the mesh's default collision unless it says otherwise, so
when `profile` and `enabled` are both absent the mesh's `default_profile` applies.

### landscape

A landscape is exported as the height grid it is, not as triangles:

| Key | Content |
|-----|---------|
| `heightfield` | Raw file: one little-endian `uint16` per grid vertex, row-major, rows along +Y |
| `width`, `height` | Grid size in vertices |
| `origin` | Grid coordinates of the first column and row |
| `z_offset`, `z_scale` | Height decoding (`32768`, `1/128`) |
| `component_size_quads` | Edge length of one landscape component in quads |
| `components` | `[column, row]` of each component present; cells outside all of them hold no terrain |

A grid vertex sits at `x = origin[0] + column`, `y = origin[1] + row`,
`z = (value - z_offset) * z_scale` in the landscape actor's local space; apply the actor's
`world` transform (its scale is usually 100) to place it. Holes cut with a visibility layer are
not marked.

### navmesh

`file` is a JSON document with the agent size (`agent_height`, `agent_radius`,
`walkable_climb`) and the Recast `tiles`. Each tile has `vertices` in Unreal world space and
`polys`: convex polygons as `{"v": [vertex indices], "area", "type", "flags"}`, where `type`
`0` is walkable ground and `1`/`2` are off-mesh links. A map has one navmesh per agent size.

## Mesh files

`.uemodel` is [UEFormat](https://github.com/h4lfheart/UEFormat) with zstd compression, the same
format the asset export writes: vertices, normals, UVs, triangle indices and material sections of
one LOD, in the mesh's local space, plus the mesh's convex collision hulls.

`.meshinfo.json` adds what the model file lacks:

| Key | Content |
|-----|---------|
| `object`, `package` | Object and package path of the mesh |
| `model`, `vertices`, `triangles`, `nanite`, `bounds` | As in the scene's `meshes` |
| `materials` | `slot` name and `material` path per material slot |
| `sockets` | `name` and local `transform` of each socket |
| `collision.trace_flag` | `ECollisionTraceFlag::...`; `null` is the engine default (simple shapes for simple queries, render triangles for complex ones). `CTF_UseComplexAsSimple` collides per triangle and has no shapes |
| `collision.default_profile`, `collision.default_instance` | The mesh's default collision body |
| `collision.boxes` | `center`, rotation `q`, full edge lengths `size` |
| `collision.spheres` | `center`, `radius` |
| `collision.capsules` | `center`, rotation `q`, `radius`, and `length` of the cylinder part along local Z |
| `collision.convex` | Hulls as `vertices` and triangle `indices`, element transform already applied |

## Not exported

- Skeletal meshes placed in levels, decals, lights, particles and audio appear as components with
  their transform and properties, but without geometry.
- Materials and textures.
- Landscape holes, and the separate simplified landscape collision.
- Anything a Blueprint builds at runtime in its construction script or event graph.
