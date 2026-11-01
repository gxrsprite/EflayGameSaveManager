$ErrorActionPreference = 'Stop'
# Import only the audit functions, never execute the release scanner or load real credentials.
$tokens = $null
$parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'verify-cotl-public.ps1'), [ref]$tokens, [ref]$parseErrors)
foreach ($name in 'Get-NativeCodeAuditBytes', 'Assert-NoPrivateValues') {
    $function = $ast.Find({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    . ([ScriptBlock]::Create($function.Extent.Text))
}
$privateValues = [Collections.Generic.HashSet[string]]::new()
[void]$privateValues.Add('86420975')
[void]$privateValues.Add('alphaKeyZ')
function New-TestElf([int]$Flags, [string]$Value, [bool]$Wide = $false) {
    $bytes = [byte[]]::new(384)
    ([byte[]](127, 69, 76, 70, 2, 1)).CopyTo($bytes, 0)
    [BitConverter]::GetBytes([UInt64]256).CopyTo($bytes, 40)
    [BitConverter]::GetBytes([UInt16]64).CopyTo($bytes, 58)
    [BitConverter]::GetBytes([UInt16]1).CopyTo($bytes, 60)
    [BitConverter]::GetBytes([UInt32]1).CopyTo($bytes, 260)
    [BitConverter]::GetBytes([UInt64]$Flags).CopyTo($bytes, 264)
    [BitConverter]::GetBytes([UInt64]128).CopyTo($bytes, 280)
    [BitConverter]::GetBytes([UInt64]64).CopyTo($bytes, 288)
    $encoding = if ($Wide) { [Text.Encoding]::Unicode } else { [Text.Encoding]::UTF8 }
    $encoding.GetBytes($Value).CopyTo($bytes, 128)
    return ,$bytes
}
$aotLabel = 'APK/lib/arm64-v8a/libaot-Test.dll.so'
Assert-NoPrivateValues (New-TestElf 4 '86420975') $aotLabel
foreach ($case in @(
    @{ Bytes = (New-TestElf 2 '86420975'); Label = $aotLabel },
    @{ Bytes = (New-TestElf 4 'alphaKeyZ'); Label = $aotLabel },
    @{ Bytes = (New-TestElf 4 '86420975' $true); Label = $aotLabel },
    @{ Bytes = (New-TestElf 4 '86420975'); Label = 'APK/lib/arm64-v8a/libother.so' },
    @{ Bytes = ([Text.Encoding]::UTF8.GetBytes('86420975')); Label = 'Managed/App.dll' }
)) {
    $rejected = $false
    try { Assert-NoPrivateValues $case.Bytes $case.Label } catch { $rejected = $true }
    if (!$rejected) { throw 'Privacy audit regression: sensitive string was accepted.' }
}
Write-Host '6 privacy regression cases passed.'
