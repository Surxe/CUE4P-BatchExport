using System.Collections.Generic;
using Newtonsoft.Json;
// Narrows a list of directories to the narrowest directories that need to be created to hold all exports
public class DirectoriesCoverage
{
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
}
