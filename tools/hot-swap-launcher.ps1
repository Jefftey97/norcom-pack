# Smoke-launch a published NorcomLauncher.exe before it replaces the installed copy.
# Keeps LocalAppData\NORCOM\Launcher\NorcomLauncher.exe.bak-pre-<stamp>
# and restores that backup if the new process exits with a failure.
# Do not use this on a binary-patched bundle. The candidate must be a clean dotnet publish.
# A GUI that dies on startup (2.0.37: Cannot locate resource 'app.xaml', exit -532462766)
# returns a non-zero exit here and must not replace the installed exe. Inflate is not enough.
param(
    [Parameter(Mandatory = $true)]
    [string]$Candidate
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Candidate)) {
    throw "Candidate exe was not found: $Candidate"
}

$launcherDir = Join-Path $env:LOCALAPPDATA 'NORCOM\Launcher'
$live = Join-Path $launcherDir 'NorcomLauncher.exe'
New-Item -ItemType Directory -Force -Path $launcherDir | Out-Null

function Test-Smoke([string]$Path) {
    $proc = Start-Process -FilePath $Path -PassThru -WindowStyle Normal
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline) {
        $proc.Refresh()
        if ($proc.HasExited) {
            return $proc.ExitCode
        }
        Start-Sleep -Milliseconds 400
    }
    if (-not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        return 0
    }
    return $proc.ExitCode
}

Write-Host "Smoke launching $Candidate"
$smoke = Test-Smoke $Candidate
if ($smoke -ne 0) {
    Write-Error "Smoke failed with exit $smoke. Installed launcher was not replaced."
    exit 1
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (Test-Path -LiteralPath $live) {
    $bak = "$live.bak-pre-$stamp"
    Copy-Item -LiteralPath $live -Destination $bak -Force
    Write-Host "Backup $bak"
}

Copy-Item -LiteralPath $Candidate -Destination $live -Force
Write-Host "Replaced $live"
$installed = Test-Smoke $live
if ($installed -ne 0) {
    $latest = Get-ChildItem -LiteralPath $launcherDir -Filter 'NorcomLauncher.exe.bak-pre-*' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -ne $latest) {
        Copy-Item -LiteralPath $latest.FullName -Destination $live -Force
        Write-Host "Rolled back to $($latest.FullName)"
    }
    Write-Error "Installed exe failed smoke with exit $installed."
    exit 1
}

Write-Host "Smoke PASS $live"
exit 0
