using Avalonia;
namespace XstReader.App;
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        if (args.FirstOrDefault() == "--extract-search-text") { Environment.ExitCode = AttachmentText.RunWorker(args); return; }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect().WithInterFont().LogToTrace();
}
