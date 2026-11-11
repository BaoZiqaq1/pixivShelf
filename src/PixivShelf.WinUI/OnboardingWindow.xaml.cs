using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PixivShelf.Core.Models;
using PixivShelf.Core.Services;
using PixivShelf.Services;
using Windows.Graphics;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PixivShelf;

/// <summary>
/// First-run wizard: disclaimer → login → download preferences → ready.
/// Uses its own settings instance and only writes config.json on the last step.
/// </summary>
public sealed partial class OnboardingWindow : Window
{

    private readonly AppSettings _settings = new();
    private int _step = 1;
    private int _stepVersion;
    private int _userId;
    private string _userName = "";

    public OnboardingWindow()
    {
        InitializeComponent();
        Title = "pixivShelf快速设置";

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(880, 780));

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
            // A missing icon must never block the wizard.
        }
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        try
        {
            var png = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.png");
            if (System.IO.File.Exists(png))
            {
                ReadyIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(png));
            }
        }
        catch
        {
            // The ready step simply shows without an icon.
        }

        _settings.Normalize();
        RootBox.Text = _settings.Storage.DownloadRoot;
        FormatBox.Text = _settings.Storage.FileNameFormat;
        UpdateFormatSample();
        CreationSwitch.IsOn = _settings.Storage.SetCreationTime;

        Closed += (_, _) => Cancelled?.Invoke();
        ShowStep(1);
    }

    /// <summary>Raised when the user finishes the wizard (settings already saved).</summary>
    public event Action? Completed;

    /// <summary>Raised when the window is closed without finishing.</summary>
    public event Action? Cancelled;

    // ------------------------------------------------------------------ //
    // step switching
    // ------------------------------------------------------------------ //
    private void ShowStep(int step)
    {
        var previousStep = _step;
        _step = step;
        var version = ++_stepVersion;

        var grids = new FrameworkElement[] { StepDisclaimer, StepLogin, StepPreferences, StepReady };
        var target = grids[step - 1];
        var old = grids.FirstOrDefault(g => g.Visibility == Visibility.Visible && !ReferenceEquals(g, target));

        StepDisclaimer.Visibility = ReferenceEquals(StepDisclaimer, target) || ReferenceEquals(StepDisclaimer, old) ? Visibility.Visible : Visibility.Collapsed;
        StepLogin.Visibility = ReferenceEquals(StepLogin, target) || ReferenceEquals(StepLogin, old) ? Visibility.Visible : Visibility.Collapsed;
        StepPreferences.Visibility = ReferenceEquals(StepPreferences, target) || ReferenceEquals(StepPreferences, old) ? Visibility.Visible : Visibility.Collapsed;
        StepReady.Visibility = ReferenceEquals(StepReady, target) || ReferenceEquals(StepReady, old) ? Visibility.Visible : Visibility.Collapsed;

        var title = step switch
        {
            1 => "免责声明",
            2 => "登录 pixiv",
            3 => "下载偏好",
            _ => "准备就绪",
        };
        if (StepTitle.Text != title)
        {
            StepTitle.Text = title;
            StepTitle.Opacity = 0;
            Motion.FadeIn(StepTitle, 160);
        }

        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var dim = (Brush)Application.Current.Resources["ControlStrongFillColorDefaultBrush"];
        var dots = new[] { Dot1, Dot2, Dot3, Dot4 };
        for (var i = 0; i < dots.Length; i++)
        {
            var active = i < step;
            dots[i].Fill = active ? accent : dim;
            Motion.FadeTo(dots[i], active ? 1 : 0.35, 180);
        }

        // Only the first step needs an explicit exit; later steps can still be closed with the title bar.
        ExitButton.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        BackButton.Visibility = step is 2 or 3 ? Visibility.Visible : Visibility.Collapsed;
        SkipButton.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;

        NextButton.Content = step switch
        {
            1 => "同意并继续",
            2 => "下一步",
            3 => "下一步",
            _ => "开始使用",
        };

        // Only the login step needs a completed sign-in before continuing.
        NextButton.IsEnabled = step != 2 || _userId > 0;

        target.Visibility = Visibility.Visible;
        target.Opacity = 1;
        target.IsHitTestVisible = true;

        if (old is not null)
        {
            old.IsHitTestVisible = false;
            Motion.FadeOut(old, 160, 0, () =>
            {
                if (version != _stepVersion)
                {
                    return;
                }

                old.Visibility = Visibility.Collapsed;
                old.Opacity = 1;
                old.IsHitTestVisible = true;
            });
        }

        if (previousStep != step && old is not null)
        {
            var direction = step > previousStep ? 24 : -24;
            target.Opacity = 0;
            Motion.SetTranslation(target, direction, 0);
            Motion.FadeIn(target, 240);
            Motion.SlideTo(target, 0, 0, 240);
        }

        if (step == 4)
        {
            ReadyPanel.Opacity = 0;
            Motion.FadeIn(ReadyPanel, 300, 0, 0, 6);
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_step > 1)
        {
            ShowStep(_step - 1);
        }
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        switch (_step)
        {
            case 1:
                ShowStep(2);
                break;
            case 2:
                ShowStep(3);
                break;
            case 3:
                ApplyPreferences();
                ShowStep(4);
                break;
            default:
                Finish();
                break;
        }
    }

    private void OnSkip(object sender, RoutedEventArgs e) => ShowStep(3);

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    private void Finish()
    {
        ApplyPreferences();
        _settings.OnboardingCompleted = true;
        _settings.Normalize();

        try
        {
            _settings.Save();
        }
        catch
        {
            // Saving may fail on a read-only folder; the app still runs with defaults.
        }

        Completed?.Invoke();
        Close();
    }

    // ------------------------------------------------------------------ //
    // step 2: login
    // ------------------------------------------------------------------ //
    private async void OnWebLogin(object sender, RoutedEventArgs e)
    {
        var window = new LoginWindow();
        window.Activate();
        var cookie = await window.Result;
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return;
        }

        LoginStatus.Text = "状态：正在读取账号…";
        await ApplyCookieAsync(cookie);
    }

    private void OnShowCookieInput(object sender, RoutedEventArgs e)
    {
        CookieError.Text = "";
        Motion.Reveal(CookieCard, true, Motion.Normal, double.NaN);
        OnboardingCookieBox.Focus(FocusState.Programmatic);
    }

    private void OnCancelCookieInput(object sender, RoutedEventArgs e)
    {
        Motion.Reveal(CookieCard, false, Motion.Fast);
    }

    private async void OnVerifyCookie(object sender, RoutedEventArgs e)
    {
        var cookie = OnboardingCookieBox.Password.Trim();
        if (cookie.Length == 0)
        {
            CookieError.Text = "请先粘贴 Cookie。";
            return;
        }

        VerifyCookieButton.IsEnabled = false;
        CookieError.Text = "验证中…";
        await ApplyCookieAsync(cookie);
        VerifyCookieButton.IsEnabled = true;
    }

    private async Task ApplyCookieAsync(string cookie)
    {
        try
        {
            var probe = new AppSettings();
            probe.Auth.Cookie = cookie;
            probe.Network.Timeout = 20;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var client = new PixivClient(probe);
            var userId = await client.ResolveSelfUserIdAsync(cts.Token);
            var name = await client.GetUserNameAsync(userId, cts.Token);

            _settings.Auth.Cookie = cookie;
            _userId = userId;
            _userName = string.IsNullOrWhiteSpace(name) ? $"ID {userId}" : name;

            LoginStatus.Text = $"状态：✓ 已登录：{_userName}（ID {userId}）";
            Motion.Reveal(CookieCard, false, Motion.Fast);
            CookieError.Text = "";
            NextButton.IsEnabled = true;
        }
        catch (PixivException ex)
        {
            _userId = 0;
            NextButton.IsEnabled = false;
            var message = ex.Kind == PixivErrorKind.Auth
                ? "Cookie 无效或已过期，请重新获取。"
                : $"验证失败：{ex.Message}";
            CookieError.Text = message;
            LoginStatus.Text = "状态：未登录";
            if (CookieCard.Visibility != Visibility.Visible)
            {
                Motion.Reveal(CookieCard, true, Motion.Normal, double.NaN);
            }
        }
        catch (Exception ex)
        {
            _userId = 0;
            NextButton.IsEnabled = false;
            CookieError.Text = $"连接失败：{ex.Message}";
            LoginStatus.Text = "状态：未登录";
        }
    }

    // ------------------------------------------------------------------ //
    // step 3: preferences
    // ------------------------------------------------------------------ //
    private void ApplyPreferences()
    {
        var root = RootBox.Text.Trim();
        if (root.Length == 0)
        {
            root = _settings.Storage.DownloadRoot;
        }

        _settings.Storage.DownloadRoot = root;
        _settings.Storage.FileNameFormat = string.IsNullOrWhiteSpace(FormatBox.Text)
            ? "%image_id%_p%page_index%"
            : FormatBox.Text.Trim();
        _settings.Storage.FileTimeSource = TimeBookmark.IsChecked == true
            ? FileTimeSource.Bookmark
            : FileTimeSource.Publish;
        _settings.Storage.SetCreationTime = CreationSwitch.IsOn;
    }

    private async void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
            {
                RootBox.Text = folder.Path;
            }
        }
        catch
        {
            // Picker failures should not block onboarding.
        }
    }

    private void OnFormatBoxPressed(object sender, PointerRoutedEventArgs e)
    {
        ShowFormatMenu();
        e.Handled = true;
    }

    private void OnFormatMenuClick(object sender, RoutedEventArgs e) => ShowFormatMenu();

    private void ShowFormatMenu()
    {
        var flyout = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            ShouldConstrainToRootBounds = false,
        };

        foreach (var (label, format) in FileNamePresets.All)
        {
            var item = new MenuFlyoutItem { Text = label };
            if (string.Equals(FormatBox.Text, format, StringComparison.Ordinal))
            {
                item.Icon = new FontIcon { Glyph = "\uE73E" };
            }

            item.Click += (_, _) =>
            {
                FormatBox.Text = format;
                UpdateFormatSample();
            };
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        var custom = new MenuFlyoutItem { Text = "自定义…" };
        custom.Click += (_, _) =>
        {
            FormatBox.IsReadOnly = false;
            FormatBox.Focus(FocusState.Programmatic);
            FormatBox.SelectAll();
        };
        flyout.Items.Add(custom);

        flyout.ShowAt(FormatMenuButton);
    }

    private void UpdateFormatSample()
    {
        var format = FormatBox.Text;
        if (string.IsNullOrWhiteSpace(format))
        {
            FormatSample.Text = "";
            return;
        }

        FormatSample.Text = "示例：" + FileNamePresets.BuildSample(format);
    }
}
