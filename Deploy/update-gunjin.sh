#!/usr/bin/env bash
# 軍人将棋サーバーの更新（初回の登録も兼ねる）。VM 上で実行する。
#
#   使い方:  bash ~/update-gunjin.sh [zip のパス]
#            zip を省略すると ~/GunjinShogiServer.zip を使う
#
# ・サービス（gunjin-server）が登録済みなら、その WorkingDirectory と User に展開する
# ・未登録なら、今のユーザーの ~/gunjin に展開してサービスを登録する
# ・Nine（nine-server）には触らない
set -euo pipefail

SERVICE=gunjin-server
UNIT=/etc/systemd/system/$SERVICE.service
EXE=GunjinShogiServer.x86_64
PORT=7778
ZIP="${1:-$HOME/GunjinShogiServer.zip}"

say() { printf '\n== %s\n' "$*"; }

[ -f "$ZIP" ] || { echo "zip が見つかりません: $ZIP" >&2; exit 1; }
command -v unzip > /dev/null || { say "unzip を入れます"; sudo apt-get update -qq && sudo apt-get install -y -qq unzip; }

# 展開先とサービスの実行ユーザーを決める
if [ -f "$UNIT" ]; then
  DEST=$(sed -n 's/^WorkingDirectory=//p' "$UNIT" | head -1)
  RUNAS=$(sed -n 's/^User=//p' "$UNIT" | head -1)
fi
DEST=${DEST:-$HOME/gunjin}
RUNAS=${RUNAS:-$(id -un)}
id "$RUNAS" > /dev/null 2>&1 || { echo "サービスのユーザー $RUNAS が存在しません。$UNIT の User= を直してください" >&2; exit 1; }
# 誤ってホームなどを消さないよう、展開先は「gunjin という名前のフォルダ」か「すでにサーバーが入っているフォルダ」に限る
if [ "$(basename "$DEST")" != "gunjin" ] && [ ! -f "$DEST/$EXE" ]; then
  echo "展開先 $DEST は軍人将棋のフォルダに見えないので中止します（$UNIT の WorkingDirectory を確認してください）" >&2
  exit 1
fi
echo "zip:      $ZIP"
echo "展開先:   $DEST"
echo "実行ユーザー: $RUNAS"

# 展開して中身を確かめてから入れ替える（壊れた zip で動いている版を消さないため）
say "展開"
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
unzip -q -o "$ZIP" -d "$TMP"
SRC=$TMP
# zip の中に 1 段フォルダがある場合にも対応
if [ ! -f "$SRC/$EXE" ]; then
  FOUND=$(find "$TMP" -maxdepth 3 -name "$EXE" -type f | head -1)
  [ -n "$FOUND" ] || { echo "zip の中に $EXE がありません" >&2; exit 1; }
  SRC=$(dirname "$FOUND")
fi

say "停止"
sudo systemctl stop "$SERVICE" 2> /dev/null || true

say "入れ替え"
sudo mkdir -p "$DEST"
sudo find "$DEST" -mindepth 1 -delete
sudo cp -a "$SRC"/. "$DEST"/
sudo chown -R "$RUNAS": "$DEST"
sudo chmod -R u+rwX,go+rX "$DEST"
sudo chmod +x "$DEST/$EXE"

# 初回：サービスを登録
if [ ! -f "$UNIT" ]; then
  say "サービスを登録"
  sudo tee "$UNIT" > /dev/null << EOF
[Unit]
Description=Gunjin Shogi Game Server
After=network.target

[Service]
Type=simple
User=$RUNAS
WorkingDirectory=$DEST
ExecStart=$DEST/$EXE -batchmode -nographics -port $PORT -logFile -
Restart=always
RestartSec=5
MemoryMax=400M

[Install]
WantedBy=multi-user.target
EOF
  sudo systemctl daemon-reload
  sudo systemctl enable "$SERVICE"
fi

say "起動"
sudo systemctl start "$SERVICE"
sleep 4
if systemctl is-active --quiet "$SERVICE"; then
  echo "起動しました"
else
  echo "起動に失敗しました。ログ:" >&2
  sudo journalctl -u "$SERVICE" -n 30 --no-pager >&2
  exit 1
fi
sudo journalctl -u "$SERVICE" -n 8 --no-pager
echo
free -h | head -2
echo
echo "完了。zip は不要なら削除してかまいません:  rm \"$ZIP\""
