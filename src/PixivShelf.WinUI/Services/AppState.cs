using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using PixivShelf.Core.Models;
using PixivShelf.Core.Services;

namespace PixivShelf.Services;

public sealed class TaskRowVm : INotifyPropertyChanged
{
    private TaskRecord _record;
    private DownloadTaskStatus _lastStatus;
    private double _lastProgress;
    private int _lastDone;
    private int _lastTotal;
    private string _lastNote = "";

    public TaskRowVm(TaskRecord record)
    {
        _record = record;
        _lastStatus = record.Status;
        _lastProgress = record.Progress;
        _lastDone = record.Done;
        _lastTotal = record.Total;
        _lastNote = record.Note;
    }

    public string Id => _record.Id;

    public TaskRecord Record => _record;

    public string Title => _record.Title;

    public string StatusText => _record.Status switch
    {
        DownloadTaskStatus.Pending => "等待中",
        DownloadTaskStatus.Collecting => "抓取中",
        DownloadTaskStatus.Running => "进行中",
        DownloadTaskStatus.Paused => "已暂停",
        DownloadTaskStatus.Done => "已完成",
        DownloadTaskStatus.Cancelled => "已取消",
        DownloadTaskStatus.Failed => "失败",
        _ => "未知",
    };

    public double Progress => _record.Progress;

    public string Counts => _record.Total > 0 ? $"{_record.Done}/{_record.Total} 个作品" : "";

    public string Note => _record.Note;

    public bool IsActive => _record.Status is DownloadTaskStatus.Running or DownloadTaskStatus.Collecting;

    public bool CanResume => _record.Status is DownloadTaskStatus.Paused or DownloadTaskStatus.Failed;

    public bool IsPaused => _record.Status == DownloadTaskStatus.Paused;

    public bool IsFailed => _record.Status == DownloadTaskStatus.Failed;

    public string ActionText => IsActive ? "暂停" : CanResume ? "继续" : "打开目录";

    public void Update(TaskRecord record)
    {
        var statusChanged = _lastStatus != record.Status;
        var progressChanged = Math.Abs(_lastProgress - record.Progress) > 0.0001;
        var countsChanged = _lastDone != record.Done || _lastTotal != record.Total;
        var noteChanged = !string.Equals(_lastNote, record.Note, StringComparison.Ordinal);

        _record = record;

        if (statusChanged)
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsActive));
            OnPropertyChanged(nameof(CanResume));
            OnPropertyChanged(nameof(ActionText));
        }

        if (progressChanged)
        {
            OnPropertyChanged(nameof(Progress));
        }

        if (countsChanged)
        {
            OnPropertyChanged(nameof(Counts));
        }

        if (noteChanged)
        {
            OnPropertyChanged(nameof(Note));
        }

        _lastStatus = record.Status;
        _lastProgress = record.Progress;
        _lastDone = record.Done;
        _lastTotal = record.Total;
        _lastNote = record.Note;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TaskPayload
{
    public DownloadRequest Request { get; set; } = new();
    public List<int> Ids { get; set; } = new();
}

/// <summary>Owns settings, the task store and the running tasks (single instance).</summary>
public sealed class AppState
{
    private readonly Dictionary<string, CancellationTokenSource> _running = new();
    private readonly Dictionary<string, bool> _cancelRequested = new();

    public static AppState Instance { get; } = new();

