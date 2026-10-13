using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Formats.Meshes;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Textures;
using Newtonsoft.Json;
using SkiaSharp;
using ETextureFormat = BatchExport.Enums.ETextureFormat;
using ExportOptions = CUE4Parse_Conversion.Options.ExportOptions;
using ConversionMeshFormat = CUE4Parse_Conversion.Options.EMeshFormat;
using ConversionNaniteMeshFormat = CUE4Parse_Conversion.Options.ENaniteMeshFormat;
using ConversionSocketFormat = CUE4Parse_Conversion.Options.ESocketFormat;

namespace BatchExport
{
    public class AssetExporter
    {
        private readonly ExporterOptions _options;
        private readonly string _outputPath;
        private readonly bool _isLoggingEnabled;
        private readonly bool _shouldExportTextures;
        private readonly bool _shouldExportMeshes;
        private readonly string[] _textureExportDirectories;

        public AssetExporter(ExporterOptions options, string outputPath, bool isLoggingEnabled, bool shouldExportTextures, bool shouldExportMeshes, string[] textureExportDirectories)
        {
            _options = options;
            _outputPath = outputPath;
            _isLoggingEnabled = isLoggingEnabled;
            _shouldExportTextures = shouldExportTextures;
            _shouldExportMeshes = shouldExportMeshes;
            _textureExportDirectories = textureExportDirectories ?? Array.Empty<string>();
        }

