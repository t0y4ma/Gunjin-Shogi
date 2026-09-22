# 軍人将棋 オンライン対戦の配置手順（Nine の VM に追加する）

軍人将棋のサーバーは、Nine と同じ GCP の VM（e2-micro・`nine.freeddns.org`）に**追加**します。Nine の設定・ファイル・サービスはそのまま残ります。
WebGL クライアントは、Nine と同じく Cloudflare Pages から配信します。VM からは配りません（GCP の無料枠は外向きの通信量が小さく、約 30MB のゲームを VM から配るとすぐ使い切ってしまうため）。

## 全体の形

```
ブラウザ（Cloudflare Pages から WebGL を読み込む）
   │  wss://nine.freeddns.org/gunjin
   ▼
Caddy（VM・TLS 終端）
   ├─ /gunjin で始まる接続 ──▶ 127.0.0.1:7778   軍人将棋サーバー（gunjin-server.service・~/gunjin）
   └─ それ以外（今までどおり）──▶ 127.0.0.1:27777  Nine サーバー（nine-server.service・~/game2）
```

| | Nine | 軍人将棋 |
|---|---|---|
| systemd のサービス | `nine-server` | `gunjin-server` |
| 置き場所 | `~/game2` | `~/gunjin` |
| ポート（VM の中だけ） | 27777 | 7778 |
| 接続先 | `wss://nine.freeddns.org` | `wss://nine.freeddns.org/gunjin` |
| WebGL | `https://nine-mja.pages.dev` | Cloudflare Pages（このリポジトリの `Builds/WebGL`） |

外に開けるポートは今までどおり 80 / 443 / 22 だけです。GCP のファイアウォールの変更は要りません（7778 は開けないでください）。

## 1. WebGL を Cloudflare Pages に登録する（初回のみ）

1. Cloudflare のダッシュボード →「Workers & Pages」→「作成」→「Pages」→「Git に接続」
2. リポジトリ `t0y4ma/Gunjin-Shogi` を選ぶ
3. ビルドの設定
   - フレームワークのプリセット：なし
   - ビルドコマンド：空欄（ビルド済みのファイルをそのまま公開する）
   - ビルド出力ディレクトリ：`Builds/WebGL`
4. 保存すると公開されます（例 `https://gunjin-shogi.pages.dev`）。以後は **GitHub に push するたびに自動で更新**されます。

## 2. ゲームサーバーを VM に追加する（初回）

Windows 側の `Builds/GunjinShogiServer.zip`（ビルドスクリプトが作ります）を使います。

1. GCP コンソールの VM インスタンス一覧から、該当インスタンスの「SSH」を開く
2. 右上の歯車 →「ファイルをアップロード」で `GunjinShogiServer.zip` をアップロード（ホームディレクトリ `~` に置かれる）
3. ターミナルで次を実行

```bash
# 置き場所を作って展開
mkdir -p ~/gunjin
unzip -o ~/GunjinShogiServer.zip -d ~/gunjin
chmod -R +rX ~/gunjin
chmod +x ~/gunjin/GunjinShogiServer.x86_64

# サービスを登録（Nine の nine-server とは別のサービス）
sudo tee /etc/systemd/system/gunjin-server.service > /dev/null << 'EOF'
[Unit]
Description=Gunjin Shogi Game Server
After=network.target

[Service]
Type=simple
User=toyama_family_from2006
WorkingDirectory=/home/toyama_family_from2006/gunjin
ExecStart=/home/toyama_family_from2006/gunjin/GunjinShogiServer.x86_64 -batchmode -nographics -port 7778 -logFile -
Restart=always
RestartSec=5
MemoryMax=400M

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now gunjin-server
sudo systemctl status gunjin-server   # active (running) なら成功
```

ログに `[Gunjin] サーバー起動 ポート 7778` が出ていれば動いています。

```bash
sudo journalctl -u gunjin-server -f
```

