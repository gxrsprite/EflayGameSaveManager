@echo off
call "H:\Program Files\Microsoft Visual Studio\18\Professional\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
cd /d "%~dp0"
dotnet publish ./EflayGameSaveManager.Avalonia.csproj -c Release -r win-x64 -f net10.0 --self-contained true -p:PublishAot=true -p:PublishTrimmed=true
if %ERRORLEVEL% EQU 0 (
    echo.
    echo === AOT publish SUCCESS ===
    dir /b "bin\Release\net10.0\win-x64\publish\GameSaveManager.exe"
) else (
    echo === FAILED ===
)
