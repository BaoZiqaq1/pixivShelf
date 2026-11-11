using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PixivShelf.Core.Models;
using PixivShelf.Core.Services;
using PixivShelf.Services;
using Windows.Storage.Pickers;

namespace PixivShelf.Pages;

public sealed partial class SettingsPage : Page
{

    private readonly DispatcherTimer _savedTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _loading = true;
    private bool _dirty;

    public SettingsPage()
    {
        InitializeComponent();
        PageSizing.Center(ContentRoot, RootScroll, 980);
        _savedTimer.Tick += (_, _) =>
        {
            _savedTimer.Stop();
            HideSavedBar();
        };

        Motion.AttachPressScale(FormatMenuButton);

        // Leaving the page must not keep it alive through a running timer.
        Unloaded += (_, _) => _savedTimer.Stop();
        LoadFromSettings();
    }

    /// <summary>Raised when the unsaved-changes state flips, so the shell can enable/disable 保存.</summary>
    public event Action? DirtyStateChanged;

    public bool IsDirty => _dirty;

    // ------------------------------------------------------------------ //
    // load / dirty / save
    // ------------------------------------------------------------------ //
    private void LoadFromSettings()
    {
        _loading = true;
        var settings = AppState.Instance.Settings;

        CookieBox.Password = settings.Auth.Cookie;
        TimeoutBox.Value = settings.Network.Timeout;
        RetryBox.Value = settings.Network.Retry;
        RetryWaitBox.Value = settings.Network.RetryWait;
        DelayBox.Value = settings.Network.DownloadDelay;
        WorkersBox.Value = settings.Network.MaxWorkers;
        RootBox.Text = settings.Storage.DownloadRoot;
        FormatBox.Text = settings.Storage.FileNameFormat;
        CreationSwitch.IsOn = settings.Storage.SetCreationTime;

        TimePublish.IsChecked = settings.Storage.FileTimeSource == FileTimeSource.Publish;
        TimeBookmark.IsChecked = settings.Storage.FileTimeSource == FileTimeSource.Bookmark;

        _loading = false;
        SetDirty(false);
    }

    private void SetDirty(bool dirty)
    {
        if (_dirty == dirty)
        {
            return;
        }

        _dirty = dirty;
        DirtyStateChanged?.Invoke();
    }

    private void OnAnySettingChanged(object sender, object e)
    {
        if (_loading)
        {
            return;
        }

        SetDirty(true);
    }

    private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => OnAnySettingChanged(sender, args);

    private void OnTimeChanged(object sender, RoutedEventArgs e) => OnAnySettingChanged(sender, e);

    private void OnCreationToggled(object sender, RoutedEventArgs e) => OnAnySettingChanged(sender, e);

    /// <summary>Writes the current controls into the config and applies them immediately.</summary>
    public void SaveSettings()
    {
        var settings = AppState.Instance.Settings;

        settings.Auth.Cookie = CookieBox.Password.Trim();
        settings.Network.Timeout = ToInt(TimeoutBox, 15);
        settings.Network.Retry = ToInt(RetryBox, 3);
        settings.Network.RetryWait = ToInt(RetryWaitBox, 5);
        settings.Network.DownloadDelay = ToInt(DelayBox, 1);
        settings.Network.MaxWorkers = ToInt(WorkersBox, 3);
        settings.Storage.DownloadRoot = RootBox.Text.Trim();
        settings.Storage.FileNameFormat = string.IsNullOrWhiteSpace(FormatBox.Text)
            ? "%image_id%_p%page_index%"
            : FormatBox.Text.Trim();
        settings.Storage.SetCreationTime = CreationSwitch.IsOn;
        settings.Storage.FileTimeSource = TimeBookmark.IsChecked == true ? FileTimeSource.Bookmark : FileTimeSource.Publish;
        settings.Normalize();

        try
        {
            settings.Save();
            AppState.Instance.ReloadSettings();
        }
        catch (Exception ex)
        {
            AppState.Instance.Log($"保存设置失败：{ex.Message}", "error");
            ShowSavedBar(InfoBarSeverity.Error, $"保存失败：{ex.Message}", false);
            return;
        }

        // Reflect normalization back into the controls.
        LoadFromSettings();

        ShowSavedBar(InfoBarSeverity.Success, $"已保存 · {DateTime.Now:HH:mm:ss}", true);
        AppState.Instance.Log("设置已保存并应用。");
    }

