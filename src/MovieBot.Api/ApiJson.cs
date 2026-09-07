using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using TheKrystalShip.MovieBot.Api.Discord;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.Subtitles;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api;

/// <summary>
/// What <c>/health</c> answers: occupancy, and no names.
///
/// <c>ColdStorage</c> carries what is wrong with the disk films are kept on and is absent when
/// nothing is, which is also the answer when there is no such disk. Silence is the ordinary
/// reading, so a line there is always something to act on.
/// </summary>
public sealed record HealthReport(string Status, int Rooms, int Watching, string? ColdStorage = null);

/// <summary>What the player needs to know about this host before it can sign anybody in.</summary>
public sealed record ClientConfig(string? DiscordClientId, string? PublicBaseUrl);

/// <summary>A refusal and its reason, which is the one shape every error body takes.</summary>
public sealed record ErrorReply(string Error);

/// <summary>What the Activity gets back for the authorization code it was handed.</summary>
public sealed record DiscordSignIn(string AccessToken, string RoomToken, SignedInUser User);

public sealed record SignedInUser(string Id, string Username, string DisplayName);

/// <summary>A token for a caller that has no Discord user of its own.</summary>
public sealed record ServiceTokenReply(string RoomToken);

/// <summary>
/// The serializer for everything this service puts on the wire that the Core library does not
/// already describe. Every root is named here and the two contexts together are the only
/// resolver the hub and the endpoints use: nothing is reached by reflection, so a type left out
/// fails in the JIT build the tests run exactly as it would in the native one.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(HealthReport))]
[JsonSerializable(typeof(ClientConfig))]
[JsonSerializable(typeof(ErrorReply))]
[JsonSerializable(typeof(DiscordSignIn))]
[JsonSerializable(typeof(ServiceTokenReply))]
[JsonSerializable(typeof(DiscordCodeRequest))]
[JsonSerializable(typeof(ServiceTokenRequest))]
[JsonSerializable(typeof(DiscordUser))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(SubtitleSearchView))]
[JsonSerializable(typeof(AddSubtitleRequest))]
[JsonSerializable(typeof(PinSubtitleRequest))]
[JsonSerializable(typeof(SubtitleAdded))]
[JsonSerializable(typeof(QuotaReply))]
// A list is looked up by the type the endpoint declares and written by the type it holds, so
// both are named. The Core context carries the same pair for the participants and the rooms.
[JsonSerializable(typeof(IReadOnlyList<TitleSummary>))]
[JsonSerializable(typeof(List<TitleSummary>))]
[JsonSerializable(typeof(List<Participant>))]
[JsonSerializable(typeof(List<RoomSummary>))]
// Hub arguments and the clock the hub answers with.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(DateTimeOffset))]
internal partial class ApiJsonContext : JsonSerializerContext;

public static class ApiJson
{
    /// <summary>The whole wire vocabulary, this service's own shapes and the Core library's.</summary>
    public static IJsonTypeInfoResolver Resolver { get; } =
        JsonTypeInfoResolver.Combine(ApiJsonContext.Default, ManifestJsonContext.Default);
}
