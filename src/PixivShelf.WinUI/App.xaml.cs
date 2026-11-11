using Microsoft.UI.Xaml;
using PixivShelf.Core.Models;

namespace PixivShelf;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = AppSettings.Load();
        ConfigureWebView2DataFolder();

        if (!settings.OnboardingCompleted)
        {
            StartOnboarding();
            return;
        }

        StartMainWindow();
    }

    private void StartOnboarding()
    {
        var wizard = new OnboardingWindow();

        wizard.Completed += StartMainWindow;

        wizard.Cancelled += () =>
        {
            if (MainWindow is null)
            {
                Exit();
            }
        };

        wizard.Activate();
    }

    /// <summary>
    /// Keeps the WebView2 login cache inside data\WebView2 instead of the
    /// PixivShelf.exe.WebView2 folder next to the executable. The variable is
    /// read by WebView2 before it creates its user data folder.
    /// </summary>
    private static void ConfigureWebView2DataFolder()
    {
        try
        {
            var dataDirectory = AppSettings.DefaultDirectory;
            var target = Path.Combine(dataDirectory, "WebView2");
            var legacy = Path.Combine(AppSettings.BaseDirectory, "PixivShelf.exe.WebView2");

            if (!Directory.Exists(target))
            {
                if (Directory.Exists(legacy))
                {
                    try
                    {
                        Directory.CreateDirectory(dataDirectory);
                        Directory.Move(legacy, target);
                    }
                    catch
                    {
                        try { Directory.CreateDirectory(target); }
                        catch { }
                    }
                }
                else
                {
                    Directory.CreateDirectory(target);
                }
            }

            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", target);
        }
        catch
        {
            // WebView2 falls back to its default folder; login still works.
        }
    }

    private static void StartMainWindow()
    {
        if (MainWindow is not null)
        {
            return;
        }

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
