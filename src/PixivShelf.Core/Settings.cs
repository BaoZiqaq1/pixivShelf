using System.Text.Json;
using System.Text.Json.Serialization;

namespace PixivShelf.Core.Models;

public sealed class NetworkSettings
{
    public string UserAgent { get; set; } = "Mozilla/5.0";
    public int Timeout { get; set; } = 15;
    public int Retry { get; set; } = 3;
    public int RetryWait { get; set; } = 5;
    public int DownloadDelay { get; set; } = 1;
    public int MaxWorkers { get; set; } = 3;
}

public sealed class StorageSettings
{
    public string DownloadRoot { get; set; } = "";
    public string FileNameFormat { get; set; } = "%image_id%_p%page_index%";
    public bool Overwrite { get; set; }
    public FileTimeSource FileTimeSource { get; set; } = FileTimeSource.Publish;
    public bool SetCreationTime { get; set; } = true;
}

public sealed class FilterSettings
{
    public ContentScope Scope { get; set; } = ContentScope.All;
    public bool SkipAiGenerated { get; set; }

    /// <summary>Extra filter on top of <see cref="Scope"/>: drop R-18G works.</summary>
    public bool BlockR18G { get; set; }

    /// <summary>Which bookmarks to download: public, private, or both.</summary>
    public BookmarkRest BookmarkRest { get; set; } = BookmarkRest.All;
}

public sealed class AuthSettings
{
    public string Cookie { get; set; } = "";
}