        /// <summary>
        /// Whether a texture's .png should be written. When TextureExportDirectories is
        /// configured, only textures under those asset-path prefixes (e.g. UI icons) are
        /// decoded; model material textures are skipped.
        /// </summary>
        private bool IsIconTexture(string assetPath)
        {
            if (_textureExportDirectories.Length == 0)
                return true;

            return _textureExportDirectories.Any(dir => assetPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
        }

        public void ExportAsset(DefaultFileProvider provider, string assetPath)
        {
            try
            {
                var package = provider.LoadPackage(assetPath);
                Lazy<UObject>[] exportsLazy = package.ExportsLazy;

                // Force evaluation of all lazy exports upfront
                UObject[] exports = new UObject[exportsLazy.Length];
                for (int i = 0; i < exportsLazy.Length; i++)
                {
                    exports[i] = exportsLazy[i].Value;
                }

                bool textureExported = false;

                // Single pass through all exports
                foreach (UObject export in exports)
                {
                    // Handle SVG assets
                    if (export.ExportType.Contains("SvgAsset"))
                    {
                        try
                        {
                            ExportSvgAsset(export, assetPath);
                        }
                        catch (Exception ex)
                        {
                            Utils.LogInfo($"Failed to export SVG {export.Name} from {assetPath}: {ex.Message}", _isLoggingEnabled);
                        }
                        continue;
                    }

                    // Export only the first texture found if texture export is enabled
                    if (export is UTexture2D texture && !textureExported && _shouldExportTextures && IsIconTexture(assetPath))
                    {
                        try
                        {
                            ExportTexture(texture, assetPath);
                            textureExported = true;
                        }
                        catch (Exception ex)
                        {
                            Utils.LogInfo($"Failed to export texture from {assetPath}: {ex.Message}", _isLoggingEnabled);
                        }
                        continue;
                    }

                    // Handle other asset types
                    try
                    {
                        switch (export)
                        {
                            case UMaterialInterface material:
                                ExportMaterial(material, assetPath);
                                break;

                            case UAnimSequence anim:
                                ExportAnimation(anim, assetPath);
                                break;

                            case UStaticMesh staticMesh:
                                ExportStaticMesh(staticMesh, assetPath);
                                break;

                            case USkeletalMesh skeletalMesh:
                                ExportSkeletalMesh(skeletalMesh, assetPath);
                                break;

                            case USkeleton skeleton:
                                ExportSkeleton(skeleton, assetPath);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Utils.LogInfo($"Failed to export {export.Name} from {assetPath}: {ex.Message}", _isLoggingEnabled);
                    }
                }

                // Always export JSON
                ExportToJson(exports, assetPath);
            }
            catch (Exception ex)
            {
                Utils.LogInfo($"Failed to export asset {assetPath}: {ex.Message}", _isLoggingEnabled);
            }
        }

        private void ExportSvgAsset(UObject export, string assetPath)
        {
            Utils.LogInfo($"Found SVG asset in {assetPath}", _isLoggingEnabled);

            var svgProperty = export.Properties?.FirstOrDefault(p => p.Name == "Data");
            var svgData = svgProperty?.Tag?.ToString();

            if (!string.IsNullOrEmpty(svgData))
            {
                var svgPath = Path.Combine(_outputPath, $"{assetPath}.svg");
                CreateNeededDirectories(svgPath);
                File.WriteAllText(svgPath, svgData);
                Utils.LogInfo($"Exported SVG: {svgPath}", _isLoggingEnabled);
            }
        }

        private void ExportTexture(UTexture2D texture, string assetPath)
        {
            try
            {
                string extension = GetTextureFormatAsString();

                var destinationFilePath = Path.Combine(_outputPath, $"{assetPath}.{extension}");
                CreateNeededDirectories(destinationFilePath);

                var textureData = texture.Decode();
                if (textureData == null)
                {
                    Utils.LogInfo($"Failed to decode texture {assetPath} - skipping", _isLoggingEnabled);
                    return;
                }

                // Create empty SKBitmap from SKImage frame
                var imageInfo = new SKImageInfo(textureData.Width, textureData.Height, GetSKColorType(textureData.PixelFormat));
                using var bitmap = new SKBitmap(imageInfo);

                var pixelPtr = bitmap.GetPixels();
                if (pixelPtr == IntPtr.Zero)
                {
                    Utils.LogInfo($"Failed to get pixel pointer for texture {assetPath} - skipping", _isLoggingEnabled);
                    return;
                }

                // Validate data size matches expected size
                int expectedSize = bitmap.ByteCount;
                if (textureData.Data.Length != expectedSize)
                {
                    Utils.LogInfo($"Data size mismatch for texture {assetPath}: expected {expectedSize} bytes, got {textureData.Data.Length} bytes - skipping", _isLoggingEnabled);
                    return;
                }

                // Set the bitmap's pointer to the CTexture's byte array (pixel data)
                System.Runtime.InteropServices.Marshal.Copy(textureData.Data, 0, pixelPtr, textureData.Data.Length);

                if (_options.TextureFormat == ETextureFormat.Png)
                {
                    using var pixmap = bitmap.PeekPixels();
                    var options = new SKPngEncoderOptions(SKPngEncoderFilterFlags.Sub | SKPngEncoderFilterFlags.Up , 1);
                    using var data = pixmap.Encode(options);
                    if (data == null)
                    {
                        Utils.LogInfo($"Failed to create data from bitmap for texture {assetPath} - skipping", _isLoggingEnabled);
                        return;
                    }
                    using var stream = File.OpenWrite(destinationFilePath);
                    data.SaveTo(stream);
                }
                // Encode for all types other than png
                else
                {
                    using var image = SKImage.FromBitmap(bitmap);
                    if (image == null)
                    {
                        Utils.LogInfo($"Failed to create image from bitmap for texture {assetPath} - skipping", _isLoggingEnabled);
                        return;
                    }
                    using var data = image.Encode(GetSkiaFormat(_options.TextureFormat), 100);
                    if (data == null)
                    {
                        Utils.LogInfo($"Failed to encode image for texture {assetPath} - skipping", _isLoggingEnabled);
                        return;
                    }
                    using var stream = File.OpenWrite(destinationFilePath);
                    data.SaveTo(stream);
                }
            }
            catch (Exception ex)
            {
                Utils.LogInfo($"Failed to export texture {assetPath}: {ex.Message}", _isLoggingEnabled);
                if (_isLoggingEnabled)
                {
                    Utils.LogInfo($"Stack trace: {ex.StackTrace}", _isLoggingEnabled);
                    if (ex.InnerException != null)
                    {
                        Utils.LogInfo($"Inner exception: {ex.InnerException.Message}", _isLoggingEnabled);
                        Utils.LogInfo($"Inner stack trace: {ex.InnerException.StackTrace}", _isLoggingEnabled);
                    }
                }
                // Don't attempt JSON fallback for memory-related errors
                if (!(ex is OutOfMemoryException || ex.InnerException is OutOfMemoryException))
                {
                    try
                    {
                        // Export as a list containing the single texture
                        ExportToJson(new[] { texture }, assetPath);
                    }
                    catch
                    {
                        // Ignore JSON fallback errors
                    }
                }
            }
        }

        private void ExportMaterial(UMaterialInterface material, string assetPath)
        {
            // Export as a list containing the single material
            ExportToJson(new[] { material }, assetPath);
        }

        private void ExportAnimation(UAnimSequence anim, string assetPath)
        {
            // Export as a list containing the single animation
            ExportToJson(new[] { anim }, assetPath);
        }

        private void ExportStaticMesh(UStaticMesh mesh, string assetPath)
        {
            // Export as a list containing the single mesh
            ExportToJson(new[] { mesh }, assetPath);

            if (_shouldExportMeshes)
            {
                ExportMeshGeometry(mesh, assetPath);
            }
        }

        private void ExportSkeletalMesh(USkeletalMesh mesh, string assetPath)
        {
            // Export as a list containing the single mesh
            ExportToJson(new[] { mesh }, assetPath);

            if (_shouldExportMeshes)
            {
                ExportMeshGeometry(mesh, assetPath);
            }
        }

        private void ExportSkeleton(USkeleton skeleton, string assetPath)
        {
            // Export as a list containing the single skeleton
            ExportToJson(new[] { skeleton }, assetPath);

            if (_shouldExportMeshes)
            {
                ExportMeshGeometry(skeleton, assetPath);
            }
        }

        /// <summary>
        /// Exports mesh geometry (vertices/indices/normals, plus bones and sockets for
        /// skeletal meshes) as a UEFormat .uemodel file via CUE4Parse-Conversion, so the
        /// hitbox + untextured model can be reconstructed downstream.
        /// </summary>
        private void ExportMeshGeometry(UObject export, string assetPath)
        {
            // Map the (subset) BatchExport enums onto CUE4Parse-Conversion's.
            var conversionOptions = new ExportOptions(
                meshFormat: _options.MeshFormat switch
                {
                    BatchExport.Enums.EMeshFormat.ActorX => ConversionMeshFormat.ActorX,
                    BatchExport.Enums.EMeshFormat.GLB => ConversionMeshFormat.Gltf2,
                    _ => ConversionMeshFormat.UEFormat
                },
                naniteMeshFormat: _options.NaniteMeshFormat switch
                {
                    BatchExport.Enums.ENaniteMeshFormat.OnlyInterimLOD => ConversionNaniteMeshFormat.NoNanite,
                    BatchExport.Enums.ENaniteMeshFormat.BothLODs => ConversionNaniteMeshFormat.NaniteFirst,
                    _ => ConversionNaniteMeshFormat.NaniteOnly
                },
                meshQuality: _options.LodFormat == BatchExport.Enums.ELodFormat.AllLods ? EMeshQuality.All : EMeshQuality.Highest,
                texturePlatform: _options.Platform,
                exportHdrTexturesAsHdr: _options.ExportHdrTexturesAsHdr,
                exportMaterials: _options.ExportMaterials,
                exportMorphTargets: _options.ExportMorphTargets,
                socketFormat: _options.SocketFormat == BatchExport.Enums.ESocketFormat.None ? ConversionSocketFormat.None : ConversionSocketFormat.Bone,
                // Match FModel's output: zstd-compressed .uemodel payloads.
                compressionFormat: CUE4Parse_Conversion.Writers.UEFormat.Enums.EFileCompressionFormat.ZSTD
            );

            IMeshExportFormat format = conversionOptions.MeshFormat switch
            {
                ConversionMeshFormat.ActorX => new ActorXMeshFormat(),
                ConversionMeshFormat.Gltf2 => new GltfMeshFormat(),
                _ => new UEFormatMeshFormat()
            };

            try
            {
                // Build the files directly instead of going through ExportSession, whose
                // output path resolution rewrites '/' to '\' and breaks on Linux.
                IReadOnlyList<ExportFile> files;
                switch (export)
                {
                    case UStaticMesh staticMesh:
                    {
                        using var dto = new StaticMeshDto(staticMesh, conversionOptions.MeshQuality, conversionOptions.NaniteMeshFormat);
                        files = dto.LODs.Count == 0 ? [] : format.BuildStaticMesh(export.Name, export.GetPathName(), conversionOptions, dto);
                        break;
                    }
                    case USkeletalMesh skeletalMesh:
                    {
                        using var dto = new SkeletalMeshDto(skeletalMesh, conversionOptions.MeshQuality, conversionOptions.NaniteMeshFormat, conversionOptions.ExportMorphTargets);
                        files = dto.LODs.Count == 0 ? [] : format.BuildSkeletalMesh(export.Name, export.GetPathName(), conversionOptions, dto);
                        break;
                    }
                    case USkeleton skeleton:
                    {
                        using var dto = new SkeletonDto(skeleton);
                        files = format.BuildSkeleton(export.Name, export.GetPathName(), conversionOptions, dto);
                        break;
                    }
                    default:
                        throw new NotSupportedException($"Mesh geometry export of '{export.GetType()}' is not supported");
                }

                if (files.Count == 0)
                {
                    Utils.LogInfo($"No mesh data to export for {assetPath} (empty LODs) - skipping", _isLoggingEnabled);
                    return;
                }

                foreach (var file in files)
                {
                    var savedFilePath = Path.Combine(_outputPath, $"{assetPath}{file.NameSuffix}.{file.Extension}");
                    CreateNeededDirectories(savedFilePath);
                    File.WriteAllBytes(savedFilePath, file.Data);
                    Utils.LogInfo($"Exported mesh: {savedFilePath}", _isLoggingEnabled);
                }
            }
            catch (Exception ex)
            {
                Utils.LogInfo($"Failed to export mesh geometry for {assetPath}: {ex.Message}", _isLoggingEnabled);
            }
        }

        private void ExportToJson<T>(T obj, string assetPath)
        {
            try
            {
                var jsonDestinationPath = Path.Combine(_outputPath, $"{assetPath}.json");
                CreateNeededDirectories(jsonDestinationPath);

                using (FileStream fs = File.Open(jsonDestinationPath, FileMode.Create)) // FileMode.Create will overwrite if file exists
                using (StreamWriter sw = new StreamWriter(fs))
                using (JsonTextWriter jw = new JsonTextWriter(sw))
                {
                    jw.Formatting = Formatting.Indented; // For human-readable output with indentation
                    JsonSerializer serializer = new JsonSerializer();
                    serializer.Serialize(jw, obj);
                }
            }
            catch (Exception ex)
            {
                Utils.LogInfo($"Failed to export {assetPath} to JSON: {ex.Message}", _isLoggingEnabled);
            }
        }

        private static SKEncodedImageFormat GetSkiaFormat(ETextureFormat format)
        {
            return format switch
            {
                ETextureFormat.Png => SKEncodedImageFormat.Png,
                ETextureFormat.Jpeg => SKEncodedImageFormat.Jpeg,
                ETextureFormat.Bmp => SKEncodedImageFormat.Bmp,
                _ => SKEncodedImageFormat.Png // Default to PNG for unsupported formats
            };
        }

        private static SKColorType GetSKColorType(EPixelFormat format)
        {
            // Map EPixelFormat to SKColorType
            SKColorType colorType = format switch
            {
                EPixelFormat.PF_B8G8R8A8 => SKColorType.Bgra8888,
                EPixelFormat.PF_R8G8B8A8 => SKColorType.Rgba8888,
                EPixelFormat.PF_DXT1 => SKColorType.Rgba8888,
                EPixelFormat.PF_DXT5 => SKColorType.Rgba8888,
                EPixelFormat.PF_BC4 => SKColorType.Rgba8888,
                EPixelFormat.PF_BC5 => SKColorType.Rgba8888,
                EPixelFormat.PF_BC6H => SKColorType.Rgba8888,
                EPixelFormat.PF_BC7 => SKColorType.Rgba8888,
                EPixelFormat.PF_A8R8G8B8 => SKColorType.Bgra8888,
                EPixelFormat.PF_G8 => SKColorType.Gray8,
                EPixelFormat.PF_FloatRGBA => SKColorType.RgbaF16,
                _ => SKColorType.Rgba8888 // Default fallback
            };
            return colorType;
        }

        private string GetTextureFormatAsString()
        {
            var extension = _options.TextureFormat switch
            {
                ETextureFormat.Png => "png",
                ETextureFormat.Jpeg => "jpg",
                ETextureFormat.Tga => "tga",
                ETextureFormat.Bmp => "bmp",
                ETextureFormat.Dds => "dds",
                ETextureFormat.Hdr => "hdr",
                _ => "png"
            };
            return extension;
        }

        private static void CreateNeededDirectories(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
}