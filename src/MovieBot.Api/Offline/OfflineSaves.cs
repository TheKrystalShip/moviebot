using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TheKrystalShip.MovieBot.Api.Data;
using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Offline;

/// <summary>One save of one title onto one of a person's devices, as it is kept.</summary>
public sealed record StoredSave
{
    public required string Id { get; init; }
    public required string TitleId { get; init; }

    /// <summary>The video rendition saved, by its directory.</summary>
    public required string Rung { get; init; }

    public required List<string> Audio { get; init; }
    public required List<string> Subtitles { get; init; }

    /// <summary>
    /// A language whose text subtitles the save takes besides <see cref="Subtitles"/>, whichever the title
    /// carries when the package is asked for; absent for none.
    /// </summary>
    public string? SubtitleLanguage { get; init; }

    /// <summary>
    /// Whether a subtitle in <see cref="SubtitleLanguage"/> was looked for outside the title, found or
    /// not; it is looked for once.
    /// </summary>
    public bool SubtitleSought { get; init; }

    /// <summary>The name the device goes by, as it gave it.</summary>
    public required string Device { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the device said it holds every byte; absent while it is still being made.</summary>
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>
/// A film or an episode a person asked to download onto a device before the library could give it to
/// them, as it is kept. The device downloads it once the library holds it, and the want goes then.
/// </summary>
public sealed record StoredWant
{
    public required string Id { get; init; }

    /// <summary>The film's IMDb id, or an episode's show's.</summary>
    public required string ImdbId { get; init; }

    /// <summary>The episode; absent for a film.</summary>
    public EpisodePick? Episode { get; init; }

    /// <summary>The name the device goes by, as it gave it.</summary>
    public required string Device { get; init; }

    /// <summary>The picture height the device downloads it at; absent for the device's own pick.</summary>
    public int? Height { get; init; }

    /// <summary>The audio language asked for; absent for the title's own.</summary>
    public string? AudioLanguage { get; init; }

    /// <summary>The subtitle language asked for; absent for none.</summary>
    public string? SubtitleLanguage { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The download fetching it, once one was started or found.</summary>
    public string? Hash { get; init; }

    /// <summary>Why nothing could be started for it (<see cref="WantStages"/>' problems); absent otherwise.</summary>
    public string? Problem { get; init; }

    /// <summary>The disk budget's or the tracker's words, for a download refused.</summary>
    public string? Refusal { get; init; }

    /// <summary>Whether this is the film or episode <paramref name="imdbId"/> and <paramref name="episode"/> name.</summary>
    public bool Is(string? imdbId, EpisodePick? episode) =>
        string.Equals(ImdbId, imdbId, StringComparison.OrdinalIgnoreCase) && Episode == episode;
}

/// <summary>
/// How a want is to be downloaded: the picture height (absent for the device's own pick), the audio
/// language (absent for the title's own) and the subtitle language (absent for none).
/// </summary>
public sealed record WantPick(int? Height, string? AudioLanguage = null, string? SubtitleLanguage = null);

/// <summary>The titles a save still being made keeps in the library and in place.</summary>
public interface ISaveHolds
{
    IReadOnlySet<string> Holding(DateTimeOffset now);
}

/// <summary>
/// What each person saved onto which device.
///
/// A save still being made holds its title: the device is copying the title's files one track after
/// another, and a title that left the library, moved between disks or was encoded again under it
/// would leave the device holding half of one film and half of another. The hold ends when the device
/// says it has every byte, when the save is forgotten, or <see cref="OfflineOptions.HoldHours"/> after
/// the save was made, so a device that walked away mid-save holds nothing for long.
///
/// Beside the saves are the wants: what a person asked a device to download before the library held
/// it. A want holds nothing; it is what the device waits on, and it goes when the device saves the
/// title it became, when the person lets it go, or <see cref="OfflineOptions.WantDays"/> after it was
/// asked for.
/// </summary>
public sealed class OfflineSaves(CinemaDatabase database, IOptionsMonitor<OfflineOptions> options) : ISaveHolds
{
    private const string SaveColumns = "id, title_id, rung, subtitle_language, subtitle_sought, device, created_at, completed_at";

    private const string WantColumns =
        "id, imdb_id, season, episode, device, height, audio_language, subtitle_language, created_at, hash, problem, refusal";

    /// <summary>A person's saves, newest first.</summary>
    public List<StoredSave> For(string userId) =>
        database.Read(c => Saves(c, "user_id = $user ORDER BY created_at DESC, rowid DESC", ("$user", userId)));

    /// <summary>Whether the person has a save of the title on any device, which is what opens its tracks to them.</summary>
    public bool Has(string userId, string titleId) =>
        database.Read(c => c.Exists("SELECT 1 FROM offline_saves WHERE user_id = $user AND title_id = $title",
            ("$user", userId), ("$title", titleId)));

    /// <summary>
    /// Records a save. One of the same title on the same device is that save made again: it keeps its
    /// id, starts its hold over and is no longer complete.
    /// </summary>
    public StoredSave Save(string userId, string titleId, string rung, List<string> audio, List<string> subtitles,
        string device, DateTimeOffset now, string? subtitleLanguage = null, bool subtitleSought = false) => database.Write(c =>
    {
        var id = c.First("SELECT id FROM offline_saves WHERE user_id = $user AND title_id = $title AND device = $device",
            r => r.GetString(0), ("$user", userId), ("$title", titleId), ("$device", device));
        var save = new StoredSave
        {
            Id = id ?? NewId(),
            TitleId = titleId,
            Rung = rung,
            Audio = audio,
            Subtitles = subtitles,
            SubtitleLanguage = subtitleLanguage,
            SubtitleSought = subtitleSought,
            Device = device,
            CreatedAt = now,
        };

        c.Execute("DELETE FROM offline_saves WHERE id = $id", ("$id", save.Id));
        c.Execute(
            $"INSERT INTO offline_saves (user_id, {SaveColumns}) VALUES ($user, $id, $title, $rung, $language, $sought, $device, $created, NULL)",
            ("$user", userId), ("$id", save.Id), ("$title", titleId), ("$rung", rung), ("$language", subtitleLanguage),
            ("$sought", subtitleSought), ("$device", device), ("$created", now));
        foreach (var (kind, tracks) in new[] { ("audio", audio), ("subtitle", subtitles) })
            for (var i = 0; i < tracks.Count; i++)
                c.Execute("INSERT INTO offline_save_tracks (save_id, kind, position, track) VALUES ($id, $kind, $position, $track)",
                    ("$id", save.Id), ("$kind", kind), ("$position", i), ("$track", tracks[i]));
        return save;
    });

    /// <summary>Marks a save complete. Null when the person has no save by that id.</summary>
    public StoredSave? Complete(string userId, string id, DateTimeOffset now) => database.Write(c =>
    {
        if (Saves(c, "user_id = $user AND id = $id", ("$user", userId), ("$id", id)).FirstOrDefault() is not { } save) return null;
        if (save.CompletedAt is not null) return save;

        c.Execute("UPDATE offline_saves SET completed_at = $now WHERE id = $id", ("$id", id), ("$now", now));
        return save with { CompletedAt = now };
    });

    /// <summary>Forgets a save. False when the person had none by that id.</summary>
    public bool Forget(string userId, string id) =>
        database.Write(c => c.Execute("DELETE FROM offline_saves WHERE user_id = $user AND id = $id", ("$user", userId), ("$id", id)) > 0);

    /// <summary>When the save stops holding its title, or null when it holds nothing.</summary>
    public DateTimeOffset? HoldsUntil(StoredSave save, DateTimeOffset now)
    {
        if (save.CompletedAt is not null) return null;
        var until = save.CreatedAt + options.CurrentValue.Hold;
        return until > now ? until : null;
    }

    public IReadOnlySet<string> Holding(DateTimeOffset now) =>
        database.Read(c => c.Query("SELECT title_id FROM offline_saves WHERE completed_at IS NULL AND created_at > $since",
                r => r.GetString(0), ("$since", now - options.CurrentValue.Hold)))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>A person's wants not yet past <see cref="OfflineOptions.WantDays"/>, the oldest first.</summary>
    public List<StoredWant> WantsOf(string userId, DateTimeOffset now) =>
        database.Read(c => Wants(c, userId, now));

    /// <summary>
    /// Records a want for each of <paramref name="episodes"/> of a show, or for the film when there are
    /// none. Asking again for one the device already wants keeps it, with the height and languages asked
    /// now; one that could not be fetched is asked for afresh. Answers the wants, in the order asked.
    /// </summary>
    public List<StoredWant> Want(string userId, string imdbId, IReadOnlyList<EpisodePick>? episodes, string device,
        WantPick pick, DateTimeOffset now) => database.Write(c =>
    {
        c.Execute("DELETE FROM offline_wants WHERE user_id = $user AND created_at <= $since",
            ("$user", userId), ("$since", now - options.CurrentValue.WantFor));
        var mine = Wants(c, userId, now);
        var asked = new List<StoredWant>();
        var each = episodes is { Count: > 0 } picked
            ? picked.Distinct().Select(e => (EpisodePick?)e).ToList()
            : new List<EpisodePick?> { null };
        foreach (var episode in each)
        {
            var existing = mine.FirstOrDefault(w => w.Device == device && w.Is(imdbId, episode));
            var want = existing is { Problem: null }
                ? existing with { Height = pick.Height, AudioLanguage = pick.AudioLanguage, SubtitleLanguage = pick.SubtitleLanguage }
                : new StoredWant
                {
                    Id = existing?.Id ?? NewId(), ImdbId = imdbId, Episode = episode, Device = device, Height = pick.Height,
                    AudioLanguage = pick.AudioLanguage, SubtitleLanguage = pick.SubtitleLanguage, CreatedAt = now,
                };
            c.Execute("DELETE FROM offline_wants WHERE id = $id", ("$id", want.Id));
            Insert(c, userId, want);
            asked.Add(want);
        }
        return asked;
    });

    /// <summary>
    /// Records what fetching a want came to: the download fetching it, or the problem that stopped one
    /// being started. A want let go of meanwhile stays gone.
    /// </summary>
    public void Fetched(string userId, string id, string? hash, string? problem = null, string? refusal = null) =>
        database.Write(c => c.Execute(
            "UPDATE offline_wants SET hash = $hash, problem = $problem, refusal = $refusal WHERE user_id = $user AND id = $id",
            ("$user", userId), ("$id", id), ("$hash", hash), ("$problem", problem), ("$refusal", refusal)));

    /// <summary>Lets a want go. False when the person had none by that id.</summary>
    public bool Unwant(string userId, string id) =>
        database.Write(c => c.Execute("DELETE FROM offline_wants WHERE user_id = $user AND id = $id", ("$user", userId), ("$id", id)) > 0);

    /// <summary>Lets go of the device's wants for a title it has just saved: it has what it waited on.</summary>
    public void Fulfilled(string userId, string device, string? imdbId, EpisodePick? episode) =>
        database.Write(c => c.Execute(
            "DELETE FROM offline_wants WHERE user_id = $user AND device = $device AND imdb_id = $imdb COLLATE NOCASE "
            + "AND season IS $season AND episode IS $episode",
            ("$user", userId), ("$device", device), ("$imdb", imdbId), ("$season", episode?.Season), ("$episode", episode?.Episode)));

    private static List<StoredSave> Saves(SqliteConnection c, string where, params ReadOnlySpan<(string Name, object? Value)> args)
    {
        var saves = c.Query($"SELECT {SaveColumns} FROM offline_saves WHERE {where}", r => new StoredSave
        {
            Id = r.GetString(0),
            TitleId = r.GetString(1),
            Rung = r.GetString(2),
            SubtitleLanguage = r.GetStringOrNull(3),
            SubtitleSought = r.GetBool(4),
            Device = r.GetString(5),
            CreatedAt = r.GetInstant(6),
            CompletedAt = r.GetInstantOrNull(7),
            Audio = [],
            Subtitles = [],
        }, args);

        foreach (var save in saves)
            foreach (var (kind, track) in c.Query("SELECT kind, track FROM offline_save_tracks WHERE save_id = $id ORDER BY kind, position",
                         r => (r.GetString(0), r.GetString(1)), ("$id", save.Id)))
                (kind == "audio" ? save.Audio : save.Subtitles).Add(track);
        return saves;
    }

    private List<StoredWant> Wants(SqliteConnection c, string userId, DateTimeOffset now) =>
        c.Query($"SELECT {WantColumns} FROM offline_wants WHERE user_id = $user AND created_at > $since ORDER BY created_at, rowid",
            r => new StoredWant
            {
                Id = r.GetString(0),
                ImdbId = r.GetString(1),
                Episode = r.IsDBNull(2) ? null : new EpisodePick(r.GetInt32(2), r.GetInt32(3)),
                Device = r.GetString(4),
                Height = r.GetIntOrNull(5),
                AudioLanguage = r.GetStringOrNull(6),
                SubtitleLanguage = r.GetStringOrNull(7),
                CreatedAt = r.GetInstant(8),
                Hash = r.GetStringOrNull(9),
                Problem = r.GetStringOrNull(10),
                Refusal = r.GetStringOrNull(11),
            },
            ("$user", userId), ("$since", now - options.CurrentValue.WantFor));

    private static void Insert(SqliteConnection c, string userId, StoredWant want) =>
        c.Execute(
            $"INSERT INTO offline_wants (user_id, {WantColumns}) VALUES "
            + "($user, $id, $imdb, $season, $episode, $device, $height, $audio, $subtitle, $created, $hash, $problem, $refusal)",
            ("$user", userId), ("$id", want.Id), ("$imdb", want.ImdbId), ("$season", want.Episode?.Season),
            ("$episode", want.Episode?.Episode), ("$device", want.Device), ("$height", want.Height),
            ("$audio", want.AudioLanguage), ("$subtitle", want.SubtitleLanguage), ("$created", want.CreatedAt),
            ("$hash", want.Hash), ("$problem", want.Problem), ("$refusal", want.Refusal));

    private static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
}
