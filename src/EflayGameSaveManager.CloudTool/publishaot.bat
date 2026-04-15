@echo off
dotnet publish .\EflayGameSaveManager.CloudTool.csproj -c Release -r win-x64 -f net10.0 --self-contained true /p:PublishAot=true /p:PublishTrimmed=true /p:InvariantGlobalization=true -o ..\EflayGameSaveManager.Lazarus\bin
