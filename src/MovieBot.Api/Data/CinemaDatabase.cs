using Microsoft.Data.Sqlite;

namespace TheKrystalShip.MovieBot.Api.Data;

/// <summary>
/// Everything this service keeps, in one SQLite file in its state directory (<see cref="Schema"/>).
///
/// Each read opens a connection from the pool and each change is one transaction, taken immediately
/// and one at a time: a store that reads before it writes (a list's limit, a notification already
/// held, the first poster pinned) decides inside the transaction it writes in, so two requests can
/// never both decide on what neither has written yet. The file is in write-ahead mode, so a read never
/// waits on a write.
///
/// The file holds this server's push key and every person's records, so it is readable by this
/// service's user alone, and its journal files take the same mode from it.
/// </summary>
public sealed class CinemaDatabase
{
    /// <summary>What the file is called in the state directory when no path is configured.</summary>
    public const string FileName = "moviebot.db";

    private readonly string _connectionString;
    private readonly Lock _writing = new();

    public CinemaDatabase(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        if (!File.Exists(Path))
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            try
            {
                using var _ = new FileStream(Path, options);
            }
            catch (IOException) when (File.Exists(Path))
            {
                // Made by somebody else between the check and here, which is the same end state.
            }
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = true,
        }.ToString();

        using var connection = Open();
        connection.Execute("PRAGMA journal_mode = WAL;");
        Schema.Apply(connection);
    }

    public string Path { get; }

    /// <summary>A connection with this service's settings, for one read or one change.</summary>
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

    /// <summary>The single fact kept under <paramref name="key"/>, or null.</summary>
    public string? Meta(string key) =>
        Read(c => c.First("SELECT value FROM meta WHERE key = $key", r => r.GetString(0), ("$key", key)));

    public void SetMeta(string key, string value) =>
        Write(c => c.Execute(
            "INSERT INTO meta (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value",
            ("$key", key), ("$value", value)));
}
