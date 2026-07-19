@echo off
setlocal
set "REPO_ROOT=%~dp0.."
set "PROJECT=%REPO_ROOT%\src\EflayGameSaveManager.CloudTool\EflayGameSaveManager.CloudTool.csproj"
set "OUTPUT_DIR=%REPO_ROOT%\src\EflayGameSaveManager.Lazarus\bin"

dotnet publish "%PROJECT%" -c Release -r win-x64 -f net10.0 --self-contained true -p:PublishAot=true -p:PublishTrimmed=true -p:InvariantGlobalization=true -o "%OUTPUT_DIR%"
endlocal
