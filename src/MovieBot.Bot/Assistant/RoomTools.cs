using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using TheKrystalShip.Agent.Replies;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.Llm.Models;
using TheKrystalShip.MovieBot.Acquire;
using TheKrystalShip.MovieBot.Acquire.Download;
using TheKrystalShip.MovieBot.Acquire.Tracker;
using TheKrystalShip.MovieBot.Acquire.Imdb;
using TheKrystalShip.MovieBot.Acquire.Search;
using TheKrystalShip.MovieBot.Bot.Api;
using TheKrystalShip.MovieBot.Bot.Download;
using TheKrystalShip.MovieBot.Bot.Keep;
using TheKrystalShip.MovieBot.Bot.Launch;
using TheKrystalShip.MovieBot.Bot.Library;
using TheKrystalShip.MovieBot.Bot.Notify;
using TheKrystalShip.MovieBot.Bot.Watch;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Bot.Assistant;

/// <summary>
/// Every tool the assistant can call, carried out by the same code the slash commands and the room
/// verbs run.
/// </summary>
/// <remarks>
/// <para>
/// <b>What runs at once and what waits is decided here, per tool.</b> Reading and moving the room run
/// immediately: pausing is the point of a film room, it is undone by saying so, and the player already
/// tells everyone who did it. A download, a fetched subtitle, a wish and a keep each spend something —
/// disk, a daily allowance, a place on the disk — so those are proposed, and happen when somebody
/// agrees.
/// </para>
/// <para>
/// <b>No second spelling of any act.</b> Loading a film is <see cref="WatchCommand"/>, a download is
/// <see cref="DownloadCommand"/>, a wish is <see cref="NotifyCommand"/> and a keep is
/// <see cref="KeepCommand"/>. A tool that did its own version would not disagree loudly; it would
/// leave a download without its progress message or a film under a second id.
/// </para>
/// <para>
/// <b>Never throws.</b> A failure is a sentence the model reads and can say, naming what to do
/// instead, because a refusal that names no alternative is answered by the same call again.
/// </para>
/// </remarks>
public sealed class RoomToolbox(
    MovieBotApiClient api,
    DiscordSocketClient discord,
    WatchCommand watch,
    DownloadCommand download,
    NotifyCommand notify,
    KeepCommand keep,
    WishList wishes,
    AcquisitionService acquisition,
    IReleaseSearch tracker,
    ImdbClient catalogue,
    StagedActions staged,
    OfferedReleases offered,
    TimeProvider clock,
    ILogger<RoomTools> logger)
{
    /// <param name="said">What the person said, which is what a tool holds the model's arguments against.</param>
    public RoomTools For(RoomTurn turn, string said = "") =>
        new(turn, said, api, discord, watch, download, notify, keep, wishes, acquisition, tracker, catalogue,
            staged, offered, clock, logger);
}

