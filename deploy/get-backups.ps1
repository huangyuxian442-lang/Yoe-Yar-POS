# Downloads the server's nightly database backups to this PC (keep a copy off the server).
#   .\deploy\get-backups.ps1 -Server 203.0.113.10                   -> .\server-backups\
#   .\deploy\get-backups.ps1 -Server 203.0.113.10 -To D:\Backups
param(
    [Parameter(Mandatory)] [string] $Server,
    [string] $User = 'root',
    [string] $To = (Join-Path (Split-Path $PSScriptRoot -Parent) 'server-backups')
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $To | Out-Null
ssh "$User@$Server" "yoeyar-server backup >/dev/null"   # include a fresh one
scp "${User}@${Server}:/var/backups/yoeyar/licenses-*.sql.gz" "$To\"
if ($LASTEXITCODE) { throw "download failed" }
Get-ChildItem $To -Filter 'licenses-*.sql.gz' | Sort-Object LastWriteTime -Descending | Select-Object -First 5 Name, LastWriteTime
