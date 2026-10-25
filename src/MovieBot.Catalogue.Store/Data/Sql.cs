using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TheKrystalShip.MovieBot.Catalogue.Data;

/// <summary>
/// The few ways the catalogue speaks to the database, so every part binds and reads a value the same way.
///
/// An instant is kept as text, ISO-8601 in UTC with seven fractional digits: it reads back as exactly
/// the instant written, and two of them compare in SQL as they do in C#, because every one has the
/// same width and the same offset. A boolean is an integer, 0 or 1.
/// </summary>
public static class Sql
{
    private const string InstantFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    public static string Instant(DateTimeOffset at) =>
        at.UtcDateTime.ToString(InstantFormat, CultureInfo.InvariantCulture);

    public static DateTimeOffset ReadInstant(string text) =>
        new(DateTime.ParseExact(text, InstantFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal), TimeSpan.Zero);

    public static SqliteCommand Command(this SqliteConnection connection, string sql, params ReadOnlySpan<(string Name, object? Value)> args)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in args)
            command.Parameters.AddWithValue(name, value switch
            {
                null => DBNull.Value,
                DateTimeOffset at => Instant(at),
                bool flag => flag ? 1 : 0,
                _ => value,
            });
        return command;
    }

    public static int Execute(this SqliteConnection connection, string sql, params ReadOnlySpan<(string Name, object? Value)> args)
    {
        using var command = connection.Command(sql, args);
        return command.ExecuteNonQuery();
    }

    public static List<T> Query<T>(this SqliteConnection connection, string sql, Func<SqliteDataReader, T> map,
        params ReadOnlySpan<(string Name, object? Value)> args)
    {
        using var command = connection.Command(sql, args);
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read()) rows.Add(map(reader));
        return rows;
    }

    /// <summary>The first row, or the default when there is none.</summary>
    public static T? First<T>(this SqliteConnection connection, string sql, Func<SqliteDataReader, T> map,
        params ReadOnlySpan<(string Name, object? Value)> args)
    {
        using var command = connection.Command(sql, args);
        using var reader = command.ExecuteReader();
        return reader.Read() ? map(reader) : default;
    }

    public static bool Exists(this SqliteConnection connection, string sql, params ReadOnlySpan<(string Name, object? Value)> args)
    {
        using var command = connection.Command(sql, args);
        using var reader = command.ExecuteReader();
        return reader.Read();
    }

    public static long Count(this SqliteConnection connection, string sql, params ReadOnlySpan<(string Name, object? Value)> args)
    {
        using var command = connection.Command(sql, args);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset GetInstant(this SqliteDataReader reader, int ordinal) => ReadInstant(reader.GetString(ordinal));

    public static DateTimeOffset? GetInstantOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadInstant(reader.GetString(ordinal));

    public static string? GetStringOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static int? GetIntOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    public static double? GetDoubleOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    public static bool? GetBoolOrNull(this SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal) != 0;

    public static bool GetBool(this SqliteDataReader reader, int ordinal) => reader.GetInt64(ordinal) != 0;
}
