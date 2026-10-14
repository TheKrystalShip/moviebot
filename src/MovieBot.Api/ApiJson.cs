using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using TheKrystalShip.MovieBot.Api.Discord;
using TheKrystalShip.MovieBot.Api.Library;
using TheKrystalShip.MovieBot.Api.People;
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

/// <summary>
/// What the page needs to know about this host before it can sign anybody in: the identity provider's
/// issuer, the client the site signs in with and the one it signs in with on a television, the claim a
/// person's actions arrive in, the Discord application the Activity runs as, and the public address.
/// </summary>
public sealed record ClientConfig(
    string? DiscordClientId, string? PublicBaseUrl, string Issuer, string ClientId, string TvClientId,
    string ActionsClaim);

/// <summary>A refusal and its reason, which is the one shape every error body takes.</summary>
public sealed record ErrorReply(string Error);

/// <summary>
/// What a player carries on a request for one film's bytes, and when it stops working: at
/// <c>ExpiresAt</c> by the server's clock, which is <c>ExpiresIn</c> seconds from the reply. A
/// device whose own clock is off measures the ticket's life by <c>ExpiresIn</c>.
///
/// Every viewer of a title is handed the same string, so their segment requests are one URL a
/// cache can answer once rather than one per person.
/// </summary>
public sealed record MediaTicketReply(string Ticket, DateTimeOffset ExpiresAt, long ExpiresIn);

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
[JsonSerializable(typeof(Sessions.ScreenView))]
[JsonSerializable(typeof(Sessions.ScreenWatch))]
[JsonSerializable(typeof(List<Sessions.ScreenView>))]
[JsonSerializable(typeof(Sessions.PairingCodeView))]
[JsonSerializable(typeof(Sessions.PairedPhone))]
[JsonSerializable(typeof(List<Sessions.PairedPhone>))]
[JsonSerializable(typeof(Sessions.PairRequest))]
[JsonSerializable(typeof(MediaTicketReply))]
[JsonSerializable(typeof(ActivitySignInRequest))]
[JsonSerializable(typeof(ActivitySignInReply))]
[JsonSerializable(typeof(ActivitySignInRefusal))]
[JsonSerializable(typeof(Auth.ActionManifest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(PinSubtitleRequest))]
[JsonSerializable(typeof(ViewerPreferences))]
[JsonSerializable(typeof(FilmTracks))]
[JsonSerializable(typeof(TrackChoice))]
[JsonSerializable(typeof(SortOrder))]
[JsonSerializable(typeof(Dictionary<string, SortOrder>))]
[JsonSerializable(typeof(Films.FilmView))]
[JsonSerializable(typeof(Films.PopularNowView))]
[JsonSerializable(typeof(Films.CataloguePersonView))]
[JsonSerializable(typeof(Films.CatalogueGenreView))]
[JsonSerializable(typeof(Films.CatalogueSearchView))]
[JsonSerializable(typeof(Films.CataloguePeopleView))]
[JsonSerializable(typeof(Films.PersonCreditsView))]
[JsonSerializable(typeof(Films.CatalogueDiscoverView))]
[JsonSerializable(typeof(Films.CatalogueListView))]
[JsonSerializable(typeof(Films.LibraryAdditionsView))]
[JsonSerializable(typeof(Films.DiscoverIndexView))]
[JsonSerializable(typeof(Films.ShelfView))]
[JsonSerializable(typeof(List<Films.ShelfView>))]
[JsonSerializable(typeof(List<Films.FilmView>))]
[JsonSerializable(typeof(List<Films.ShelfTitleView>))]
[JsonSerializable(typeof(List<Films.ShelfSectionView>))]
[JsonSerializable(typeof(List<Films.ShelfHeadView>))]
[JsonSerializable(typeof(List<Films.DiscoverRowView>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<Films.LanguageOptionView>))]
[JsonSerializable(typeof(List<Acquisition.FoundTitle>))]
[JsonSerializable(typeof(TitleProgress))]
[JsonSerializable(typeof(TitleLastPlayed))]
[JsonSerializable(typeof(List<ProgressEntry>))]
[JsonSerializable(typeof(List<HistoryEntry>))]
// A list is looked up by the type the endpoint declares and written by the type it holds, so
// both are named. The Core context carries the same pair for the participants and the rooms.
[JsonSerializable(typeof(IReadOnlyList<TitleSummary>))]
[JsonSerializable(typeof(List<TitleSummary>))]
[JsonSerializable(typeof(Shows.ShowView))]
[JsonSerializable(typeof(List<Shows.ShowSummary>))]
[JsonSerializable(typeof(Shows.FollowView))]
[JsonSerializable(typeof(List<Shows.FollowView>))]
[JsonSerializable(typeof(Shows.FollowRequest))]
[JsonSerializable(typeof(Lists.ListSummary))]
[JsonSerializable(typeof(List<Lists.ListSummary>))]
[JsonSerializable(typeof(Lists.ListView))]
[JsonSerializable(typeof(Lists.ListEntryView))]
[JsonSerializable(typeof(Lists.ListNameRequest))]
[JsonSerializable(typeof(Lists.SaveEntryRequest))]
[JsonSerializable(typeof(Lists.ListMemberRequest))]
[JsonSerializable(typeof(Sessions.RoomAround))]
[JsonSerializable(typeof(Offline.OfflineOffer))]
[JsonSerializable(typeof(Offline.OfflineSaveRequest))]
[JsonSerializable(typeof(Offline.OfflinePackage))]
[JsonSerializable(typeof(Offline.OfflineSave))]
[JsonSerializable(typeof(List<Offline.OfflineSave>))]
[JsonSerializable(typeof(List<Offline.OfflineProgress>))]
[JsonSerializable(typeof(Offline.OfflineWantRequest))]
[JsonSerializable(typeof(Offline.OfflineWant))]
[JsonSerializable(typeof(Offline.OfflineEstimate))]
[JsonSerializable(typeof(List<Offline.OfflineWant>))]
[JsonSerializable(typeof(List<Participant>))]
[JsonSerializable(typeof(List<RoomSummary>))]
// Hub arguments and the clock the hub answers with.
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTimeOffset))]
internal partial class ApiJsonContext : JsonSerializerContext;

public static class ApiJson
{
    /// <summary>The whole wire vocabulary, this service's own shapes and the Core library's.</summary>
    public static IJsonTypeInfoResolver Resolver { get; } =
        JsonTypeInfoResolver.Combine(ApiJsonContext.Default, ManifestJsonContext.Default);
}