    public AppSettings Settings { get; private set; } = new();
    public TaskStore Store { get; private set; } = null!;
    public ObservableCollection<TaskRowVm> Tasks { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public DispatcherQueue? Dispatcher { get; private set; }

    public event Action<string>? Toast;

    /// <summary>Raised (on the UI thread) when a task reaches the Done state.</summary>
    public event Action<string>? TaskCompleted;

    /// <summary>Raised (on the UI thread) whenever a task is added or its state changes.</summary>
    public event Action? TasksChanged;

    public bool HasActiveTask => Tasks.Any(task => task.IsActive);

    public TaskRowVm? LatestActiveTask => Tasks.FirstOrDefault(task => task.IsActive);

    private void NotifyTasksChanged()
    {
        RunOnUi(() => TasksChanged?.Invoke());
    }

    public void Initialize(DispatcherQueue dispatcher)
    {
        Dispatcher = dispatcher;
        StartupTrace.Write($"init: dispatcher ok; dir={AppSettings.DefaultDirectory}");
        Settings = AppSettings.Load();
        StartupTrace.Write("init: settings loaded");

        // Materialise config.json on first run (it starts from the legacy config next to the exe).
        if (!File.Exists(AppSettings.DefaultConfigPath))
        {
            Settings.Save();
            StartupTrace.Write("init: settings saved");
        }

        try
        {
            Store = new TaskStore();
            StartupTrace.Write("init: task store opened");
        }
        catch (Exception ex)
        {
            StartupTrace.Write($"init: task store FAILED {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        Store.PurgeDoneOlderThan(TimeSpan.FromDays(30));
        StartupTrace.Write("init: purge done");

        // A task cannot still be running after a restart: mark it paused so the UI
        // offers "继续" instead of pretending it is alive.
        foreach (var record in Store.All())
        {
            if (record.Status is DownloadTaskStatus.Running or DownloadTaskStatus.Collecting)
            {
                record.Status = DownloadTaskStatus.Paused;
                record.Note = "上次异常退出，点「继续」接着下";
                Store.Save(record);
            }
        }

        foreach (var record in Store.All())
        {
            Tasks.Add(new TaskRowVm(record));
        }
    }

    public void ReloadSettings() => Settings = AppSettings.Load();

    // ------------------------------------------------------------------ //
    // log / toast
    // ------------------------------------------------------------------ //
    public void Log(string message, string level = "info")
    {
        var prefix = level switch
        {
            "error" => "✖ ",
            "warn" => "! ",
            _ => "· ",
        };
        var line = $"{DateTime.Now:HH:mm:ss} {prefix}{message}";
        RunOnUi(() =>
        {
            LogLines.Add(line);
            while (LogLines.Count > 800)
            {
                LogLines.RemoveAt(0);
            }
        });
    }

    private void RunOnUi(Action action)
    {
        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            action();
            return;
        }
        dispatcher.TryEnqueue(() => action());
    }

    // ------------------------------------------------------------------ //
    // task lifecycle
    // ------------------------------------------------------------------ //
    public async Task<string?> StartAsync(DownloadRequest request, IProgress<DownloadProgress>? progress = null)
    {
        var record = new TaskRecord
        {
            Title = request.Title,
            Source = request.Source,
            Status = DownloadTaskStatus.Collecting,
        };
        Store.Save(record);
        var vm = new TaskRowVm(record);
        RunOnUi(() =>
        {
            Tasks.Insert(0, vm);
            TasksChanged?.Invoke();
        });

        var cts = new CancellationTokenSource();
        _running[record.Id] = cts;
        _cancelRequested[record.Id] = false;

        try
        {
            var engine = new DownloadEngine(Settings, message => Log(message));
            var ids = await engine.CollectAsync(request, cts.Token, message => Log(message)).ConfigureAwait(false);

            record.Total = ids.Count;
            record.Done = 0;
            record.Status = DownloadTaskStatus.Running;
            record.ParamsJson = JsonSerializer.Serialize(new TaskPayload { Request = request, Ids = ids });
            Store.Save(record);
            Publish(vm, record);

            // Per-artwork rows let a later resume continue instead of re-scanning.
            foreach (var artworkId in ids)
            {
                Store.SaveItem(new TaskItemRecord { TaskId = record.Id, ArtworkId = artworkId, Status = "pending" });
            }

            var completed = 0;
            void OnArtworkCompleted(int artworkId)
            {
                Store.SaveItem(new TaskItemRecord { TaskId = record.Id, ArtworkId = artworkId, Status = "done" });
                record.Done = Interlocked.Increment(ref completed);
                Store.Save(record);
                Publish(vm, record);
            }

            var engineProgress = new Progress<DownloadProgress>(p =>
            {
                record.Note = $"图片 {p.ImagesDone} 张" + (p.ImagesFailed > 0 ? $"，失败 {p.ImagesFailed}" : "");
                Publish(vm, record);
                progress?.Report(p);
            });

            var result = await engine
                .RunAsync(request, ids, engineProgress, cts.Token, message => Log(message), OnArtworkCompleted)
                .ConfigureAwait(false);

            record.Status = _cancelRequested[record.Id] ? DownloadTaskStatus.Paused : DownloadTaskStatus.Done;
            record.Done = record.Status == DownloadTaskStatus.Done ? ids.Count : record.Done;
            record.Failed = result.ImagesFailed;
            record.Note = $"成功 {result.ImagesOk}，跳过 {result.ImagesSkipped}，失败 {result.ImagesFailed}";
            record.FinishedAt = DateTimeOffset.Now;
            Store.Save(record);
            Publish(vm, record);
            Log($"任务「{record.Title}」结束：{record.Note}");
            if (record.Status == DownloadTaskStatus.Done)
            {
                RunOnUi(() => TaskCompleted?.Invoke($"任务完成：{record.Title}"));
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            record.Status = DownloadTaskStatus.Paused;
            record.Note = "已暂停，可点「继续」接着下";
            Store.Save(record);
            Publish(vm, record);
            Log($"任务「{record.Title}」已暂停。", "warn");
            return null;
        }
        catch (Exception ex)
        {
            record.Status = DownloadTaskStatus.Failed;
            record.Note = ex.Message.Length > 120 ? ex.Message[..120] : ex.Message;
            record.FinishedAt = DateTimeOffset.Now;
            Store.Save(record);
            Publish(vm, record);
            Log($"任务「{record.Title}」失败：{ex.Message}", "error");
            RunOnUi(() => Toast?.Invoke($"失败：{ex.Message}"));
            return ex.Message;
        }
        finally
        {
            _running.Remove(record.Id);
            _cancelRequested.Remove(record.Id);
            cts.Dispose();
        }
    }

    public void Pause(string taskId)
    {
        _cancelRequested[taskId] = true;
        if (_running.TryGetValue(taskId, out var cts))
        {
            cts.Cancel();
        }
    }

    public void Cancel(string taskId)
    {
        _cancelRequested[taskId] = true;
        if (_running.TryGetValue(taskId, out var cts))
        {
            cts.Cancel();
        }
        var record = Store.Get(taskId);
        if (record is not null && record.Status is not DownloadTaskStatus.Running and not DownloadTaskStatus.Collecting)
        {
            record.Status = DownloadTaskStatus.Cancelled;
            record.FinishedAt = DateTimeOffset.Now;
            Store.Save(record);
            Refresh(taskId, record);
        }
    }

    public async Task ResumeAsync(string taskId)
    {
        var record = Store.Get(taskId);
        if (record is null)
        {
            return;
        }

        TaskPayload? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<TaskPayload>(record.ParamsJson);
        }
        catch
        {
            // fall through to failure below
        }

        if (payload is null || payload.Ids.Count == 0)
        {
            Log("这个任务没有保存抓取结果，无法继续，请重新新建任务。", "warn");
            return;
        }

        // Resume only the artworks that were not finished yet.
        var items = Store.Items(taskId);
        var pendingIds = items
            .Where(item => !string.Equals(item.Status, "done", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ArtworkId)
            .ToList();

        if (items.Count == 0)
        {
            // Tasks created before per-artwork rows existed: fall back to the payload.
            pendingIds = payload.Ids.ToList();
            foreach (var artworkId in pendingIds)
            {
                Store.SaveItem(new TaskItemRecord { TaskId = taskId, ArtworkId = artworkId, Status = "pending" });
            }
            items = Store.Items(taskId);
        }

        var alreadyDone = items.Count - pendingIds.Count;
        if (pendingIds.Count == 0)
        {
            record.Status = DownloadTaskStatus.Done;
            record.Done = record.Total > 0 ? record.Total : items.Count;
            record.Note = "所有作品都已下载";
            record.FinishedAt = DateTimeOffset.Now;
            Store.Save(record);
            Refresh(taskId, record);
            Log("任务已完成，没有需要继续的内容。");
            return;
        }

        record.Total = items.Count;
        record.Done = alreadyDone;
        record.Status = DownloadTaskStatus.Running;
        record.Note = $"继续中（剩余 {pendingIds.Count} 个作品）";
        Store.Save(record);
        Refresh(taskId);
        Log($"继续任务：已完成 {alreadyDone}，剩余 {pendingIds.Count}。");

        var cts = new CancellationTokenSource();
        _running[taskId] = cts;
        _cancelRequested[taskId] = false;

        try
        {
            var engine = new DownloadEngine(Settings, message => Log(message));

            var completed = alreadyDone;
            void OnArtworkCompleted(int artworkId)
            {
                Store.SaveItem(new TaskItemRecord { TaskId = taskId, ArtworkId = artworkId, Status = "done" });
                var done = Interlocked.Increment(ref completed);
                var current = Store.Get(taskId);
                if (current is not null)
                {
                    current.Done = done;
                    Store.Save(current);
                }
                Refresh(taskId, current);
            }

            var progress = new Progress<DownloadProgress>(p =>
            {
                // Per-image callback: keep it off the database, only refresh the row.
                record.Note = $"图片 {p.ImagesDone} 张";
                Refresh(taskId, record);
            });

            var result = await engine
                .RunAsync(payload.Request, pendingIds, progress, cts.Token, message => Log(message), OnArtworkCompleted)
                .ConfigureAwait(false);

            record = Store.Get(taskId) ?? record;
            record.Status = _cancelRequested[taskId] ? DownloadTaskStatus.Paused : DownloadTaskStatus.Done;
            if (record.Status == DownloadTaskStatus.Done)
            {
                record.Done = record.Total;
            }
            record.Note = $"成功 {result.ImagesOk}，跳过 {result.ImagesSkipped}，失败 {result.ImagesFailed}";
            record.FinishedAt = DateTimeOffset.Now;
            Store.Save(record);
            Refresh(taskId, record);
            if (record.Status == DownloadTaskStatus.Done)
            {
                RunOnUi(() => TaskCompleted?.Invoke($"任务完成：{record.Title}"));
            }
        }
        catch (OperationCanceledException)
        {
            record = Store.Get(taskId) ?? record;
            record.Status = DownloadTaskStatus.Paused;
            record.Note = "已暂停";
            Store.Save(record);
            Refresh(taskId, record);
        }
        catch (Exception ex)
        {
            record = Store.Get(taskId) ?? record;
            record.Status = DownloadTaskStatus.Failed;
            record.Note = ex.Message;
            Store.Save(record);
            Refresh(taskId, record);
            Log($"继续任务失败：{ex.Message}", "error");
        }
        finally
        {
            _running.Remove(taskId);
            _cancelRequested.Remove(taskId);
            cts.Dispose();
        }
    }

    private void Publish(TaskRowVm vm, TaskRecord record) =>
        RunOnUi(() =>
        {
            vm.Update(record);
            TasksChanged?.Invoke();
        });


    /// <summary>Pushes a record into its row; pass the snapshot you already have to skip a DB read.</summary>
    private void Refresh(string taskId, TaskRecord? snapshot = null)
    {
        RunOnUi(() =>
        {
            var vm = Tasks.FirstOrDefault(t => t.Id == taskId);
            if (vm is not null)
            {
                vm.Update(snapshot ?? vm.Record);
            }

            TasksChanged?.Invoke();
        });
    }

    public List<TaskRecord> LoadActiveTasks() => Store.Active();
}
