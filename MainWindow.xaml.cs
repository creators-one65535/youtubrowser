using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using Youtubrowser.Browser;
using Youtubrowser.Settings;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Rect = Windows.Foundation.Rect;

namespace Youtubrowser;

public sealed partial class MainWindow : Window
{
    private const string DefaultHomeUrl = "https://www.youtube.com/";
    private const int MinWindowWidth = 320;
    private const int MinWindowHeight = 200;
    private const int HistoryDisplayLimit = 20;
    private const int HistoryStoreLimit = 50;

    private static readonly SolidColorBrush InactiveTabBrush = new(Windows.UI.Color.FromArgb(0xFF, 0x3A, 0x3A, 0x3A));
    private static readonly SolidColorBrush ActiveTabBrush = new(Windows.UI.Color.FromArgb(0xFF, 0x55, 0x55, 0x55));

    private static readonly TimeSpan ToolbarHideDelay = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan HoverPollInterval = TimeSpan.FromMilliseconds(150);

    private readonly AppSettings _settings;
    private readonly OverlappedPresenter _presenter;
    private readonly DispatcherQueueTimer _hideDelayTimer;
    private readonly DispatcherQueueTimer _hoverPollTimer;
    private readonly IntPtr _hwnd;

    private bool _toolbarVisible;
    private bool _isCursorOverToolbarStrip;
    private bool _toolbarHiddenUntilCursorLeavesStrip;
    private PointInt32 _restoredPosition;
    private SizeInt32 _restoredSize;
    private CoreWebView2Environment? _environment;
    private readonly List<BrowserTab> _tabs = new();
    private readonly List<HistoryEntry> _history;
    private BrowserTab? _activeTab;
    private BrowserTab? _rightPaneTab;

    private WebView2? ActiveWebView => _activeTab?.WebView;

