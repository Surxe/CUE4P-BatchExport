using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SkiaSharp;
using System.Collections.Concurrent;
using CUE4Parse_Conversion.Textures;
using CUE4Parse_Conversion.Textures.BC;

namespace BatchExport
{
    public static class BatchExportProgram
    {
        /// <summary>
        /// Loads settings from config file (if available) and command-line arguments
        /// </summary>
        /// <param name="args">Command-line arguments</param>
        /// <returns>Configured Settings instance</returns>
        private static Settings LoadSettings(string[] args)
        {
            Settings settings;
            
            // Try to load from config file first (optional)
            string configFilePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            
            if (File.Exists(configFilePath))
            {
                try
                {
                    settings = Settings.LoadFromFile(configFilePath);
                    Console.WriteLine($"Loaded configuration from: {configFilePath}");
                    if (settings.Preset != GamePreset.None)
                    {
                        Console.WriteLine($"Using preset: {settings.Preset}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to load config file {configFilePath}: {ex.Message}");
                    Console.WriteLine("Using default settings...");
                    settings = new Settings();
                }
            }
            else
            {
                Console.WriteLine("No appsettings.json found. Using command-line arguments and defaults.");
                settings = new Settings();
            }
            
            // Apply command-line argument overrides
            settings = ApplyCommandLineArguments(settings, args);
            
            return settings;
        }

        /// <summary>
        /// Applies command-line arguments to override settings values
        /// </summary>
        /// <param name="settings">Base settings to override</param>
        /// <param name="args">Command-line arguments</param>
        /// <returns>Settings with command-line overrides applied</returns>
        private static Settings ApplyCommandLineArguments(Settings settings, string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                string? value = i + 1 < args.Length ? args[i + 1] : null;

                switch (arg)
                {
                    case "--pak-files-directory":
                        if (value != null)
                        {
                            settings.PakFilesDirectory = value;
                            Console.WriteLine($"Override: PakFilesDirectory = {value}");
                            i++; // Skip the value argument
                        }
                        break;
                        
                    case "--export-output-path":
                        if (value != null)
                        {
                            settings.ExportOutputPath = value;
                            Console.WriteLine($"Override: ExportOutputPath = {value}");
                            i++; // Skip the value argument
                        }
                        break;
                        
                    case "--mapping-file-path":
                        if (value != null)
                        {
                            settings.MappingFilePath = value;
                            Console.WriteLine($"Override: MappingFilePath = {value}");
                            i++; // Skip the value argument
                        }
                        break;
                        
                    case "--aes-key-hex":
                        if (value != null)
                        {
                            settings.AesKeyHex = value.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : value;
                            Console.WriteLine($"Override: AesKeyHex = {(settings.AesKeyHex != null ? "[Set]" : "null")}");
                            i++; // Skip the value argument
                        }
                        break;
                        
                    case "--preset":
                        if (value != null && Enum.TryParse<GamePreset>(value, true, out var preset))
                        {
                            settings.Preset = preset;
                            Console.WriteLine($"Override: Preset = {preset}");
                            // Apply preset after parsing all arguments
                            i++; // Skip the value argument
                        }
                        break;
                        
                    case "--unreal-engine-version":
                        if (value != null)
                        {
                            settings.UnrealEngineVersion = value;
                            Console.WriteLine($"Override: UnrealEngineVersion = {value}");
                            i++; // Skip the value argument
                        }
                        break;
                        
                    case "--texture-platform":
                    case "--textureplatform":
                        if (value != null)
                        {
                            settings.TexturePlatform = value;
                            Console.WriteLine($"Override: TexturePlatform = {value}");
                            i++; // Skip the value argument
                        }
                        break;

                    case "--texture-format":
                    case "--textureformat":
                        if (value != null)
                        {
                            settings.TextureFormat = value;
                            Console.WriteLine($"Override: TextureFormat = {value}");
                            i++; // Skip the value argument
                        }
                        break;
                        
                    case "--needed-exports-file-path":
                        if (value != null)
                        {
                            settings.NeededExportsFilePath = (value.Equals("null", StringComparison.OrdinalIgnoreCase) || value.Equals("All", StringComparison.OrdinalIgnoreCase)) ? null : value;
                            Console.WriteLine($"Override: NeededExportsFilePath = {settings.NeededExportsFilePath ?? "null (export all)"}");
                            i++; // Skip the value argument
                        }
                        break;
                        
                    case "--is-logging-enabled":
                        if (value != null && bool.TryParse(value, out var logging))
                        {
                            settings.IsLoggingEnabled = logging;
                            Console.WriteLine($"Override: IsLoggingEnabled = {logging}");
                            i++; // Skip the value argument
                        }
                        break;
                        
                    case "--should-wipe-output-directory":
                        if (value != null && bool.TryParse(value, out var wipe))
                        {
                            settings.ShouldWipeOutputDirectory = wipe;
                            Console.WriteLine($"Override: ShouldWipeOutputDirectory = {wipe}");
                            i++; // Skip the value argument
                        }
                        break;
                        
                    case "--should-export-textures":
                        if (value != null && bool.TryParse(value, out var exportTextures))
                        {
                            settings.ShouldExportTextures = exportTextures;
                            Console.WriteLine($"Override: ShouldExportTextures = {exportTextures}");
                            i++; // Skip the value argument
                        }
                        break;
                        
                    case "--help":
                    case "-h":
                        ShowHelp();
                        Environment.Exit(0);
                        break;
                        
                    default:
                        if (arg.StartsWith("--"))
                        {
                            Console.WriteLine($"Warning: Unknown argument '{arg}' ignored.");
                        }
                        break;
                }
            }
            
            // Apply preset after all arguments have been parsed (in case preset was specified via command line)
            if (settings.Preset != GamePreset.None)
            {
                settings.ApplyPreset();
            }
            
            return settings;
        }