（同じ内容の service ファイルは `Deploy/gunjin-server.service` にもあります）

## 3. Caddy に振り分けを追加する（初回）

`/etc/caddy/Caddyfile` の `nine.freeddns.org { ... }` を、`Deploy/Caddyfile.gunjin` の内容に書き換えます。Nine 向けの `reverse_proxy 127.0.0.1:27777` は `handle { ... }` の中にそのまま残ります。

```bash
sudo cp /etc/caddy/Caddyfile /etc/caddy/Caddyfile.bak   # 念のため控えを取る
sudo nano /etc/caddy/Caddyfile                          # 書き換える
sudo caddy validate --config /etc/caddy/Caddyfile       # 書式の確認
sudo systemctl reload caddy
```

書き換え後の形：

```
nine.freeddns.org {
	handle /gunjin* {
		reverse_proxy 127.0.0.1:7778 {
			stream_timeout 1h
			stream_close_delay 30s
		}
	}

	handle {
		reverse_proxy 127.0.0.1:27777 {
			stream_timeout 1h
			stream_close_delay 30s
		}
	}
}
```

うまくいかなければ `sudo cp /etc/caddy/Caddyfile.bak /etc/caddy/Caddyfile && sudo systemctl reload caddy` で元に戻せます。

## 4. 確認

1. **Nine が今までどおり動くこと**（`https://nine-mja.pages.dev` で部屋を作れる）
2. 軍人将棋の Pages の URL を開き、「オンライン対戦」で「サーバーに接続しています（nine.freeddns.org）」と出る
3. 2つのブラウザ（片方はシークレットウィンドウなど）で、部屋を作る → 部屋番号で入る → 対局できる
4. 「招待」でコピーしたリンクを別のブラウザで開くと、その部屋に直接入れる
5. メモリに余裕があるか：`free -h`（e2-micro は 1GB。足りなければスワップの追加を検討）

## 更新するとき

WebGL は push すれば Pages が自動で更新しますが、**サーバーは自動では更新されません**。通信の中身が変わった版では、**サーバーと WebGL を必ず一緒に**更新してください（版が違うと「サーバーとゲームのバージョンが違います」と出て接続できません）。

1. Unity でビルド（メニュー「軍人将棋 → ビルド → 両方」）→ GitHub に push（WebGL が更新される）
2. `GunjinShogiServer.zip` を GCP の SSH でアップロードして、次を実行

```bash
sudo systemctl stop gunjin-server
rm -rf ~/gunjin && mkdir -p ~/gunjin
unzip -o ~/GunjinShogiServer.zip -d ~/gunjin
chmod -R +rX ~/gunjin
chmod +x ~/gunjin/GunjinShogiServer.x86_64
sudo systemctl start gunjin-server
sudo systemctl status gunjin-server
```

軍人将棋のサーバーを再起動しても、Nine には影響しません。軍人将棋の進行中の部屋は消えます（プレイヤーには「部屋がなくなりました」と表示されます）。

## うまくいかないとき

| 症状 | 確認すること |
|---|---|
| 軍人将棋で「サーバーにつながっていません」 | `sudo systemctl status gunjin-server`、Caddyfile の `handle /gunjin*` |
| Nine がつながらなくなった | Caddyfile の `handle { reverse_proxy 127.0.0.1:27777 ... }` が残っているか。控えから戻す |
| 「バージョンが違います」 | サーバーと WebGL の片方だけ更新していないか |
| サーバーがすぐ落ちる | `sudo journalctl -u gunjin-server -n 100`。実行権限（`chmod +x`）、メモリ（`free -h`） |
| ページが真っ白・読み込みが止まる | Pages のビルド出力ディレクトリが `Builds/WebGL` になっているか |

## 通信量の目安

1手あたり、送信は約 7 バイト、受信は 12〜17 バイト（Mirror と WebSocket の枠組み分は別）です。1局 150 手でも数 KB 程度で、VM の通信量にはほとんど影響しません。
