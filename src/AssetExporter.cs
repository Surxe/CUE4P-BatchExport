using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.FileProvider;
using Newtonsoft.Json;
using SkiaSharp;
using BatchExport.Enums;

namespace BatchExport
{
    public class AssetExporter
    {
        private readonly ExporterOptions _options;
        private readonly string _outputPath;
        private readonly bool _isLoggingEnabled;

        public AssetExporter(ExporterOptions options, string outputPath, bool isLoggingEnabled)
        {
            _options = options;
            _outputPath = outputPath;
            _isLoggingEnabled = isLoggingEnabled;
        }

        public void ExportAsset(DefaultFileProvider provider, string assetPath)
        {
            try
            {
                var package = provider.LoadPackage(assetPath);
                var assetExports = package.GetExports();

                foreach (var export in assetExports)
                {
                    switch (export)
                    {
                        case UTexture2D texture:
                            ExportTexture(texture, assetPath);
                            break;
                        
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
                        
                        default:
                            // Fallback to JSON export for unsupported types
                            ExportToJson(export, assetPath);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.LogInfo($"Failed to export asset {assetPath}: {ex.Message}", _isLoggingEnabled);
            }
        }

        private void ExportTexture(UTexture2D texture, string assetPath)
        {
            try
            {
                var firstMip = texture.GetFirstMip();
                if (firstMip == null || firstMip.BulkData.Data == null || firstMip.BulkData.Data.Length == 0)
                {
                    Utils.LogInfo($"Texture {assetPath} has no valid mip data - skipping", _isLoggingEnabled);
                    return;
                }

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

                var destinationFilePath = Path.Combine(_outputPath, $"{assetPath}.{extension}");
                CreateNeededDirectories(destinationFilePath);

                using (var bitmap = new SKBitmap(new SKImageInfo(firstMip.SizeX, firstMip.SizeY, SKColorType.Rgba8888)))
                {
                    var bitmapPtr = bitmap.GetPixels();
                    System.Runtime.InteropServices.Marshal.Copy(firstMip.BulkData.Data, 0, bitmapPtr, firstMip.BulkData.Data.Length);

                    using var image = SKImage.FromBitmap(bitmap);
                    using var data = image.Encode(GetSkiaFormat(_options.TextureFormat), 100);
                    using var stream = File.OpenWrite(destinationFilePath);
                    data.SaveTo(stream);
                }
            }
            catch (Exception ex)
            {
                Utils.LogInfo($"Failed to export texture {assetPath}: {ex.Message}", _isLoggingEnabled);
                // Fall back to JSON export
                ExportToJson(texture, assetPath);
            }
        }

        private void ExportMaterial(UMaterialInterface material, string assetPath)
        {
            // For now, just export as JSON since we don't have material conversion code yet
            ExportToJson(material, assetPath);
        }

        private void ExportAnimation(UAnimSequence anim, string assetPath)
        {
            // For now, just export as JSON since we don't have animation conversion code yet
            ExportToJson(anim, assetPath);
        }

        private void ExportStaticMesh(UStaticMesh mesh, string assetPath)
        {
            // For now, just export as JSON since we don't have mesh conversion code yet
            ExportToJson(mesh, assetPath);
        }

        private void ExportSkeletalMesh(USkeletalMesh mesh, string assetPath)
        {
            // For now, just export as JSON since we don't have skeletal mesh conversion code yet
            ExportToJson(mesh, assetPath);
        }

        private void ExportToJson(UObject obj, string assetPath)
        {
            try
            {
                var jsonDestinationPath = Path.Combine(_outputPath, $"{assetPath}.json");
                CreateNeededDirectories(jsonDestinationPath);

                var serializedJson = JsonConvert.SerializeObject(obj, Formatting.Indented);
                File.WriteAllText(jsonDestinationPath, serializedJson);
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