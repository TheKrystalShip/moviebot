using System.Runtime.CompilerServices;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// Every service under test layers the host's settings file from the XDG configuration directories
/// into its configuration. Pointing both at a directory that does not exist keeps a developer's own
/// <c>~/.config/moviebot/moviebot.settings.json</c> out of every test, so the shipped defaults and
/// what each test sets are the whole of what it runs with.
/// </summary>
internal static class SettingsIsolation
{
    [ModuleInitializer]
    internal static void KeepTheHostSettingsFileOut()
    {
        var nowhere = Path.Combine(Path.GetTempPath(), "moviebot-tests-no-settings");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", nowhere);
        Environment.SetEnvironmentVariable("XDG_CONFIG_DIRS", nowhere);
    }
}
