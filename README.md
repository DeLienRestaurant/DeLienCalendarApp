# DeLien Calendar for Windows

calendar.delien.xyz を WebView2 でホストする常駐型デスクトップアプリです。
ブラウザ版と同じ画面・機能をそのまま利用でき、Windows ネイティブの通知／アラームを追加しています。

## 特徴

- **ブラウザ版と同一の機能**（WebView2 でサイトをそのまま表示）
- **タスクトレイ常駐** + Windows サインイン時の自動起動
- **デスクトップ通知 / アラーム**: 予定の開始 N 分前にトースト通知（既定 15 分前）
- **通知スタイルを選択**: トースト（標準音）/ アラーム（ループ音）/ サイレント。通知音も 6 種類から選択
- **アプリ未起動時でも鳴る**: タスクスケジューラに登録するため、アプリを終了していてもアラームが発火します
- **未起動中でも予定変更に追従**: 15 分ごとのヘッドレス同期（`--sync`）でアラーム時刻を自動更新
- **端末単位の通知設定**: 対象カレンダー・リマインド時刻・本文の内容を PC ごとに設定
- **閲覧のみの権限**: 端末トークンは `calendars:read` のみ。予定の改変はできません

## 動作要件

| 項目 | 要件 |
|---|---|
| OS | Windows 10 1809 以降 / Windows 11 |
| ランタイム | .NET 8 デスクトップランタイム |
| WebView2 | Microsoft Edge WebView2 ランタイム（Win11 は標準搭載） |

## ビルド

```powershell
# 配布用（framework-dependent / win-x64）
.\desktop\build.ps1
# 出力: desktop\dist\DeLienCalendar-win-x64.zip
```

手動でビルドする場合:

```powershell
dotnet publish desktop\src\DeLienCalendar.Desktop\DeLienCalendar.Desktop.csproj `
  -c Release -r win-x64 --self-contained false -o desktop\dist\app
```

### アイコン

アプリアイコン（`src/DeLienCalendar.Desktop/Resources/app.ico`）は Web 版のロゴから生成しています。
差し替える場合は元画像を更新して次を実行してください（16〜256px の 7 サイズを生成）。

```powershell
powershell -ExecutionPolicy Bypass -File .\desktop\tools\make_icon.ps1
```

## リリース

- バージョンは `src/DeLienCalendar.Desktop/DeLienCalendar.Desktop.csproj` の `<Version>` で管理します。
- 配布物のビルドと GitHub Release の作成は、アプリ用リポジトリ（`DeLienCalendarApp`）側の
  `release.yml` がタグ push を契機に実行します（このリポジトリには置いていません）。
- アプリ内のバージョンは設定画面の下部に表示されます。

## インストール（未署名配布）

コード署名証明書を使用しないため、MSIX ではなく **ZIP + インストールスクリプト**で配布します。
SmartScreen の警告（「詳細情報」→「実行」）が表示される場合があります。

```powershell
# 展開後、フォルダ内で実行（管理者権限不要）
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

インストール先は `%LOCALAPPDATA%\Programs\DeLienCalendar`、スタートメニューにショートカットを作成します。
アンインストールはスタートメニューの「DeLien Calendar」を削除し、上記フォルダと `%LOCALAPPDATA%\DeLienCalendar` を削除してください。

## 仕組み

### ログイン（ブラウザ経由ループバック + PKCE）

```
アプリ: 127.0.0.1:{任意ポート} で一時 HTTP サーバを起動
   ↓ 既定ブラウザで /desktop/login.php を開く
ブラウザ: Discord でログイン（既存フロー）→「この端末を許可する」
   ↓ ワンタイムコードを 127.0.0.1 へ返す
アプリ: POST /api/v1/auth/desktop/token（PKCE 検証）→ 端末トークン取得
   ↓
アプリ: /desktop/session.php?code=... を WebView2 で開き、
        WebView2 側にもログインセッションを確立する
```

