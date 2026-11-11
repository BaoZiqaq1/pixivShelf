using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PixivShelf.Core.Services;
using PixivShelf.Services;

namespace PixivShelf.Pages;

public sealed partial class TasksPage : Page
{
    public TasksPage()
    {
        InitializeComponent();
        PageSizing.Center(ContentRoot, this, 1280);
    }

    public ObservableCollection<TaskRowVm> Tasks => AppState.Instance.Tasks;

    private async void OnTaskAction(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string taskId })
        {
            return;
        }

        var record = AppState.Instance.Store.Get(taskId);
        if (record is null)
        {
            return;
        }

        switch (record.Status)
        {
            case Core.Models.DownloadTaskStatus.Running:
            case Core.Models.DownloadTaskStatus.Collecting:
                AppState.Instance.Pause(taskId);
                break;

            case Core.Models.DownloadTaskStatus.Paused:
            case Core.Models.DownloadTaskStatus.Failed:
                await AppState.Instance.ResumeAsync(taskId);
                break;

            default:
                OnOpenFolder(this, new RoutedEventArgs());
                break;
        }
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

    private async void OnPurge(object sender, RoutedEventArgs e)
    {
        var removed = AppState.Instance.Store.PurgeDoneOlderThan(TimeSpan.FromDays(30));
        foreach (var row in AppState.Instance.Tasks.Where(t => AppState.Instance.Store.Get(t.Id) is null).ToList())
        {
            AppState.Instance.Tasks.Remove(row);
        }

        AppState.Instance.Log($"已清理 {removed} 个 30 天前的已完成任务。");
        await ShowDialog("清理完成", $"删除了 {removed} 个旧任务记录（未完成的任务不会动）。");
    }

    private async void OnBackfill(object sender, RoutedEventArgs e)
    {
        var rootBox = new TextBox { Text = AppState.Instance.Settings.Storage.DownloadRoot, Header = "目录" };
        var dryRun = new CheckBox { Content = "只预览（不修改文件）", IsChecked = true };
        var rename = new CheckBox { Content = "同时改名为 pid_pN" };
        var zeroBased = new CheckBox { Content = "页码从 0 开始（p1 → p0）" };

        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        panel.Children.Add(rootBox);
        panel.Children.Add(dryRun);
        panel.Children.Add(rename);
        panel.Children.Add(zeroBased);

        var dialog = new ContentDialog
        {
            Title = "整理已有文件",
            Content = panel,
            PrimaryButtonText = "开始",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        dialog.Opened += (_, _) => AnimateDialogContent(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var root = rootBox.Text.Trim();
        if (!Directory.Exists(root))
        {
            AppState.Instance.Log($"目录不存在：{root}", "error");
            return;
        }

        AppState.Instance.Log($"开始整理 {root}…");
        SetBusyOverlay(true);
        try
        {
            var (plan, result, message) = await Task.Run(() => RunBackfill(root, dryRun.IsChecked == true, rename.IsChecked == true, zeroBased.IsChecked == true));
            AppState.Instance.Log(message, "info");
            await ShowDialog("整理完成", message);
        }
        finally
        {
            SetBusyOverlay(false);
        }
    }

    private static (int Plan, BackfillResult Result, string Message) RunBackfill(string root, bool dryRun, bool rename, bool zeroBased)
    {
        var settings = AppState.Instance.Settings;
        using var client = new PixivClient(settings, message => AppState.Instance.Log(message));
        var userId = client.ResolveSelfUserIdAsync(CancellationToken.None).GetAwaiter().GetResult();
        var periods = client.GetBookmarkPeriodsAsync(userId, CancellationToken.None).GetAwaiter().GetResult();
        var ordered = client.CollectBookmarkIdsAsync(userId, "", CancellationToken.None).GetAwaiter().GetResult();
        var times = BookmarkTime.Estimate(periods, ordered);

        var files = Backfill.Scan(root);
        var plan = Backfill.Plan(files, times);
        var result = Backfill.Apply(plan, dryRun, rename, zeroBased, message => AppState.Instance.Log(message));

        var prefix = dryRun ? "[预览] " : "";
        var message = $"{prefix}扫描 {files.Count} 个文件，可回填 {plan.Count} 个；" +
                      $"写入时间 {result.Times}，改名 {result.Renamed}，跳过 {result.Skipped}，失败 {result.Failed}。";
        return (plan.Count, result, message);
    }

    private async Task ShowDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "好",
            XamlRoot = XamlRoot,
        };
        dialog.Opened += (_, _) => AnimateDialogContent(dialog);
        await dialog.ShowAsync();
    }

    private void SetBusyOverlay(bool busy)
    {
        if (busy)
        {
            BusyOverlay.Visibility = Visibility.Visible;
            BusyOverlay.Opacity = 0;
            Motion.FadeIn(BusyOverlay, Motion.Fast);
        }
        else
        {
            Motion.FadeOut(BusyOverlay, Motion.Fast, 0, () =>
            {
                BusyOverlay.Visibility = Visibility.Collapsed;
                BusyOverlay.Opacity = 0;
            });
        }
    }

    private static void AnimateDialogContent(ContentDialog dialog)
    {
        if (dialog.Content is FrameworkElement content)
        {
            content.Opacity = 0;
            Motion.FadeIn(content, 180, 0, 0, 6);
        }
    }

    private readonly HashSet<Border> _hoveredCards = new();

    private void OnTaskCardPointerEntered(object sender, PointerRoutedEventArgs e) => SetCardHover(sender, true);

    private void OnTaskCardPointerMoved(object sender, PointerRoutedEventArgs e) => SetCardHover(sender, true);

    private void OnTaskCardPointerExited(object sender, PointerRoutedEventArgs e) => SetCardHover(sender, false);

    private void SetCardHover(object sender, bool hover)
    {
        if (sender is not Border card)
        {
            return;
        }

        if (hover)
        {
            if (!_hoveredCards.Add(card))
            {
                return;
            }

            Motion.ScaleTo(card, 1.004f, 200);
            card.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"];
        }
        else
        {
            if (!_hoveredCards.Remove(card))
            {
                return;
            }

            Motion.ScaleTo(card, 1f, 160);
            card.Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        }
    }
}