    private static int ToInt(NumberBox box, int fallback) => double.IsNaN(box.Value) ? fallback : (int)box.Value;

    /// <summary>
    /// Called by the shell when the user navigates away. Returns false when the user
    /// decides to stay on the page.
    /// </summary>
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (!IsDirty)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            Title = "有未保存的修改",
            Content = new TextBlock { Text = "离开设置页前是否保存？", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "保存并离开",
            SecondaryButtonText = "不保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        switch (result)
        {
            case ContentDialogResult.Primary:
                SaveSettings();
                return true;
            case ContentDialogResult.Secondary:
                LoadFromSettings(); // discard edits
                return true;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ //
    // cookie reveal
    // ------------------------------------------------------------------ //
    private void OnToggleCookieReveal(object sender, RoutedEventArgs e)
    {
        var reveal = CookieBox.PasswordRevealMode != PasswordRevealMode.Visible;
        CookieBox.PasswordRevealMode = reveal ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        CookieRevealButton.Content = reveal ? "隐藏" : "显示";
    }

    // ------------------------------------------------------------------ //
    // cookie
    // ------------------------------------------------------------------ //
    private void OnClearCookie(object sender, RoutedEventArgs e)
    {
        CookieBox.Password = "";
        SetDirty(true);
    }

    // ------------------------------------------------------------------ //
    // file name format picker
    // ------------------------------------------------------------------ //
    private void OnFormatBoxPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!FormatBox.IsReadOnly)
        {
            return;
        }

        ShowFormatMenu(FormatBox);
        e.Handled = true;
    }

    private void OnFormatMenuClick(object sender, RoutedEventArgs e) => ShowFormatMenu(FormatMenuButton);

    private void ShowSavedBar(InfoBarSeverity severity, string title, bool autoHide)
    {
        SavedBar.Severity = severity;
        SavedBar.Title = title;

        if (!SavedBar.IsOpen)
        {
            SavedBar.IsOpen = true;
            Motion.Reveal(SavedBar, true, 200);
        }

        _savedTimer.Stop();
        if (autoHide)
        {
            _savedTimer.Start();
        }
    }

    private void HideSavedBar()
    {
        if (!SavedBar.IsOpen)
        {
            return;
        }

        Motion.Reveal(SavedBar, false, 160, completed: () =>
        {
            SavedBar.IsOpen = false;
            SavedBar.Height = double.NaN;
            SavedBar.Opacity = 1;
        });
    }

    private void ShowFormatMenu(FrameworkElement anchor)
    {
        // Drop straight down from the anchor instead of the default centred popup.
        var flyout = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            // Let the popup extend past the window edge instead of flipping above the anchor.
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
                SetDirty(true);
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

        flyout.Closed += (_, _) => AnimateChevron(false);
        flyout.ShowAt(anchor);
        AnimateChevron(true);
    }

    private void AnimateChevron(bool open)
    {
        var animation = new DoubleAnimation
        {
            To = open ? 180 : 0,
            Duration = TimeSpan.FromMilliseconds(150),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(animation, FormatChevronRotate);
        Storyboard.SetTargetProperty(animation, "Angle");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    // ------------------------------------------------------------------ //
    // in-app login (system WebView2 runtime)
    // ------------------------------------------------------------------ //
    private async void OnLoginWithBrowser(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = new LoginWindow();
            window.Activate();

            var cookie = await window.Result;
            if (string.IsNullOrWhiteSpace(cookie))
            {
                return;
            }

            CookieBox.Password = cookie;
            SetDirty(true);

            ShowSavedBar(InfoBarSeverity.Informational, "已从登录窗口获取 Cookie，记得点底部「保存」", true);
        }
        catch (Exception ex)
        {
            AppState.Instance.Log($"打开登录窗口失败：{ex.Message}", "error");
            ShowSavedBar(InfoBarSeverity.Error, "内置登录窗口不可用，请手动粘贴 Cookie", false);
        }
    }

    // ------------------------------------------------------------------ //
    // download folder picker
    // ------------------------------------------------------------------ //
    private async void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add("*");

            var window = App.MainWindow;
            if (window is null)
            {
                return;
            }
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            RootBox.Text = folder.Path;
            SetDirty(true);
        }
        catch (Exception ex)
        {
            AppState.Instance.Log($"选择目录失败：{ex.Message}", "error");
        }
    }
}
