# Installs or updates Yoe Yar POS for this Windows user:
# copies the app to %LOCALAPPDATA%\Programs\Yoe Yar POS and adds Start menu and desktop shortcuts.
# Shop data (sales, debts, licence) lives in %LOCALAPPDATA%\Yoe Yar and is never touched here.
$ErrorActionPreference = 'Stop'
$name = 'Yoe Yar POS'
$src = Join-Path $PSScriptRoot 'app'
$dest = Join-Path $env:LOCALAPPDATA "Programs\$name"

if (-not (Test-Path (Join-Path $src 'YoeYarPOS.exe'))) { throw "Unzip the whole folder first, then run the installer again." }

# an update can't replace files while the app is open
Get-Process YoeYarPOS -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Closing $name..."
    $_.CloseMainWindow() | Out-Null
    if (-not $_.WaitForExit(15000)) { throw "Close $name, then run the installer again." }
}

Write-Host "Installing $name to $dest"
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }   # program files only
Copy-Item $src $dest -Recurse

$exe = Join-Path $dest 'YoeYarPOS.exe'
$shell = New-Object -ComObject WScript.Shell
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) {
    $lnk = $shell.CreateShortcut((Join-Path $dir "$name.lnk"))
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $dest
    $lnk.IconLocation = "$exe,0"
    $lnk.Save()
}

Write-Host "Done. Starting $name..."
Start-Process $exe