public sealed class AppSettings
{
    public NetworkSettings Network { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public FilterSettings Filter { get; set; } = new();
    public AuthSettings Auth { get; set; } = new();

    /// <summary>False until the first-run wizard has been completed.</summary>
    public bool OnboardingCompleted { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Folder the executable lives in (the portable home).</summary>
    public static string BaseDirectory =>
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Writable user data lives in this sub-folder next to the exe.</summary>
    public const string DataFolderName = "data";

    private static string? _portableDirectory;

    /// <summary>
    /// Where config.json, tasks.db and logs live. Portable builds use
    /// <c>app\data</c>; a CLI in <c>app\cli</c> shares <c>app\data</c>.
    /// If the exe folder is not writable, %LOCALAPPDATA%\PixivShelf is used.
    /// </summary>
    public static string DefaultDirectory
    {
        get
        {
            if (_portableDirectory is not null)
            {
                return _portableDirectory;
            }

            var baseDirectory = BaseDirectory;
            var parent = Path.GetDirectoryName(baseDirectory);
            var baseData = Path.Combine(baseDirectory, DataFolderName);
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PixivShelf");

            var baseConfig = Path.Combine(baseDirectory, "config.json");
            var baseDataConfig = Path.Combine(baseData, "config.json");

            // 1. Already using data\ next to this exe.
            if (File.Exists(baseDataConfig) && IsWritable(baseData))
            {
                _portableDirectory = baseData;
                return _portableDirectory;
            }

            // 2. Legacy portable layout: config/tasks sat next to the exe.
            if (File.Exists(baseConfig))
            {
                if (TryMigratePortable(baseDirectory, baseData))
                {
                    _portableDirectory = baseData;
                }
                else
                {
                    _portableDirectory = baseDirectory;
                }

                return _portableDirectory;
            }

            // 3. The CLI lives in app\cli and shares app\data with the GUI.
            if (parent is not null && File.Exists(Path.Combine(parent, "PixivShelf.exe")))
            {
                var parentData = Path.Combine(parent, DataFolderName);
                var parentConfig = Path.Combine(parent, "config.json");
                var parentDataConfig = Path.Combine(parentData, "config.json");

                if (File.Exists(parentDataConfig) && IsWritable(parentData))
                {
                    _portableDirectory = parentData;
                    return _portableDirectory;
                }

                if (File.Exists(parentConfig))
                {
                    if (TryMigratePortable(parent, parentData))
                    {
                        _portableDirectory = parentData;
                    }
                    else
                    {
                        _portableDirectory = parent;
                    }

                    return _portableDirectory;
                }

                if (Directory.Exists(parentData) && IsWritable(parentData))
                {
                    _portableDirectory = parentData;
                    return _portableDirectory;
                }
            }

            // 4. A data\ folder already exists (for example the README shipped by publish).
            if (Directory.Exists(baseData) && IsWritable(baseData))
            {
                _portableDirectory = baseData;
                return _portableDirectory;
            }

            // 5. CLI started before the GUI: create app\data next to PixivShelf.exe.
            if (parent is not null && File.Exists(Path.Combine(parent, "PixivShelf.exe")))
            {
                var parentData = Path.Combine(parent, DataFolderName);
                if (IsWritable(parentData))
                {
                    _portableDirectory = parentData;
                    return _portableDirectory;
                }
            }

            // 6. First run in a writable portable folder.
            if (IsWritable(baseDirectory) && IsWritable(baseData))
            {
                _portableDirectory = baseData;
                return _portableDirectory;
            }

            // Installed read-only (for example Program Files).
            if (IsWritable(fallback))
            {
                _portableDirectory = fallback;
                MigrateLegacyAppData(fallback);
                return _portableDirectory;
            }

            // Last resort: keep the old behaviour rather than crash.
            _portableDirectory = baseDirectory;
            return _portableDirectory;
        }
    }

    /// <summary>
    /// Moves config and task history from the old portable root into data\.
    /// Returns false only when a config exists and could not be migrated.
    /// </summary>
    private static bool TryMigratePortable(string legacyDirectory, string dataDirectory)
    {
        try
        {
            if (!IsWritable(dataDirectory))
            {
                return false;
            }

            // Move the config first: if it cannot be migrated, keep the legacy
            // directory so the user does not lose the saved login/settings.
            var legacyConfig = Path.Combine(legacyDirectory, "config.json");
            var dataConfig = Path.Combine(dataDirectory, "config.json");
            if (File.Exists(legacyConfig) && !File.Exists(dataConfig))
            {
                try
                {
                    File.Move(legacyConfig, dataConfig);
                }
                catch
                {
                    try { File.Copy(legacyConfig, dataConfig); }
                    catch { }
                }

                if (!File.Exists(dataConfig))
                {
                    return false;
                }
            }

            foreach (var name in new[] { "tasks.db", "tasks.db-wal", "tasks.db-shm" })
            {
                var source = Path.Combine(legacyDirectory, name);
                var target = Path.Combine(dataDirectory, name);
                if (!File.Exists(source) || File.Exists(target))
                {
                    continue;
                }

                try
                {
                    File.Move(source, target);
                }
                catch
                {
                    try { File.Copy(source, target); }
                    catch { }
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// One-time move from the old %LOCALAPPDATA%\PixivFetcher folder so the rename
    /// does not cost the user their login state and task history.
    /// </summary>
    private static void MigrateLegacyAppData(string target)
    {
        try
        {
            if (File.Exists(Path.Combine(target, "config.json")))
            {
                return;
            }

            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PixivFetcher");

            if (!Directory.Exists(legacy))
            {
                return;
            }

            Directory.CreateDirectory(target);
            foreach (var name in new[] { "config.json", "tasks.db" })
            {
                var source = Path.Combine(legacy, name);
                if (File.Exists(source))
                {
                    File.Copy(source, Path.Combine(target, name), overwrite: true);
                }
            }
        }
        catch
        {
            // Migration is best effort; a fresh config still works.
        }
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string DefaultConfigPath => Path.Combine(DefaultDirectory, "config.json");

    public static AppSettings Load(string? path = null)
    {
        var target = path ?? DefaultConfigPath;
        try
        {
            if (File.Exists(target))
            {
                var raw = File.ReadAllText(target);
                var settings = JsonSerializer.Deserialize<AppSettings>(raw, JsonOptions);
                if (settings is not null)
                {
                    // Configs written before the wizard existed count as "already onboarded".
                    if (!raw.Contains("OnboardingCompleted", StringComparison.Ordinal))
                    {
                        settings.OnboardingCompleted = true;
                    }

                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch
        {
            // A broken config must never block startup, but keep a copy for diagnosis.
            TryBackupBroken(target);
        }

        var fresh = new AppSettings();
        fresh.ImportLegacyYaml();
        fresh.Normalize();
        return fresh;
    }

    public void Save(string? path = null)
    {
        var target = path ?? DefaultConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Write to a temp file first: a crash mid-write then leaves the previous
        // config intact instead of a truncated one.
        var temp = target + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));

        try
        {
            if (File.Exists(target))
            {
                File.Replace(temp, target, null);
            }
            else
            {
                File.Move(temp, target);
            }
        }
        catch (IOException)
        {
            // Some file systems refuse Replace; fall back to a plain overwrite.
            File.Move(temp, target, overwrite: true);
        }
    }

    private static void TryBackupBroken(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            File.Copy(path, path + ".bad", overwrite: true);
        }
        catch
        {
            // Backup is best effort only.
        }
    }

    public void Normalize()
    {
        Network.MaxWorkers = Math.Clamp(Network.MaxWorkers, 1, 8);
        Network.Timeout = Math.Clamp(Network.Timeout, 3, 120);
        Network.Retry = Math.Clamp(Network.Retry, 0, 10);
        Network.RetryWait = Math.Clamp(Network.RetryWait, 0, 60);
        Network.DownloadDelay = Math.Clamp(Network.DownloadDelay, 0, 60);
        if (string.IsNullOrWhiteSpace(Storage.FileNameFormat))
        {
            Storage.FileNameFormat = "%image_id%_p%page_index%";
        }

        if (string.IsNullOrWhiteSpace(Storage.DownloadRoot))
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            Storage.DownloadRoot = Path.Combine(pictures, "PixivShelf");
        }
    }

    /// <summary>One-time import of cookie / download root from the old Python config.</summary>
    public void ImportLegacyYaml()
    {
        foreach (var candidate in LegacyConfigCandidates())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }
            try
            {
                var values = ReadSimpleYaml(File.ReadAllText(candidate));
                if (values.TryGetValue("auth.cookie", out var cookie) && !string.IsNullOrWhiteSpace(cookie))
                {
                    Auth.Cookie = cookie;
                }
                if (values.TryGetValue("storage.download_root", out var root) && !string.IsNullOrWhiteSpace(root))
                {
                    Storage.DownloadRoot = root;
                }
                if (values.TryGetValue("storage.flat_filename_format", out var format) && !string.IsNullOrWhiteSpace(format))
                {
                    Storage.FileNameFormat = format;
                }
                if (values.TryGetValue("storage.set_creation_time", out var creation))
                {
                    Storage.SetCreationTime = creation.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                if (values.TryGetValue("storage.file_time_source", out var timeSource))
                {
                    Storage.FileTimeSource = timeSource.Trim().Equals("bookmark", StringComparison.OrdinalIgnoreCase)
                        ? FileTimeSource.Bookmark
                        : FileTimeSource.Publish;
                }
                if (values.TryGetValue("filter.skip_ai_generated", out var ai))
                {
                    Filter.SkipAiGenerated = ai.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                return;
            }
            catch
            {
                // ignore and try the next candidate
            }
        }
    }

    /// <summary>
    /// Optional migration sources that ship *with this app folder* — the new build
    /// never reaches into the old Python installation by itself.
    /// Drop a legacy YAML next to the exe (or in a config\ subfolder) to import it.
    /// </summary>
    private static IEnumerable<string> LegacyConfigCandidates()
    {
        yield return Path.Combine(BaseDirectory, "legacy-config.yaml");
        yield return Path.Combine(BaseDirectory, "config", "default.yaml");
    }

    /// <summary>Minimal reader for the flat "section.key: value" needs above.</summary>
    internal static Dictionary<string, string> ReadSimpleYaml(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        var lastKey = "";

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;
            var trimmed = line.Trim();

            if (indent == 0 && trimmed.EndsWith(':'))
            {
                section = trimmed[..^1].Trim();
                lastKey = "";
                continue;
            }

            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim().Trim('\'', '"');

            if (indent > 0 && value.Length == 0 && section.Length > 0)
            {
                lastKey = $"{section}.{key}";
                continue;
            }

            if (indent == 0 && value.Length > 0)
            {
                section = "";
                result[key] = value;
                lastKey = "";
                continue;
            }

            if (section.Length > 0)
            {
                if (value.Length == 0 && lastKey.Length > 0)
                {
                    continue;
                }
                result[$"{section}.{key}"] = value;
                lastKey = "";
            }
        }

        // Multi-line YAML scalars: "  cookie: part1\n    part2..." — join continuations.
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("cookie:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = new List<string>();
            var first = trimmed["cookie:".Length..].Trim().Trim('\'', '"');
            if (first.Length > 0)
            {
                parts.Add(first);
            }

            var baseIndent = lines[i].Length - lines[i].TrimStart().Length;
            for (var j = i + 1; j < lines.Length; j++)
            {
                if (string.IsNullOrWhiteSpace(lines[j]))
                {
                    continue;
                }
                var indent = lines[j].Length - lines[j].TrimStart().Length;
                if (indent <= baseIndent)
                {
                    break;
                }
                parts.Add(lines[j].Trim());
            }

            if (parts.Count > 0)
            {
                result["auth.cookie"] = string.Join(" ", parts);
            }
            break;
        }

        return result;
    }
}
