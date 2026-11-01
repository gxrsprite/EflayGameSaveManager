[CmdletBinding()]
param(
    [string]$AndroidSdkDirectory = 'C:\Android\Sdk',
    [string]$RarPath = 'C:\Program Files\WinRAR\Rar.exe',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$profileRoot = Join-Path $projectRoot 'distribution/cotl'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $projectRoot "artifacts/cotl-public-$stamp"
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'OutputDirectory must be new, so previous configs, logs or saves cannot enter this release.'
}
if (!(Test-Path -LiteralPath $RarPath)) { throw 'RAR executable not found.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$bundleName = "CultOfTheLamb-SaveManager-Windows-Android-$stamp"
$bundleRoot = Join-Path $OutputDirectory $bundleName
$windowsRoot = Join-Path $bundleRoot 'Windows-x64'
$androidRoot = Join-Path $bundleRoot 'Android-arm64'
$buildRoot = Join-Path $OutputDirectory 'build'
New-Item -ItemType Directory -Path $windowsRoot, $androidRoot, $buildRoot | Out-Null
$configPath = Join-Path $profileRoot 'GameSaveManager.config.json'
$windowsRuntime = Join-Path $profileRoot 'Windows.runtime.json'
$androidRuntime = Join-Path $profileRoot 'Android.runtime.json'

function Invoke-Dotnet([string[]]$Arguments, [string]$LogName) {
    & dotnet @Arguments *> (Join-Path $buildRoot $LogName)
    if ($LASTEXITCODE -ne 0) { throw "Build failed; see build/$LogName (not included in RAR)." }
}

Write-Host 'Publishing self-contained Windows x64...'
$desktopPublish = Join-Path $buildRoot 'windows'
Invoke-Dotnet @(
    'publish', (Join-Path $projectRoot 'src/EflayGameSaveManager.Avalonia/EflayGameSaveManager.Avalonia.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $desktopPublish,
    '-p:PublishAot=false', '-p:PublishTrimmed=false', '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:DebugSymbols=false',
    "-p:BundledConfigPath=$configPath", "-p:BundledRuntimePath=$windowsRuntime"
) 'windows-build.log'

Write-Host 'Publishing Android arm64 APK with public-only assets...'
$androidPublish = Join-Path $buildRoot 'android'
# Android's incremental manifest can retain the previous package/title even when
# the newly named APK uses different properties. Clear generated Android outputs.
Invoke-Dotnet @(
    'clean', (Join-Path $projectRoot 'src/EflayGameSaveManager.Maui/EflayGameSaveManager.Maui.csproj'),
    '-c', 'Release', '-f', 'net10.0-android'
) 'android-clean.log'
Invoke-Dotnet @(
    'publish', (Join-Path $projectRoot 'src/EflayGameSaveManager.Maui/EflayGameSaveManager.Maui.csproj'),
    '-c', 'Release', '-f', 'net10.0-android', '-o', $androidPublish,
    "-p:AndroidSdkDirectory=$AndroidSdkDirectory", '-p:AndroidPackageFormats=apk',
    "-p:ApplicationDisplayVersion=1.0.$(Get-Date -Format yyMM).$(Get-Date -Format dd)",
    "-p:ApplicationVersion=$(Get-Date -Format yyyyMMdd)",
    '-p:DebugType=None', '-p:DebugSymbols=false',
    "-p:BundledConfigPath=$configPath", "-p:BundledRuntimePath=$androidRuntime"
) 'android-build.log'

# Copy an explicit allowlist. No existing publish directory, app data or personal config is copied.
Copy-Item -LiteralPath (Join-Path $desktopPublish 'GameSaveManager.exe') -Destination $windowsRoot
Copy-Item -LiteralPath $configPath -Destination (Join-Path $windowsRoot 'GameSaveManager.config.json')
Copy-Item -LiteralPath $windowsRuntime -Destination (Join-Path $windowsRoot 'GameSaveManager.runtime.json')
$signedApks = @(Get-ChildItem -LiteralPath $androidPublish -Filter '*-Signed.apk')
if ($signedApks.Count -ne 1) { throw 'Expected exactly one signed APK.' }
$aapt2 = Get-ChildItem -Path (Join-Path $AndroidSdkDirectory 'build-tools/*/aapt2.exe') |
    Sort-Object FullName -Descending | Select-Object -First 1
if (!$aapt2) { throw 'aapt2 not found; cannot verify the APK package identity.' }
$badging = & $aapt2.FullName dump badging $signedApks[0].FullName
if ($LASTEXITCODE -ne 0 -or ($badging -join "`n") -notmatch "package: name='com\.eflay\.gamesavemanager\.maui'") {
    throw 'APK manifest must retain the normal com.eflay.gamesavemanager.maui package ID.'
}
Copy-Item -LiteralPath $signedApks[0].FullName -Destination (Join-Path $androidRoot 'CultOfTheLamb-SaveManager.apk')
Copy-Item -LiteralPath $configPath -Destination (Join-Path $androidRoot 'GameSaveManager.config.json')
Copy-Item -LiteralPath $androidRuntime -Destination (Join-Path $androidRoot 'GameSaveManager.runtime.json')
Copy-Item -LiteralPath (Join-Path $profileRoot 'README.txt'), (Join-Path $profileRoot 'CONFIG_GUIDE.md') -Destination $bundleRoot

$linkedAssemblies = Join-Path $projectRoot 'src/EflayGameSaveManager.Maui/obj/Release/net10.0-android/android-arm64/linked'
& (Join-Path $PSScriptRoot 'verify-cotl-public.ps1') -BundleRoot $bundleRoot -ManagedAssemblyDirectory $linkedAssemblies

$checksumLines = Get-ChildItem -LiteralPath $bundleRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($bundleRoot, $_.FullName).Replace('\', '/')
    "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
}
$checksumLines | Set-Content -LiteralPath (Join-Path $bundleRoot 'SHA256SUMS.txt') -Encoding utf8

$archivePath = Join-Path $OutputDirectory "$bundleName.rar"
Push-Location $OutputDirectory
try {
    & $RarPath a -r -m5 -ma5 -ep1 -idq $archivePath $bundleName
    if ($LASTEXITCODE -ne 0) { throw 'RAR creation failed.' }
    & $RarPath t -idq $archivePath
    if ($LASTEXITCODE -ne 0) { throw 'RAR integrity test failed.' }
}
finally { Pop-Location }
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  $([IO.Path]::GetFileName($archivePath))" | Set-Content -LiteralPath "$archivePath.sha256" -Encoding utf8
Write-Host "Release: $archivePath"
Write-Host "SHA256: $archiveHash"
