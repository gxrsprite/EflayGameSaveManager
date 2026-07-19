@echo off
setlocal
set "REPO_ROOT=%~dp0.."
set "PROJECT=%REPO_ROOT%\src\EflayGameSaveManager.Avalonia\EflayGameSaveManager.Avalonia.csproj"
set "PUBLISH_DIR=%REPO_ROOT%\src\EflayGameSaveManager.Avalonia\bin\x64\Release\net10.0\win-x64\publish"

call "H:\Program Files\Microsoft Visual Studio\18\Professional\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
dotnet publish "%PROJECT%" -c Release -r win-x64 -f net10.0 --self-contained true -p:PublishAot=true -p:PublishTrimmed=true -p:DebugType=None -p:DebugSymbols=false
if errorlevel 1 (
    echo === AOT publish FAILED ===
    exit /b 1
)

del /q "%PUBLISH_DIR%\*.pdb" "%PUBLISH_DIR%\*.dbg" "%PUBLISH_DIR%\*.ipdb" "%PUBLISH_DIR%\*.iobj" "%PUBLISH_DIR%\*.ilk" "%PUBLISH_DIR%\*.map" >nul 2>&1
for /d %%D in ("%PUBLISH_DIR%\*.dSYM") do rd /s /q "%%~fD"

echo.
echo === AOT publish SUCCESS: debug symbols removed ===
dir /b "%PUBLISH_DIR%\GameSaveManager.exe"
endlocal
