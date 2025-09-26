# BatchExport
At its core, this is just a wrapper for the CUE4Parse repository that exports assets from an Unreal Engine game's pak folders.

## Repack using Unreal Engine
Some games, such as Dark and Darker, will require repackaging the game first to address other issues. Following is what this process looks like for Dark and Darker.
1. Download [Unreal Engine](https://www.unrealengine.com/en-US/download) 5.3. When prompted for what to install, use:
   1.  "Core Components" (required, 40gb)
   2.  "Starter Content" (might not be necessary)
   3.  "Templates and Feature Packs" (might not be necessary)
   4.  "Engine Source" (likely necessary)
   5.  NOT "Editor symbols for debugging" (80gb)
2. Download `repackaging\Crypto.json`, open with Notepad, ensure the AES key is set for your game.
3. Download `repackaging\extract.bat`, open with Notepad, and set the line `<cd "C:\Program Files\IRONMACE\Dark and Darker\DungeonCrawler\Content\Paks">` to your own game's pak path
4. Run `extract.bat` as Administrator (might not work if not as Admin), enter your UE5.3 download path, by default it is `C:\Program Files\Epic Games\UE_5.3` on Windows, let it repackage for ~10 minutes

## Decrypt and batch export with CUE4Parse
1. Get a 1.0 (NOT 2.0) .usmap mapper file for the current game version  
2. Install dependencies
   1. .NET SDK https://dotnet.microsoft.com/en-us/download
   2. Run in terminal: `dotnet add package CUE4Parse`
3. Create `src/appsettings.json` using `appsettings.template.json` as a template, with `SETTINGS.md` and `PRESETS.md` as guidelines 
4. `cd BatchExport/src`
5. `dotnet build`
6. `dotnet run BatchExport.csproj`