/// <summary>The tools for one turn: one room, one person, and what the turn proposed and launched.</summary>
public sealed partial class RoomTools(
    RoomTurn turn,
    string said,
    MovieBotApiClient api,
    DiscordSocketClient discord,
    WatchCommand watch,
    DownloadCommand download,
    NotifyCommand notify,
    KeepCommand keep,
    WishList wishes,
    AcquisitionService acquisition,
    IReleaseSearch tracker,
    ImdbClient catalogue,
    StagedActions staged,
    OfferedReleases offered,
    TimeProvider clock,
    ILogger<RoomTools> logger) : IToolDispatcher
{
    public const string RoomState = "room_state";
    public const string ListRooms = "list_rooms";
    public const string SearchLibrary = "search_library";
    public const string GetTitle = "get_title";
    public const string SearchCatalogue = "search_catalogue";
    public const string SearchTracker = "search_tracker";
    public const string DownloadStatus = "download_status";
    public const string WishListTool = "wish_list";
    public const string Play = "play";
    public const string Pause = "pause";
    public const string Seek = "seek";
    public const string SeekRelative = "seek_relative";
    public const string LoadTitle = "load_title";
    public const string DownloadFilm = "download_film";
    public const string FetchSubtitle = "fetch_subtitle";
    public const string AddWish = "add_wish";
    public const string KeepFilm = "keep_film";
    public const string LetGo = "let_go";

    /// <summary>Every tool this dispatcher implements, which <see cref="ToolCatalog"/> holds the file to.</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        RoomState, ListRooms, SearchLibrary, GetTitle, SearchCatalogue, SearchTracker, DownloadStatus,
        WishListTool, Play, Pause, Seek, SeekRelative, LoadTitle, DownloadFilm, FetchSubtitle, AddWish,
        KeepFilm, LetGo,
    ];

    /// <summary>The tools that propose rather than act.</summary>
    public static readonly IReadOnlySet<string> Proposals =
        new HashSet<string>(StringComparer.Ordinal) { DownloadFilm, FetchSubtitle, AddWish, KeepFilm, LetGo };

    /// <summary>The tools that move the room, which is what a question about why the film stopped is answered from.</summary>
    public static readonly IReadOnlySet<string> RoomMoves =
        new HashSet<string>(StringComparer.Ordinal) { Play, Pause, Seek, SeekRelative, LoadTitle };

    private const int MostRows = 8;

    private readonly List<StagedAction> _proposed = [];
    private readonly List<LaunchReply> _launched = [];
    private readonly List<string> _told = [];

    /// <summary>What this turn proposed, in the order it proposed it.</summary>
    public IReadOnlyList<StagedAction> Proposed => _proposed;

    /// <summary>
    /// Whether this turn did anything to a room or proposed anything — the fact a reply claiming to
    /// have acted is held against.
    /// </summary>
    public bool Acted => _moved || _proposed.Count > 0 || _launched.Count > 0;

    private bool _moved;

    /// <summary>The launch cards this turn produced, for the chat to post.</summary>
    public IReadOnlyList<LaunchReply> Launched => _launched;

    /// <summary>
    /// What the tools found or did this turn, in sentences written for the room rather than for the
    /// model — the answer when the model writes none.
    /// </summary>
    /// <remarks>
    /// This model ends its turn straight after a tool result more often than not, writing nothing, and
    /// an act with no reply reads as a request that was not heard. What the tool said is the honest
    /// answer then: it is what happened, in words that were never the model's to get wrong. A proposal
    /// and a launch tell nothing here, because each posts a message of its own.
    /// </remarks>
    public IReadOnlyList<string> Told => _told;

    /// <summary>
    /// A result the room could read as it stands, followed for the model alone by what to do next.
    /// Guidance names a tool, so it is never shown to a person.
    /// </summary>
    private string Tell(string people, string? guidance = null)
    {
        _told.Add(people);
        return guidance is null ? people : $"{people} {guidance}";
    }

    /// <remarks>
    /// Every answer is noted for the turn's reply review, here rather than per handler, because a tool's
    /// answer is what it returns and a handler added later would otherwise go unrecorded — and a figure
    /// missing from the record reads as one the model invented.
    /// </remarks>
    public async Task<ToolOutput> ExecuteAsync(LlmToolCall call, CancellationToken ct = default)
    {
        var output = await DispatchAsync(call, ct);
        MeasuredValues.Note(output.Summary);
        return output;
    }

    private async Task<ToolOutput> DispatchAsync(LlmToolCall call, CancellationToken ct)
    {
        try
        {
            return call.Name.Name switch
            {
                RoomState => await RoomStateAsync(ct),
                ListRooms => await ListRoomsAsync(ct),
                SearchLibrary => await SearchLibraryAsync(call.Arg("query"), ct),
                GetTitle => await GetTitleAsync(call.Arg("title"), ct),
                SearchCatalogue => await SearchCatalogueAsync(call.Arg("query"), call.Arg("year"), ct),
                SearchTracker => await SearchTrackerAsync(call.Arg("query"), call.Arg("imdb_id"), ct),
                DownloadStatus => await DownloadStatusAsync(ct),
                WishListTool => WishListing(),
                Play => await PlaybackAsync(pause: false, ct),
                Pause => await PlaybackAsync(pause: true, ct),
                Seek => await SeekAsync(call.Arg("position"), ct),
                SeekRelative => await SeekRelativeAsync(call.Arg("seconds"), ct),
                LoadTitle => await LoadTitleAsync(call.Arg("title"), ct),
                DownloadFilm => await ProposeDownloadAsync(call.Arg("torrent_id"), ct),
                FetchSubtitle => await ProposeSubtitleAsync(call.Arg("title"), ct),
                AddWish => await ProposeWishAsync(call.Arg("imdb_id"), ct),
                KeepFilm => await ProposeKeepAsync(call.Arg("title"), keeping: true, ct),
                LetGo => await ProposeKeepAsync(call.Arg("title"), keeping: false, ct),
                _ => $"There is no tool called {call.Name}. Use one of the tools you were given.",
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (MovieBotApiException ex)
        {
            logger.LogWarning(ex, "Assistant: {Tool} could not reach the API", call.Name);
            return Tell("The MovieBot API is not answering, so nothing happened.", "Do not retry.");
        }
        catch (TrackerException ex)
        {
            logger.LogWarning(ex, "Assistant: {Tool} could not reach the tracker", call.Name);
            return Tell("The tracker is not answering, so nothing happened.", "Do not retry.");
        }
        catch (QBittorrentException ex)
        {
            logger.LogWarning(ex, "Assistant: {Tool} could not reach the torrent client", call.Name);
            return Tell("The torrent client is not answering, so nothing happened.", "Do not retry.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Assistant: {Tool} failed", call.Name);
            return Tell($"That did not work: {ex.Message}", "Do not retry.");
        }
    }

    // ── Reads ────────────────────────────────────────────────────────────────────────────────

    private async Task<string> RoomStateAsync(CancellationToken ct)
    {
        var library = await api.ListTitlesAsync(ct);
        var state = await api.ReadRoomAsync(turn.SessionId, ct);
        return Tell(RoomFacts.Describe(turn.ChannelName, state, library, clock.GetUtcNow()));
    }

    private async Task<string> ListRoomsAsync(CancellationToken ct)
    {
        var rooms = await api.ListRoomsAsync(ct);
        var watching = rooms.Where(r => r.TitleId is not null).ToList();
        if (watching.Count == 0) return Tell("No room is watching anything.");

        var text = new StringBuilder();
        foreach (var room in watching)
        {
            var name = ulong.TryParse(room.SessionId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                       && discord.GetChannel(id) is SocketGuildChannel channel
                ? channel.Name
                : room.SessionId;

            text.AppendLine(
                $"- {name}: {room.Name ?? room.TitleId} at {FilmClock.Format(room.PositionSeconds)}, "
                + $"{(room.Paused ? "paused" : "playing")}, {room.Participants} watching"
                + (room.SessionId == turn.SessionId ? " (this room)" : ""));
        }

        return Tell(text.ToString().TrimEnd());
    }

    private async Task<string> SearchLibraryAsync(string? query, CancellationToken ct)
    {
        var library = await api.ListTitlesAsync(ct);
        if (library.Count == 0) return Tell("The library is empty.", "search_tracker finds films that can be downloaded.");

        var found = TitleMatcher.Suggest(library, query ?? "");
        if (found.Count == 0)
            return Tell($"Nothing in the library matches \"{query}\".",
                "search_catalogue identifies the film, and search_tracker finds releases of it to download.");

        return Tell(string.Join('\n', found.Take(MostRows).Select(t => $"- {t.Name}{RoomFacts.StatusNote(t)} (id {t.Id})"))
               + (found.Count > MostRows ? $"\n({found.Count - MostRows} more match.)" : ""));
    }

    private async Task<string> GetTitleAsync(string? query, CancellationToken ct)
    {
        var (title, refusal) = await ResolveAsync(query, ct);
        if (title is null) return refusal!;

        var manifest = await api.GetTitleAsync(title.Id, ct);
        if (manifest is null) return Tell($"{title.Name} has just left the library.");

        var text = new StringBuilder($"{title.Name} (id {title.Id}), {FilmClock.Format(manifest.DurationSeconds)} long");
        text.Append(RoomFacts.StatusNote(title)).AppendLine(".");

        if (manifest.Film?.Starring is { Length: > 0 } starring) text.AppendLine($"Starring {starring}.");

        var audio = manifest.Audio.Where(a => a.Kind == TrackKind.Feature).Select(a => a.Label).ToList();
        if (audio.Count > 0) text.AppendLine($"Audio: {string.Join("; ", audio)}.");

        var subtitles = manifest.Subtitles.Where(s => s.Kind == TrackKind.Feature).ToList();
        text.AppendLine(subtitles.Count == 0
            ? "Subtitles: none."
            : "Subtitles: " + string.Join("; ", subtitles.Select(s =>
                s.Label + (s.Available ? "" : " (a picture track, cannot be shown)")
                        + (s.Pin is { } pin ? $" (confirmed by {pin.PinnedBy})" : ""))) + ".");

        if (manifest.Chapters.Count > 0) text.AppendLine($"{manifest.Chapters.Count} chapters.");

        if (await keep.NoticeForAsync(title.Id, ct) is { } notice) text.AppendLine(notice);

        return Tell(text.ToString().TrimEnd());
    }

    private async Task<string> SearchCatalogueAsync(string? query, string? year, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return "search_catalogue needs the film's name as query.";

        var films = await catalogue.SuggestAsync(query, ct);
        if (int.TryParse(year, NumberStyles.None, CultureInfo.InvariantCulture, out var wanted))
            films = films.Where(f => f.Year == wanted).ToList();

        if (films.Count == 0)
            return Tell($"The catalogue has no film matching \"{query}\"{(year is null ? "" : $" from {year}")}.");

        // In the order the films came out. The index orders them by what people search for now, which
        // puts the latest sequel above the film it follows, and "the second one" read off that list
        // is whichever sequel is popular this week.
        var library = await api.ListTitlesAsync(ct);
        return Tell(string.Join('\n', films.Take(MostRows).OrderBy(f => f.Year ?? int.MaxValue).Select(f =>
        {
            var here = library.FirstOrDefault(t =>
                string.Equals(t.Film?.ImdbId, f.ImdbId, StringComparison.OrdinalIgnoreCase));
            return $"- {f.Display}"
                   + (f.Starring is { Length: > 0 } cast ? $", starring {cast}" : "")
                   + (here is null ? "" : $", already in the library as {here.Id}")
                   + $" (imdb {f.ImdbId})";
        })), "Listed in the order they came out, earliest first.");
    }

    private async Task<string> SearchTrackerAsync(string? query, string? imdbId, CancellationToken ct)
    {
        RankedReleases ranked;
        if (ImdbId.FromText(imdbId) is { } id)
            ranked = await tracker.ByImdbAsync(id, ct);
        else if (!string.IsNullOrWhiteSpace(query))
            ranked = await tracker.ByTextAsync(query, ct);
        else
            return "search_tracker needs query (the film's name) or imdb_id.";

        if (ranked.Candidates.Count == 0)
            return Tell(ranked.EmptyExplanation ?? "The tracker offered nothing.",
                "add_wish can wait for the film to appear, given its imdb_id from search_catalogue.");

        var shown = ranked.Candidates.Take(5).ToList();
        offered.Remember(shown);

        return Tell(string.Join('\n', shown.Select(r => $"- {r.Display} · {r.Summary} (torrent {r.TorrentId})")),
            "download_film takes the torrent id of the one to download.");
    }

    private async Task<string> DownloadStatusAsync(CancellationToken ct)
    {
        var downloads = await acquisition.ListAsync(ct);
        if (downloads.Count == 0) return Tell("Nothing is downloading or seeding.");

        var library = await api.ListTitlesAsync(ct);
        return Tell(string.Join('\n', downloads
            .OrderBy(d => d.IsFinished)
            .Take(MostRows)
            .Select(d =>
            {
                var libraryId = d.Tags.Select(TorrentTags.ReadLibrary).FirstOrDefault(i => i is not null);
                var name = library.FirstOrDefault(t => t.Id == libraryId)?.Name
                           ?? ReleaseParser.ParseName(d.Name).Display;
                var watchable = !d.IsFinished && d.Tags.Contains(TorrentTags.Watchable) ? ", already watchable" : "";
                var kept = d.Tags.Contains(TorrentTags.Keep) ? ", kept" : "";
                return $"- {name}: {d.Summary}{watchable}{kept}";
            })));
    }

    private string WishListing()
    {
        var all = wishes.All();
        if (all.Count == 0) return Tell("Nobody is waiting on a film.");

        return Tell(string.Join('\n', all.Take(MostRows).Select(w =>
            $"- {w.Display}, waited on by {w.Subscribers.Count} "
            + (w.Subscribers.Count == 1 ? "person" : "people")
            + (w.Subscribers.Any(s => s.UserId == turn.SpeakerId) ? $", {turn.SpeakerName} among them" : "")
            + $" (imdb {w.ImdbId})")));
    }

    // ── The room, immediately ────────────────────────────────────────────────────────────────

    private async Task<string> PlaybackAsync(bool pause, CancellationToken ct)
    {
        if (await RefuseWithoutFilmAsync(ct) is { } refusal) return refusal;

        var change = pause
            ? await api.PauseAsync(turn.SessionId, turn.SpeakerUserId, turn.SpeakerName, ct)
            : await api.PlayAsync(turn.SessionId, turn.SpeakerUserId, turn.SpeakerName, ct);
        _moved = true;

        var at = FilmClock.Format(change.Push.State.PositionSeconds);
        return Tell(pause ? $"Paused at {at}." : $"Playing from {at}.");
    }

    private async Task<string> SeekAsync(string? position, CancellationToken ct)
    {
        if (FilmClock.Parse(position) is not { } seconds)
            return $"\"{position}\" is not a position. Give it as h:mm:ss, m:ss, or seconds.";

        if (await RefuseWithoutFilmAsync(ct) is { } refusal) return refusal;

        var change = await api.SeekAsync(turn.SessionId, seconds, turn.SpeakerUserId, turn.SpeakerName, ct);
        _moved = true;
        return Tell(Moved(change));
    }

    private async Task<string> SeekRelativeAsync(string? amount, CancellationToken ct)
    {
        if (!double.TryParse(amount, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds == 0)
            return $"\"{amount}\" is not an amount of seconds. Negative goes back, positive goes forward.";

        if (await RefuseWithoutFilmAsync(ct) is { } refusal) return refusal;

        var change = await api.NudgeAsync(turn.SessionId, seconds, turn.SpeakerUserId, turn.SpeakerName, ct);
        _moved = true;
        return Tell(Moved(change));
    }

    private static string Moved(RoomChanged change) =>
        change.Clamped is { } clamped
            ? $"Asked for {FilmClock.Format(clamped.RequestedSeconds)}, but the film is only playable up to "
              + $"{FilmClock.Format(clamped.HeadSeconds)} while it transcodes, so it went to "
              + $"{FilmClock.Format(clamped.GrantedSeconds)}."
            : $"Moved to {FilmClock.Format(change.Push.State.PositionSeconds)}.";

    private async Task<string?> RefuseWithoutFilmAsync(CancellationToken ct)
    {
        var rooms = await api.ListRoomsAsync(ct);
        return rooms.Any(r => r.SessionId == turn.SessionId && r.TitleId is not null)
            ? null
            : Tell($"No film is loaded in {turn.ChannelName}, so there is nothing to move.", "load_title puts one on.");
    }

    private async Task<string> LoadTitleAsync(string? query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return "load_title needs the film's id or name as title.";

        var result = await WatchAsync(query, ct);

        // A film named whole under a year nobody said is the film the model meant with a year it made up:
        // it writes "…-black-pearl-2004" and "The Curse of the Black Pearl (2023)" for the 2003 film, and
        // almost never calls again when told so. A number somebody did say may be the only thing saying
        // which film they meant — "pirates of the caribbean 2" came back as the first film under 2011 —
        // so then the film is offered and not put on.
        if (result.Status == WatchStatus.TitleNotFound && result.Match?.Nearest is { } guessed && !SaysANumber().IsMatch(said))
            result = await WatchAsync(guessed.Id, ct);

        // The command's own wording is written for somebody typing /watch, and a model reads "run the
        // command again" as nothing it can do. What a film that could not be picked needs is the
        // films it could have been, by name: the model retypes an id wrongly far more often than a
        // name.
        switch (result.Status)
        {
            case WatchStatus.TitleAmbiguous:
                return Tell(
                    $"Several films in the library match: {string.Join("; ", (result.Match?.Candidates ?? []).Select(t => t.Name))}.",
                    "Nothing was put on. They are listed earliest first. Call load_title again with the name of the "
                    + "one they mean, written as it is here.");

            case WatchStatus.TitleNotFound when result.Match?.Nearest is { } nearest:
                return Tell(
                    $"The library has {nearest.Name}, which is not the year asked for.",
                    $"Nothing was put on. If that is the film they asked for, call load_title with \"{nearest.Name}\". "
                    + "If they meant a different film, search_catalogue and then search_tracker find it to download.");

            case WatchStatus.TitleNotFound:
                return Tell(
                    "That film is not in the library.",
                    "Nothing was put on. Call load_title with a name from the library list you were given, written "
                    + "as it is there, or search_catalogue and then search_tracker to download a film it does not have.");
        }

        if (result.Reply is not { } reply) return Tell(result.Message);

        _launched.Add(reply);
        return reply.Text + " The card with the way in is posted in the chat; do not repeat what it says.";
    }

    private Task<WatchResult> WatchAsync(string query, CancellationToken ct) =>
        watch.ExecuteAsync(new WatchRequest
        {
            VoiceChannelId = turn.ChannelId,
            VoiceChannelName = turn.ChannelName,
            Query = query,
            RequestedBy = turn.SpeakerName,
        }, ct);

    [GeneratedRegex(@"\d|\b(?:two|three|four|five|six|seven|eight|nine|ten|second|third|fourth|fifth|sixth|last|latest|newest|oldest|original)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SaysANumber();

    // ── Proposals ────────────────────────────────────────────────────────────────────────────

    private async Task<string> ProposeDownloadAsync(string? torrentId, CancellationToken ct)
    {
        if (!long.TryParse(torrentId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return "download_film needs a torrent id from search_tracker.";

        if (offered.Find(id) is not { } release)
            return $"Torrent {id} was not offered by search_tracker. Call search_tracker, then download_film with "
                   + "a torrent id from its results.";

        if (release.ImdbId is { Length: > 0 } imdbId)
        {
            var library = await api.ListTitlesAsync(ct);
            if (library.FirstOrDefault(t => string.Equals(t.Film?.ImdbId, imdbId, StringComparison.OrdinalIgnoreCase))
                is { } here)
                return Tell($"{here.Name} is already in the library, so nothing needs downloading.",
                    "Nothing was downloaded or put on. Tell them it is already here.");
        }

        var asker = turn;
        return Propose(DownloadFilm, $"download {release.Display} · {release.Summary}", async ct2 =>
        {
            var result = await download.ExecuteAsync(new DownloadRequest
            {
                TorrentId = release.TorrentId,
                ChannelId = asker.ChannelId,
                RequestedBy = asker.SpeakerName,
                RequesterId = asker.SpeakerId,
                RoomId = asker.ChannelId,
            }, release, ct2);

            if (result.Outcome != DownloadOutcome.Started || result.Release is null)
                return new StagedOutcome(result.Message);

            return new StagedOutcome(
                result.Message,
                DownloadEmbed.Starting(result.Release, asker.ChannelName),
                posted => result.Hash is { } hash
                    ? download.RecordProgressMessageAsync(hash, asker.ChannelId, posted.Id, CancellationToken.None)
                    : Task.CompletedTask);
        });
    }

    private async Task<string> ProposeSubtitleAsync(string? query, CancellationToken ct)
    {
        var (title, refusal) = await ResolveAsync(query, ct);
        if (title is null) return refusal!;

        var search = await api.SearchSubtitlesAsync(title.Id, ct);
        if (search is null) return Tell($"{title.Name} has just left the library.");

        if (search.Candidates.OrderByDescending(c => c.Score).FirstOrDefault() is not { } best)
            return Tell(search.Explanation ?? $"The subtitle index has no English subtitles for {title.Name}.");

        var left = search.RemainingDownloads is { } remaining ? $" ({remaining} downloads left today)" : "";
        var asker = turn;

        return Propose(FetchSubtitle, $"fetch English subtitles for {title.Name}: {best.Release}{left}", async ct2 =>
        {
            var (added, refused) = await api.AddSubtitleAsync(title.Id, best.FileId, asker.SpeakerName, ct2);
            if (added is null) return new StagedOutcome(refused ?? "The subtitle could not be fetched.");

            if (added.AlreadyPinned is true)
                return new StagedOutcome($"{title.Name} already has that subtitle, confirmed by somebody watching. Nothing was spent.");

            return new StagedOutcome(
                $"Added **{added.Track.Label}** to {title.Name} for everyone. Pick it from the subtitle menu."
                + (added.LooksWrong is true ? " It still looks damaged after repair, so check it early." : "")
                + (added.RemainingDownloads is { } rest ? $" {rest} subtitle downloads left today." : ""));
        });
    }

    private async Task<string> ProposeWishAsync(string? imdbId, CancellationToken ct)
    {
        if (ImdbId.FromText(imdbId) is not { } id)
            return "add_wish needs the film's imdb_id, which search_catalogue gives.";

        if (await catalogue.LookupAsync(id, ct) is not { } film)
            return $"The catalogue has nothing under {id}. Call search_catalogue for the right id.";

        if (!film.IsFeature) return Tell($"{film.Title} is not a film, and only films can be fetched here.");

        var asker = turn;
        return Propose(AddWish, $"tell {asker.SpeakerName} when {film.Display} can be downloaded", async ct2 =>
        {
            var result = await notify.SubscribeAsync(new NotifyRequest
            {
                Chosen = id,
                ChannelId = asker.ChannelId,
                UserId = asker.SpeakerId,
                RequestedBy = asker.SpeakerName,
            }, ct2);

            return new StagedOutcome(result.Message, WishEmbed.For(result));
        });
    }

    private async Task<string> ProposeKeepAsync(string? query, bool keeping, CancellationToken ct)
    {
        var (title, refusal) = await ResolveAsync(query, ct);
        if (title is null) return refusal!;

        var downloads = await acquisition.ListAsync(ct);
        var behind = downloads.FirstOrDefault(d => d.Tags.Select(TorrentTags.ReadLibrary).Any(i => i == title.Id));
        if (behind is null)
            return Tell($"No download stands behind {title.Name}, so it is on no clock and is never pruned.");

        var kept = behind.Tags.Contains(TorrentTags.Keep);
        if (keeping && kept) return Tell($"{title.Name} is already kept.");
        if (!keeping && !kept) return Tell($"{title.Name} is not kept, so it is already on the clock to be pruned.");

        var asker = turn;
        var hash = behind.Hash;

        return keeping
            ? Propose(KeepFilm, $"keep {title.Name} from being pruned", async ct2 =>
                new StagedOutcome((await keep.KeepAsync(hash, asker.SpeakerId, ct2)).Message))
            : Propose(LetGo, $"let {title.Name} go back on the clock to be pruned", async ct2 =>
                new StagedOutcome((await keep.ReleaseAsync(hash, asker.SpeakerId, ct2)).Message));
    }

    /// <summary>
    /// Holds an act for somebody to agree to. The same act proposed twice in one turn is one
    /// proposal: a model that calls a tool again is asking again, not asking for a second download.
    /// </summary>
    private string Propose(string kind, string describes, Func<CancellationToken, Task<StagedOutcome>> run)
    {
        if (_proposed.All(p => p.Describes != describes))
        {
            _proposed.Add(staged.Stage(kind, describes, turn, run));
            logger.LogInformation("Assistant: {Speaker} was offered to {What}", turn.SpeakerName, describes);
        }

        return $"Proposed: {describes}. Nothing has happened yet. It waits for somebody to press the button "
               + "under it in the chat or say yes. Tell them in one sentence that it needs confirming.";
    }

    /// <summary>The one film a name or id means, or a sentence saying why there is not one.</summary>
    private async Task<(LibraryTitle? Title, string? Refusal)> ResolveAsync(string? query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return (null, "Name the film by its id or its name as title.");

        var library = await api.ListTitlesAsync(ct);
        var match = TitleMatcher.Resolve(library, query);

        return match.Kind switch
        {
            TitleMatchKind.Resolved => (match.Title, null),
            TitleMatchKind.Ambiguous => (null, Tell(
                $"\"{query}\" could be {string.Join(", ", match.Candidates.Take(MostRows).Select(t => t.Name))}.",
                "Ask which one they mean.")),
            _ => (null, Tell(
                $"Nothing in the library is called \"{query}\".",
                "search_catalogue and search_tracker find films that are not here.")),
        };
    }
}

/// <summary>
/// The releases search_tracker has shown the model, by torrent id, so a download it proposes is
/// always a release it was actually offered.
/// </summary>
/// <remarks>
/// A torrent id the model writes without having been shown is either invented or copied wrong, and a
/// download of the wrong film is what that costs. Kept across turns, because "the second one" is
/// often said a turn after the list.
/// </remarks>
public sealed class OfferedReleases
{
    private const int Kept = 200;

    private readonly Dictionary<long, Release> _byId = [];
    private readonly Queue<long> _order = new();

    public void Remember(IEnumerable<Release> releases)
    {
        lock (_byId)
        {
            foreach (var release in releases)
            {
                if (_byId.TryAdd(release.TorrentId, release)) _order.Enqueue(release.TorrentId);
                else _byId[release.TorrentId] = release;
            }

            while (_order.Count > Kept) _byId.Remove(_order.Dequeue());
        }
    }

    public Release? Find(long torrentId)
    {
        lock (_byId) return _byId.GetValueOrDefault(torrentId);
    }
}
