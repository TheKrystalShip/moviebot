using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Catalogue.Data;

namespace TheKrystalShip.MovieBot.Catalogue;

/// <summary>
/// The catalogue as a reader opens it: read-only, from the file <c>moviebot-catalogue</c> writes. A file
/// that is absent, or at a schema version this build does not know, is a catalogue that is not
/// available, which the reader says and never guesses past. Each read takes a pooled connection; the
/// file is in write-ahead mode, so a read never waits on the writer and never sees half a change.
/// </summary>
public sealed class CatalogueReader
{
    private readonly string _connectionString;

    public CatalogueReader(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = true,
        }.ToString();
    }

    public string Path { get; }

    /// <summary>
    /// Whether the catalogue can be read: the file is there and at the schema version this build knows.
    /// Null when it can; otherwise what is wrong, in words.
    /// </summary>
    public string? Problem()
    {
        if (!File.Exists(Path)) return $"No catalogue at {Path}.";
        try
        {
            var version = Read(c => c.Count("PRAGMA user_version;"));
            return version == Schema.Version ? null : $"The catalogue at {Path} is at schema version {version}; this build reads version {Schema.Version}.";
        }
        catch (SqliteException ex)
        {
            return $"The catalogue at {Path} cannot be read: {ex.Message}";
        }
    }

    public T Read<T>(Func<SqliteConnection, T> read)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA busy_timeout = 5000;");
        // How well known a title is, for a search to weigh beside how well it matches.
        connection.CreateFunction("known", (long? votes) => Math.Log((votes ?? 0) + 10), isDeterministic: true);
        return read(connection);
    }
}
