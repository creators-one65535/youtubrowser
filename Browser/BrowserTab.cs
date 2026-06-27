using Microsoft.UI.Xaml.Controls;

namespace Youtubrowser.Browser;

internal sealed class BrowserTab
{
    public required WebView2 WebView { get; init; }
    public required Border HeaderPanel { get; init; }
    public required Button TitleButton { get; init; }
    public required Button MuteButton { get; init; }
    public required Button PaneButton { get; init; }
}
