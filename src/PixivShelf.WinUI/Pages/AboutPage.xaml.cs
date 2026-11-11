using System.Reflection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PixivShelf.Services;
using Windows.ApplicationModel;

namespace PixivShelf.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        PageSizing.Center(ContentRoot, RootScroll, 820);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? "版本 1.0.0" : $"版本 {version.Major}.{version.Minor}.{version.Build}";
        FooterText.Text = $"© {DateTime.Now.Year} pixivShelf · 仅用于个人学习与收藏";

        try
        {
            var png = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.png");
            if (System.IO.File.Exists(png))
            {
                AppIcon.Opacity = 0;
                AppIcon.ImageOpened += (_, _) => Motion.FadeIn(AppIcon, 200);
                AppIcon.Source = new BitmapImage(new Uri(png));
            }
        }
        catch
        {
            // The header simply stays empty if the icon cannot be loaded.
        }
    }

    private async void OnOpenLink(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.HyperlinkButton { Tag: string url })
        {
            return;
        }

        try
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
        catch
        {
            // Opening the browser is best effort; the URL stays selectable in the UI.
        }
    }
}
