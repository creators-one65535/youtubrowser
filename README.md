# Youtubrowser

Windows専用のネイティブブラウザアプリです。起動時にYouTube公式サイト（https://www.youtube.com/）を開きますが、
URL欄から任意のサイトへ自由に移動できる汎用ブラウザとして動作します（ドメイン制限なし）。
C# / .NET 8 / WinUI 3（Windows App SDK） / Microsoft WebView2（Chromiumベース）で実装しています。
YouTubeの画面を模倣・スクレイピング・改変することはありません。表示は各サイトをそのままWebView2で描画しています。

## 機能

- 起動時に https://www.youtube.com/ を開く
- 複数タブに対応。タブごとに独立したナビゲーション履歴を持ち、Cookie・ログイン状態は全タブで共有される（同じWebView2プロファイルを使うため）。タブ表示領域は横スクロール可能で、タブが増えてあふれた場合は両脇の「<」「>」ボタンでスクロールできる
- 「+」ボタンで新しいタブを開く、または最後のタブを閉じると、直近の表示履歴(タイトル・URL、最大20件)を一覧表示するページが開く。一覧のリンクをクリックするとそのページへ移動する。履歴は `%LOCALAPPDATA%\Youtubrowser\history.json` に保存され、次回起動時にも引き継がれる（最大50件保持）
- 各タブヘッダの「→」ボタンでそのタブを右ペインに固定表示し、左右で別々のページを同時に閲覧できる（左右分割表示）。「←」で元に戻る
- 音声を再生中、またはミュート中のタブにはヘッダにスピーカーアイコンが表示され、クリックでそのタブだけミュート/解除できる
- ページ内でtarget="_blank"やwindow.open()によって新しいウィンドウが要求された場合、新しいタブとして開く
- リンクを右クリックした際の既定のコンテキストメニューに「新しいタブで開く」を追加
- URL欄はGoogle検索窓を兼ねるオムニボックス。URLとして解釈できる入力は現在のタブでそのページへ移動し、それ以外は検索語句として新しいタブでGoogle検索結果を開きアクティブにする。サイト間の遷移に制限は無い
- ウィンドウサイズは既定で1280×400。サイズ・位置・最大化状態は次回起動時に復元
- OS標準のタイトルバーは完全に非表示。ツールバー（戻る・進む・再読み込み・ホーム・URL欄・タブバー・拡張機能追加(ADD)ボタン／拡張機能ストア(Store)ボタン・最小化／最大化⇔元に戻す／閉じるボタン）は通常は非表示で、マウスカーソルをウィンドウ上のどこかに乗せて少し待つとHUDのように表示される（ウィンドウが非アクティブでもカーソルさえ乗っていれば表示される。他のウィンドウに隠れている場合は表示されない）。カーソルがウィンドウから離れて一定時間経つと自動的に隠れる
- ツールバーのボタンが無い空き領域をドラッグするとウィンドウを移動できる（タイトルバーの代わり）
- Chromiumベース（WebView2）のため、アンパック済み（unpacked）のローカル拡張機能フォルダを「ADD」ボタンから読み込み可能。「Store」ボタンはChromeウェブストアを新しいタブで開く
- 「Manage」ボタンで追加済み拡張機能の一覧ページを新しいタブで開く。各拡張機能の有効/無効状態を確認でき、`manifest.json`の`options_page`/`options_ui.page`から検出した設定ページへのリンクをクリックして開ける（WebView2では`edge://extensions/`は開けないため独自実装）
- WebView2のユーザーデータ（Cookie・ログイン状態等）は `%LOCALAPPDATA%\Youtubrowser\WebView2UserData` というアプリ専用フォルダに保存。WebView2標準の仕組みに任せており、アプリ独自にパスワードやCookieを収集・表示・送信することはない
- 最後のタブを閉じてもアプリは終了せず、表示履歴ページのタブが自動的に開く

## 作らない機能（意図的に対象外）

