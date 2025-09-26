@echo off

set /p UserInputPath=Path of your Unreal Engine installation(C:\Program Files\Epic Games\UE_5.3):

set scriptpath=%~dp0

cd "C:\Program Files\IRONMACE\Dark and Darker\DungeonCrawler\Content\Paks"

for /r %%i in (*) do "%UserInputPath%\Engine\Binaries\Win64\UnrealPak.exe" -cryptokeys="%scriptpath%Crypto.json" "%%i" -Extract "%scriptpath%PakExtract" -extracttomountpoint 

"%UserInputPath%\Engine\Binaries\Win64\UnrealPak.exe" -cryptokeys="%scriptpath%Crypto.json" "C:\Datamining\DaD Repack\Repack\pakchunk.pak" -Create="%scriptpath%PakExtract" -compress -compressionformat=Oodle

cd %scriptpath%

echo f|xcopy /s /y "%scriptpath%PakExtract\DungeonCrawler\Config\DefaultGame.ini" "C:\Users\ruvim\Repository\DaD-Scripts\Exports\DungeonCrawler\Config\DefaultGame.ini"

rmdir /s /q "%scriptpath%\PakExtract"