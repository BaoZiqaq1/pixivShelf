using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PixivShelf.Pages;
using PixivShelf.Core.Services;
using PixivShelf.Services;
using Windows.Graphics;

namespace PixivShelf;

public sealed partial class MainWindow : Window
{
    private string? _resumeTaskId;
    private readonly DispatcherTimer _speedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private long _lastSpeedBytes;
    private DateTimeOffset _lastSpeedSample = DateTimeOffset.UtcNow;
    private double _speedBytesPerSecond;
    private SettingsPage? _settingsPage;
    private string _currentTag = "";
    private bool _suppressNav;
    private bool _navDialogOpen;
    private bool _saveBarVisible;
    private bool _noticeProgrammatic;
    private bool _firstActivated;
    private bool _speedDimmed = true;
    private int _toastVersion;
    private NavigationViewItem? _lastNavItem;
    private DispatcherTimer? _logScrollTimer;
    private DispatcherTimer? _toastTimer;

    public MainWindow()
    {
        StartupTrace.Write("ctor: begin");
        InitializeComponent();
        StartupTrace.Write("ctor: InitializeComponent done");

        Root.Opacity = 0;
        Activated += OnFirstActivated;

        Title = "pixivShelf";
        AppState.Instance.Initialize(DispatcherQueue);
        StartupTrace.Write("ctor: AppState initialized");
        AppState.Instance.Toast += OnToast;
        AppState.Instance.TaskCompleted += OnTaskCompleted;
        AppState.Instance.LogLines.CollectionChanged += (_, _) => QueueLogScroll();
        Notice.Closing += OnNoticeClosing;
        LogPane.SizeChanged += (_, _) => UpdateLogClip();
        Motion.AttachPressScale(SaveButton);
        Motion.AttachPressScale(LogToggle);

        ResizeWindow();
        StartupTrace.Write("ctor: resized");
        Navigate("download");
        _lastNavItem = Nav.SelectedItem as NavigationViewItem;
        StartupTrace.Write("ctor: navigated to download");
        ApplyStartupPageOverride();
        CheckUnfinishedTasks();
        UpdateStatus();

        _speedTimer.Tick += (_, _) => UpdateSpeed();
        _speedTimer.Start();

        StartupTrace.Write("ctor: done");
    }