        /// <summary>
        /// Displays help information for command-line usage
        /// </summary>
        private static void ShowHelp()
        {
            Console.WriteLine("CUE4Parse BatchExport - Unreal Engine Asset Exporter");
            Console.WriteLine();
            Console.WriteLine("Usage: BatchExport.exe [OPTIONS]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --pak-files-directory <path>        Path to directory containing .pak files");
            Console.WriteLine("  --export-output-path <path>         Path where exported files will be saved");
            Console.WriteLine("  --mapping-file-path <path>          Path to .usmap mappings file");
            Console.WriteLine("  --aes-key-hex <hex>                 AES key for encrypted pak files (or 'null')");
            Console.WriteLine("  --preset <name>                     Game preset: None, DarkAndDarker, WarRobotsFrontiers");
            Console.WriteLine("  --unreal-engine-version <version>   Unreal Engine version (e.g., GAME_UE5_4)");
            Console.WriteLine("  --texture-platform <n>           Texture platform (e.g., DesktopMobile)");
            Console.WriteLine("  --texture-format <n>              Texture format for exports (PNG, JPG, TGA, BMP, DDS, HDR)");
            Console.WriteLine("  --needed-exports-file-path <path>   Path to NeededExports.json file (or 'null'/'All' to export everything)");
            Console.WriteLine("  --is-logging-enabled <true|false>   Enable detailed logging");
            Console.WriteLine("  --should-wipe-output-directory <true|false> Clear output directory before exporting");
            Console.WriteLine("  --should-export-textures <true|false> Export texture files");
            Console.WriteLine("  --help, -h                          Show this help message");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  BatchExport.exe --preset WarRobotsFrontiers \\");
            Console.WriteLine("    --pak-files-directory \"C:\\Game\\Paks\" \\");
            Console.WriteLine("    --export-output-path \"C:\\Export\" \\");
            Console.WriteLine("    --mapping-file-path \"C:\\mappings.usmap\"");
            Console.WriteLine();
            Console.WriteLine("  BatchExport.exe --pak-files-directory \"C:\\Game\\Paks\" \\");
            Console.WriteLine("    --export-output-path \"C:\\Export\" \\");
            Console.WriteLine("    --mapping-file-path \"C:\\mappings.usmap\" \\");
            Console.WriteLine("    --unreal-engine-version GAME_UE5_4 \\");
            Console.WriteLine("    --aes-key-hex null");
            Console.WriteLine();
            Console.WriteLine("Note: Command-line arguments override values in appsettings.json");
            Console.WriteLine("      appsettings.json is optional if all required parameters are provided");
        }

