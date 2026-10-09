using System.Reflection;
using Microsoft.Extensions.Options;
using TheKrystalShip.Agent.Plugin;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>
/// A tool's context as the plugin package's tool host builds it. The package keeps the constructor and
/// the book of staged actions internal, so the tests reach them by reflection; this is the one place
/// the suite depends on the package's internals.
/// </summary>
internal static class ToolContexts
{
    private static readonly Assembly Package = typeof(PluginPerson).Assembly;
    private static readonly Type BookType = Package.GetType("TheKrystalShip.Agent.Plugin.ConfirmationBook", throwOnError: true)!;
    private static readonly ConstructorInfo ContextCtor =
        typeof(PluginToolContext).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();

    public static PluginToolContext For(PluginPerson person, string tool, CancellationToken cancellation = default)
    {
        object book = Activator.CreateInstance(
            BookType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [TimeProvider.System, Options.Create(new AssistantPluginOptions())], null)!;

        return (PluginToolContext)ContextCtor.Invoke(
        [
            person, "test-conversation", "test-turn", "test-call", tool,
            (Func<string, CancellationToken, Task>)((_, _) => Task.CompletedTask), book, cancellation,
        ]);
    }

    /// <summary>A context with nobody signed in.</summary>
    public static PluginToolContext Nobody(string tool) => For(new PluginPerson("nobody", "Nobody"), tool);
}
