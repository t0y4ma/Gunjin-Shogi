# 軍人将棋 オンライン対戦の配置手順（Caddy の入った Linux VM）

## 全体の形

```
ブラウザ ──https / wss (443)──▶ Caddy ─┬─ WebGL のファイル（/srv/gunjin-shogi/Builds/WebGL）
                                        └─ WebSocket だけ ──▶ 127.0.0.1:7778 ゲームサーバー（systemd）
```

- クライアントは、ページを開いたホストと同じ場所へ `wss://` で接続します（Unity 側の設定変更は不要）。
- ゲームサーバーの 7778 番は Caddy からだけ使うので、外には開けません。
- サーバーとクライアントは同じ版を一緒に置いてください。版が違うと「サーバーとゲームのバージョンが違います」と表示され接続できません。

## 用意するもの

| もの | 場所 |
|---|---|
| サーバー本体 | Windows の `Builds/GunjinShogiServer.zip`（リポジトリには含めません。VM へ転送します） |
| WebGL クライアント | リポジトリの `Builds/WebGL/` |
| Caddy の設定 | `Deploy/Caddyfile.gunjin` |
| サーバーの常駐設定 | `Deploy/gunjin-server.service` |
| 更新用スクリプト | `Deploy/update-server.sh`・`Deploy/update-web.sh` |

## 初回の配置

以下は Ubuntu / Debian の例です。`gunjin.example.com` は自分のドメインに置き換えてください（サブドメインを1つ割り当てるのが簡単です。DNS の A レコードを VM に向けておきます）。

### 1. サーバー用のユーザーと必要なパッケージ

```bash
sudo useradd --system --no-create-home --shell /usr/sbin/nologin gunjin
sudo apt-get update && sudo apt-get install -y unzip git
```

### 2. WebGL（リポジトリから必要なフォルダだけ取得）

```bash
sudo mkdir -p /srv && cd /srv
sudo git clone --filter=blob:none --sparse https://github.com/t0y4ma/Gunjin-Shogi.git gunjin-shogi
cd gunjin-shogi
sudo git sparse-checkout set Builds/WebGL Deploy
sudo chmod +x Deploy/*.sh
```

リポジトリが非公開の場合は、GitHub の Deploy key（読み取り専用）か、読み取り権限だけのトークンで clone してください。

### 3. ゲームサーバー

Windows 側で zip を VM に送ります（PowerShell など）。

```bash
scp Builds/GunjinShogiServer.zip ユーザー名@VMのアドレス:/tmp/
```

VM で配置して常駐させます。

```bash
sudo cp /srv/gunjin-shogi/Deploy/gunjin-server.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable gunjin-server
sudo /srv/gunjin-shogi/Deploy/update-server.sh /tmp/GunjinShogiServer.zip
```

ログに `[Gunjin] サーバー起動 ポート 7778` が出れば動いています。

```bash
journalctl -u gunjin-server -f
```

### 4. Caddy

`Deploy/Caddyfile.gunjin` の中身を、既存の Caddyfile（通常 `/etc/caddy/Caddyfile`）に追記し、ドメインを書き換えてから再読み込みします。

```bash
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

### 5. ファイアウォール

80 / 443 だけを開け、7778 は開けません（ufw の例）。

```bash
sudo ufw allow 80,443/tcp
sudo ufw deny 7778/tcp
```

クラウドのセキュリティグループ（VM の外側のファイアウォール）がある場合も同様にします。

### 6. 確認

1. `https://gunjin.example.com` を開き、タイトルが表示される。
2. 「オンライン対戦」を開くと「サーバーに接続しています（gunjin.example.com）」と出る。
3. 2つのブラウザ（片方はシークレットウィンドウなど）で、部屋を作る → 部屋番号で入る → 対局できる。
4. 「招待」で出るリンクを別のブラウザで開くと、その部屋に直接入れる。

## 更新するとき

通信の中身が変わった版では、**サーバーと WebGL を必ず一緒に**更新してください。

```bash
# WebGL（リポジトリに push 済みの最新を取り込む）
sudo /srv/gunjin-shogi/Deploy/update-web.sh
# サーバー（新しい zip を /tmp に送ってから）
sudo /srv/gunjin-shogi/Deploy/update-server.sh /tmp/GunjinShogiServer.zip
```

サーバーを再起動すると、進行中の部屋はすべて消えます（プレイヤーには「部屋がなくなりました」と表示されます）。人の少ない時間に行ってください。

## うまくいかないとき

| 症状 | 確認すること |
|---|---|
| ページは出るが「サーバーにつながっていません」 | `systemctl status gunjin-server`、Caddyfile の `reverse_proxy @ws 127.0.0.1:7778` |
| ページが真っ白・読み込みが止まる | ブラウザの開発者ツールのコンソール。`Builds/WebGL` の中身がそろっているか |
| 「バージョンが違います」 | サーバーと WebGL の片方だけ更新していないか |
| サーバーがすぐ落ちる | `journalctl -u gunjin-server -n 100`。zip の展開先と実行権限（`chmod +x`） |

## 通信量の目安

1手あたり、送信は約 7 バイト、受信は 12〜17 バイト（Mirror と WebSocket の枠組み分は別）です。1局 150 手でも数 KB 程度に収まります。
