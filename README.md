# Youtubrowser

Youtubrowser は、Windows 専用のネイティブブラウザアプリです。

起動時は YouTube 公式サイト（https://www.youtube.com/）を開きますが、URL 欄から任意のサイトへ移動できる汎用ブラウザとして動作します。YouTube の画面を模倣、スクレイピング、改変するものではなく、各サイトを Microsoft Edge WebView2 でそのまま表示します。

実装は C# / .NET 8 / WinUI 3 / Windows App SDK / Microsoft Edge WebView2 です。

## 何ができるか

- 起動時に YouTube を開く
- URL 入力または Google 検索として使えるオムニボックス
- 複数タブ表示
- タブの横スクロール
- 最後のタブを閉じたときの履歴一覧表示
- 最近開いたページの履歴保存
- タブごとのミュート切り替え
- 左右分割表示
- `target="_blank"` や `window.open()` を新しいタブとして開く
- リンク右クリックメニューから「新しいタブで開く」
- ウィンドウ位置、サイズ、最大化状態の保存
- 上端にマウスを乗せると表示される HUD 風ツールバー
- タイトルバーなしの独自ウィンドウ操作
- アンパック済みローカル拡張機能フォルダの読み込み
- 追加済み拡張機能の管理ページ表示
- WebView2 の Cookie、ログイン状態、ユーザーデータ保存

詳しい操作方法は [USAGE.md](USAGE.md) を参照してください。

## 意図的に作らない機能

- 動画ダウンロード
- 広告ブロック、広告の自動スキップ
- バックグラウンド再生など、YouTube の通常動作を回避する機能
- YouTube の非公式 API 利用
- YouTube 画面のスクレイピング
- Chrome ウェブストアからのワンクリック拡張機能インストール
- 起動時に前回開いていたタブ一覧を復元する機能

## コンパイルに必要なもの

### 必須環境

- Windows 10 1809（10.0.17763）以降、または Windows 11
- x64 環境
- .NET 8 SDK
- NuGet パッケージを復元できるインターネット接続

### プロジェクト設定

- Target Framework: `net8.0-windows10.0.19041.0`
- Runtime Identifier: `win-x64`
- Self-contained: `true`
- Windows App SDK self-contained: `true`

### NuGet 依存関係

直接参照している主なパッケージは次の通りです。

- `Microsoft.WindowsAppSDK` 2.2.0
- `Microsoft.Windows.SDK.BuildTools` 10.0.26100.8249

推移依存として `Microsoft.Web.WebView2`、`.NET` 関連パッケージ、Windows App SDK 関連パッケージも使用します。詳細は [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) を参照してください。

Visual Studio の WinUI 3 ワークロードは必須ではありません。通常は .NET SDK と NuGet 復元だけでビルドできます。

## ビルド方法

このディレクトリで実行します。

```powershell
dotnet build Youtubrowser.csproj
```

または、親ディレクトリから実行する場合:

```powershell
dotnet build Youtubrowser\Youtubrowser.csproj
```

## 実行方法

```powershell
dotnet run --project Youtubrowser.csproj
```

ビルド後の実行ファイルは通常、次の場所に生成されます。

```text
bin\Debug\net8.0-windows10.0.19041.0\win-x64\Youtubrowser.exe
```

## 実行に必要なもの

- Windows 10 1809 以降、または Windows 11
- Microsoft Edge WebView2 Runtime

WebView2 Runtime は多くの Windows 環境に Microsoft Edge とともに導入済みです。未導入の場合は Microsoft 公式サイトから入手してください。

https://developer.microsoft.com/microsoft-edge/webview2/

Windows App SDK ランタイムは、`WindowsAppSDKSelfContained=true` によりビルド成果物へ同梱される想定です。

## 保存されるデータ

ユーザーごとのローカルデータは主に次の場所に保存されます。

```text
%LOCALAPPDATA%\Youtubrowser
```

保存対象の例:

- 設定ファイル
- 表示履歴
- WebView2 ユーザーデータ
- Cookie やログイン状態

アプリ独自にパスワードや Cookie を収集、表示、送信する処理はありません。WebView2 標準のユーザーデータ保存を使用します。

## ライセンス情報

Youtubrowser はオープンソースソフトウェア（OSS）ではありません。

本ソフトウェア独自のソースコード、ドキュメント、成果物には [LICENSE.txt](LICENSE.txt) が適用されます。主な条件は次の通りです。

- 改変禁止
- 無断再配布禁止
- 商用利用は事前相談と明示的な許可が必要
- 個人かつ非営利目的での閲覧、実行、テストは許可

外部ライブラリ、SDK、ランタイム、第三者コンポーネントには、それぞれの権利者のライセンスが適用されます。第三者コンポーネントの通知、再配布条件、免責事項は [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) を参照してください。

`LICENSE.txt` と第三者コンポーネントのライセンスが矛盾する場合、その第三者コンポーネントについては当該第三者ライセンスが優先されます。

商用利用、改変、組み込みなどの相談先:

```text
emotion65535@gmail.com
```
