@echo off
set nowPath=%cd%
cd /
cd %nowPath%

::delete specify file(*.pdb,*.vshost.*,)
for /r %nowPath% %%i in (*.pdb,*.vshost.*) do (del %%i)

::delete specify folder(obj,bin,.vs)
for /r %nowPath% %%i in (obj,bin,.vs) do (IF EXIST %%i RD /s /q %%i)

:: === Kotlin / Android Gradle ===
set KOTLIN_DIR=%nowPath%\src\EflayGameSaveManager.Kotlin
IF EXIST "%KOTLIN_DIR%\.gradle" RD /s /q "%KOTLIN_DIR%\.gradle"
IF EXIST "%KOTLIN_DIR%\build" RD /s /q "%KOTLIN_DIR%\build"
IF EXIST "%KOTLIN_DIR%\local.properties" del "%KOTLIN_DIR%\local.properties"

:: === MAUI / .NET Android ===
set MAUI_DIR=%nowPath%\src\EflayGameSaveManager.Maui
:: Shizuku AARs (downloaded at build time)
IF EXIST "%MAUI_DIR%\libs\*.aar" del "%MAUI_DIR%\libs\*.aar"

:: Temp / misc
IF EXIST "%nowPath%\screenshot.png" del "%nowPath%\screenshot.png

echo OK
pause