    /// <summary>Test hook: PIXIV_UI_START=tasks|settings opens that page at launch.</summary>
    private void ApplyStartupPageOverride()
    {
        var target = Environment.GetEnvironmentVariable("PIXIV_UI_START");
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            if (string.Equals(item.Tag as string, target, StringComparison.OrdinalIgnoreCase))
            {
                Nav.SelectedItem = item;
                Navigate(target);
                break;
            }
        }
    }


    public ObservableCollection<string> LogLines => AppState.Instance.LogLines;

    private void ResizeWindow()
    {
        try
        {
            AppWindow.Resize(new SizeInt32(1440, 900));
        }
        catch
        {
            // keep default size
        }

        try
        {
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(icon))
            {
                AppWindow.SetIcon(icon);
            }
        }
        catch
        {
            // icon is cosmetic
        }
    }

    // ------------------------------------------------------------------ //
    private void Navigate(string tag)
    {
        var pageType = tag switch
        {
            "tasks" => typeof(TasksPage),
            "settings" => typeof(SettingsPage),
            "about" => typeof(AboutPage),
            _ => typeof(DownloadPage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType, null, new EntranceNavigationTransitionInfo());
        }

        _currentTag = tag;

        if (pageType == typeof(SettingsPage) && ContentFrame.Content is SettingsPage page)
        {
            if (!ReferenceEquals(_settingsPage, page))
            {
                if (_settingsPage is not null)
                {
                    _settingsPage.DirtyStateChanged -= UpdateSaveBar;
                }
                _settingsPage = page;
                _settingsPage.DirtyStateChanged += UpdateSaveBar;
            }
        }

        UpdateSaveBar();
    }

    private void UpdateSaveBar()
    {
        var onSettings = _currentTag == "settings";
        var appeared = onSettings && !_saveBarVisible;

        SaveButton.Visibility = onSettings ? Visibility.Visible : Visibility.Collapsed;
        SaveSeparator.Visibility = onSettings ? Visibility.Visible : Visibility.Collapsed;
        _saveBarVisible = onSettings;

        if (appeared)
        {
            Motion.FadeIn(SaveButton, Motion.Fast);
            Motion.FadeIn(SaveSeparator, Motion.Fast);
        }

        var dirty = onSettings && _settingsPage?.IsDirty == true;
        if (dirty && DirtyHint.Visibility != Visibility.Visible)
        {
            DirtyHint.Visibility = Visibility.Visible;
            Motion.FadeIn(DirtyHint, Motion.Fast);
        }
        else if (!dirty)
        {
            DirtyHint.Visibility = Visibility.Collapsed;
        }

        SaveButton.IsEnabled = dirty;
    }

    private void OnSaveSettings(object sender, RoutedEventArgs e)
    {
        _settingsPage?.SaveSettings();
        UpdateSaveBar();
    }

    private async void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressNav || _navDialogOpen)
        {
            return;
        }

        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        // Leaving the settings page with pending edits asks first.
        if (_currentTag == "settings" && tag != "settings" && _settingsPage?.IsDirty == true)
        {
            _navDialogOpen = true;
            bool canLeave;
            try
            {
                canLeave = await _settingsPage.ConfirmLeaveAsync();
            }
            finally
            {
                _navDialogOpen = false;
            }

            if (!canLeave)
            {
                _suppressNav = true;
                foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
                {
                    if (string.Equals(item.Tag as string, "settings", StringComparison.Ordinal))
                    {
                        Nav.SelectedItem = item;
                        break;
                    }
                }
                _suppressNav = false;
                return;
            }
        }

        Navigate(tag);
        AnimateNavIcon(args.SelectedItem as NavigationViewItem);
    }

    private void CheckUnfinishedTasks()
    {
        var active = AppState.Instance.LoadActiveTasks()
            .Where(t => t.Status is Core.Models.DownloadTaskStatus.Paused or Core.Models.DownloadTaskStatus.Running)
            .ToList();

        if (active.Count == 0)
        {
            return;
        }

        _resumeTaskId = active[0].Id;
        ShowNotice(InfoBarSeverity.Warning, $"有 {active.Count} 个未完成任务", $"「{active[0].Title}」上次没下完，点「继续」接着下（已下载的文件会跳过）。");
    }

    private async void OnResumePrompt(object sender, RoutedEventArgs e)
    {
        HideNotice();
        if (_resumeTaskId is not null)
        {
            await AppState.Instance.ResumeAsync(_resumeTaskId);
            _resumeTaskId = null;
        }
    }

    private void OnToast(string message)
    {
        ShowNotice(InfoBarSeverity.Error, "出错了", message);
    }

    private void OnTaskCompleted(string message) => ShowToast(message);

    private void ShowNotice(InfoBarSeverity severity, string title, string message)
    {
        Notice.Severity = severity;
        Notice.Title = title;
        Notice.Message = message;

        if (Notice.IsOpen)
        {
            return;
        }

        Notice.IsOpen = true;
        Motion.Reveal(Notice, true, 200);
    }

    private void HideNotice()
    {
        if (_noticeProgrammatic)
        {
            return;
        }

        if (!Notice.IsOpen && Notice.ActualHeight <= 0)
        {
            return;
        }

        _noticeProgrammatic = true;
        Motion.Reveal(Notice, false, 160, completed: () =>
        {
            Notice.IsOpen = false;
            Notice.Height = double.NaN;
            Notice.Opacity = 1;
            _noticeProgrammatic = false;
        });
    }

    private void OnNoticeClosing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        if (_noticeProgrammatic)
        {
            return;
        }

        args.Cancel = true;
        DispatcherQueue.TryEnqueue(HideNotice);
    }

    private void ShowToast(string message)
    {
        var version = ++_toastVersion;
        ToastText.Text = message;
        ToastHost.Visibility = Visibility.Visible;
        ToastHost.Opacity = 0;
        Motion.SetTranslation(ToastHost, 0, 12);
        Motion.FadeIn(ToastHost, 200, 0, 0, 12);

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer?.Stop();
            if (version != _toastVersion)
            {
                return;
            }

            Motion.FadeOut(ToastHost, 160, 0, () =>
            {
                if (version != _toastVersion)
                {
                    return;
                }

                ToastHost.Visibility = Visibility.Collapsed;
                ToastHost.Opacity = 0;
                Motion.SetTranslation(ToastHost, 0, 0);
            });
        };
        _toastTimer.Start();
    }

    private void OnToggleLog(object sender, RoutedEventArgs e)
    {
        var show = LogToggle.IsChecked == true;
        if (show)
        {
            Motion.Reveal(LogPane, true, 200, 200, completed: ScrollLogToEnd);
        }
        else
        {
            Motion.Reveal(LogPane, false, 200, 200);
        }
    }

    private void QueueLogScroll()
    {
        if (_logScrollTimer is null)
        {
            _logScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _logScrollTimer.Tick += (_, _) =>
            {
                _logScrollTimer?.Stop();
                ScrollLogToEnd();
            };
        }

        _logScrollTimer.Stop();
        _logScrollTimer.Start();
    }

    private void ScrollLogToEnd()
    {
        if (LogPane.Visibility != Visibility.Visible || LogLines.Count == 0)
        {
            return;
        }

        try
        {
            LogList.ScrollIntoView(LogLines[^1]);
        }
        catch
        {
            // list not measured yet
        }
    }

    private void UpdateLogClip()
    {
        if (LogPane.ActualWidth <= 0 || LogPane.ActualHeight <= 0)
        {
            LogPane.Clip = null;
            return;
        }

        LogPane.Clip = new RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, LogPane.ActualWidth, LogPane.ActualHeight),
        };
    }

    private void UpdateSpeed()
    {
        var now = DateTimeOffset.UtcNow;
        var seconds = (now - _lastSpeedSample).TotalSeconds;
        if (seconds <= 0.05)
        {
            return;
        }

        var total = TransferMeter.Read();
        var instant = Math.Max(0, total - _lastSpeedBytes) / seconds;
        _lastSpeedBytes = total;
        _lastSpeedSample = now;

        // Smooth so the number does not flicker between samples.
        _speedBytesPerSecond = _speedBytesPerSecond <= 0
            ? instant
            : (_speedBytesPerSecond * 0.5) + (instant * 0.5);

        // No task running (paused / finished): show zero right away instead of decaying.
        if (!AppState.Instance.Tasks.Any(task => task.IsActive))
        {
            _speedBytesPerSecond = 0;
        }
        else if (instant < 1 && _speedBytesPerSecond < 1024)
        {
            _speedBytesPerSecond = 0;
        }

        var speedText = "↓" + FormatSpeed(_speedBytesPerSecond);
        if (SpeedText.Text != speedText)
        {
            SpeedText.Text = speedText;
        }

        var dimmed = _speedBytesPerSecond < 1024;
        if (dimmed != _speedDimmed)
        {
            _speedDimmed = dimmed;
            Motion.FadeTo(SpeedText, dimmed ? 0.55 : 1.0, Motion.Fast);
        }

        // Keep the left hand status in step with the task list.
        UpdateStatus();
    }

    internal static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1024 * 1024)
        {
            return $"{bytesPerSecond / (1024 * 1024):0.0} MB/s";
        }

        if (bytesPerSecond >= 1024)
        {
            return $"{bytesPerSecond / 1024:0} KB/s";
        }

        return "0 KB/s";
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        var root = AppState.Instance.Settings.Storage.DownloadRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            AppState.Instance.Log("下载路径为空，请先到设置里选择目录。", "warn");
            return;
        }

        try
        {
            Directory.CreateDirectory(root);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{root}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppState.Instance.Log($"打开目录失败：{ex.Message}", "error");
        }
    }

    private void UpdateStatus()
    {
        // The bar only carries a state word: 就绪 / 下载中 / 暂停.
        var tasks = AppState.Instance.Tasks;
        var active = tasks.Any(task => task.IsActive);
        var paused = !active && tasks.Any(task => task.Record.Status == Core.Models.DownloadTaskStatus.Paused);

        var text = active ? "下载中" : paused ? "暂停" : "就绪";
        if (StatusText.Text != text)
        {
            Motion.SwapText(StatusText, text, 120);
        }
    }

    private void AnimateNavIcon(NavigationViewItem? item)
    {
        if (_lastNavItem is not null && !ReferenceEquals(_lastNavItem, item) && _lastNavItem.Icon is FrameworkElement oldIcon)
        {
            Motion.ScaleTo(oldIcon, 1f, 120);
        }

        if (item?.Icon is FrameworkElement icon)
        {
            Motion.Pulse(icon, 1.06f, Motion.Fast);
        }

        _lastNavItem = item;
    }

    private void OnFirstActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_firstActivated)
        {
            return;
        }

        _firstActivated = true;
        Motion.FadeIn(Root, 200);
    }
}
