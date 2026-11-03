# Builds the licence server on this PC and installs it on your VPS over SSH (uses Windows' built-in ssh/scp).
#
# First time (after ordering the VPS and pointing your domain at it):
#   .\deploy\publish-server.ps1 -Server 203.0.113.10 -FirstTime -Domain license.yoeyar.com -Support "Viber 09-xxx-xxx-xxx"
# Every update after that:
#   .\deploy\publish-server.ps1 -Server 203.0.113.10
#
# You type the server's root password when asked (or set up an SSH key, see ZCOM-GUIDE.md).
param(
    [Parameter(Mandatory)] [string] $Server,
    [string] $User = 'root',
    [switch] $FirstTime,
    [string] $Domain,
    [string] $Support = '',
    [string] $KeyFile = (Join-Path $PSScriptRoot '..\FreshMart.LicenseServer\App_Data\license-signing-key.pem')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = "$User@$Server"
$upload = '/root/yoeyar-upload'

if ($FirstTime) {
    if (-not $Domain) { throw "-Domain is required with -FirstTime (e.g. license.yoeyar.com)" }
    if (-not (Test-Path $KeyFile)) { throw "Signing key not found: $KeyFile. Use -KeyFile to point at your backup copy." }
    $resolved = try { [Net.Dns]::GetHostAddresses($Domain) | ForEach-Object IPAddressToString } catch { @() }
    if ($resolved -notcontains $Server) {
        Write-Warning "$Domain does not point to $Server yet (it resolves to: $($resolved -join ', ')). HTTPS will only work once it does."
    }
}

Write-Host "== Building the licence server"
$out = Join-Path $env:TEMP 'yoeyar-license-publish'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root 'FreshMart.LicenseServer\FreshMart.LicenseServer.csproj') -c Release -o $out --nologo -v q
if ($LASTEXITCODE) { throw "build failed" }
Remove-Item (Join-Path $out 'App_Data') -Recurse -Force -ErrorAction SilentlyContinue   # never ship local data or keys
$tar = Join-Path $env:TEMP 'license-server.tar.gz'
tar -czf $tar -C $out .
if ($LASTEXITCODE) { throw "packing failed" }

Write-Host "== Uploading to $Server"
ssh $target "mkdir -p $upload && chmod 700 $upload"
if ($LASTEXITCODE) { throw "cannot connect to $target" }
$files = @($tar, (Join-Path $PSScriptRoot 'server.sh'))
if ($FirstTime) { $files += (Resolve-Path $KeyFile).Path }
scp @files "${target}:$upload/"
if ($LASTEXITCODE) { throw "upload failed" }

if ($FirstTime) {
    $safeSupport = $Support -replace "'", ''
    Write-Host "== First-time setup (5-10 minutes)"
    ssh $target "bash $upload/server.sh setup '$Domain' '$safeSupport'"
} else {
    Write-Host "== Installing the update"
    ssh $target "install -m 755 $upload/server.sh /usr/local/bin/yoeyar-server && yoeyar-server deploy"
}
if ($LASTEXITCODE) { throw "the server reported an error (see above)" }
Remove-Item $tar -Force
Write-Host "Done."
