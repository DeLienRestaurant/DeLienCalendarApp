# DeLien Calendar for Windows

calendar.delien.xyz を WebView2 でホストする常駐型デスクトップアプリです。
ブラウザ版と同じ画面・機能をそのまま利用でき、Windows ネイティブの通知／アラームを追加しています。

## 特徴

- **ブラウザ版と同一の機能**（WebView2 でサイトをそのまま表示）
- **アプリ内タブ**: 「カレンダー」と「設定」をタブで切り替え。設定はアプリを閉じずに変更できます（トレイメニューの「設定…」からも開けます）
- **設定はジャンル別**: 「一般 / 通知 / アラーム音 / カレンダー / バージョン情報」に分割
- **バージョン情報**: 現在のバージョン、更新確認、過去バージョンの更新履歴を表示
- **タスクトレイ常駐** + Windows サインイン時の自動起動
- **デスクトップ通知 / アラーム**: 予定の開始 N 分前にトースト通知（既定 15 分前）
- **通知スタイルを選択**: トースト（標準音）/ アラーム（ループ音）/ サイレント
- **音声ファイルを自由に選択**: `%WINDIR%\Media` のプリセットのほか、任意の WAV / MP3 / M4A などを指定可能
- **再生デバイスを指定**: Windows の既定、または任意の出力デバイス（スピーカー／ヘッドホン／仮想デバイス）を選択
- **アプリ未起動時でも鳴る**: タスクスケジューラに登録するため、アプリを終了していてもアラームが発火します
- **未起動中でも予定変更に追従**: 15 分ごとのヘッドレス同期（`--sync`）でアラーム時刻を自動更新
- **更新確認**: 起動時に新しいバージョンを確認し、タスクトレイで通知
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
# ZIP（ポータブル版）のみ
.\desktop\build.ps1

# ZIP + インストーラ
.\desktop\build.ps1 -Version 0.1.0 -WithInstaller
```

出力物:

| ファイル | 内容 |
|---|---|
| `desktop\dist\DeLienCalendar-win-x64.zip` | ポータブル版 |
| `desktop\dist\DeLienCalendar-<version>-setup.exe` | インストーラ（`-WithInstaller` 時） |

インストーラの作成には [Inno Setup 6](https://jrsoftware.org/isdl.php) が必要です。
`build.ps1` は次の場所から `ISCC.exe` を自動的に探します。

- `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`
- `%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe`
- `%ProgramFiles%\Inno Setup 6\ISCC.exe`

手動でインストーラだけ作る場合:

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" `
  /DAppVersion=0.1.0 .\desktop\installer\setup.iss
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
- アプリ内のバージョンは「設定」タブの下部に表示されます。

## インストール

### インストーラ（推奨）

配布物の `DeLienCalendar-<version>-setup.exe` を実行し、ウィザードに従ってください。

- **インストール先は自由に指定できます**（既定: `%LOCALAPPDATA%\Programs\DeLienCalendar`）
- 管理者権限は不要です。`C:\Program Files` などへ入れる場合は、インストーラを「管理者として実行」してください
- スタートメニューにショートカットを作成（デスクトップへの作成は任意）
- アンインストールは「設定 > アプリ」または スタートメニューの「DeLien Calendar のアンインストール」から

コマンドラインでのインストール（サイレント・インストール先指定）:

```powershell
.\DeLienCalendar-0.1.0-setup.exe /VERYSILENT /DIR="D:\Apps\DeLienCalendar"
```

### ポータブル版（ZIP）

`DeLienCalendar-win-x64.zip` を展開すると、そのまま `DeLienCalendar.exe` を実行できます。
スタートメニュー登録やアンインストール情報が不要な場合はこちらを使ってください。

スタートメニュー登録と自動起動を行いたい場合は、展開したフォルダ内で `install.ps1` を実行します
（既定のインストール先は `%LOCALAPPDATA%\Programs\DeLienCalendar`）。

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1

