using PixivShelf.Core.Models;
using PixivShelf.Core.Services;
using Xunit;

namespace PixivShelf.Tests;

public class SettingsTests
{
    [Fact]
    public void Reads_flat_yaml_values()
    {
        const string yaml = """
            network:
              timeout: 15
              max_workers: 5
            storage:
              download_root: D:\pixiv
              flat_filename_format: '%image_id%_p%page_index%'
              file_time_source: bookmark
              set_creation_time: true
            filter:
              skip_ai_generated: true
            auth:
              cookie: cc1=abc; PHPSESSID=xyz
            """;

        var values = AppSettings.ReadSimpleYaml(yaml);

        Assert.Equal(@"D:\pixiv", values["storage.download_root"]);
        Assert.Equal("bookmark", values["storage.file_time_source"]);
        Assert.Equal("cc1=abc; PHPSESSID=xyz", values["auth.cookie"]);
        Assert.Equal("true", values["filter.skip_ai_generated"]);
    }

    [Fact]
    public void Reads_multi_line_cookie()
    {
        const string yaml = """
            auth:
              cookie: cc1=2026-11-09; PHPSESSID=1234567_abc;
                device_token=xyz; cf_clearance=zzz
            """;

        var values = AppSettings.ReadSimpleYaml(yaml);

        Assert.Equal("cc1=2026-11-09; PHPSESSID=1234567_abc; device_token=xyz; cf_clearance=zzz", values["auth.cookie"]);
    }

