using Microsoft.Extensions.Options;
using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.Transcode;

/// <summary>
/// The library as the planner reads it: one <see cref="PlanTitle"/> per title.
///
/// Asking the filesystem about every title costs a second on the cold disk, and the plan is asked
/// for every few seconds, so the list is rebuilt every <c>CatalogRefreshSeconds</c> and reused
/// between. A title still being made or held by a room is read fresh each time: its head is what
/// the plan turns on, and there are few of them.
/// </summary>
public sealed class TranscodeCatalog(
    MediaRoots roots,
    Library.TitleLibrary library,
    IManifestHeads heads,
    IOptionsMonitor<TranscodeOptions> options,
    TimeProvider clock)
{
    private readonly Lock _gate = new();
    private Dictionary<string, PlanTitle> _titles = [];
    private DateTimeOffset _builtAt = DateTimeOffset.MinValue;

    public IReadOnlyList<PlanTitle> Titles(IReadOnlySet<string> held)
    {
        var now = clock.GetUtcNow();
        Dictionary<string, PlanTitle> snapshot;

        lock (_gate)
        {
            if (now - _builtAt >= TimeSpan.FromSeconds(options.CurrentValue.CatalogRefreshSeconds))
            {
                _titles = Build();
                _builtAt = now;
            }

            snapshot = new Dictionary<string, PlanTitle>(_titles, StringComparer.Ordinal);
        }

        foreach (var (id, title) in snapshot.ToList())
        {
            if (title.Status != TitleStatus.Transcoding && !held.Contains(id)) continue;
            if (library.Get(id) is { } manifest) snapshot[id] = Of(manifest);
            else snapshot.Remove(id);
        }

        return [.. snapshot.Values];
    }

    private Dictionary<string, PlanTitle> Build()
    {
        var titles = new Dictionary<string, PlanTitle>(StringComparer.Ordinal);
        foreach (var id in roots.Ids())
            if (library.Get(id) is { } manifest)
                titles[id] = Of(manifest);

        return titles;
    }

    private PlanTitle Of(Manifest manifest) => new(
        manifest.Id,
        manifest.Status,
        manifest.DurationSeconds,
        manifest.Video.Width,
        manifest.Video.Height,
        [.. manifest.Video.Renditions.Select(r =>
        {
            var (width, height) = r.SizeIn(manifest.Video);
            return new TranscodeRung(DirectoryOf(r), width, height, r.BitrateKbps);
        })],
        manifest.Audio.Count > 0,
        heads.Of(manifest));

    /// <summary>The directory a rendition is written to: the first part of its playlist's URI.</summary>
    public static string DirectoryOf(Rendition rendition)
    {
        var slash = rendition.Uri.IndexOf('/');
        return slash < 0 ? rendition.Uri : rendition.Uri[..slash];
    }
}
