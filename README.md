# BatchExport
 Quickly decrypts and exports using CUE4PARSE

# Process

README for the complete export process from downloaded update to exports

Looking how to download assets from someone who already did the below process? Just Extract/Unzip src/Exports.zip and skip all of the following

## Repack using Unreal Engine
1. Download [Unreal Engine](https://www.unrealengine.com/en-US/download) 5.3. When prompted for what to install, use:

1.1 "Core Components" (required, 40gb)

1.2 "Starter Content" (might not be necessary)

1.3 "Templates and Feature Packs" (might not be necessary)

1.4 "Engine Source" (likely necessary)

1.5 NOT "Editor symbols for debugging" (80gb)
1. Download src\Crypto.json, open with Notepad, ensure the AES key is set for your game.

2. Download src\extract.bat, open with Notepad, and set the line <cd "C:\Program Files\IRONMACE\Dark and Darker\DungeonCrawler\Content\Paks"> to your own DaD download path

3. Run extract.bat as Administrator (might not work if not as Admin), enter your UE5.3 download path, by default it is C:\Program Files\Epic Games\UE_5.3, let it repackage for ~10 minutes

## Decrypt and export with CUE4Parse
4. Get a 1.0 (NOT 2.0) .usmap mapper file for the current game version
   
6. Configure settings for src\Program.cs
   
5.1 Install dependencies
   
5.1.1 .NET SDK https://dotnet.microsoft.com/en-us/download
   
5.1.2 Run in terminal: "dotnet add package CUE4Parse"
   
5.2 _gameDirectory to directory of the "Repacked" files created by the extract.bat native repacker
   
5.3 _outputPath to directory to save exports to
   
5.3 _mapping to name of .usmap file
   
5.4 _aesKey which hasn't changed since the game was released
  
7. Change the directories you need exported in src\NeededExports.json. Paths can appear more than once. Paths are grouped simply for readability, naming the groups something meaningful is not necessary. If exporting all, simply add the root path and all files within will be exported
   
8. Delete pre-existing exports by deleting the folder BatchExport\Exports if it exists (is in .gitignore)
   
9. cd BatchExport/src
   
10. 'dotnet run' on the .csproj
    
11. Compress/Zip src\Exports to src\Exports.zip and push so others on the team can Extract/Unzip

.usmap and .uasset are currently the only files this will export
