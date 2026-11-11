using Microsoft.Data.Sqlite;
using PixivShelf.Core.Models;

namespace PixivShelf.Core.Services;

/// <summary>SQLite backed task bookkeeping so progress survives a restart.</summary>
public sealed class TaskStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly object _gate = new();

    public TaskStore(string? dbPath = null)
    {
        var path = dbPath ?? Path.Combine(AppSettings.DefaultDirectory, "tasks.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            // Keeping the pool alive would hold the file handle and break tests/portability.
            Pooling = false,
        };
        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();

        // WAL + NORMAL keeps the per-artwork writes cheap (no fsync per commit)
        // while staying crash safe enough for progress bookkeeping.
        using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            pragma.ExecuteNonQuery();
        }

        Initialize();
    }

    private void Initialize()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS tasks (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                source INTEGER NOT NULL,
                params_json TEXT NOT NULL,
                status INTEGER NOT NULL,
                total INTEGER NOT NULL DEFAULT 0,
                done INTEGER NOT NULL DEFAULT 0,
                failed INTEGER NOT NULL DEFAULT 0,
                note TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                finished_at TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS task_items (
                task_id TEXT NOT NULL,
                artwork_id INTEGER NOT NULL,
                title TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'pending',
                path TEXT NOT NULL DEFAULT '',
                error TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (task_id, artwork_id)
            );
            """;
        command.ExecuteNonQuery();
    }

    public void Save(TaskRecord task)
    {
        lock (_gate)
        {
            task.UpdatedAt = DateTimeOffset.Now;
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO tasks (id, title, source, params_json, status, total, done, failed, note, created_at, updated_at, finished_at)
                VALUES ($id, $title, $source, $params, $status, $total, $done, $failed, $note, $created, $updated, $finished)
                ON CONFLICT(id) DO UPDATE SET
                    title = excluded.title,
                    params_json = excluded.params_json,
                    status = excluded.status,
                    total = excluded.total,
                    done = excluded.done,
                    failed = excluded.failed,
                    note = excluded.note,
                    updated_at = excluded.updated_at,
                    finished_at = excluded.finished_at;
                """;
            command.Parameters.AddWithValue("$id", task.Id);
            command.Parameters.AddWithValue("$title", task.Title);
            command.Parameters.AddWithValue("$source", (int)task.Source);
            command.Parameters.AddWithValue("$params", task.ParamsJson);
            command.Parameters.AddWithValue("$status", (int)task.Status);
            command.Parameters.AddWithValue("$total", task.Total);
            command.Parameters.AddWithValue("$done", task.Done);
            command.Parameters.AddWithValue("$failed", task.Failed);
            command.Parameters.AddWithValue("$note", task.Note);
            command.Parameters.AddWithValue("$created", task.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$updated", task.UpdatedAt.ToString("O"));
            command.Parameters.AddWithValue("$finished", task.FinishedAt?.ToString("O") ?? (object)DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    public TaskRecord? Get(string id)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT * FROM tasks WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadTask(reader) : null;
        }
    }

    public List<TaskRecord> All(int limit = 200)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT * FROM tasks ORDER BY created_at DESC LIMIT $limit";
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var tasks = new List<TaskRecord>();
            while (reader.Read())
            {
                tasks.Add(ReadTask(reader));
            }
            return tasks;
        }
    }

    public List<TaskRecord> Active()
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT * FROM tasks WHERE status IN ($pending, $collecting, $running, $paused) ORDER BY created_at DESC";
            command.Parameters.AddWithValue("$pending", (int)DownloadTaskStatus.Pending);
            command.Parameters.AddWithValue("$collecting", (int)DownloadTaskStatus.Collecting);
            command.Parameters.AddWithValue("$running", (int)DownloadTaskStatus.Running);
            command.Parameters.AddWithValue("$paused", (int)DownloadTaskStatus.Paused);
            using var reader = command.ExecuteReader();
            var tasks = new List<TaskRecord>();
            while (reader.Read())
            {
                tasks.Add(ReadTask(reader));
            }
            return tasks;
        }
    }

    public void SaveItem(TaskItemRecord item)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO task_items (task_id, artwork_id, title, status, path, error)
                VALUES ($task, $artwork, $title, $status, $path, $error)
                ON CONFLICT(task_id, artwork_id) DO UPDATE SET
                    status = excluded.status,
                    path = excluded.path,
                    error = excluded.error;
                """;
            command.Parameters.AddWithValue("$task", item.TaskId);
            command.Parameters.AddWithValue("$artwork", item.ArtworkId);
            command.Parameters.AddWithValue("$title", item.Title);
            command.Parameters.AddWithValue("$status", item.Status);
            command.Parameters.AddWithValue("$path", item.Path);
            command.Parameters.AddWithValue("$error", item.Error);
            command.ExecuteNonQuery();
        }
    }

    public List<TaskItemRecord> Items(string taskId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT task_id, artwork_id, title, status, path, error FROM task_items WHERE task_id = $task ORDER BY artwork_id";
            command.Parameters.AddWithValue("$task", taskId);
            using var reader = command.ExecuteReader();
            var items = new List<TaskItemRecord>();
            while (reader.Read())
            {
                items.Add(new TaskItemRecord
                {
                    TaskId = reader.GetString(0),
                    ArtworkId = reader.GetInt32(1),
                    Title = reader.GetString(2),
                    Status = reader.GetString(3),
                    Path = reader.GetString(4),
                    Error = reader.GetString(5),
                });
            }
            return items;
        }
    }

    /// <summary>Removes finished tasks older than the given age (unfinished are kept).</summary>
    public int PurgeDoneOlderThan(TimeSpan age)
    {
        lock (_gate)
        {
            var cutoff = DateTimeOffset.Now - age;
            var ids = new List<string>();
            using (var select = _connection.CreateCommand())
            {
                select.CommandText = "SELECT id FROM tasks WHERE status IN ($done, $cancelled, $failed) AND COALESCE(finished_at, updated_at) < $cutoff";
                select.Parameters.AddWithValue("$done", (int)DownloadTaskStatus.Done);
                select.Parameters.AddWithValue("$cancelled", (int)DownloadTaskStatus.Cancelled);
                select.Parameters.AddWithValue("$failed", (int)DownloadTaskStatus.Failed);
                select.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    ids.Add(reader.GetString(0));
                }
            }

            foreach (var id in ids)
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "DELETE FROM task_items WHERE task_id = $id; DELETE FROM tasks WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id);
                command.ExecuteNonQuery();
            }

            return ids.Count;
        }
    }

    public void Delete(string taskId)
    {
        lock (_gate)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM task_items WHERE task_id = $id; DELETE FROM tasks WHERE id = $id;";
            command.Parameters.AddWithValue("$id", taskId);
            command.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }

    private static TaskRecord ReadTask(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(reader.GetOrdinal("id")),
        Title = reader.GetString(reader.GetOrdinal("title")),
        Source = (DownloadSource)reader.GetInt32(reader.GetOrdinal("source")),
        ParamsJson = reader.GetString(reader.GetOrdinal("params_json")),
        Status = (DownloadTaskStatus)reader.GetInt32(reader.GetOrdinal("status")),
        Total = reader.GetInt32(reader.GetOrdinal("total")),
        Done = reader.GetInt32(reader.GetOrdinal("done")),
        Failed = reader.GetInt32(reader.GetOrdinal("failed")),
        Note = reader.GetString(reader.GetOrdinal("note")),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
        UpdatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at"))),
        FinishedAt = reader.IsDBNull(reader.GetOrdinal("finished_at"))
            ? null
            : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("finished_at"))),
    };
}
