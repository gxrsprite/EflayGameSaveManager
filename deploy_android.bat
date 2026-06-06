@echo off
setlocal
set JAVA_HOME=F:\Program Files\Android\Android Studio\jbr
set ANDROID_HOME=%USERPROFILE%\AppData\Local\Android\Sdk
set ADB=%ANDROID_HOME%\platform-tools\adb.exe

echo === Deploy GameSaveManager Android ===
echo.

:: Build Kotlin
echo [1/3] Building Kotlin APK...
cd /d "%~dp0src\EflayGameSaveManager.Kotlin"
call gradlew.bat assembleDebug >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo       BUILD FAILED
    cd /d "%~dp0"
    pause
    exit /b 1
)
echo       Build OK.
cd /d "%~dp0"

:: Install Kotlin
echo [2/3] Installing Kotlin APK...
%ADB% install -r "src\EflayGameSaveManager.Kotlin\app\build\outputs\apk\debug\app-debug.apk" >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo       INSTALL FAILED (check ADB connection)
    pause
    exit /b 1
)
echo       Installed.

:: Build and install MAUI
echo [3/3] Building MAUI...
dotnet build src\EflayGameSaveManager.Maui\EflayGameSaveManager.Maui.csproj -c Debug -f net10.0-android -p:JavaSdkDirectory="%JAVA_HOME%" >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo       MAUI build failed, skipping install.
) else (
    echo       Installing MAUI...
    for /f "delims=" %%f in ('dir /s /b "src\EflayGameSaveManager.Maui\bin\Debug\net10.0-android\*-Signed.apk" 2^>nul') do %ADB% install -r "%%f" >nul 2>&1
    echo       MAUI installed.
)

echo.
echo === Done ===
%ADB% shell am force-stop com.eflay.gamesavemanager
%ADB% shell am force-stop com.eflay.gamesavemanager.maui
echo Both apps force-stopped. Launch from launcher or run:
echo   adb shell am start -n com.eflay.gamesavemanager/.MainActivity
echo   adb shell am start -n com.eflay.gamesavemanager.maui/crc64a09852a5d5e3db09.MainActivity
pause