        //Create all preceding directories of a given file if they don't yet exist
        private static void CreateNeededDirectories(string destinationFilePath, string baseExportPath)
        {
            string[] directoryParts = destinationFilePath.Split('/'); //List of directories to create
            Array.Resize(ref directoryParts, directoryParts.Length - 1); //Remove last element which is the actual object name
            string currentDirectoryPath = baseExportPath;

            foreach (string directoryName in directoryParts)
            {
                currentDirectoryPath = Path.Combine(currentDirectoryPath, directoryName);
                if (!Directory.Exists(currentDirectoryPath))
                {
                    Directory.CreateDirectory(currentDirectoryPath);
                }
            }
        }

        //Extract an asset and write it to a JSON file or image file
        private static void ExtractAsset(DefaultFileProvider gameFileProvider, string assetPath, Settings settings)
        {
            try
            {
                // Check if this is a .locres file (localization resource)
                if (assetPath.EndsWith(".locres", StringComparison.OrdinalIgnoreCase))
                {
                    ExtractLocresFile(gameFileProvider, assetPath, settings);
                    return;
                }

                // Handle regular UE packages (.uasset, .umap)
                var exporterOptions = new ExporterOptions
                {
                    TextureFormat = settings.GetTextureFormat(),
                    Platform = settings.GetTexturePlatform(),
                    ExportHdrTexturesAsHdr = true,
                    ExportMaterials = true,
                    ExportMorphTargets = true
                };

                var exporter = new AssetExporter(exporterOptions, settings.ExportOutputPath, settings.IsLoggingEnabled, settings.ShouldExportTextures);
                exporter.ExportAsset(gameFileProvider, assetPath);
            }
            catch (Exception ex)
            {
                Utils.LogInfo($"Failed to extract asset {assetPath}: {ex.Message}", settings.IsLoggingEnabled);
                if (settings.IsLoggingEnabled)
                {
                    Utils.LogInfo($"Stack trace: {ex.StackTrace}", settings.IsLoggingEnabled);
                    if (ex.InnerException != null)
                    {
                        Utils.LogInfo($"Inner exception: {ex.InnerException.Message}", settings.IsLoggingEnabled);
                        Utils.LogInfo($"Inner stack trace: {ex.InnerException.StackTrace}", settings.IsLoggingEnabled);
                    }
                }
            }
        }

