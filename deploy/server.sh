#!/usr/bin/env bash
# Yoe Yar POS licence server on a fresh Ubuntu VPS (Z.com Cloud VPS or any other).
# Run as root. Uploaded and started for you by deploy/publish-server.ps1; see deploy/ZCOM-GUIDE.md.
#
#   server.sh setup  <domain> "<support text>"   once: .NET, PostgreSQL, Caddy (HTTPS), firewall, service, nightly backup
#   server.sh deploy                             install the app uploaded to /root/yoeyar-upload and restart it
#   server.sh backup                             back up the database now
#   server.sh status                             service state and the last log lines
set -euo pipefail

APP_DIR=/opt/yoeyar/license-server
CONF_DIR=/etc/yoeyar
DATA_DIR=/var/lib/yoeyar
BACKUP_DIR=/var/backups/yoeyar
UPLOAD=/root/yoeyar-upload
SERVICE=yoeyar-license
DOTNET=/usr/share/dotnet

say() { printf '\n\033[1;32m== %s\033[0m\n' "$*"; }
die() { printf '\n\033[1;31mERROR: %s\033[0m\n' "$*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || die "run as root"

setup() {
    local domain="${1:-}" support="${2:-}"
    [ -n "$domain" ] || die "usage: server.sh setup license.yourdomain.com \"Viber 09-xxx\""
    [ -f "$UPLOAD/license-signing-key.pem" ] || die "upload license-signing-key.pem first (publish-server.ps1 -FirstTime does it)"

    say "Packages"
    export DEBIAN_FRONTEND=noninteractive
    apt-get update -q
    apt-get install -y -q curl gnupg debian-keyring debian-archive-keyring apt-transport-https \
        libicu-dev postgresql ufw tar

    say ".NET 10 runtime (ASP.NET Core)"
    if [ ! -x "$DOTNET/dotnet" ] || ! "$DOTNET/dotnet" --list-runtimes | grep -q 'AspNetCore.App 10\.'; then
        curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
        bash /tmp/dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir "$DOTNET"
        ln -sf "$DOTNET/dotnet" /usr/local/bin/dotnet
    fi

    say "Caddy (automatic HTTPS)"
    if ! command -v caddy >/dev/null; then
        curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor --yes -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
        curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' > /etc/apt/sources.list.d/caddy-stable.list
        apt-get update -q
        apt-get install -y -q caddy
    fi

    say "Service user and folders"
    id yoeyar >/dev/null 2>&1 || useradd --system --home "$DATA_DIR" --shell /usr/sbin/nologin yoeyar
    install -d -o yoeyar -g yoeyar -m 750 "$APP_DIR" "$DATA_DIR" "$DATA_DIR/dp-keys"
    install -d -o root -g yoeyar -m 750 "$CONF_DIR"
    install -d -o postgres -g postgres -m 700 "$BACKUP_DIR"

    say "Signing key"
    if [ -f "$CONF_DIR/license-signing-key.pem" ] && ! cmp -s "$UPLOAD/license-signing-key.pem" "$CONF_DIR/license-signing-key.pem"; then
        die "a DIFFERENT signing key is already installed in $CONF_DIR - not replacing it"
    fi
    install -o root -g yoeyar -m 640 "$UPLOAD/license-signing-key.pem" "$CONF_DIR/license-signing-key.pem"
    shred -u "$UPLOAD/license-signing-key.pem" 2>/dev/null || rm -f "$UPLOAD/license-signing-key.pem"

    say "PostgreSQL database"
    local env_file="$CONF_DIR/license-server.env" db_pass
    if [ -f "$env_file" ]; then
        db_pass=$(sed -n 's/.*Password=\([^;]*\).*/\1/p' "$env_file")
    else
        db_pass=$(openssl rand -hex 24)
    fi
    if ! sudo -u postgres psql -tAc "select 1 from pg_roles where rolname='yoeyar'" | grep -q 1; then
        sudo -u postgres psql -q -c "create user yoeyar with password '$db_pass'"
    fi
    if ! sudo -u postgres psql -tAc "select 1 from pg_database where datname='licenses'" | grep -q 1; then
        sudo -u postgres psql -q -c "create database licenses owner yoeyar"
    fi

    say "Settings ($env_file)"
    cat > "$env_file" <<EOF
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5080
Database__Provider=postgres
ConnectionStrings__Licenses=Host=127.0.0.1;Database=licenses;Username=yoeyar;Password=$db_pass
License__SigningKeyPath=$CONF_DIR/license-signing-key.pem
License__RequireExistingKey=true
License__ServerUrl=https://$domain
DataProtection__KeysPath=$DATA_DIR/dp-keys
Vendor__Support=$support
EOF
    chown root:yoeyar "$env_file"
    chmod 640 "$env_file"

    say "Service ($SERVICE)"
    cat > /etc/systemd/system/$SERVICE.service <<EOF
[Unit]
Description=Yoe Yar POS licence server
After=network.target postgresql.service
Requires=postgresql.service

[Service]
User=yoeyar
Group=yoeyar
WorkingDirectory=$APP_DIR
ExecStart=$DOTNET/dotnet $APP_DIR/FreshMart.LicenseServer.dll
EnvironmentFile=$env_file
Restart=always
RestartSec=5
NoNewPrivileges=true
ProtectSystem=full
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF
    systemctl daemon-reload
    systemctl enable $SERVICE >/dev/null

    say "HTTPS for $domain"
    cat > /etc/caddy/Caddyfile <<EOF
$domain {
    encode gzip
    reverse_proxy 127.0.0.1:5080
}
EOF
    systemctl reload caddy || systemctl restart caddy

    say "Firewall (SSH, HTTP, HTTPS only)"
    ufw allow OpenSSH >/dev/null
    ufw allow 80/tcp >/dev/null
    ufw allow 443/tcp >/dev/null
    ufw --force enable >/dev/null

    say "Nightly database backup (02:30, kept 30 days in $BACKUP_DIR)"
    install -m 755 "$0" /usr/local/bin/yoeyar-server
    echo "30 2 * * * root /usr/local/bin/yoeyar-server backup >/dev/null 2>&1" > /etc/cron.d/yoeyar-backup

    if [ -f "$UPLOAD/license-server.tar.gz" ]; then deploy; fi
    say "Setup finished. Open https://$domain and create your admin account."
}

deploy() {
    [ -f "$UPLOAD/license-server.tar.gz" ] || die "no $UPLOAD/license-server.tar.gz - run publish-server.ps1"
    say "Installing the app"
    systemctl stop $SERVICE 2>/dev/null || true
    find "$APP_DIR" -mindepth 1 -maxdepth 1 ! -name App_Data -exec rm -rf {} +
    tar -xzf "$UPLOAD/license-server.tar.gz" -C "$APP_DIR"
    chown -R yoeyar:yoeyar "$APP_DIR"
    rm -f "$UPLOAD/license-server.tar.gz"
    systemctl start $SERVICE
    sleep 4
    if systemctl is-active --quiet $SERVICE; then
        say "Running. Public key in use:"
        journalctl -u $SERVICE -n 50 --no-pager | grep -o 'Licence public key: .*' | tail -1 || true
    else
        journalctl -u $SERVICE -n 30 --no-pager
        die "the service did not start - see the log above"
    fi
}

backup() {
    install -d -o postgres -g postgres -m 700 "$BACKUP_DIR"
    local file="$BACKUP_DIR/licenses-$(date +%F-%H%M).sql.gz"
    sudo -u postgres pg_dump licenses | gzip > "$file"
    chown postgres:postgres "$file"
    chmod 600 "$file"
    find "$BACKUP_DIR" -name 'licenses-*.sql.gz' -mtime +30 -delete
    echo "$file"
}

status() {
    systemctl --no-pager status $SERVICE | head -5 || true
    journalctl -u $SERVICE -n 15 --no-pager
}

case "${1:-}" in
    setup) shift; setup "$@" ;;
    deploy) deploy ;;
    backup) backup ;;
    status) status ;;
    *) die "usage: server.sh setup <domain> \"<support>\" | deploy | backup | status" ;;
esac