- 動画ダウンロード
- 広告ブロック・広告の自動スキップ
- バックグラウンド再生など、YouTubeの通常動作を回避する機能
- YouTubeの非公式API利用や画面スクレイピング
- Chromeウェブストアからの拡張機能インストール（後述の制約により非対応）
- タブ状態（開いているタブ一覧・各タブのURL）の永続化（次回起動時は常にYouTubeホームのタブ1本から開始する。表示履歴は永続化される）

## WPF版からWinUI 3への移行について

当初はWPF + WebView2で実装していましたが、実機検証で**WebView2はネイティブHWND（airspace違反コントロール）としてレンダリングされるため、XAML上の重なり順に関係なく常に最前面に来る**という致命的な問題が判明しました。具体的には、ホバーツールバー（検索・戻る/進む・拡張機能管理など全操作）の領域にマウスを置いても、`WindowFromPoint`で調べると最前面のHWNDはWebView2の子プロセス（別プロセス）のものであり、WPF側のツールバーは一切マウス・キーボード入力を受け取れませんでした。

WinUI 3（Windows App SDK）はDirectComposition方式の「穴あけ合成」モデルを採用しており、XAML要素をWebView2の手前に重ねた状態でも入力が正しく自分のプロセスに渡ることを最小限のPOCで実証した上で、全面移行しました。あわせて、タイトルバーを完全に隠し、最小化・最大化・閉じるボタンをホバーツールバーに統合し、ツールバーの空き領域でウィンドウをドラッグ移動できるようにしています。

## 動作要件

- Windows 10 (1809/10.0.17763) 以降、または Windows 11
- WebView2 Runtime（多くのWindows環境にEdgeとともに標準導入済み。未導入の場合は https://developer.microsoft.com/microsoft-edge/webview2/ から入手）
- Windows App SDKランタイムは別途インストール不要（`WindowsAppSDKSelfContained=true`設定により、ビルド成果物に同梱されています。代わりに配布サイズは大きくなります）

ビルドには .NET SDK が必要です（後述）。

## ビルド方法

```
dotnet build Youtubrowser.csproj
```

`net8.0-windows10.0.19041.0` をターゲットにしています。Visual Studio・WinUI 3関連のワークロードのインストールは不要で、`dotnet build`（NuGet経由でMicrosoft.WindowsAppSDK / Microsoft.Windows.SDK.BuildToolsを取得）だけでビルドできます。

## 実行方法

```
dotnet run --project Youtubrowser.csproj
```

またはビルド後に `bin\Debug\net8.0-windows10.0.19041.0\win-x64\Youtubrowser.exe` を直接起動してください。

## 拡張機能の追加方法

WebView2が公式にサポートしているのは「アンパック済み（unpacked）のローカル拡張機能フォルダの読み込み」のみです。Chromeウェブストアからの直接インストール（ワンクリックインストール）には対応していません。

1. Chromeウェブストア等から拡張機能の `.crx`/ソースを入手し、ローカルフォルダに展開（`manifest.json` を含むフォルダにする）
2. アプリのツールバー（マウスを上端に近づけて表示）から「ADD」をクリック
3. 展開済みフォルダを選択

### 制約の調査結果

