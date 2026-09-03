namespace TheKrystalShip.MovieBot.Bot.Configuration;

/// <summary>
/// Where the bot writes the little it has to remember.
///
/// systemd makes the directory and names it in an environment variable, so nothing has to be
/// configured for the service and nothing has to exist for a developer running it from a
/// checkout. One spelling of it, because two files landing in different directories because two
/// options classes read the variable differently is a bug nothing reports.
/// </summary>
public static class StatePath
{
    /// <summary>
    /// The configured path when there is one, and the named file inside the state directory
    /// otherwise. The variable may name more than one directory; the first is the service's own.
    /// </summary>
    public static string Resolve(string configured, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);

        var state = Environment.GetEnvironmentVariable("STATE_DIRECTORY")?.Split(':').FirstOrDefault();

        return Path.GetFullPath(Path.Combine(
            string.IsNullOrWhiteSpace(state) ? Directory.GetCurrentDirectory() : state,
            fileName));
    }
}
