# Stamp payload-version.txt with the exe ProductVersion before a friends feed zip is cut.
# A tip whose payload-version is not identical to ProductVersion must not ship (2.0.37/38).
param(
    [Parameter(Mandatory = $true)]
    [string]$Exe,
    [Parameter(Mandatory = $true)]
    [string]$ProductVersion
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Exe)) {
    throw "Launcher exe was not found: $Exe"
}

Add-Type -AssemblyName System.Diagnostics.FileVersionInfo
$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path -LiteralPath $Exe))
$product = $info.ProductVersion
if ([string]::IsNullOrWhiteSpace($product)) {
    $product = $info.FileVersion
}
if ([string]::IsNullOrWhiteSpace($product)) {
    throw "Exe has no ProductVersion: $Exe"
}

function Normalize([string]$Value) {
    $head = ($Value -split '\+')[0].Trim()
    $parts = $head.Split('.')
    $major = if ($parts.Length -gt 0 -and $parts[0]) { [int]$parts[0] } else { 0 }
    $minor = if ($parts.Length -gt 1 -and $parts[1]) { [int]$parts[1] } else { 0 }
    $build = if ($parts.Length -gt 2 -and $parts[2]) { [int]$parts[2] } else { 0 }
    return "$major.$minor.$build"
}

$stamped = Normalize $ProductVersion
$fromExe = Normalize $product
if ($stamped -ne $fromExe) {
    throw "payload-version $stamped does not match ProductVersion $fromExe"
}

$dir = Split-Path -Parent (Resolve-Path -LiteralPath $Exe)
$path = Join-Path $dir 'payload-version.txt'
Set-Content -LiteralPath $path -Value ($stamped + "`n") -NoNewline -Encoding ascii
Write-Host "Stamped $path $stamped"