    [Fact]
    public void Normalize_clamps_values()
    {
        var settings = new AppSettings();
        settings.Network.MaxWorkers = 99;
        settings.Network.Timeout = 1;
        settings.Network.DownloadDelay = -5;
        settings.Storage.FileNameFormat = "  ";

        settings.Normalize();

        Assert.Equal(8, settings.Network.MaxWorkers);
        Assert.Equal(3, settings.Network.Timeout);
        Assert.Equal(0, settings.Network.DownloadDelay);
        Assert.Equal("%image_id%_p%page_index%", settings.Storage.FileNameFormat);
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pixiv-cfg-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings();
            settings.Auth.Cookie = "PHPSESSID=abc";
            settings.Storage.DownloadRoot = @"D:\out";
            settings.Storage.FileTimeSource = FileTimeSource.Bookmark;
            settings.Filter.Scope = ContentScope.SkipR18;
            settings.Save(path);

            var loaded = AppSettings.Load(path);

            Assert.Equal("PHPSESSID=abc", loaded.Auth.Cookie);
            Assert.Equal(@"D:\out", loaded.Storage.DownloadRoot);
            Assert.Equal(FileTimeSource.Bookmark, loaded.Storage.FileTimeSource);
            Assert.Equal(ContentScope.SkipR18, loaded.Filter.Scope);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class TaskStoreTests
{
    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"pixiv-tasks-{Guid.NewGuid():N}.db");

    [Fact]
    public void Saves_and_updates_tasks()
    {
        var path = TempDb();
        try
        {
            using var store = new TaskStore(path);
            var task = new TaskRecord { Title = "collect", Source = DownloadSource.Bookmarks };
            store.Save(task);

            task.Total = 10;
            task.Done = 4;
            task.Status = DownloadTaskStatus.Running;
            store.Save(task);

            var loaded = store.Get(task.Id);
            Assert.NotNull(loaded);
            Assert.Equal(4, loaded!.Done);
            Assert.Equal(DownloadTaskStatus.Running, loaded.Status);
            Assert.Single(store.Active());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Persists_params_json_on_update()
    {
        var path = TempDb();
        try
        {
            using var store = new TaskStore(path);
            var task = new TaskRecord { Title = "collect", Source = DownloadSource.Bookmarks };
            store.Save(task);

            // The payload is written after collection, i.e. on a later Save call.
            task.ParamsJson = """{"Ids":[1,2,3]}""";
            task.Total = 3;
            store.Save(task);

            var loaded = store.Get(task.Id);
            Assert.NotNull(loaded);
            Assert.Contains("[1,2,3]", loaded!.ParamsJson);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Purges_only_old_finished_tasks()
    {
        var path = TempDb();
        try
        {
            using var store = new TaskStore(path);

            var old = new TaskRecord { Title = "old", Status = DownloadTaskStatus.Done };
            old.FinishedAt = DateTimeOffset.Now.AddDays(-40);
            store.Save(old);

            var fresh = new TaskRecord { Title = "fresh", Status = DownloadTaskStatus.Done };
            fresh.FinishedAt = DateTimeOffset.Now;
            store.Save(fresh);

            var paused = new TaskRecord { Title = "paused", Status = DownloadTaskStatus.Paused };
            store.Save(paused);

            var removed = store.PurgeDoneOlderThan(TimeSpan.FromDays(30));

            Assert.Equal(1, removed);
            Assert.Null(store.Get(old.Id));
            Assert.NotNull(store.Get(fresh.Id));
            Assert.NotNull(store.Get(paused.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Stores_items()
    {
        var path = TempDb();
        try
        {
            using var store = new TaskStore(path);
            store.SaveItem(new TaskItemRecord { TaskId = "t1", ArtworkId = 42, Status = "done", Path = @"D:\x.png" });

            var items = store.Items("t1");
            Assert.Single(items);
            Assert.Equal(42, items[0].ArtworkId);
            Assert.Equal("done", items[0].Status);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class BackfillTests
{
    [Theory]
    [InlineData("123_p0.png", 123, 0)]
    [InlineData("123_p12.jpg", 123, 12)]
    [InlineData("name_p1_123.png", 123, 1)]
    [InlineData("26_02_p3_456.webp", 456, 3)]
    public void Parses_image_names(string name, int pid, int page)
    {
        var parsed = Backfill.ParseImageName(name);
        Assert.NotNull(parsed);
        Assert.Equal(pid, parsed!.Value.Pid);
        Assert.Equal(page, parsed.Value.Page);
    }

    [Theory]
    [InlineData("random.png")]
    [InlineData("123_p0.txt")]
    public void Rejects_other_names(string name)
    {
        Assert.Null(Backfill.ParseImageName(name));
    }

    [Fact]
    public void Plan_skips_unmatched()
    {
        var files = new List<(string, int, int)> { ("a.png", 1, 0), ("b.png", 2, 0) };
        var times = new Dictionary<int, DateTimeOffset> { [1] = new(2024, 5, 1, 0, 0, 0, TimeSpan.Zero) };

        var plan = Backfill.Plan(files, times);

        Assert.Single(plan);
        Assert.Equal(1, plan[0].Pid);
    }

    [Fact]
    public void Apply_writes_times_and_renames()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pixiv-backfill-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "111_p1.png");
            File.WriteAllText(source, "x");
            var when = new DateTimeOffset(2021, 2, 3, 4, 5, 0, TimeSpan.Zero);

            var result = Backfill.Apply(
                new[] { new BackfillItem(source, 111, 1, when) },
                rename: true,
                zeroBased: true);

            Assert.Equal(1, result.Times);
            Assert.Equal(1, result.Renamed);
            var renamed = Path.Combine(dir, "111_p0.png");
            Assert.True(File.Exists(renamed));
            Assert.False(File.Exists(source));
            Assert.True(Math.Abs((File.GetLastWriteTimeUtc(renamed) - when.UtcDateTime).TotalSeconds) < 2);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Dry_run_changes_nothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pixiv-backfill-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "222_p1.png");
            File.WriteAllText(source, "x");
            var before = File.GetLastWriteTimeUtc(source);

            var result = Backfill.Apply(
                new[] { new BackfillItem(source, 222, 1, new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)) },
                dryRun: true,
                rename: true,
                zeroBased: true);

            Assert.Equal(1, result.Times);
            Assert.True(File.Exists(source));
            Assert.False(File.Exists(Path.Combine(dir, "222_p0.png")));
            Assert.Equal(before, File.GetLastWriteTimeUtc(source));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
