using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PixivShelf.Core.Models;
using PixivShelf.Core.Services;
using PixivShelf.Services;

namespace PixivShelf.Pages;

public sealed partial class DownloadPage : Page
{
    private List<int> _previewIds = new();
    private string _previewSignature = "";
    private bool _scopeLoading = true;
    private bool _layoutReady;
    private bool _previewing;
    private bool _starting;
    private bool? _primaryBusy;
    private int _lastArtworkDone;
    private int _previewVersion;

    public DownloadPage()
    {
        InitializeComponent();
        PageSizing.Center(ContentRoot, RootScroll, 1280);
        Motion.AttachPressScale(PreviewButton);
        Motion.AttachPressScale(StartButton);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private DownloadSource CurrentSource =>
        SourcePid.IsChecked == true ? DownloadSource.Pid :
        SourceArtist.IsChecked == true ? DownloadSource.Artist :
        DownloadSource.Bookmarks;

    // ------------------------------------------------------------------ //
    // lifecycle
    // ------------------------------------------------------------------ //
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AppState.Instance.TasksChanged += OnTasksChanged;
        LoadScopeFromSettings();
        LoadBookmarkRest();
        _scopeLoading = false;
        RefreshLayout();
        _layoutReady = true;
        UpdatePrimaryButton();
        AutoStartIfRequested();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppState.Instance.TasksChanged -= OnTasksChanged;
    }

    private void OnTasksChanged()
    {
        // AppState raises this on the UI thread.
        UpdatePrimaryButton();
    }

    private async void AutoStartIfRequested()
    {
        var auto = Environment.GetEnvironmentVariable("PIXIV_UI_AUTOSTART");
        if (string.IsNullOrWhiteSpace(auto))
        {
            return;
        }

        if (auto.StartsWith("pid:", StringComparison.OrdinalIgnoreCase))
        {
            SourcePid.IsChecked = true;
            PasteBox.Text = auto[4..];
        }
        else if (auto.StartsWith("artist:", StringComparison.OrdinalIgnoreCase))
        {
            SourceArtist.IsChecked = true;
            PasteBox.Text = auto[7..];
        }
        else if (auto.Equals("bookmarks", StringComparison.OrdinalIgnoreCase))
        {
            SourceBookmarks.IsChecked = true;
        }
        else
        {
            return;
        }

        await Task.Delay(600);
        OnStart(this, new RoutedEventArgs());
    }

    // ------------------------------------------------------------------ //
    // layout state
    // ------------------------------------------------------------------ //
    private void OnSourceChanged(object sender, RoutedEventArgs e) => RefreshLayout();

    private void RefreshLayout()
    {
        if (PasteBox is null)
        {
            return;
        }

        var source = CurrentSource;
        var bookmarks = source == DownloadSource.Bookmarks;

        if (!_layoutReady)
        {
            InputPanel.Visibility = bookmarks ? Visibility.Collapsed : Visibility.Visible;
            BookmarkOptions.Visibility = bookmarks ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            Motion.Reveal(InputPanel, !bookmarks, 200);
            Motion.Reveal(BookmarkOptions, bookmarks, 200);
        }

        if (bookmarks)
        {
            ParseInfo.Text = "";
        }
        else
        {
            UpdateParseInfo();
        }

        // Switching source only clears the stale result, without nagging the user.
        _previewIds = new List<int>();
        _previewSignature = "";
        _previewVersion++;
        PreviewText.Text = "";
        PreviewText.Opacity = 1;
        ProgressBar.Value = 0;
        if (ProgressBar.Visibility == Visibility.Visible)
        {
            Motion.Reveal(ProgressBar, false, 140);
        }
    }

    private void OnPasteChanged(object sender, TextChangedEventArgs e) => UpdateParseInfo();

    private void UpdateParseInfo()
    {
        if (CurrentSource == DownloadSource.Bookmarks)
        {
            ParseInfo.Text = "";
            return;
        }

        if (CurrentSource == DownloadSource.Artist)
        {
            var artistId = PasteParser.ParseArtistId(PasteBox.Text);
            ParseInfo.Text = artistId is null ? "" : $"识别到画师 {artistId}";
            return;
        }

        var (ids, rejected) = PasteParser.Parse(PasteBox.Text);
        var message = ids.Count > 0 ? $"识别到 {ids.Count} 个 PID" : "";
        if (ids.Count > 0 && rejected.Count > 0)
        {
            message += $"（忽略 {rejected.Count} 段无法识别的内容）";
        }
        ParseInfo.Text = message;
    }

