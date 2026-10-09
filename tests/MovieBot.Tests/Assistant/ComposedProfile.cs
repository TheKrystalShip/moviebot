using Microsoft.Extensions.DependencyInjection;
using TheKrystalShip.Agent.Contracts;
using TheKrystalShip.Agent.Plugin;
using AssistantProgram = TheKrystalShip.MovieBot.Assistant.Program;

namespace TheKrystalShip.MovieBot.Tests.Assistant;

/// <summary>
/// The profile the service composes when it starts: the prompt and skills read from the profile
/// directory beside the binary, the tools, guards and speech from the code. It is built, never started,
/// so no harness is asked for anything.
/// </summary>
internal static class ComposedProfile
{
    /// <summary>The version the service reports, which is part of the profile and of its hash.</summary>
    public static readonly string Version = typeof(AssistantProgram).Assembly.GetName().Version!.ToString(3);

    private static readonly Lazy<PluginProfile> Composed = new(() =>
    {
        using var app = AssistantProgram.Build(
        [
            $"--Assistant:Version={Version}",
            "--Oidc:Issuer=https://issuer.test",
        ]);
        return app.Services.GetRequiredService<PluginRegistration>().Profile;
    });

    public static PluginProfile Load() => Composed.Value;
}