- `CoreWebView2Profile.AddBrowserExtensionAsync` はローカルのアンパック済み拡張機能専用で、Chromeウェブストアからの直接インストールには非対応（[Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2profile.addbrowserextensionasync)）
- `ExtensionInstallForcelist` ポリシーでウェブストア拡張を強制インストールする方法もあるが、Active Directory参加・Azure AD参加・Chrome Enterprise Core登録済みの端末でないと機能しないため、一般配布の個人向けアプリには不向き（[Chrome Enterprise](https://chromeenterprise.google/policies/extension-install-forcelist/)）

## 確認済みの動作

実機検証（PowerShell + Win32 APIによるカーソル操作・クリック・スクリーンショット）で以下を確認済み。

- ビルド成功（0警告・0エラー）
- WinUI 3 + WebView2の最小POCで、WebView2の手前に重ねたXAMLボタンが実際にクリックで反応すること（WPF版で見つかったairspace問題が解消されていること）
- ホバーツールバーがウィンドウ内のどこにマウスを置いても3秒後に表示され、ウィンドウが非アクティブ（フォーカスなし）でも表示されること。カーソルをウィンドウ外に出すと一定時間後に自動的に隠れること（`GetCursorPos` + `WindowFromPoint` によるポーリングで実機確認済み）
- URL欄にアドレスを直接入力し「移動」ボタンで該当ページへ正しく遷移すること
- 最小化・最大化・閉じるボタンがそれぞれ正しく動作すること（`OverlappedPresenter`経由）
- マルチモニタ環境のクランプ処理（`ApplyBoundsWithScreenClamp`）が、画面外座標・最小サイズ未満のどちらでも正しく機能すること。高DPI環境（150%スケール確認済み）でも`settings.json`の論理(DIP)サイズが物理ピクセルへ正しく変換され、ツールバーのボタンがウィンドウ幅に収まること

### 実装上の注意点（実機検証で判明した制約）

- `OverlappedPresenter.SetBorderAndTitleBar(true, false)` でタイトルバーを消すと、WebView2に重ねたXAML要素が一切描画されなくなる不具合がある。必ず `Window.ExtendsContentIntoTitleBar = true` + `AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed` の組み合わせを使うこと
- `ExtendsContentIntoTitleBar = true` にすると、既定でウィンドウ上端の帯全体がシステムタイトルバーと同じ「ドラッグ領域」として扱われ、その範囲のXAML要素は`PointerEntered`等を受け取れなくなる。`InputNonClientPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, ...)` で表示中のツールバーのボタン群を明示的に「素通し」指定する必要がある
- Toolbar要素をXAML上 `Opacity="0"` で初期化すると、WebView2に重なる構成では後から`Opacity`を1に戻しても二度と描画されない（`Visibility`の切り替えでも同様の問題を確認）。表示/非表示は`RenderTransform`（`TranslateTransform`）でウィンドウ外に移動させる方式にしている
- `AppWindow`は物理ピクセル基準。`settings.json`の値は論理(DIP)サイズとして扱い、適用時に`GetDpiForWindow`で取得したスケールを掛けて物理ピクセルに変換している（高DPI環境でこれを怠るとツールバーの右側ボタン群がウィンドウ幅をはみ出す）
- ホバーツールバーの表示トリガーをウィンドウ全体に広げるにあたり、当初はウィンドウ全面を覆う透明なXAML要素で`PointerEntered`を拾う案を検討したが、WebView2は別HWNDで描画されるため、その透明要素がヒットテスト対象である限りWebView2へのすべてのクリックをブロックしてしまう（動画操作が一切できなくなる）ことが判明した。そのため、XAMLのポインターイベントには頼らず`GetCursorPos`で定期的にカーソル座標を取得し、`WindowFromPoint` + `GetAncestor(GA_ROOT)`でその地点に実際に見えている最前面ウィンドウが自分自身かどうかを判定する方式にした。これによりウィンドウ非アクティブ時でもカーソルが乗っていればHUDが表示され、他ウィンドウに隠れている場合は表示されない

## 未確認事項

- ホバーツールバーの空き領域（ドラッグ可能領域、`InputNonClientPointerSource`の`Caption`指定）をドラッグしてウィンドウを移動できるか。自動テストでは合成入力によるモーダルドラッグループの再現ができず未検証（実装自体は公式ドキュメント記載の標準的なAPI使用法）
- 拡張機能フォルダを実際に読み込ませた際の動作（テスト用のアンパック拡張機能で未検証）
- タブを多数開いた場合のタブバーのレイアウト（横スクロール等は未実装のため、ウィンドウ幅を超えるタブは見切れる）
