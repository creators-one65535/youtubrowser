using System.Collections.Generic;

namespace Youtubrowser.Settings;

public sealed class AppSettings
{
    public double WindowLeft { get; set; } = 100;
    public double WindowTop { get; set; } = 100;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 400;
    public bool IsMaximized { get; set; }
    public string HomeUrl { get; set; } = "https://www.youtube.com/";

    // 拡張機能ID -> 追加時に選択したアンパック済みフォルダのパス。
    // WebView2は追加時にフォルダの中身をコピーしないため、後から同じフォルダの
    // manifest.jsonを読み直してオプション(設定)ページのパスを調べるために使う。
    public Dictionary<string, string> ExtensionFolders { get; set; } = new();
}