    public MainWindow()
    {
        InitializeComponent();

        _settings = SettingsService.Load();
        _history = HistoryService.Load();
        _presenter = (OverlappedPresenter)AppWindow.Presenter;
        _hwnd = WindowNative.GetWindowHandle(this);

        // タイトルバーを完全に非表示にする。
        // 注意: OverlappedPresenter.SetBorderAndTitleBar(true, false) でも見た目は同じことができるが、
        // そちらを使うとWebView2に重ねたXAML要素(ホバーツールバー等)が描画されなくなる不具合を実機で確認したため、
        // 必ず ExtendsContentIntoTitleBar + PreferredHeightOption.Collapsed の組み合わせを使うこと。
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Collapsed;

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "app.ico"));

        ApplyBoundsWithScreenClamp();
        ClearDragRegion();
        UpdateHomeButtonToolTip();

        AppWindow.Changed += AppWindow_Changed;
        Closed += MainWindow_Closed;

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _hideDelayTimer = dispatcherQueue.CreateTimer();
        _hideDelayTimer.Interval = ToolbarHideDelay;
        _hideDelayTimer.Tick += (_, _) =>
        {
            _hideDelayTimer.Stop();
            HideToolbar(waitForCursorLeave: true);
        };

        // ツールバー(HUD)の表示トリガーはツールバー帯の範囲を対象にしたいが、WebView2は別HWNDで
        // レンダリングされるため、その範囲を覆う透明なXAML要素でPointerEnteredを
        // 取ろうとするとWebView2へのクリックがすべてブロックされてしまう(動画操作不能になる)。
        // そのため、XAMLのヒットテストには頼らずGetCursorPosで定期的にカーソル座標を
        // 取得し、ツールバー帯の範囲内かどうかを判定する方式にしている。
        _hoverPollTimer = dispatcherQueue.CreateTimer();
        _hoverPollTimer.Interval = HoverPollInterval;
        _hoverPollTimer.Tick += (_, _) => PollCursorPosition();
        _hoverPollTimer.Start();
    }

    // ツールバー(HUD)帯の判定に使う高さの既定値。Toolbar.ActualHeightが取得できるまでの
    // フォールバックとして使う(初回レイアウト前は0になるため)。
    private const double FallbackToolbarStripHeightDip = 40;

    private void PollCursorPosition()
    {
        if (!GetCursorPos(out var pt))
        {
            UpdateCursorOverToolbarStrip(false);
            return;
        }

        var bounds = AppWindow.Position;
        var size = AppWindow.Size;
        double scale = GetDpiForWindow(_hwnd) / 96.0;
        double stripHeightDip = Toolbar.ActualHeight > 0 ? Toolbar.ActualHeight : FallbackToolbarStripHeightDip;
        int stripHeightPx = (int)Math.Round(stripHeightDip * scale);

        // ウィンドウ全体ではなく、ツールバー(HUD)が表示される上端の帯の範囲だけを判定対象にする。
        // 動画再生エリアなどにカーソルがあるときはHUDの表示状態に影響しないようにするため。
        bool withinBounds = pt.X >= bounds.X && pt.X < bounds.X + size.Width
            && pt.Y >= bounds.Y && pt.Y < bounds.Y + stripHeightPx;

        // 座標がツールバー帯に入っているだけでなく、その地点で実際に一番手前に
        // 見えているのが自分のウィンドウ(またはWebView2等の子要素)であることも確認する。
        // これにより、他のウィンドウが重なって自分のウィンドウを隠している場合に
        // 誤ってHUDが表示されるのを防ぐ。フォーカス(アクティブ状態)は問わない。
        bool isOver = withinBounds && GetAncestor(WindowFromPoint(pt), GA_ROOT) == _hwnd;

        UpdateCursorOverToolbarStrip(isOver);
    }

    private void UpdateCursorOverToolbarStrip(bool isOver)
    {
        if (isOver == _isCursorOverToolbarStrip) return;
        _isCursorOverToolbarStrip = isOver;

        if (isOver)
        {
            _hideDelayTimer.Stop();
            if (!_toolbarVisible && !_toolbarHiddenUntilCursorLeavesStrip)
            {
                ShowToolbar();
            }
        }
        else
        {
            _toolbarHiddenUntilCursorLeavesStrip = false;
            if (_toolbarVisible)
            {
                StartToolbarHideCountdown();
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointInt32 lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(PointInt32 point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    private const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void ApplyBoundsWithScreenClamp()
    {
        // settings.json の Width/Height はWPF版から引き継いだ「論理(DIP)」サイズ。
        // AppWindowは物理ピクセルで座標・サイズを扱うため、現在のモニターのDPIスケールを
        // 掛けて物理ピクセルに変換してから使う(そうしないと高DPI環境でツールバーの
        // ボタン群がウィンドウ幅をはみ出す)。
        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;

        int screenLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int screenTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int screenWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        int screenRight = screenLeft + screenWidth;
        int screenBottom = screenTop + screenHeight;

        int minWidthPx = (int)Math.Round(MinWindowWidth * scale);
        int minHeightPx = (int)Math.Round(MinWindowHeight * scale);
        int desiredWidthPx = (int)Math.Round(_settings.WindowWidth * scale);
        int desiredHeightPx = (int)Math.Round(_settings.WindowHeight * scale);

        int width = Math.Max(minWidthPx, Math.Min(desiredWidthPx, screenWidth));
        int height = Math.Max(minHeightPx, Math.Min(desiredHeightPx, screenHeight));

        int left = (int)Math.Round(_settings.WindowLeft);
        int top = (int)Math.Round(_settings.WindowTop);

        if (left < screenLeft || left + width > screenRight) left = screenLeft + 100;
        if (top < screenTop || top + height > screenBottom) top = screenTop + 100;

        AppWindow.MoveAndResize(new RectInt32(left, top, width, height));
        _restoredPosition = new PointInt32(left, top);
        _restoredSize = new SizeInt32(width, height);

        if (_settings.IsMaximized)
        {
            _presenter.Maximize();
        }
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePassthroughRegions();

        var envOptions = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = true,
        };

        _environment = await CoreWebView2Environment.CreateWithOptionsAsync(
            browserExecutableFolder: null,
            userDataFolder: SettingsService.WebView2UserDataFolder,
            options: envOptions);

        await CreateTabAsync(GetHomeUrl());

        UpdatePassthroughRegions();
    }

    // --- タブ管理 ---
    // 各タブは独立したWebView2インスタンスを持ち、同じCoreWebView2Environment(同じuserDataFolder)を
    // 共有することでCookie・ログイン状態をタブ間で共有する。非アクティブなタブのWebView2は
    // Visibility=Collapsedにして裏に隠しておくだけで、ナビゲーション状態はタブ切り替えでも保持される。

    private Task<BrowserTab> CreateTabAsync(string url) => CreateTabAsync(url: url, html: null);

    // 新しいタブ(「+」ボタン、または最後のタブを閉じた直後)は履歴一覧ページを表示する
    private Task<BrowserTab> CreateNewTabPageAsync() => CreateTabAsync(url: null, html: BuildHistoryPageHtml());

    private async Task<BrowserTab> CreateTabAsync(string? url, string? html)
    {
        var webView = new WebView2 { Visibility = Visibility.Collapsed };
        WebViewHost.Children.Add(webView);

        var titleButton = new Button
        {
            Content = new TextBlock { Text = "新しいタブ", TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 80 },
            Height = 24,
            Padding = new Thickness(6, 0, 2, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        var muteButton = new Button
        {
            Content = new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "", FontSize = 8 },
            Width = 18,
            Height = 24,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Collapsed,
        };
        ToolTipService.SetToolTip(muteButton, "ミュート");
        var paneButton = new Button
        {
            Content = new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "", FontSize = 8 },
            Width = 18,
            Height = 24,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(paneButton, "右側に表示");
        var closeButton = new Button
        {
            Content = new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "", FontSize = 8 },
            Width = 18,
            Height = 24,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(closeButton, "タブを閉じる");

        var headerContent = new StackPanel { Orientation = Orientation.Horizontal };
        headerContent.Children.Add(titleButton);
        headerContent.Children.Add(muteButton);
        headerContent.Children.Add(paneButton);
        headerContent.Children.Add(closeButton);

        // Chrome風に上部だけ丸めた「タブ」の見た目にする
        var headerPanel = new Border
        {
            Margin = new Thickness(1, 0, 1, 0),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Background = InactiveTabBrush,
            Child = headerContent,
        };
        TabStripPanel.Children.Add(headerPanel);

        var tab = new BrowserTab { WebView = webView, HeaderPanel = headerPanel, TitleButton = titleButton, MuteButton = muteButton, PaneButton = paneButton };
        titleButton.Click += (_, _) => ActivateTab(tab);
        muteButton.Click += (_, _) => ToggleMute(tab);
        paneButton.Click += (_, _) => ToggleRightPane(tab);
        closeButton.Click += (_, _) => CloseTab(tab);
        _tabs.Add(tab);

        await webView.EnsureCoreWebView2Async(_environment);
        webView.CoreWebView2.DocumentTitleChanged += (_, _) =>
        {
            UpdateTabTitle(tab);
            RecordHistory(tab);
        };
        webView.CoreWebView2.SourceChanged += (_, _) =>
        {
            if (tab == _activeTab) UrlBox.Text = webView.CoreWebView2.Source;
        };
        webView.CoreWebView2.NewWindowRequested += (s, args) =>
        {
            args.Handled = true;
            _ = CreateTabAsync(args.Uri);
        };
        webView.CoreWebView2.IsDocumentPlayingAudioChanged += (_, _) => UpdateMuteButtonState(tab);
        webView.CoreWebView2.IsMutedChanged += (_, _) => UpdateMuteButtonState(tab);
        webView.CoreWebView2.ContextMenuRequested += (ctxSender, ctxArgs) =>
        {
            if (!ctxArgs.ContextMenuTarget.HasLinkUri) return;

            var linkUri = ctxArgs.ContextMenuTarget.LinkUri;
            var openInNewTabItem = _environment!.CreateContextMenuItem("新しいタブで開く", null!, CoreWebView2ContextMenuItemKind.Command);
            openInNewTabItem.CustomItemSelected += (menuSender, menuArgs) => _ = CreateTabAsync(linkUri);
            ctxArgs.MenuItems.Insert(0, openInNewTabItem);
        };
        // ページの描画プロセスが異常終了した場合(例: WebView2が対応していないAPIをページが
        // 呼び続けてクラッシュした場合)に、タブを固まったままにせず自動的に再読み込みする。
        webView.CoreWebView2.ProcessFailed += (_, args) =>
        {
            if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
            {
                webView.CoreWebView2.Reload();
            }
        };

        if (html is not null) webView.CoreWebView2.NavigateToString(html);
        else webView.CoreWebView2.Navigate(url ?? GetHomeUrl());

        ActivateTab(tab);
        UpdatePassthroughRegions();
        return tab;
    }

    private void ActivateTab(BrowserTab tab)
    {
        // 右ペイン表示中のタブは左側のアクティブタブにはしない(右ボタンでしか出し入れしない)
        if (tab == _rightPaneTab) return;

        if (_activeTab is not null && _activeTab != _rightPaneTab)
        {
            _activeTab.WebView.Visibility = Visibility.Collapsed;
        }
        if (_activeTab is not null) _activeTab.HeaderPanel.Background = InactiveTabBrush;

        _activeTab = tab;
        Grid.SetColumn(tab.WebView, 0);
        tab.WebView.Visibility = Visibility.Visible;
        tab.HeaderPanel.Background = ActiveTabBrush;

        UrlBox.Text = tab.WebView.CoreWebView2?.Source ?? string.Empty;
    }

    // --- 左右分割表示 ---
    // タブヘッダの「→」ボタンで指定したタブをWebViewHostのColumn1に固定表示する。
    // 左側(_activeTab)は通常通りタブ切り替えで変わるが、右ペインのタブだけは
    // 「←」を押すかタブを閉じるまでColumn1に表示され続ける。

    private async void ToggleRightPane(BrowserTab tab)
    {
        // 唯一のタブを右ペインに送ると左側が空になってしまうため、その場合は先に新しいタブを開く
        if (_rightPaneTab != tab && _tabs.Count < 2)
        {
            await CreateTabAsync(GetHomeUrl());
        }
        SetRightPaneTab(_rightPaneTab == tab ? null : tab);
    }

    private void SetRightPaneTab(BrowserTab? tab)
    {
        if (_rightPaneTab is not null)
        {
            var prev = _rightPaneTab;
            _rightPaneTab = null;
            ((FontIcon)prev.PaneButton.Content).Glyph = "";
            ToolTipService.SetToolTip(prev.PaneButton, "右側に表示");
            if (prev != _activeTab) prev.WebView.Visibility = Visibility.Collapsed;
        }

        _rightPaneTab = tab;

        if (tab is null)
        {
            RightPaneColumn.Width = new GridLength(0);
            return;
        }

        RightPaneColumn.Width = new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(tab.WebView, 1);
        tab.WebView.Visibility = Visibility.Visible;
        ((FontIcon)tab.PaneButton.Content).Glyph = "";
        ToolTipService.SetToolTip(tab.PaneButton, "左に戻す");

        // 右ペインに送ったタブが左側のアクティブタブと同じだった場合、左側には他のタブを表示する
        // (ToggleRightPaneが唯一のタブを送る前に新規タブを開くため、必ず他のタブが存在する)
        if (_activeTab == tab)
        {
            _activeTab = null;
            ActivateTab(_tabs.First(t => t != tab));
        }
    }

    private void CloseTab(BrowserTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0) return;

        if (_rightPaneTab == tab) SetRightPaneTab(null);

        _tabs.RemoveAt(index);
        TabStripPanel.Children.Remove(tab.HeaderPanel);
        WebViewHost.Children.Remove(tab.WebView);
        tab.WebView.Close();

        if (_tabs.Count == 0)
        {
            // 最後のタブを閉じてもアプリは終了せず、履歴一覧ページのタブを開いて表示する
            _activeTab = null;
            _ = CreateNewTabPageAsync();
            UpdatePassthroughRegions();
            return;
        }

        if (_activeTab == tab)
        {
            _activeTab = null;
            ActivateTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
        }

        UpdatePassthroughRegions();
    }

    private void UpdateTabTitle(BrowserTab tab)
    {
        var title = tab.WebView.CoreWebView2?.DocumentTitle;
        var text = string.IsNullOrWhiteSpace(title) ? "新しいタブ" : title;
        ((TextBlock)tab.TitleButton.Content).Text = text;
        if (tab == _activeTab) Title = text;
    }

    // --- 表示履歴 ---
    // タブの無い状態(最後のタブを閉じた直後)や新規タブでは、この履歴一覧ページを表示する。

    private void RecordHistory(BrowserTab tab)
    {
        var core = tab.WebView.CoreWebView2;
        if (core is null) return;

        var url = core.Source;
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;

        var title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? url : core.DocumentTitle;

        // 同じURLが既に履歴にあれば一旦取り除き、最新として先頭に追加し直す
        _history.RemoveAll(h => h.Url == url);
        _history.Insert(0, new HistoryEntry { Title = title, Url = url });
        if (_history.Count > HistoryStoreLimit)
        {
            _history.RemoveRange(HistoryStoreLimit, _history.Count - HistoryStoreLimit);
        }
    }

    private string BuildHistoryPageHtml()
    {
        var sb = new StringBuilder();
        sb.Append("<html><head><meta charset='utf-8'><title>新しいタブ</title></head>");
        sb.Append("<body style='background:#202020;color:#eee;font-family:Segoe UI,sans-serif;padding:24px;'>");
        sb.Append("<h2 style='font-weight:400;'>最近の履歴</h2>");

        if (_history.Count == 0)
        {
            sb.Append("<p style='color:#888;'>表示履歴はまだありません。</p>");
        }
        else
        {
            sb.Append("<ul style='list-style:none;padding:0;margin:0;'>");
            foreach (var entry in _history.Take(HistoryDisplayLimit))
            {
                var encodedUrl = WebUtility.HtmlEncode(entry.Url);
                var encodedTitle = WebUtility.HtmlEncode(entry.Title);
                sb.Append("<li style='margin-bottom:12px;'>");
                sb.Append($"<a href='{encodedUrl}' style='color:#8ab4f8;text-decoration:none;font-size:14px;'>{encodedTitle}</a><br>");
                sb.Append($"<span style='color:#888;font-size:12px;'>{encodedUrl}</span>");
                sb.Append("</li>");
            }
            sb.Append("</ul>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => _ = CreateNewTabPageAsync();

    private const double TabScrollStep = 120;

    private void TabScrollLeftButton_Click(object sender, RoutedEventArgs e) =>
        TabStripScrollViewer.ChangeView(Math.Max(0, TabStripScrollViewer.HorizontalOffset - TabScrollStep), null, null);

    private void TabScrollRightButton_Click(object sender, RoutedEventArgs e) =>
        TabStripScrollViewer.ChangeView(TabStripScrollViewer.HorizontalOffset + TabScrollStep, null, null);

    // --- タブごとのミュート ---

    private void ToggleMute(BrowserTab tab)
    {
        var core = tab.WebView.CoreWebView2;
        if (core is null) return;
        core.IsMuted = !core.IsMuted;
        UpdateMuteButtonState(tab);
    }

    private void UpdateMuteButtonState(BrowserTab tab)
    {
        var core = tab.WebView.CoreWebView2;
        if (core is null) return;

        ((FontIcon)tab.MuteButton.Content).Glyph = core.IsMuted ? "" : "";
        ToolTipService.SetToolTip(tab.MuteButton, core.IsMuted ? "ミュート解除" : "ミュート");
        // 再生中、またはミュート中のタブだけアイコンを表示する(Chrome等の挙動に合わせる)
        tab.MuteButton.Visibility = (core.IsDocumentPlayingAudio || core.IsMuted) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        // 物理ピクセル(AppWindow基準)から settings.json の論理(DIP)単位に戻す。
        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;

        _settings.WindowLeft = _restoredPosition.X;
        _settings.WindowTop = _restoredPosition.Y;
        _settings.WindowWidth = _restoredSize.Width / scale;
        _settings.WindowHeight = _restoredSize.Height / scale;
        _settings.IsMaximized = _presenter.State == OverlappedPresenterState.Maximized;

        SettingsService.Save(_settings);
        HistoryService.Save(_history);
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if ((args.DidPositionChange || args.DidSizeChange) && _presenter.State == OverlappedPresenterState.Restored)
        {
            _restoredPosition = sender.Position;
            _restoredSize = sender.Size;
        }

        if (args.DidSizeChange)
        {
            if (_toolbarVisible) UpdateDragRegion();
            else ClearDragRegion();
        }

        if (args.DidPresenterChange || args.DidSizeChange)
        {
            MaximizeRestoreIcon.Glyph = _presenter.State == OverlappedPresenterState.Maximized ? "" : "";
        }
    }

    // --- ホバーツールバー(兼タイトルバー)の表示/非表示 ---
    // ウィンドウ上端のツールバー帯にカーソルが入ると表示し(PollCursorPosition参照)、
    // カーソルがツールバー帯から離れた後、3秒で自動的に隠す。

    private void Toolbar_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isCursorOverToolbarStrip = true;
        _hideDelayTimer.Stop();
        ShowToolbar();
    }

    private void Toolbar_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_toolbarVisible) StartToolbarHideCountdown();
    }

    private void Toolbar_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_toolbarVisible) StartToolbarHideCountdown();
    }

    private void Toolbar_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_toolbarVisible) StartToolbarHideCountdown();
    }

    // 注意: Toolbar はXAML上 Opacity="0" として初期化すると、WebView2に重なる構成では
    // 実機検証でその後 Opacity を 1 に戻しても二度と描画されない不具合を確認したため、
    // Opacity アニメーションではなく Visibility の即時切り替えで表示/非表示を行う。
    private void ShowToolbar()
    {
        ToolbarTransform.Y = 0;
        Toolbar.IsHitTestVisible = true;
        _toolbarVisible = true;
        _toolbarHiddenUntilCursorLeavesStrip = false;
        UpdateDragRegion();
        UpdatePassthroughRegions();
        StartToolbarHideCountdown();
    }

    private void StartToolbarHideCountdown()
    {
        _hideDelayTimer.Stop();
        if (_toolbarVisible && !_isCursorOverToolbarStrip) _hideDelayTimer.Start();
    }

    private void HideToolbar(bool waitForCursorLeave = false)
    {
        // タブが1つも無い間は操作に気づけるよう、ツールバーを隠さず常時表示する
        if (_tabs.Count == 0) return;

        ToolbarTransform.Y = -60;
        Toolbar.IsHitTestVisible = false;
        _toolbarVisible = false;
        _toolbarHiddenUntilCursorLeavesStrip = waitForCursorLeave && _isCursorOverToolbarStrip;
        UpdatePassthroughRegions();
        ClearDragRegion();
    }

    private void DragArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_toolbarVisible) UpdateDragRegion();
    }

    private void UpdateDragRegion()
    {
        if (Toolbar.ActualWidth <= 0 || Toolbar.ActualHeight <= 0) return;

        InputNonClientPointerSource.GetForWindowId(AppWindow.Id)
            .SetRegionRects(NonClientRegionKind.Caption, new[] { ToScreenRect(Toolbar), GetTopDragHandleRect() });
    }

    private void ClearDragRegion()
    {
        InputNonClientPointerSource.GetForWindowId(AppWindow.Id)
            .SetRegionRects(NonClientRegionKind.Caption, new[] { GetTopDragHandleRect() });
    }

    // HUD表示中はHUD全体をドラッグ可能領域にし、ボタンやタブだけをPassthroughに逃がす。
    // HUD非表示中も移動できるよう、ウィンドウ最上端に常時ドラッグ可能な細い帯を残しておく。
    private const int TopDragHandleHeightDip = 6;

    private RectInt32 GetTopDragHandleRect()
    {
        double scale = GetDpiForWindow(_hwnd) / 96.0;
        var size = AppWindow.Size;
        int heightPx = (int)Math.Round(TopDragHandleHeightDip * scale);
        return new RectInt32(0, 0, size.Width, heightPx);
    }

    // ExtendsContentIntoTitleBar=true にすると、既定では上端の帯全体が
    // システムタイトルバーと同じ「ドラッグ領域」として扱われ、その範囲内のXAML要素は
    // ポインターイベント(PointerEntered等)を一切受け取れなくなる。
    // そのため、ツールバーのボタン群(表示中のみ)は明示的に Passthrough 指定して
    // 通常のクライアント領域として入力を受け取れるようにする。
    // (表示トリガー自体はGetCursorPosのポーリングで行うため、ここでの登録は不要)
    private void UpdatePassthroughRegions()
    {
        if (RootGrid.XamlRoot is null) return;

        var rects = new List<RectInt32>();
        if (_toolbarVisible)
        {
            AddPassthroughRect(rects, BrowserButtonsPanel);
            AddPassthroughRect(rects, TabScrollLeftButton);
            AddPassthroughRect(rects, TabScrollRightButton);
            AddPassthroughRect(rects, NewTabButton);
            AddPassthroughRect(rects, WindowButtonsPanel);

            foreach (var tab in _tabs)
            {
                AddPassthroughRect(rects, tab.HeaderPanel);
            }
        }

        InputNonClientPointerSource.GetForWindowId(AppWindow.Id)
            .SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
    }

    private void AddPassthroughRect(List<RectInt32> rects, FrameworkElement element)
    {
        if (element.ActualWidth > 0 && element.ActualHeight > 0 && element.Visibility == Visibility.Visible)
        {
            rects.Add(ToScreenRect(element));
        }
    }

    private RectInt32 ToScreenRect(FrameworkElement element)
    {
        double scale = RootGrid.XamlRoot.RasterizationScale;
        var transform = element.TransformToVisual(null);
        var bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

        return new RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));
    }

    // --- ウィンドウ操作ボタン(タイトルバー代替) ---

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => _presenter.Minimize();

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_presenter.State == OverlappedPresenterState.Maximized)
            _presenter.Restore();
        else
            _presenter.Maximize();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // --- ツールバー操作 ---

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveWebView?.CanGoBack == true) ActiveWebView.GoBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveWebView?.CanGoForward == true) ActiveWebView.GoForward();
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        ActiveWebView?.CoreWebView2?.Reload();
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        var homeUrl = GetHomeUrl();
        if (ActiveWebView?.CoreWebView2 is { } core) core.Navigate(homeUrl);
        else _ = CreateTabAsync(homeUrl);
    }

    private async void SetCurrentTabAsHome_Click(object sender, RoutedEventArgs e)
    {
        await SetCurrentTabAsHomeAsync();
    }

    private async Task SetCurrentTabAsHomeAsync()
    {
        var url = GetActiveUrl();
        if (string.IsNullOrWhiteSpace(url) || !TryResolveAsUrl(url.Trim(), out var resolvedUrl))
        {
            await ShowMessageDialogAsync("ホームの変更", "ホームに設定できるURLを取得できませんでした。");
            return;
        }

        _settings.HomeUrl = resolvedUrl;
        SettingsService.Save(_settings);
        UpdateHomeButtonToolTip();
        await ShowMessageDialogAsync("ホームの変更", $"ホームを変更しました:\n{resolvedUrl}");
    }

    private void GoButton_Click(object sender, RoutedEventArgs e) => NavigateToUrlBoxValue();

    private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) NavigateToUrlBoxValue();
    }

    private void UrlBoxCut_Click(object sender, RoutedEventArgs e) => UrlBox.CutSelectionToClipboard();

    private void UrlBoxCopy_Click(object sender, RoutedEventArgs e) => UrlBox.CopySelectionToClipboard();

    private void UrlBoxPaste_Click(object sender, RoutedEventArgs e) => UrlBox.PasteFromClipboard();

    private void UrlBoxSelectAll_Click(object sender, RoutedEventArgs e) => UrlBox.SelectAll();

    private string GetHomeUrl()
    {
        var configuredHomeUrl = _settings.HomeUrl?.Trim();
        return !string.IsNullOrEmpty(configuredHomeUrl) && TryResolveAsUrl(configuredHomeUrl, out var homeUrl)
            ? homeUrl
            : DefaultHomeUrl;
    }

    private string? GetActiveUrl()
    {
        var source = ActiveWebView?.CoreWebView2?.Source;
        if (!string.IsNullOrWhiteSpace(source) && !source.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        return UrlBox.Text?.Trim();
    }

    private void UpdateHomeButtonToolTip()
    {
        ToolTipService.SetToolTip(HomeButton, $"ホーム: {GetHomeUrl()}");
    }

    // URL欄はGoogle検索窓を兼ねるオムニボックスとして扱う。
    // URLとして解釈できる入力("youtube.com/watch?v=xxx"等)は現在のタブでそのページへ移動し、
    // それ以外(検索語句)は新しいタブを開いてGoogle検索結果を表示しアクティブにする。
    // タブが1つも無い状態(最後のタブを閉じた直後)では、どちらの場合も新しいタブを開く。
    private void NavigateToUrlBoxValue()
    {
        var input = UrlBox.Text?.Trim();
        if (string.IsNullOrEmpty(input)) return;

        if (TryResolveAsUrl(input, out var url))
        {
            if (ActiveWebView?.CoreWebView2 is { } core) core.Navigate(url);
            else _ = CreateTabAsync(url);
        }
        else
        {
            _ = CreateTabAsync("https://www.google.com/search?q=" + Uri.EscapeDataString(input));
        }
    }

    private static readonly Regex DomainLikePattern = new(
        @"^[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}(:\d+)?(/.*)?$", RegexOptions.Compiled);

    private static bool TryResolveAsUrl(string input, out string url)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            url = input;
            return true;
        }

        // スキームを省略したドメインらしい入力("youtube.com/watch?v=xxx" 等)は https:// を補う
        if (!input.Contains(' ') && DomainLikePattern.IsMatch(input))
        {
            url = "https://" + input;
            return true;
        }

        url = string.Empty;
        return false;
    }

    private async void AddExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveWebView?.CoreWebView2?.Profile is null)
        {
            await ShowMessageDialogAsync("拡張機能の追加", "WebView2の初期化が完了していません。");
            return;
        }

        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        try
        {
            var extension = await ActiveWebView.CoreWebView2.Profile.AddBrowserExtensionAsync(folder.Path);
            _settings.ExtensionFolders[extension.Id] = folder.Path;
            await ShowMessageDialogAsync("拡張機能の追加", $"拡張機能を追加しました: {extension.Name}");
        }
        catch (Exception ex)
        {
            await ShowMessageDialogAsync("拡張機能の追加", $"拡張機能の追加に失敗しました:\n{ex.Message}");
        }
    }

    // edge://extensions/ はWebView2では予約スキームとしてブロックされ開けないため、
    // 独自の管理ページ(追加済み拡張機能の一覧と設定ページへのリンク)を生成して表示する。
    private async void ManageExtensionsButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = ActiveWebView?.CoreWebView2?.Profile;
        if (profile is null) return;

        var extensions = await profile.GetBrowserExtensionsAsync();
        _ = CreateTabAsync(url: null, html: BuildExtensionsPageHtml(extensions));
    }

    private string BuildExtensionsPageHtml(IReadOnlyList<CoreWebView2BrowserExtension> extensions)
    {
        var sb = new StringBuilder();
        sb.Append("<html><head><meta charset='utf-8'><title>拡張機能の管理</title></head>");
        sb.Append("<body style='background:#202020;color:#eee;font-family:Segoe UI,sans-serif;padding:24px;'>");
        sb.Append("<h2 style='font-weight:400;'>追加済みの拡張機能</h2>");

        if (extensions.Count == 0)
        {
            sb.Append("<p style='color:#888;'>追加済みの拡張機能はありません。</p>");
        }
        else
        {
            sb.Append("<ul style='list-style:none;padding:0;margin:0;'>");
            foreach (var ext in extensions)
            {
                var name = WebUtility.HtmlEncode(ext.Name);
                var status = ext.IsEnabled ? "有効" : "無効";
                sb.Append("<li style='margin-bottom:16px;'>");
                sb.Append($"<div style='font-size:15px;'>{name} <span style='color:#888;font-size:12px;'>({status})</span></div>");

                string? optionsUrl = _settings.ExtensionFolders.TryGetValue(ext.Id, out var folder)
                    ? TryGetOptionsPageUrl(ext.Id, folder)
                    : null;

                sb.Append(optionsUrl is not null
                    ? $"<a href='{WebUtility.HtmlEncode(optionsUrl)}' style='color:#8ab4f8;text-decoration:none;font-size:13px;'>設定を開く</a>"
                    : "<span style='color:#666;font-size:13px;'>設定ページなし</span>");
                sb.Append("</li>");
            }
            sb.Append("</ul>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    // manifest.json の options_page(MV2) / options_ui.page(MV3) から拡張機能の設定ページを探す
    private static string? TryGetOptionsPageUrl(string extensionId, string folderPath)
    {
        try
        {
            var manifestPath = Path.Combine(folderPath, "manifest.json");
            if (!File.Exists(manifestPath)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;

            string? optionsPath = null;
            if (root.TryGetProperty("options_page", out var optionsPageEl))
            {
                optionsPath = optionsPageEl.GetString();
            }
            else if (root.TryGetProperty("options_ui", out var optionsUiEl) &&
                     optionsUiEl.TryGetProperty("page", out var pageEl))
            {
                optionsPath = pageEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(optionsPath)) return null;
            return $"chrome-extension://{extensionId}/{optionsPath.TrimStart('/')}";
        }
        catch
        {
            return null;
        }
    }

    private async Task ShowMessageDialogAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = RootGrid.XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