    // ------------------------------------------------------------------ //
    // content scope (moved here from the settings page)
    // ------------------------------------------------------------------ //
    private void LoadScopeFromSettings()
    {
        var filter = AppState.Instance.Settings.Filter;
        ScopeAll.IsChecked = filter.Scope == ContentScope.All;
        ScopeR18Only.IsChecked = filter.Scope == ContentScope.R18Only;
        ScopeSkipR18.IsChecked = filter.Scope == ContentScope.SkipR18;
        SkipAiSwitch.IsOn = filter.SkipAiGenerated;
        BlockR18GSwitch.IsOn = filter.BlockR18G;
        UpdateR18GVisibility();
    }

    /// <summary>「屏蔽 R18G」只在「所有 / 成人内容」下有意义；「全年龄」时直接隐藏。</summary>
    private void UpdateR18GVisibility()
    {
        var scope = AppState.Instance.Settings.Filter.Scope;
        var visible = scope != ContentScope.SkipR18;
        if (!_layoutReady)
        {
            BlockR18GSwitch.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        Motion.Reveal(BlockR18GSwitch, visible, 180);
    }


    private void OnScopeChanged(object sender, RoutedEventArgs e)
    {
        if (_scopeLoading)
        {
            return;
        }

        AppState.Instance.Settings.Filter.Scope = ScopeR18Only.IsChecked == true
            ? ContentScope.R18Only
            : ScopeSkipR18.IsChecked == true
                ? ContentScope.SkipR18
                : ContentScope.All;
        PersistScope();
        UpdateR18GVisibility();
    }

    private void OnSkipAiToggled(object sender, RoutedEventArgs e)
    {
        if (_scopeLoading)
        {
            return;
        }

        AppState.Instance.Settings.Filter.SkipAiGenerated = SkipAiSwitch.IsOn;
        PersistScope();
    }

    private void OnBlockR18GToggled(object sender, RoutedEventArgs e)
    {
        if (_scopeLoading)
        {
            return;
        }

        AppState.Instance.Settings.Filter.BlockR18G = BlockR18GSwitch.IsOn;
        PersistScope();
    }

    private void LoadBookmarkRest()
    {
        if (RestAll is null)
        {
            return;
        }

        var rest = AppState.Instance.Settings.Filter.BookmarkRest;
        RestAll.IsChecked = rest == BookmarkRest.All;
        RestPublic.IsChecked = rest == BookmarkRest.Public;
        RestPrivate.IsChecked = rest == BookmarkRest.Private;
    }

    private void OnBookmarkRestChanged(object sender, RoutedEventArgs e)
    {
        if (_scopeLoading)
        {
            return;
        }

        AppState.Instance.Settings.Filter.BookmarkRest = RestPublic.IsChecked == true
            ? BookmarkRest.Public
            : RestPrivate.IsChecked == true
                ? BookmarkRest.Private
                : BookmarkRest.All;

        try
        {
            AppState.Instance.Settings.Save();
        }
        catch (Exception ex)
        {
            AppState.Instance.Log($"保存收藏范围失败：{ex.Message}", "error");
        }
    }

    private void PersistScope()
    {
        try
        {
            AppState.Instance.Settings.Save();
            AppState.Instance.ReloadSettings();
        }
        catch (Exception ex)
        {
            AppState.Instance.Log($"保存内容范围失败：{ex.Message}", "error");
        }


    }

    // ------------------------------------------------------------------ //
    // primary button: 下载 / 暂停
    // ------------------------------------------------------------------ //
    private void UpdatePrimaryButton()
    {
        var busy = AppState.Instance.HasActiveTask;
        if (_primaryBusy == busy)
        {
            return;
        }

        _primaryBusy = busy;
        Motion.SwapContent(StartButton, busy ? "暂停" : "下载", 120);
    }

    private void SetPreview(string text, bool animate = true)
    {
        if (PreviewText.Text == text)
        {
            return;
        }

        var version = ++_previewVersion;
        if (animate)
        {
            Motion.FadeTo(PreviewText, 0.45, 60, 0, () =>
            {
                if (version != _previewVersion)
                {
                    return;
                }

                PreviewText.Text = text;
                Motion.FadeTo(PreviewText, 1, 120);
            });
        }
        else
        {
            PreviewText.Text = text;
        }
    }

    // ------------------------------------------------------------------ //
    // request building
    // ------------------------------------------------------------------ //
    private bool TryBuildRequest(out DownloadRequest request, out string error)
    {
        request = new DownloadRequest { Source = CurrentSource };
        error = "";

        switch (request.Source)
        {
            case DownloadSource.Pid:
                var (ids, _) = PasteParser.Parse(PasteBox.Text);
                if (ids.Count == 0)
                {
                    error = "没有识别到有效的 PID，请输入作品 ID 或作品链接。";
                    return false;
                }
                request.Pids = ids;
                return true;

            case DownloadSource.Artist:
                var artistId = PasteParser.ParseArtistId(PasteBox.Text);
                if (artistId is null)
                {
                    error = "没有识别到画师 ID，请输入画师 ID 或画师主页链接。";
                    return false;
                }
                request.ArtistId = artistId;
                return true;

            default:
                request.BookmarkRest = AppState.Instance.Settings.Filter.BookmarkRest;
                return true;
        }
    }

    private string Signature(DownloadRequest request) => string.Join("|",
        request.Source, request.ArtistId, string.Join(",", request.Pids));

    // ------------------------------------------------------------------ //
    // actions
    // ------------------------------------------------------------------ //
    private async void OnPreview(object sender, RoutedEventArgs e)
    {
        if (_previewing || _starting)
        {
            return;
        }

        if (!TryBuildRequest(out var request, out var error))
        {
            SetPreview(error);
            return;
        }

        _previewing = true;
        SetBusy(true);
        SetPreview("抓取中…");
        try
        {
            var engine = new DownloadEngine(AppState.Instance.Settings, message => AppState.Instance.Log(message));
            var ids = await engine.CollectAsync(request, CancellationToken.None, message => AppState.Instance.Log(message));
            _previewIds = ids;
            _previewSignature = Signature(request);
            if (ids.Count == 0)
            {
                SetPreview("没有可下载的作品（检查 ID 或内容范围）。");
                return;
            }
            var preview = string.Join("、", ids.Take(8)) + (ids.Count > 8 ? "…" : "");
            SetPreview($"共 {ids.Count} 个作品：{preview}");
            AppState.Instance.Log($"预览完成：{ids.Count} 个作品。");
        }
        catch (PixivException ex)
        {
            SetPreview($"抓取失败：{ex.Message}");
            AppState.Instance.Log($"抓取失败：{ex.Message}", "error");
        }
        catch (Exception ex)
        {
            SetPreview($"抓取失败：{ex.Message}");
            AppState.Instance.Log($"抓取失败：{ex.Message}", "error");
        }
        finally
        {
            _previewing = false;
            SetBusy(false);
            UpdatePrimaryButton();
        }
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        // A running task turns this button into "暂停".
        var active = AppState.Instance.LatestActiveTask;
        if (active is not null)
        {
            AppState.Instance.Pause(active.Id);
            SetPreview("已请求暂停，当前这张图下载完成后停止。");
            return;
        }

        if (_starting || _previewing)
        {
            return;
        }

        if (!TryBuildRequest(out var request, out var error))
        {
            SetPreview(error);
            return;
        }

        _starting = true;
        _lastArtworkDone = -1;
        SetBusy(true);
        ProgressBar.Value = 0;
        Motion.Reveal(ProgressBar, true, 160);
        SetPreview("已加入队列，正在准备…");

        var progress = new Progress<DownloadProgress>(p =>
        {
            if (p.ArtworksTotal > 0 && p.ArtworksDone != _lastArtworkDone)
            {
                _lastArtworkDone = p.ArtworksDone;
                Motion.SetProgress(ProgressBar, (double)p.ArtworksDone / p.ArtworksTotal, 240);
            }

            var message = $"{p.ArtworksDone}/{p.ArtworksTotal} 个作品 · 图片 {p.ImagesDone} 张";
            if (PreviewText.Text != message)
            {
                _previewVersion++;
                PreviewText.Text = message;
            }
        });

        try
        {
            var failure = await AppState.Instance.StartAsync(request, progress);
            if (failure is not null)
            {
                SetPreview($"任务失败：{failure}");
            }
            else
            {
                SetPreview("任务已结束，可在「任务」页查看记录。");
                Motion.Reveal(ProgressBar, false, 200);
            }
        }
        finally
        {
            _starting = false;
            SetBusy(false);
            UpdatePrimaryButton();
        }
    }

    private void SetBusy(bool busy)
    {
        PreviewButton.IsEnabled = !busy;
    }
}
