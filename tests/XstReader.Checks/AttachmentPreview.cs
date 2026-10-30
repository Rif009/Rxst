using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using XstReader;
using XstReader.App;

internal static class AttachmentPreview
{
    public static void Run() => AppBuilder.Configure<PreviewApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime([]);
    private sealed class PreviewApp : Application
    {
        public override void Initialize()
        {
            RequestedThemeVariant = ThemeVariant.Dark;
            Styles.Add(new FluentTheme());
            Resources["Card"] = Brush.Parse("#292931");
            Resources["InputBorder"] = Brush.Parse("#363540");
            Resources["PrimaryText"] = Brush.Parse("#E7E6EF");
            Resources["ActionText"] = Brush.Parse("#BBA3EF");
            var ghost = new Style(s => s.OfType<Button>().Class("ghost"));
            ghost.Setters.Add(new Setter(Button.BackgroundProperty, Brushes.Transparent)); Styles.Add(ghost);
        }
        public override void OnFrameworkInitializationCompleted()
        {
            Strings.Current.SetLanguage(0);
            var panel = new StackPanel { Margin = new Thickness(28), Spacing = 2 };
            panel.Children.Add(new TextBlock { Text = "Rxst · Вложения", FontSize = 22, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 22) });
            var notice = new TextBlock { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var (name, size) in new[] { ("Приложение.xls", 8972), ("Матрица.xls", 11520), ("Договор и приложения к проекту — финальная версия.pdf", 2516582), ("Презентация.pptx", 1782000), ("Документы.zip", 145800), ("Фотография.jpg", 583200) })
            {
                var card = new AttachmentCard { DataContext = new AttachmentItem(new Attachment { FileNameW = name, Size = size }) };
                card.SaveRequested += (_, _) => notice.Text = "Save requested: " + name;
                panel.Children.Add(card);
            }
            panel.Children.Add(notice);
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new Window { Title = "Rxst · Attachment preview", Width = 740, Height = 730, Background = Brush.Parse("#202027"), FontFamily = new FontFamily("Inter"), Content = new ScrollViewer { Content = panel } };
            base.OnFrameworkInitializationCompleted();
        }
    }
}