        private static void ExtractLocresFile(DefaultFileProvider gameFileProvider, string assetPath, Settings settings)
        {
            try
            {
                // Get the game file for the locres
                var gameFile = gameFileProvider.Files[assetPath];
                if (gameFile != null)
                {
                    using var archive = gameFile.CreateReader();
                    
                    // Parse the locres file using CUE4Parse
                    var locres = new FTextLocalizationResource(archive);
                    
                    // Serialize the locres object directly to JSON
                    string serializedJson = JsonConvert.SerializeObject(locres, Formatting.Indented);
                    
                    // Destination path within exports directory (change extension to .json)
                    string destinationFilePath = settings.ExportOutputPath + "/" + assetPath.Replace(".locres", ".json");

                    // Create the directories if they don't exist
                    CreateNeededDirectories(destinationFilePath, settings.ExportOutputPath);

                    // Write the JSON to file
                    File.WriteAllText(destinationFilePath, serializedJson);
                }
                else
                {
                    Console.WriteLine($"Could not find locres file: {assetPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred when parsing locres file {assetPath}: {ex.Message}");
            }
        }

        private static bool ShouldProcessFile(string assetFilePath, List<string> targetExportDirectories, Settings settings)
        {
            // Check file extension
            if (!settings.SupportedAssetFileExtensions.Any(ext => assetFilePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Skip engine files
            if (assetFilePath.StartsWith("engine", StringComparison.OrdinalIgnoreCase))
                return false;

            // Check if file is in any export directory (empty string means export all)
            return targetExportDirectories.Any(dir => string.IsNullOrEmpty(dir) || assetFilePath.StartsWith(dir, StringComparison.OrdinalIgnoreCase));
        }

        private static DefaultFileProvider CreateIndependentProvider(Settings settings)
        {
            var provider = new DefaultFileProvider(
                settings.PakFilesDirectory,
                SearchOption.AllDirectories,
                new VersionContainer(settings.GetUnrealEngineVersion(), settings.GetTexturePlatform()),
                StringComparer.Ordinal
            );

            if (!string.IsNullOrEmpty(settings.AesKeyHex))
                provider.SubmitKey(new FGuid(), new FAesKey(settings.AesKeyHex));

            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(settings.MappingFilePath);

            provider.Initialize();
            provider.Mount();
            provider.PostMount();
            provider.ChangeCulture("en");

            return provider;
        }

        public static void Main(string[] args)
        {
            // Load settings from config file and command line arguments
            var settings = LoadSettings(args);
            
            // Validate settings
            try
            {
                settings.Validate();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Configuration error: {ex.Message}");
                return;
            }

            // Create exports directory
            // All relative paths should be relative to the src directory
            string applicationRootPath = Utils.GetSrcDirectory();
            Utils.LogInfo("Output Directory: " + settings.ExportOutputPath, settings.IsLoggingEnabled);

            // Handle output directory cleanup if requested
            if (settings.ShouldWipeOutputDirectory && Directory.Exists(settings.ExportOutputPath))
            {
                try
                {
                    Utils.LogInfo("Wiping existing output directory contents...", settings.IsLoggingEnabled);
                    Directory.Delete(settings.ExportOutputPath, true);
                    Utils.LogInfo("Output directory successfully cleared.", settings.IsLoggingEnabled);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to delete output directory: {ex.Message}");
                }
            }

            // Ensure output directory exists
            if (!Directory.Exists(settings.ExportOutputPath))
            {
                Directory.CreateDirectory(settings.ExportOutputPath);
                Utils.LogInfo("Created output directory.", settings.IsLoggingEnabled);
            }

            // Decrypt .pak to assets
            if (settings.IsLoggingEnabled)
            {
                Log.Logger = new LoggerConfiguration().WriteTo.Console(theme: AnsiConsoleTheme.Literate).CreateLogger();
            }

            Utils.LogInfo("Initializing Oodle...", settings.IsLoggingEnabled);
            OodleHelper.DownloadOodleDll();
            OodleHelper.Initialize(OodleHelper.OODLE_DLL_NAME);

            Utils.LogInfo("Initializing Detex...", settings.IsLoggingEnabled);
            DetexHelper.LoadDll();
            DetexHelper.Initialize(DetexHelper.DLL_NAME);

            Utils.LogInfo("Creating version container and file provider...", settings.IsLoggingEnabled);
            Utils.LogInfo($"Game directory: {settings.PakFilesDirectory}", settings.IsLoggingEnabled);
            Utils.LogInfo($"Mappings file: {settings.MappingFilePath}", settings.IsLoggingEnabled);
            
            Utils.LogInfo("Creating provider...", settings.IsLoggingEnabled);
            Utils.LogInfo($"Using UE version: {settings.UnrealEngineVersion}, Texture platform: {settings.TexturePlatform}", settings.IsLoggingEnabled);
            var fileProvider = new DefaultFileProvider(settings.PakFilesDirectory, SearchOption.AllDirectories, new VersionContainer(settings.GetUnrealEngineVersion(), settings.GetTexturePlatform()), StringComparer.Ordinal);
            
            // Submit AES key if provided
            if (!string.IsNullOrEmpty(settings.AesKeyHex))
            {
                Utils.LogInfo("Submitting AES encryption key...", settings.IsLoggingEnabled);
                fileProvider.SubmitKey(new FGuid(), new FAesKey(settings.AesKeyHex));
            }
            else
            {
                Utils.LogInfo("No AES key provided - assuming unencrypted pak files", settings.IsLoggingEnabled);
            }
            
            Utils.LogInfo("Setting mappings...", settings.IsLoggingEnabled);
            fileProvider.MappingsContainer = new FileUsmapTypeMappingsProvider(settings.MappingFilePath);
            
            Utils.LogInfo("Initializing provider...", settings.IsLoggingEnabled);
            fileProvider.Initialize();
            Utils.LogInfo($"Files found after Initialize(): {fileProvider.Files.Count}", settings.IsLoggingEnabled);
            
            Utils.LogInfo("Mounting provider...", settings.IsLoggingEnabled);
            fileProvider.Mount();
            Utils.LogInfo($"Files found after Mount(): {fileProvider.Files.Count}", settings.IsLoggingEnabled);
            
            Utils.LogInfo("Post-mounting provider...", settings.IsLoggingEnabled);
            fileProvider.PostMount();
            Utils.LogInfo($"Files found after PostMount(): {fileProvider.Files.Count}", settings.IsLoggingEnabled);

            fileProvider.ChangeCulture("en");

            Utils.LogInfo($"Total files found by provider: {fileProvider.Files.Count}", settings.IsLoggingEnabled);

            // Determine which directories to export
            List<string> exportDirectoriesToProcess;
            
            if (settings.NeededExportsFilePath == null || settings.NeededExportsFilePath.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                // Export all assets when no specific export file is specified (null or 'All')
                Utils.LogInfo("No NeededExports file specified - exporting all assets", settings.IsLoggingEnabled);
                exportDirectoriesToProcess = new List<string> { "" }; // Empty string matches all paths
            }
            else
            {
                // Retrieve the list of directories to export from the specified file
                string neededExportsFilePath = settings.GetNeededExportsFilePath(applicationRootPath);

                // Check if the file exists
                if (!File.Exists(neededExportsFilePath))
                {
                    Console.WriteLine(neededExportsFilePath + " file not found.");
                    return;
                }

                string neededExportsJsonContent = File.ReadAllText(neededExportsFilePath);
                exportDirectoriesToProcess = Utils.GetNarrowestDirectories(neededExportsJsonContent);
                Utils.LogInfo("Narrowed Directories that will be exported:", settings.IsLoggingEnabled);
                foreach (var exportDirectory in exportDirectoriesToProcess)
                {
                    Utils.LogInfo("\t"+exportDirectory, settings.IsLoggingEnabled);
                }
            }

            // Export to .json
            Console.WriteLine("Starting file export...");
            Utils.LogInfo("Please wait while the script exports files...", settings.IsLoggingEnabled);
            var allFiles = fileProvider.Files.Select(kvp => kvp.Value.ToString()).ToList();
            int totalFilesProcessed = 0;
            int totalFilesExported = 0;

            int workerCount = Environment.ProcessorCount;

            // Shared concurrent work queue
            var workQueue = new ConcurrentQueue<string>(allFiles);

            // Run N workers pulling from the queue until empty
            Parallel.ForEach(Enumerable.Range(0, workerCount), new ParallelOptions { MaxDegreeOfParallelism = workerCount }, _ =>
                {
                    var localProvider = CreateIndependentProvider(settings);

                    while (workQueue.TryDequeue(out var currentFilePath))
                    {
                        Interlocked.Increment(ref totalFilesProcessed);

                        if (!ShouldProcessFile(currentFilePath, exportDirectoriesToProcess, settings))
                            continue;

                        string assetPathForExport = currentFilePath.EndsWith(".locres", StringComparison.OrdinalIgnoreCase)
                            ? currentFilePath
                            : currentFilePath.Replace(".uasset", "").Replace(".umap", "");

                        Utils.LogInfo("Exporting asset: " + assetPathForExport, settings.IsLoggingEnabled);

                        ExtractAsset(localProvider, assetPathForExport, settings);

                        Interlocked.Increment(ref totalFilesExported);
                    }
                });

            Utils.LogInfo($"Processing complete. Files processed: {totalFilesProcessed}, Files exported: {totalFilesExported}", settings.IsLoggingEnabled);
        }
    }
}
