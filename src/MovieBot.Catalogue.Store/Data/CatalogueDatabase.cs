using Microsoft.Data.Sqlite;

namespace TheKrystalShip.MovieBot.Catalogue.Data;

/// <summary>
/// The catalogue, one SQLite file (<see cref="Schema"/>), written by this service alone and read by the
/// API. Each read opens a pooled connection and each change is one immediate transaction, one at a
/// time. The file is in write-ahead mode, so a reader never waits on a write and never sees half of
/// one: a layer and the entry built from it land together. Opening it brings the planner's statistics
/// up to date for every table whose size has moved since they were taken.
/// </summary>
public sealed class CatalogueDatabase
{
    public const string FileName = "catalogue.db";

    private readonly string _connectionString;
    private readonly Lock _writing = new();

    public CatalogueDatabase(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();

        using var connection = Open();
        connection.Execute("PRAGMA journal_mode = WAL;");
        Schema.Apply(connection);
        connection.Execute("PRAGMA optimize = 0x10002;");
    }

    public string Path { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA foreign_keys = ON; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 5000;");
        return connection;
    }

    public T Read<T>(Func<SqliteConnection, T> read)
    {
        using var connection = Open();
        return read(connection);
    }

    /// <summary>One change, in one immediate transaction, after every change before it.</summary>
    public T Write<T>(Func<SqliteConnection, T> write)
    {
        lock (_writing)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction(deferred: false);
            var result = write(connection);
            transaction.Commit();
            return result;
        }
    }

    public void Write(Action<SqliteConnection> write) => Write(connection =>
    {
        write(connection);
        return true;
    });

    public string? Meta(string key) =>
        Read(c => c.First("SELECT value FROM meta WHERE key = $key", r => r.GetString(0), ("$key", key)));

    public void SetMeta(string key, string value) => Write(c => SetMeta(c, key, value));

    public static void SetMeta(SqliteConnection c, string key, string value) =>
        c.Execute("INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value",
            ("$key", key), ("$value", value));
}
