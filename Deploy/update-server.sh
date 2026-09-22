#!/usr/bin/env bash
# サーバーの入れ替え（VM 上で実行）。使い方: sudo ./update-server.sh /tmp/GunjinShogiServer.zip
set -euo pipefail
ZIP="${1:?サーバーの zip を指定してください}"
DEST=/opt/gunjin-server
systemctl stop gunjin-server || true
rm -rf "$DEST.new"
mkdir -p "$DEST.new"
unzip -q "$ZIP" -d "$DEST.new"
chmod +x "$DEST.new/GunjinShogiServer.x86_64"
rm -rf "$DEST.old"
[ -d "$DEST" ] && mv "$DEST" "$DEST.old"
mv "$DEST.new" "$DEST"
chown -R gunjin:gunjin "$DEST"
systemctl start gunjin-server
systemctl --no-pager status gunjin-server | head -5
