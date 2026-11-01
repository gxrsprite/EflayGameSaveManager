[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BundleRoot,
    [Parameter(Mandatory)][string]$ManagedAssemblyDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$projectRoot = Split-Path $PSScriptRoot -Parent
$profileRoot = Join-Path $projectRoot 'distribution/cotl'
$publicConfig = Get-Content -LiteralPath (Join-Path $profileRoot 'GameSaveManager.config.json') -Raw | ConvertFrom-Json

function Assert-PublicConfig([string]$Json, [string]$Label) {
    $config = $Json | ConvertFrom-Json
    if ($config.games.Count -ne 1 -or $config.games[0].name -cne 'Cult of the Lamb') { throw "$Label has unexpected games." }
    if ($config.games[0].cloud_sync_enabled -ne $false) { throw "$Label must ship with cloud sync disabled." }
    $devices = @($config.devices.PSObject.Properties.Name | Sort-Object)
    if (($devices -join ',') -cne 'android,pc') { throw "$Label has unexpected devices." }
    foreach ($key in $devices) {
        if ($config.devices.$key.id -cne $key) { throw "$Label device ID mismatch." }
    }
    if ($config.devices.pc.name -cne 'PC' -or $config.devices.android.name -cne 'Android') { throw "$Label device names are not public defaults." }
    if ($config.devices.pc.platform -cne 'windows' -or $config.devices.android.platform -cne 'android') { throw "$Label platform mismatch." }
    $backend = $config.settings.cloud_settings.backend
    foreach ($key in 'endpoint', 'bucket', 'access_key_id', 'secret_access_key') {
        if (![string]::IsNullOrEmpty($backend.$key)) { throw "$Label contains cloud connection details ($key)." }
    }
    # Exact semantic match prevents extra fields, hidden devices or stale private paths.
    if (($config | ConvertTo-Json -Depth 30 -Compress) -cne ($publicConfig | ConvertTo-Json -Depth 30 -Compress)) {
        throw "$Label differs from the public allowlisted template."
    }
}

foreach ($platform in 'Windows-x64', 'Android-arm64') {
    Assert-PublicConfig (Get-Content -LiteralPath (Join-Path $BundleRoot "$platform/GameSaveManager.config.json") -Raw) $platform
    $runtime = Get-Content -LiteralPath (Join-Path $BundleRoot "$platform/GameSaveManager.runtime.json") -Raw | ConvertFrom-Json
    $expected = if ($platform -eq 'Windows-x64') { 'PC' } else { 'Android' }
    if ($runtime.forced_device_name -cne $expected) { throw "$platform runtime points to the wrong device." }
}

# Load sensitive comparison values in memory only; never write values or private configs to the release/report.
$privateValues = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($privatePath in [Environment]::GetFolderPath('UserProfile'), $projectRoot) {
    if ($privatePath.Length -ge 6) {
        [void]$privateValues.Add($privatePath)
        [void]$privateValues.Add($privatePath.Replace('\', '/'))
    }
}
foreach ($relative in @(
    'GameSaveManager.config.json', 'src/EflayGameSaveManager.Maui/GameSaveManager.config.json',
    'src/EflayGameSaveManager.Kotlin/app/src/main/assets/GameSaveManager.config.json',
    'temp/GameSaveManager.config.maui.json', 'temp/GameSaveManager.config.kotlin.json'
)) {
    $path = Join-Path $projectRoot $relative
    if (!(Test-Path -LiteralPath $path)) { continue }
    $raw = Get-Content -LiteralPath $path -Raw
    # Temporary developer copies may be truncated JSON. Still extract their known
    # sensitive string fields and GUIDs without trusting the document as a whole.
    foreach ($field in [regex]::Matches($raw, '"(?<key>endpoint|access_key_id|secret_access_key)"\s*:\s*(?<json>"(?:\\.|[^"\\])*")')) {
        $key = $field.Groups['key'].Value
        $value = [string]($field.Groups['json'].Value | ConvertFrom-Json)
        if ($value.Length -ge 6) { [void]$privateValues.Add($value) }
        if ($key -eq 'endpoint' -and $value) {
            $uri = $null
            if ([Uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri) -and $uri.Host.Length -ge 6) {
                [void]$privateValues.Add($uri.Host)
            }
        }
    }
    foreach ($match in [regex]::Matches($raw, '(?i)\b[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\b')) {
        [void]$privateValues.Add($match.Value)
    }
}

function Get-NativeCodeAuditBytes([byte[]]$Bytes) {
    # All uncompressed linker inputs are audited separately. ARM64 instruction
    # bytes can accidentally spell a short numeric comparison value; they are
    # not stored strings. Mask only ASCII digit matches wholly in ELF executable
    # PROGBITS sections. Assets, data/string sections and UTF-16 remain audited.
    if ($Bytes.Length -lt 64 -or $Bytes[0] -ne 127 -or $Bytes[1] -ne 69 -or
        $Bytes[2] -ne 76 -or $Bytes[3] -ne 70 -or $Bytes[4] -ne 2 -or $Bytes[5] -ne 1) { return ,$Bytes }
    $table = [BitConverter]::ToUInt64($Bytes, 40)
    $entrySize = [BitConverter]::ToUInt16($Bytes, 58)
    $entryCount = [BitConverter]::ToUInt16($Bytes, 60)
    if ($entrySize -lt 64 -or $table -gt $Bytes.Length -or $entryCount -gt (($Bytes.Length - $table) / $entrySize)) { return ,$Bytes }
    $result = [byte[]]$Bytes.Clone()
    $rawText = [Text.Encoding]::Latin1.GetString($Bytes)
    for ($section = 0; $section -lt $entryCount; $section++) {
        $header = [int]($table + $section * $entrySize)
        if ([BitConverter]::ToUInt32($Bytes, $header + 4) -ne 1 -or
            ([BitConverter]::ToUInt64($Bytes, $header + 8) -band 4) -eq 0) { continue }
        $start = [BitConverter]::ToUInt64($Bytes, $header + 24)
        $size = [BitConverter]::ToUInt64($Bytes, $header + 32)
        if ($start -gt $Bytes.Length -or $size -gt $Bytes.Length - $start) { continue }
        foreach ($value in $privateValues) {
            if ($value -notmatch '^[0-9]{6,11}$' -or $value.Length -gt $size) { continue }
            $position = [int]$start
            while (($position = $rawText.IndexOf($value, $position, [StringComparison]::Ordinal)) -ge 0 -and
                $position + $value.Length -le $start + $size) {
                [Array]::Clear($result, $position, $value.Length)
                $position += $value.Length
            }
        }
    }
    return ,$result
}

function Assert-NoPrivateValues([byte[]]$Bytes, [string]$Label, [bool]$CompressedManagedStore = $false) {
    if ($Label -match '^APK/lib/[^/]+/libaot-.*\.dll\.so$') { $Bytes = Get-NativeCodeAuditBytes $Bytes }
    # Decode both common string encodings; binary/nontext data is safe to inspect this way.
    foreach ($encoding in [Text.Encoding]::UTF8, [Text.Encoding]::Unicode) {
        $text = $encoding.GetString($Bytes)
        foreach ($value in $privateValues) {
            # LZ4 literal fragments can split framework character tables. Audit
            # numeric strings in the uncompressed linker inputs below, rather
            # than mistaking a fragment of a runtime lookup table for a credential.
            if ($CompressedManagedStore -and $value -match '^[0-9]{6,11}$') { continue }
            $offset = 0
            while (($offset = $text.IndexOf($value, $offset, [StringComparison]::OrdinalIgnoreCase)) -ge 0) {
                # Runtime HTTP/Base64 tables contain the entire decimal alphabet. A
                # short numeric comparison value occurring inside that exact table
                # is not a stored credential. Outside that table it still fails.
                $isDigitTable = $false
                foreach ($table in '0123456789', '123456789') {
                    $digitIndex = $table.IndexOf($value, [StringComparison]::Ordinal)
                    $tableStart = $offset - $digitIndex
                    if ($digitIndex -ge 0 -and $tableStart -ge 0 -and $tableStart + $table.Length -le $text.Length -and
                        $text.Substring($tableStart, $table.Length) -ceq $table) {
                        $isDigitTable = $true
                        break
                    }
                }
                # Some runtime lookup tables store 0, the middle digits and 9
                # separated by control-byte string lengths. X11 also has public
                # braille_dots_<digits> key names. Neither is a credential field.
                if (!$isDigitTable -and $value -match '^[0-9]+$') {
                    if ($offset -ge 2 -and $offset + $value.Length + 2 -le $text.Length) {
                        $window = $text.Substring($offset - 2, $value.Length + 4)
                        $isDigitTable = ([regex]::Replace($window, '[\x00-\x1f]', '') -ceq '0123456789')
                    }
                    $prefix = 'braille_dots_'
                    if ($offset -ge $prefix.Length -and $value -match '^[1-8]+$' -and
                        $text.Substring($offset - $prefix.Length, $prefix.Length) -ceq $prefix) {
                        $isDigitTable = $true
                    }
                }
                if ($isDigitTable) {
                    $offset += $value.Length
                    continue
                }
                $category = if ($value -match '^[A-Z]:[\\/]') { 'local filesystem path' }
                    elseif ($value -match '^[0-9a-f]{8}-') { 'device GUID' }
                    else { 'cloud endpoint or credential' }
                throw "Private $category found in $Label (value omitted)."
            }
        }
    }
}

$managedInputs = @(Get-ChildItem -LiteralPath $ManagedAssemblyDirectory -Filter '*.dll' -File)
if (!($managedInputs.Name -contains 'GameSaveManager.Maui.dll') -or
    !($managedInputs.Name -contains 'EflayGameSaveManager.Core.dll')) {
    throw 'Uncompressed app/Core linker inputs are required for the private-value audit.'
}
foreach ($assembly in $managedInputs) {
    Assert-NoPrivateValues ([IO.File]::ReadAllBytes($assembly.FullName)) "Managed/$($assembly.Name)"
}

$apkPath = Join-Path $BundleRoot 'Android-arm64/CultOfTheLamb-SaveManager.apk'
$archive = [IO.Compression.ZipFile]::OpenRead($apkPath)
try {
    foreach ($asset in 'GameSaveManager.config.json', 'GameSaveManager.runtime.json') {
        $entry = $archive.GetEntry("assets/$asset")
        if (!$entry) { throw "APK is missing $asset." }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $json = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($asset -eq 'GameSaveManager.config.json') { Assert-PublicConfig $json 'APK asset' }
        elseif (($json | ConvertFrom-Json).forced_device_name -cne 'Android') { throw 'APK runtime mismatch.' }
    }
    foreach ($entry in $archive.Entries) {
        if (!$entry.Length) { continue }
        $stream = $entry.Open()
        $memory = [IO.MemoryStream]::new()
        try {
            $stream.CopyTo($memory)
            Assert-NoPrivateValues $memory.ToArray() "APK/$($entry.FullName)" ($entry.FullName -match 'libassembly-store\.so$')
        }
        finally { $memory.Dispose(); $stream.Dispose() }
    }
}
finally { $archive.Dispose() }

foreach ($file in Get-ChildItem -LiteralPath $BundleRoot -File -Recurse) {
    if ($file.Extension -in '.pdb', '.log', '.bak', '.keystore' -or $file.Name -match 'sync_state|Backups\.json') {
        throw "Unexpected private/debug artifact: $($file.Name)"
    }
    # APK entries were inspected after decompression above.
    if ($file.Extension -ne '.apk') {
        Assert-NoPrivateValues ([IO.File]::ReadAllBytes($file.FullName)) $file.Name
    }
}
Write-Host "Public profile, APK assets and private-value scan passed ($($privateValues.Count) sensitive comparison values; values not reported)."
