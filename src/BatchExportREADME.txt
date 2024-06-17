README for the export process from downloaded update to exports

0. Looking how to download assets from someone who already did the below process? Just Extract/Unzip BatchExport/Exports.zip to BatchExport/Exports (directory) and skip all of the following
1. Get a 1.0 (NOT 2.0) .usmap mapper file for the current game version from someone in #datamining channel or if its up to date in datamining\Mappings
2. Download [Unreal Engine](https://www.unrealengine.com/en-US/download) 5.2. When prompted for what to install, use:
    2.1 "Core Components" (required, 40gb)
    2.2 "Starter Content" (might not be necessary)
    2.3 "Templates and Feature Packs" (might not be necessary)
    2.4 "Engine Source" (likely necessary), 
    2.5 NOT "Editor symbols for debugging" (80gb)
3. Download datamining\Repacker\extract.bat, open with Notepad, and set the line <cd "C:\Program Files\IRONMACE\Dark and Darker\DungeonCrawler\Content\Paks"> to your own DaD download path
4. Run extract.bat as Administrator, enter your UE5.3 download path, by default it is C:\Program Files\Epic Games\UE_5.3, let it repackage for ~10 minutes
5. Configure settings for BatchExport\Program.cs
    5.1 Install dependencies
        5.1.1 .NET SDK https://dotnet.microsoft.com/en-us/download
        5.1.2 Run in terminal:dotnet add package CUE4Parse
    5.2 _gameDirectory to file path of the "Repack" file created by the extract.bat native repacker
    5.3 _mapping to name of .usmap file
    5.4 _aesKey which hasn't changed since the game was released
6. Delete pre-existing exports by deleting the folder BatchExport\Exports if it exists (is in .gitignore)
7. Run BatchExport\Program.cs
8. Compress/Zip BatchExport\Exports to BatchExport\Exports.zip and push so others can Extract/Unzip it back into BatchExport\Exports