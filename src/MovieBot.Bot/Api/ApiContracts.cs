using System.Text.Json;
using System.Text.Json.Serialization;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Api;

/// <summary>
/// One row of the API's library listing.
///
/// The bot never opens a manifest: everything it needs to answer "which film is this" is in the
/// listing, and the player is what renders the track graph.
/// </summary>
public sealed record LibraryTitle
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required double DurationSeconds { get; init; }
    public required TitleStatus Status { get; init; }

    /// <summary>Seconds of the film that are playable now. Null once the title is ready.</summary>
    public double? HeadSeconds { get; init; }

    /// <summary>A file name under the title's media directory, absent when the film has no artwork.</summary>
    public string? Poster { get; init; }

    /// <summary>Which film this is: its name, its year, its billing and its page.</summary>
    public FilmIdentity? Film { get; init; }

    /// <summary>
    /// What to call the film. The catalogue's name where there is one, and the title the ingest
    /// derived from the release otherwise — never the release itself where anything better exists.
    /// </summary>
    public string Name => Film?.Display ?? Title;
}

/// <summary>
/// The serializer for the shapes the API publishes that the shared contract project does not
/// carry. Session state is read through <see cref="ManifestJsonContext"/> instead, so the bot
/// and the API agree on it by compiling against the same type.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(IReadOnlyList<LibraryTitle>))]
public partial class BotJsonContext : JsonSerializerContext;

/// <summary>
/// The API answered, but not with what was asked for, or it did not answer at all. Carried as a
/// distinct type so a command can tell the room the backend is unreachable rather than failing
/// silently into a generic interaction error.
/// </summary>
public sealed class MovieBotApiException(string message, Exception? inner = null)
    : Exception(message, inner);
