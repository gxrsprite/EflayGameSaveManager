param(
    [Parameter(Mandatory)][string]$ZipPath,
    [Parameter(Mandatory)][string]$PcSavesDir,
    [switch]$Reverse  # PC -> phone(port) direction
)

Add-Type -AssemblyName System.IO.Compression
$ErrorActionPreference = 'Stop'

function New-AesCrypto($Key, $IV, $Mode) {
    $aes = [Security.Cryptography.Aes]::Create()
    $aes.Mode = 'CBC'; $aes.Padding = 'PKCS7'
    $aes.Key = $Key; $aes.IV = $IV
    if ($Mode -eq 'enc') { return $aes.CreateEncryptor() } else { return $aes.CreateDecryptor() }
}
function Get-RandomBytes($n) {
    $b = New-Object byte[] $n
    [Security.Cryptography.RandomNumberGenerator]::Fill($b)
    return $b
}
function Gunzip([byte[]]$raw) {
    $ms = New-Object IO.MemoryStream(,$raw)
    $gz = New-Object IO.Compression.GzipStream($ms, [IO.Compression.CompressionMode]::Decompress)
    $out = New-Object IO.MemoryStream
    $gz.CopyTo($out); $gz.Close()
    return $out.ToArray()
}
function Gzip([byte[]]$raw) {
    $out = New-Object IO.MemoryStream
    $gz = New-Object IO.Compression.GzipStream($out, [IO.Compression.CompressionMode]::Compress)
    $gz.Write($raw, 0, $raw.Length); $gz.Close()
    return $out.ToArray()
}
function Strip-Bom([byte[]]$raw) {
    if ($raw.Length -ge 3 -and $raw[0] -eq 0xEF -and $raw[1] -eq 0xBB -and $raw[2] -eq 0xBF) { return $raw[3..($raw.Length-1)] }
    return $raw
}

if (-not $Reverse) {
    # ---- phone(port) -> PC Steam : ZB+gzip -> E+key+iv+AES ----
    if (-not (Test-Path $PcSavesDir)) { New-Item -ItemType Directory -Force $PcSavesDir | Out-Null }
    $bak = "$PcSavesDir-backup-$(Get-Date -Format yyyyMMdd-HHmmss)"
    Copy-Item $PcSavesDir $bak -Recurse -Force
    Write-Host "PC saves backed up to: $bak"

    $zip = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $zip.Entries) {
            # NOTE: a single Stream.Read() may return fewer bytes than requested
            # (observed returning ~half of a 16 KB entry), which silently truncates
            # the gunzipped JSON. Always read until the stream ends.
            $ms = New-Object IO.MemoryStream
            $stream = $entry.Open()
            try {
                $chunk = New-Object byte[] 8192
                while (($read = $stream.Read($chunk, 0, $chunk.Length)) -gt 0) {
                    $ms.Write($chunk, 0, $read)
                }
            } finally {
                $stream.Close()
            }
            $buf = $ms.ToArray()
            if ($entry.Name -eq 'Prefs.dict') {
                [IO.File]::WriteAllBytes((Join-Path $PcSavesDir 'Prefs.dict'), $buf)
                Write-Host "copied raw: Prefs.dict"; continue
            }
            if ($buf.Length -lt 3 -or $buf[0] -ne 0x5A -or $buf[1] -ne 0x42) { throw "Not a ZB save: $($entry.Name)" }
            $json = Strip-Bom (Gunzip $buf[2..($buf.Length-1)])
            # Fail loudly on truncated saves instead of writing invalid JSON.
            try { $null = [Text.Encoding]::UTF8.GetString($json) | ConvertFrom-Json -AsHashtable -ErrorAction Stop }
            catch { throw "Save '$($entry.Name)' is not complete JSON ($($json.Length) bytes): $($_.Exception.Message)" }
            $key = Get-RandomBytes 16; $iv = Get-RandomBytes 16
            $ct = (New-AesCrypto $key $iv 'enc').TransformFinalBlock($json, 0, $json.Length)
            $out = New-Object byte[] (1 + 16 + 16 + $ct.Length)
            $out[0] = [byte][char]'E'
            [Array]::Copy($key, 0, $out, 1, 16)
            [Array]::Copy($iv, 0, $out, 17, 16)
            [Array]::Copy($ct, 0, $out, 33, $ct.Length)
            $dest = Join-Path $PcSavesDir ($entry.Name + '.json')
            [IO.File]::WriteAllBytes($dest, $out)
            Write-Host "converted: $($entry.Name) -> $(Split-Path $dest -Leaf) ($($out.Length) bytes)"
        }
    } finally { $zip.Dispose() }
} else {
    # ---- PC Steam -> phone(port) : E+key+iv+AES -> ZB+gzip ----
    $stage = Join-Path ([IO.Path]::GetTempPath()) ("cotl2phone-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $stage | Out-Null
    foreach ($f in Get-ChildItem $PcSavesDir -File) {
        if ($f.Name -eq 'Prefs.dict') { Copy-Item $f.FullName (Join-Path $stage 'Prefs.dict'); continue }
        $raw = [IO.File]::ReadAllBytes($f.FullName)
        if ($raw.Length -lt 34 -or $raw[0] -ne [byte][char]'E') { Write-Host "skip (not PC format): $($f.Name)"; continue }
        $key = $raw[1..16]; $iv = $raw[17..32]; $ct = $raw[33..($raw.Length-1)]
        $json = (New-AesCrypto $key $iv 'dec').TransformFinalBlock($ct, 0, $ct.Length)
        $bom = [byte[]](0xEF, 0xBB, 0xBF)
        $gz = Gzip ($bom + $json)
        $prefixed = New-Object byte[] (2 + $gz.Length)
        $prefixed[0] = [byte][char]'Z'; $prefixed[1] = [byte][char]'B'
        [Array]::Copy($gz, 0, $prefixed, 2, $gz.Length)
        $base = [IO.Path]::GetFileNameWithoutExtension($f.Name)
        [IO.File]::WriteAllBytes((Join-Path $stage $base), $prefixed)
        Write-Host "converted: $($f.Name) -> $base ($($prefixed.Length) bytes)"
    }
    $outZip = Join-Path ([IO.Path]::GetTempPath()) ("cotl-phone-$(Get-Date -Format yyyyMMdd-HHmmss).zip")
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $outZip)
    Write-Host "Phone-ready zip: $outZip (upload it as the phone game's save, extensionless entries inside)"
}
