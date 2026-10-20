using System.Reflection;
using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Data;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>A database file as it stood at an earlier schema version, for a test of what a later step does to it.</summary>
internal static class PreviousSchema
{
    /// <summary>
    /// Makes the file at <paramref name="path"/> by running the named steps of <see cref="Schema"/> in
    /// order, and leaves it at the version they reach. <paramref name="then"/> runs against it afterwards.
    /// </summary>
    public static void Build(string path, string[] steps, string? then = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=false");
        connection.Open();
        foreach (var step in steps)
            Run(connection, (string)typeof(Schema).GetField(step, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!);
        Run(connection, $"PRAGMA user_version = {steps.Length};");
        if (then is not null) Run(connection, then);
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
