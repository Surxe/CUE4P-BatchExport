@echo off

set /p UserInputPath=Path of your Unreal Engine installation(C:\Program Files\Epic Games\UE_5.3):

set scriptpath=%~dp0

cd "C:\Program Files\IRONMACE\Dark and Darker\DungeonCrawler\Content\Paks"

for /r %%i in (*) do "%UserInputPath%\Engine\Binaries\Win64\UnrealPak.exe" -cryptokeys="%scriptpath%Crypto.json" "%%i" -Extract "%scriptpath%PakExtract" -extracttomountpoint 

if not exist "%scriptpath%\Repack\" mkdir "%scriptpath%\Repack\"

"%UserInputPath%\Engine\Binaries\Win64\UnrealPak.exe" -cryptokeys="%scriptpath%Crypto.json" "%scriptpath%Repack\pakchunk.pak" -Create="%scriptpath%PakExtract" -compress -compressionformat=Oodle

cd %scriptpath%

rmdir /s /q "%scriptpath%\PakExtract"

