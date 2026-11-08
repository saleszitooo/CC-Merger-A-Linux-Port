using Avalonia;

namespace CCMerger;

internal static class AppInfo
{
    public const string Version = "1.4.1";
    public const string Title = "CCMerger by Lazy Duchess - Linux Port By Sales";
}

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect();
}
