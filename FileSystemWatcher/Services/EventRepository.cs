using System.Text.Json;
using Microsoft.Data.Sqlite;
using WinAgent.FileSystemWatcher.Models;

namespace WinAgent.FileSystemWatcher.Services;

public sealed class EventRepository : IDisposable
{
    private readonly string _connectionString;
    private readonly string _databasePath;

    public EventRepository(string basePath)
    {
        _databasePath = Path.Combine(basePath, "events.db");
        _connectionString = $"Data Source={_databasePath};Pooling=false";

        InitializeDatabase();
    }

    public string DatabasePath => _databasePath;

    private void InitializeDatabase()
    {
        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS FileSystemEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventType TEXT NOT NULL,
                FullPath TEXT NOT NULL,
                OldFullPath TEXT,
                Timestamp TEXT NOT NULL,
                PossibleCopySource TEXT,
                Severity TEXT NOT NULL,
                Pending INTEGER NOT NULL DEFAULT 1,
                Json TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_FileSystemEvents_Pending ON FileSystemEvents(Pending);
            CREATE INDEX IF NOT EXISTS IX_FileSystemEvents_Timestamp ON FileSystemEvents(Timestamp DESC);
        ";
        command.ExecuteNonQuery();
    }

    public long Insert(FileSystemEvent fileEvent, bool pending)
    {
        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO FileSystemEvents (EventType, FullPath, OldFullPath, Timestamp, PossibleCopySource, Severity, Pending, Json)
            VALUES ($eventType, $fullPath, $oldFullPath, $timestamp, $possibleCopySource, $severity, $pending, $json);
            SELECT last_insert_rowid();
        ";

        command.Parameters.AddWithValue("$eventType", fileEvent.EventType.ToString());
        command.Parameters.AddWithValue("$fullPath", fileEvent.FullPath);
        command.Parameters.AddWithValue("$oldFullPath", fileEvent.OldFullPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$timestamp", fileEvent.Timestamp.ToString("O"));
        command.Parameters.AddWithValue("$possibleCopySource", fileEvent.PossibleCopySource ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$severity", fileEvent.Severity.ToString());
        command.Parameters.AddWithValue("$pending", pending ? 1 : 0);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(fileEvent, JsonEventSerializer.Options));

        return (long)command.ExecuteScalar()!;
    }

    public IReadOnlyList<FileSystemEvent> GetRecentEvents(int count)
    {
        var events = new List<FileSystemEvent>();

        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Json FROM FileSystemEvents
            ORDER BY Timestamp DESC, Id DESC
            LIMIT $count;
        ";
        command.Parameters.AddWithValue("$count", count);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var json = reader.GetString(0);
            var fileEvent = JsonSerializer.Deserialize<FileSystemEvent>(json, JsonEventSerializer.Options);
            if (fileEvent is not null)
            {
                events.Add(fileEvent);
            }
        }

        return events;
    }

    public IReadOnlyList<(long Id, FileSystemEvent Event)> GetPendingEvents(int maxCount)
    {
        var events = new List<(long, FileSystemEvent)>();

        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT Id, Json FROM FileSystemEvents
            WHERE Pending = 1
            ORDER BY Timestamp ASC, Id ASC
            LIMIT $maxCount;
        ";
        command.Parameters.AddWithValue("$maxCount", maxCount);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var json = reader.GetString(1);
            var fileEvent = JsonSerializer.Deserialize<FileSystemEvent>(json, JsonEventSerializer.Options);
            if (fileEvent is not null)
            {
                events.Add((id, fileEvent));
            }
        }

        return events;
    }

    public void MarkPending(long id, bool pending)
    {
        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE FileSystemEvents SET Pending = $pending WHERE Id = $id;
        ";
        command.Parameters.AddWithValue("$pending", pending ? 1 : 0);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    public EventCounts GetCounts()
    {
        using var connection = CreateConnection();
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT
                (SELECT COUNT(*) FROM FileSystemEvents),
                (SELECT COUNT(*) FROM FileSystemEvents WHERE Pending = 1);
        ";

        using var reader = command.ExecuteReader();
        reader.Read();

        return new EventCounts
        {
            Total = reader.GetInt64(0),
            Pending = reader.GetInt64(1)
        };
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    public void Dispose()
    {
    }
}

public sealed class EventCounts
{
    public long Total { get; init; }

    public long Pending { get; init; }
}
