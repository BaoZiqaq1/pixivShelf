using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using PixivShelf.Core.Models;
using PixivShelf.Core.Services;
using PixivShelf.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace PixivShelf;

/// <summary>
/// In-app Pixiv login. Uses the system WebView2 runtime, so the app ships no browser
/// engine of its own. Once the user is actually logged in we read the cookies for
/// pixiv.net straight from the browser store (this also covers HttpOnly ones such as
/// PHPSESSID, which page scripts cannot see) and hand the header string back.
/// </summary>
public sealed partial class LoginWindow : Window
{
    private const string LoginUrl = "https://accounts.pixiv.net/login?lang=zh";
    private const string CookieScope = "https://www.pixiv.net";

    private readonly TaskCompletionSource<string?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _finished;
    private bool _checking;
    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;

    public LoginWindow()
    {
        InitializeComponent();
        Title = "登录 Pixiv";

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1000, 800));

        try
        {
            var icon = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(icon))
            {
                appWindow.SetIcon(icon);
            }
        }
        catch
        {
            // Icon is cosmetic only.
        }

        Browser.CoreWebView2Initialized += OnCoreWebView2Initialized;
        Browser.NavigationCompleted += OnNavigationCompleted;
        Closed += (_, _) => _result.TrySetResult(null);

        Browser.Source = new Uri(LoginUrl);
    }

    /// <summary>Cookie header value, or null when the window was closed without logging in.</summary>
    public Task<string?> Result => _result.Task;

    private void OnCoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        if (args.Exception is null)
        {
            return;
        }

        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;
        StatusText.Text = "内置浏览器启动失败，请改用手动粘贴 Cookie。";
        _finished = true;
        _result.TrySetResult(null);
    }

    private async void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        LoadingRing.IsActive = false;
        LoadingRing.Visibility = Visibility.Collapsed;

        if (_finished || _checking || !args.IsSuccess)
        {
            return;
        }

        var core = sender.CoreWebView2;
        if (core is null || !core.Source.Contains("pixiv.net", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // SPA route changes fire this event often; don't hammer the verification call.
        if (DateTimeOffset.UtcNow - _lastCheck < TimeSpan.FromSeconds(2))
        {
            return;
        }
        _lastCheck = DateTimeOffset.UtcNow;

        _checking = true;
        try
        {
            var cookies = await core.CookieManager.GetCookiesAsync(CookieScope);
            var pixivCookies = cookies
                .Where(cookie => (cookie.Domain ?? "").Contains("pixiv.net", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (!pixivCookies.Any(cookie => cookie.Name == "PHPSESSID"))
            {
                StatusText.Text = "还没有登录，登录成功后会自动读取。";
                return;
            }

            var header = string.Join("; ", pixivCookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));

            // Anonymous visitors also get a PHPSESSID, so ask pixiv who we are.
            if (!await IsLoggedInAsync(header))
            {
                StatusText.Text = "还没有登录成功，完成登录后会自动导入。";
                return;
            }

            _finished = true;
            StatusText.Text = "登录成功，正在导入 Cookie…";
            _result.TrySetResult(header);
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"检查登录状态失败：{ex.Message}";
        }
        finally
        {
            _checking = false;
        }
    }

    private static async Task<bool> IsLoggedInAsync(string cookie)
    {
        var probe = new AppSettings();
        probe.Auth.Cookie = cookie;
        probe.Network.Timeout = 15;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var client = new PixivClient(probe);
            var userId = await client.ResolveSelfUserIdAsync(cts.Token);
            return userId > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
