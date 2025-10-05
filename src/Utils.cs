using System.Collections.Generic;
using Newtonsoft.Json;

namespace BatchExport
{
    public static class Utils
    {
        // Logging function that respects _enableLogging setting
        public static void LogInfo(string message, bool enableLogging)
        {
            if (enableLogging)
            {
                Console.WriteLine(message);
            }
        }

        // Narrows a list of directories to the narrowest directories that need to be created to hold all exports
        public static List<string> GetNarrowestDirectories(string json)
        {
            var directories = JsonConvert.DeserializeObject<List<string>>(json);

            List<string> narrowedDirectories = new List<string>();

            if (directories != null)
            {
                foreach (var directory in directories)
                {
                    AddNarrowest(directory, narrowedDirectories);
                }
            }

            return narrowedDirectories;
        }

        private static void AddNarrowest(string directory, List<string> narrowedDirectories)
        {
            bool addDirectory = true;

            foreach (var narrowedDir in narrowedDirectories)
            {
                if (directory.StartsWith(narrowedDir))
                {
                    addDirectory = false;
                    break;
                }
                else if (narrowedDir.StartsWith(directory))
                {
                    narrowedDirectories.Remove(narrowedDir);
                    break;
                }
            }

            if (addDirectory)
            {
                narrowedDirectories.Add(directory);
            }
        }

        /// <summary>
        /// Gets the src directory path, handling both development and published scenarios
        /// </summary>
        /// <returns>Path to the src directory</returns>
        public static string GetSrcDirectory()
        {
            string baseDir = AppContext.BaseDirectory;
            
            // Check if we're in a published single-file scenario (presets folder exists in base directory)
            if (Directory.Exists(Path.Combine(baseDir, "presets")))
            {
                return baseDir;
            }
            
            // Check if we're in development (bin/Debug/net8.0 structure)
            string srcDir = Path.GetFullPath(Path.Combine(baseDir, "..\\..\\..\\"));
            if (Directory.Exists(Path.Combine(srcDir, "presets")))
            {
                return srcDir;
            }
            
            // Fallback: assume we're in src or a subdirectory of it
            string currentDir = baseDir;
            while (!string.IsNullOrEmpty(currentDir) && Path.GetFileName(currentDir) != "src")
            {
                string? parentDir = Path.GetDirectoryName(currentDir);
                if (parentDir == null || parentDir == currentDir) break; // Reached root
                currentDir = parentDir;
            }
            
            // If we found a src directory, use it, otherwise use base directory
            return Directory.Exists(Path.Combine(currentDir ?? baseDir, "presets")) 
                ? (currentDir ?? baseDir) 
                : baseDir;
        }
    }
}