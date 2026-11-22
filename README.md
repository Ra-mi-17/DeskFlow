# DeskFlow

Windows のデスクトップ背面で動画をループ再生するアプリ（.NET 8 / WPF + WebView2）。

## publish（単一ファイル）

```powershell
dotnet publish -p:PublishProfile=SingleFile
```

`publish\` に `DeskFlow.exe`（約75MB）と、`dist\` にある利用者向けの `README.txt`・`THIRD-PARTY-NOTICES.txt` が出力されます。
このフォルダーをそのまま zip にして配布できます。.NET ランタイムと UI を同梱した自己完結型なので、
この exe をコピーするだけで他の PC でも動きます（WebView2 ランタイムは Windows 11 に標準搭載）。

## インストーラー

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

`publish\installer\DeskFlow-Setup-<version>.exe` ができます（バージョンは `DeskFlow.csproj` の `<Version>`）。
必要なもの: [Inno Setup 6](https://jrsoftware.org/isinfo.php)（`winget install JRSoftware.InnoSetup`）。定義は `installer\DeskFlow.iss`。
管理者権限なしで `%LOCALAPPDATA%\Programs\DeskFlow` にインストールされ、「設定」→「アプリ」からアンインストールできます。

## 自分の PC へのインストール（開発用）

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install.ps1 -Run
```

上記の publish を行い、`%LOCALAPPDATA%\DeskFlow\DeskFlow.exe` に配置して、デスクトップとスタートメニューに「DeskFlow」ショートカットを作ります。
コードを変更したら同じコマンドで更新できます。

> exe は署名なしのため、スマートアプリコントロールがオンの PC ではブロックされます。
> その場合は `dotnet build` で作った `DeskFlow.dll` を `dotnet DeskFlow.dll` で起動する方法もあります（ただし DLL 自体もブロック対象になることがあります）。

## 使い方

| 操作 | 内容 |
| --- | --- |
| 右クリック | メニュー（動画追加・音量・再生モード・表示・ウィンドウ・壁紙モード・設定） |
| ドラッグ / 端をドラッグ | 移動 / リサイズ（画面端に吸着） |
| ダブルクリック・Space | 再生 / 一時停止 |
| ファイルをドロップ・Ctrl+V | 動画ファイル / URL を追加して再生 |
| トレイアイコン | 左クリックでプレイリスト、右クリックでメニュー |

### 対応ソース
- ローカル動画ファイル（MP4 / WebM 推奨。MKV・MOV はコーデック次第）
- YouTube（通常・Shorts・ライブ、`?t=` の開始位置）、Vimeo、動画への直接リンク
- その他サイトや埋め込み禁止の YouTube 動画は [yt-dlp](https://github.com/yt-dlp/yt-dlp) があれば再生（`yt-dlp.exe` を PATH か `%APPDATA%\DeskFlow` に配置）

### 主な機能
- フレームレス・ボーダーレスのリサイズ可能ウィンドウ（最背面固定 / 通常 / 最前面固定）
- 壁紙モード: デスクトップアイコンの背面に埋め込み（ディスプレイ選択・全画面またぎ対応）
- プレイリスト: 全体ループ / 1本リピート / シャッフル、ドラッグで並べ替え、名前変更、ループ対象のオンオフ、JSON / M3U の読み込みと書き出し
- 音量・ミュート、再生速度、明るさ、ウィンドウ不透明度、表示方法（全面 / 全体 / 引き伸ばし）
- 位置ロック、縦横比固定リサイズ、クリック透過
- 全画面アプリ使用中・バッテリー駆動中・画面ロック中の自動一時停止
- グローバルショートカット（Ctrl+Alt+P 再生/停止、Ctrl+Alt+←/→ 前/次、Ctrl+Alt+↑/↓ 音量、Ctrl+Alt+M ミュート、Ctrl+Alt+L プレイリスト）
- Windows 起動時の自動起動

設定とログ: `%APPDATA%\DeskFlow`（settings.json / deskflow.log）

## 変更を GitHub に反映する

```powershell
git add -A
git commit -m "変更内容のメモ"
git push
```

ビルド成果物（`bin/` `obj/` `publish/`）は `.gitignore` で除外しています。

## ライセンス

再配布・改変は禁止です。条件は [dist/LICENSE.txt](dist/LICENSE.txt) を参照してください。

## 構成
- `src/` … C#（`Controller.cs` が状態管理とメニュー、`Native.cs` が Win32 連携と壁紙埋め込み、`LocalServer.cs` が UI と動画を配信するローカル HTTP サーバー）
- `wwwroot/` … プレーヤー・プレイリスト画面（HTML / JS）
- `scripts/` … アイコン生成・インストール