# インストール先を指定する場合
powershell -ExecutionPolicy Bypass -File .\install.ps1 -Destination "D:\Apps\DeLienCalendar"
```

> このビルドはコード署名を行っていないため、SmartScreen の警告が表示される場合があります。

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
| トースト（既定） | 通知を表示し、選んだ音声を1回再生 |
| アラーム | 通知に加えて**アラーム画面**（停止／スヌーズ）を表示し、音声を停止するまでループ再生 |
| サイレント | 通知のみ（音なし） |

音声は Windows のトースト機能ではなく**アプリが再生**します。
トーストでは任意の音声ファイルや出力デバイスを指定できないため、トースト自体は常に無音です。

本文の内容も「カレンダー名 / 予定の説明 / アクションボタン（開く・スヌーズ）」を個別に ON/OFF できます。

### 音声と再生デバイス

設定タブの「アラーム音」で指定します。

| 項目 | 内容 |
|---|---|
| 音声 | `%WINDIR%\Media` のプリセット（アラーム 1〜5、呼び出し 1〜3、チャイム、通知音など）／「カスタムファイル…」で任意のファイル（WAV / MP3 / M4A / AAC / WMA / FLAC） |
| 再生デバイス | Windows の既定デバイス、または列挙された出力デバイス（スピーカー・ヘッドホン・仮想オーディオ等） |
| 試聴 | 選択した音声をその場で再生（最大10秒） |

再生には WASAPI（共有モード）を使用します。指定したデバイスが見つからない場合は既定デバイスで再生します。

### 更新の確認（設定 > バージョン情報）

「バージョン情報」ページに、現在のバージョン・更新確認・更新履歴をまとめています。

- 起動時に **24 時間に 1 回**、GitHub Releases（`DeLienRestaurant/DeLienCalendarApp`）を確認します
- 新しいバージョンがあればタスクトレイで通知し、「リリースページを開く」ボタンを表示します
- 手動確認: バージョン情報ページの「更新を確認」／タスクトレイメニューの「更新を確認」
- 未署名配布のため**自動インストールは行いません**。案内に従ってインストーラを実行してください
- 確認の自動実行はバージョン情報ページで OFF にできます

#### 更新履歴

`CHANGELOG.md` の内容がバージョン情報ページに表示されます（アプリと同じフォルダに同梱）。
書式は次のとおりで、上から新しい順に並べます。

```markdown
## v0.3.0 (2026-10-25)
- 設定画面をジャンル別に分割
- バージョン情報ページに更新履歴を表示
```

現在使用中のバージョンには「使用中」のラベルが付きます。
リリース時は `desktop/CHANGELOG.md` に項目を追記してください。

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
- **アンインストール時**: タスクスケジューラの登録（アラーム・定期同期）と自動起動は
  `DeLienCalendar.exe --uninstall-cleanup` により自動で解除されます。
  設定・キャッシュ（`%LOCALAPPDATA%\DeLienCalendar`）は残るため、不要なら手動で削除してください。
- **予定変更の反映遅延**: アプリ未起動中は 15 分間隔の同期のため、変更が反映されるまで最大 15 分かかります。
- **スリープ中**: スリープから復帰したタイミングで発火します（Windows の制約）。
- **集中モード / 通知のオフ**: OS の通知設定で本アプリの通知を許可してください。

## トラブルシューティング

| 症状 | 確認箇所 |
|---|---|
| 通知が鳴らない | `app.log` に「アラームを N 件登録しました」があるか。`schtasks /Query /FO LIST \| findstr DeLienCalendar` でタスクを確認 |
| 音が出ない | 設定タブの「アラーム音」で音声と再生デバイスを確認し「試聴」を実行。通知スタイルが「サイレント」になっていないか確認 |
| 更新確認が失敗する | 社内プロキシ等で `api.github.com` に接続できるか確認（失敗しても通知には影響しません） |
| 予定の変更が反映されない | `schtasks /Query /TN DeLienCalendar-Sync` が存在するか。`DeLienCalendar.exe --sync` を手動実行して `app.log` を確認 |
| 「WebView2 ランタイムが見つかりません」 | [WebView2 ランタイム](https://developer.microsoft.com/microsoft-edge/webview2/)をインストール |
| 連携が解除された | Web の「API トークン」画面で端末が「解除済み」になっていないか確認し、再ログイン |
| 予定が取得できない | 「設定」タブの「今すぐ同期」を実行し、`app.log` を確認 |
| 接続先を変更したい | 「設定」タブの「接続先」を書き換えて保存 → アプリを再起動（再ログインが必要） |
