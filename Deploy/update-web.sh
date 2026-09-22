#!/usr/bin/env bash
# WebGL クライアントの更新（VM 上で実行）。リポジトリの最新を取り込むだけで Caddy が新しいファイルを配信する
set -euo pipefail
cd /srv/gunjin-shogi
git pull --ff-only
echo "WebGL を更新しました: $(git log -1 --format='%h %s')"