- 認証コードは 5 分有効・1 回限り・SHA-256 でハッシュ保存
- 端末トークンは Windows DPAPI（現在ユーザー）で暗号化して保存

### 通知・アラーム

1. 起動時に `GET /api/v1/me` で端末トークンを検証
2. 10 分ごとに `GET /api/v1/me/calendars` と `GET /api/v1/me/events`（14 日分）を取得
3. リマインド時刻を計算し、タスクスケジューラへ 1 回実行のタスクを登録
   - タスク名: `DeLienCalendar-{予定ID}-{yyyyMMddHHmm}`
   - 動作: `DeLienCalendar.exe --notify {予定ID}`
4. 時刻になると Windows がアプリを起動し、トースト（アラーム設定時はループ音）を表示
   - アプリ本体が起動中でも `--notify` は単独で動作します（二重起動にはなりません）

終日予定は時刻を持たないため、当日 09:00 に通知します。

### 未起動中の予定変更への追従

アプリを閉じていても予定の変更に追従できるよう、**15 分間隔のヘッドレス同期**を登録します。

| タスク | 動作 |
|---|---|
| `DeLienCalendar-Sync` | 15 分ごとに `DeLienCalendar.exe --sync` を実行（非表示タスク） |
| `--sync` | トークンを検証 → 予定を取得 → アラームを登録し直して終了（UI なし） |

- 常駐アプリが動作中の場合は `--sync` は何もせず終了します（アプリ側が 10 分ごとに同期するため）
- 常駐アプリと `--sync` は名前付きセマフォで排他制御し、`cache.json` の同時書き込みを防ぎます
- 予定が変わると古いアラームタスクは削除され、新しい時刻で登録し直されます
- 反映までの遅延は**最大 15 分**です

### 通知スタイル

| スタイル | 挙動 |
|---|---|
| トースト（既定） | 標準の通知。通知音を「既定 / リマインダー / アラーム / アラーム2 / メール / メッセージ」から選択 |
| アラーム | `scenario="alarm"` + ループ音。停止するまで鳴り続けます（集中モードを回避） |
| サイレント | 音なしで通知のみ |

本文の内容も「カレンダー名 / 予定の説明 / アクションボタン（開く・スヌーズ）」を個別に ON/OFF できます。

### データの保存場所

`%LOCALAPPDATA%\DeLienCalendar\`

| ファイル | 内容 |
|---|---|
| `settings.json` | 通知設定・自動起動など |
| `token.dat` | 端末トークン（DPAPI 暗号化） |
| `cache.json` | 直近14日分の予定キャッシュ |
| `app.log` | 動作ログ（トラブルシューティング用） |
| `WebView2\` | WebView2 のユーザーデータ |

## 制約

- **コード署名なし**: SmartScreen の警告が出ます。署名証明書を用意できれば MSIX + 自動更新へ移行できます。
- **予定変更の反映遅延**: アプリ未起動中は 15 分間隔の同期のため、変更が反映されるまで最大 15 分かかります。
- **スリープ中**: スリープから復帰したタイミングで発火します（Windows の制約）。
- **集中モード / 通知のオフ**: OS の通知設定で本アプリの通知を許可してください。

## トラブルシューティング

| 症状 | 確認箇所 |
|---|---|
| 通知が鳴らない | `app.log` に「アラームを N 件登録しました」があるか。`schtasks /Query /FO LIST \| findstr DeLienCalendar` でタスクを確認 |
| 予定の変更が反映されない | `schtasks /Query /TN DeLienCalendar-Sync` が存在するか。`DeLienCalendar.exe --sync` を手動実行して `app.log` を確認 |
| 「WebView2 ランタイムが見つかりません」 | [WebView2 ランタイム](https://developer.microsoft.com/microsoft-edge/webview2/)をインストール |
| 連携が解除された | Web の「API トークン」画面で端末が「解除済み」になっていないか確認し、再ログイン |
| 予定が取得できない | 設定画面の「今すぐ同期」を実行し、`app.log` を確認 |
