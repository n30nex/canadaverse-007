#!/usr/bin/env bash
# Run on the Pi from a reviewed release checkout: sudo bash deploy/install-pi.sh
set -euo pipefail
if [ "$(id -u)" -ne 0 ]; then echo 'Run with sudo on the target Pi.' >&2; exit 1; fi
umask 022
source_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
python3 -c 'import sys; assert sys.version_info >= (3, 10), "Python 3.10+ is required"'
release_dir="/opt/canadaverse-007/releases/$(date -u +%Y%m%dT%H%M%SZ)"
if [ -e "$release_dir" ]; then echo 'Release directory already exists; retry in one second.' >&2; exit 1; fi
install -d -m 0755 "$release_dir/server/static"
install -m 0644 "$source_dir/server/app.py" "$source_dir/server/__init__.py" "$source_dir/server/requirements.txt" "$release_dir/server/"
install -m 0644 "$source_dir/server/static/index.html" "$source_dir/server/static/style.css" "$source_dir/server/static/rooms.js" "$release_dir/server/static/"
python3 -m venv "$release_dir/.venv"
"$release_dir/.venv/bin/python" -m pip install --disable-pip-version-check -r "$release_dir/server/requirements.txt"
cd "$release_dir"
"$release_dir/.venv/bin/python" -c 'from server.app import app; print("Directory import verified")'
old_target="$(readlink /opt/canadaverse-007/current || true)"
ln -s "$release_dir" /opt/canadaverse-007/current.new
mv -Tf /opt/canadaverse-007/current.new /opt/canadaverse-007/current
install -m 0644 "$source_dir/deploy/canadaverse-007.service" /etc/systemd/system/canadaverse-007.service
systemctl daemon-reload
systemctl enable canadaverse-007.service
systemctl restart canadaverse-007.service
for attempt in $(seq 1 15); do
    if python3 -c 'import urllib.request,json; r=json.load(urllib.request.urlopen("http://127.0.0.1:18707/healthz",timeout=2)); assert r["ok"]' 2>/dev/null; then
        echo "Directory healthy. Release: $release_dir"
        echo "Previous release: ${old_target:-none}"
        echo 'Add lobbies.canadaverse.org -> http://127.0.0.1:18707 to the existing Cloudflare Tunnel.'
        exit 0
    fi
    sleep 1
done
echo 'Health check failed. Inspect: journalctl -u canadaverse-007 -n 50' >&2
if [ -n "$old_target" ]; then
    ln -s "$old_target" /opt/canadaverse-007/rollback.new
    mv -Tf /opt/canadaverse-007/rollback.new /opt/canadaverse-007/current
    systemctl restart canadaverse-007.service
    echo "Restored previous release: $old_target" >&2
fi
exit 1
