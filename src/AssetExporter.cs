using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Animation;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using Newtonsoft.Json;
using SkiaSharp;
using ETextureFormat = BatchExport.Enums.ETextureFormat;

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

                bool anyExported = false;
                
                // Check for SVG assets first
                var svgAssets = assetExports.Where(x => x.ExportType.Contains("SvgAsset")).ToList();
                if (svgAssets.Any())
                {
                    Utils.LogInfo($"Found SVG asset in {assetPath}", _isLoggingEnabled);
                    // SVG data is typically stored in the asset properties
                    foreach (var svgAsset in svgAssets)
                    {
                        try
                        {
                            var svgProperty = svgAsset.Properties?.FirstOrDefault(p => p.Name == "Data");
                            var svgData = svgProperty?.Tag?.ToString();
                            if (!string.IsNullOrEmpty(svgData))
                            {
                                var svgPath = Path.Combine(_outputPath, $"{assetPath}.svg");
                                CreateNeededDirectories(svgPath);
                                File.WriteAllText(svgPath, svgData);
                                anyExported = true;
                                Utils.LogInfo($"Exported SVG: {svgPath}", _isLoggingEnabled);
                            }
                        }
                        catch (Exception ex)
                        {
                            Utils.LogInfo($"Failed to export SVG from {assetPath}: {ex.Message}", _isLoggingEnabled);
                        }
                    }
                }

                // Check for regular textures
                var textures = assetExports.OfType<UTexture2D>().ToList();
                if (textures.Count > 0)
                {
                    if (textures.Count > 1)
                    {
                        Utils.LogInfo($"Warning: Multiple textures found in {assetPath}. Using first texture only.", _isLoggingEnabled);
                    }
                    try
                    {
                        ExportTexture(textures[0], assetPath);
                        anyExported = true;
                    }
                    catch (Exception ex)
                    {
                        Utils.LogInfo($"Failed to export texture from {assetPath}: {ex.Message}", _isLoggingEnabled);
                    }
                }

                // Handle other asset types
                foreach (var export in assetExports)
                {
                    if (export is UTexture2D) continue; // Skip textures as they were handled above
                    
                    try 
                    {
                        switch (export)
                        {                            
                            case UMaterialInterface material:
                                ExportMaterial(material, assetPath);
                                anyExported = true;
                                continue;
                            
                            case UAnimSequence anim:
                                ExportAnimation(anim, assetPath);
                                anyExported = true;
                                continue;
                            
                            case UStaticMesh staticMesh:
                                ExportStaticMesh(staticMesh, assetPath);
                                anyExported = true;
                                continue;
                            
                            case USkeletalMesh skeletalMesh:
                                ExportSkeletalMesh(skeletalMesh, assetPath);
                                anyExported = true;
                                continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        Utils.LogInfo($"Failed to export individual asset {export.Name} from {assetPath}: {ex.Message}", _isLoggingEnabled);
                    }
                }

                // If no individual exports succeeded or there are multiple assets, export everything as JSON
                if (!anyExported || assetExports.Count() > 1)
                {
                    ExportToJson(assetExports.ToList(), assetPath);
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
                string extension = GetTextureFormatAsString();

                var destinationFilePath = Path.Combine(_outputPath, $"{assetPath}.{extension}");
                CreateNeededDirectories(destinationFilePath);

                SKBitmap? bitmap = null;
                try
                {
                    var textureData = texture.Decode();
                    if (textureData == null)
                    {
                        Utils.LogInfo($"Failed to decode texture {assetPath} - skipping", _isLoggingEnabled);
                        return;
                    }

                    // Create empty SKBitmap from SKImage frame
                    var imageInfo = new SKImageInfo(textureData.Width, textureData.Height, GetSKColorType(textureData.PixelFormat));
                    bitmap = new SKBitmap(imageInfo);

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
                finally
                {
                    bitmap?.Dispose();
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
        }

        private void ExportSkeletalMesh(USkeletalMesh mesh, string assetPath)
        {
            // Export as a list containing the single mesh
            ExportToJson(new[] { mesh }, assetPath);
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