# Builds the files you give to shops, into .\release:
#   YoeYarPOS-android.apk          (install on Android phones/tablets)
#   YoeYarPOS-windows-x64.zip      (unzip, double-click "Install Yoe Yar POS")
# Before a real release: raise ApplicationDisplayVersion/ApplicationVersion in FreshMart.Pos.csproj,
# set DefaultServer in LicenseService.cs to your hosted https address, and set YOEYAR_KEYSTORE / YOEYAR_KEYSTORE_PASS.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$app = Join-Path $root 'FreshMart.Pos'
$out = Join-Path $root 'release'
New-Item -ItemType Directory -Force $out | Out-Null

if (-not $env:YOEYAR_KEYSTORE) { Write-Warning "YOEYAR_KEYSTORE is not set: the APK is signed with the DEBUG key (testing only)." }
if (Select-String -Path "$app\Services\LicenseService.cs" -Pattern 'DefaultServer = "http://localhost' -Quiet) {
    Write-Warning "LicenseService.DefaultServer still points at localhost: shops will not be able to activate."
}

Write-Host "== Android"
dotnet publish "$app\FreshMart.Pos.csproj" -f net10.0-android -c Release -p:AndroidPackageFormats=apk -o "$out\android-tmp"
if ($LASTEXITCODE) { throw "Android build failed" }
Copy-Item (Get-ChildItem "$out\android-tmp\*-Signed.apk" | Select-Object -First 1).FullName "$out\YoeYarPOS-android.apk" -Force
Remove-Item "$out\android-tmp" -Recurse -Force

Write-Host "== Windows"
$stage = Join-Path $out 'windows-tmp'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
dotnet publish "$app\FreshMart.Pos.csproj" -f net10.0-windows10.0.19041.0 -p:WindowsOnly=true -c Release -r win-x64 `
    --self-contained true -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -o "$stage\Yoe Yar POS\app"
if ($LASTEXITCODE) { throw "Windows build failed" }
Copy-Item "$root\installer\windows\*" "$stage\Yoe Yar POS\"
$zip = "$out\YoeYarPOS-windows-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive "$stage\Yoe Yar POS" $zip
Remove-Item $stage -Recurse -Force

Get-ChildItem $out -File | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }, LastWriteTime
