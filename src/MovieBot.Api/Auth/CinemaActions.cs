using System.Reflection;

namespace TheKrystalShip.MovieBot.Api.Auth;

/// <summary>One thing a person can be allowed to do in Krystal Cinema.</summary>
/// <param name="Id">The full id, <c>cinema:&lt;local&gt;</c>, as the actions claim carries it.</param>
/// <param name="Title">What the provider's role editor shows.</param>
/// <param name="Effect"><c>read</c>, <c>write</c> or <c>execute</c>.</param>
/// <param name="Self">
/// An action that only reads or changes the caller's own records. Every active person holds it, and the
/// provider never files it into a role.
/// </param>
public sealed record CinemaAction(string Id, string Title, string Effect, bool Self = false)
{
    /// <summary>The id without the namespace, as the manifest writes it.</summary>
    public string LocalId => Id[(CinemaActions.Namespace.Length + 1)..];
}

/// <summary>
/// Every action Cinema checks, declared once. The endpoints name these constants and the manifest the
/// provider reads is written from <see cref="All"/>, so a check and the manifest cannot disagree.
/// </summary>
/// <remarks>
/// Cinema interprets no role. It reads the actions the provider lists in the token and checks the one a
/// route needs; which role carries it is the administrator's, on the provider.
/// </remarks>
public static class CinemaActions
{
    /// <summary>The application's id at the provider, which is its action namespace.</summary>
    public const string Namespace = "cinema";

    /// <summary>Browse the library, open and move rooms, play films.</summary>
    public const string Watch = "cinema:watch";

    /// <summary>Save a film onto a device and fetch its tracks whole.</summary>
    public const string Offline = "cinema:offline";

    /// <summary>Start a download, and search the tracker for one.</summary>
    public const string Download = "cinema:download";

    /// <summary>Wait on a film that cannot be fetched yet.</summary>
    public const string Wish = "cinema:wish";

    /// <summary>Hand out a room's guest link, which lets anybody watch along without an account.</summary>
    public const string Guests = "cinema:guests";

    /// <summary>Read your own history, progress, preferences, lists, follows and notifications.</summary>
    public const string ProfileRead = "cinema:profile.read";

    /// <summary>Change your own preferences, history, lists, follows, notifications and push devices.</summary>
    public const string ProfileWrite = "cinema:profile.write";

    public static readonly IReadOnlyList<CinemaAction> All =
    [
        new(Download, "Download films and shows", "execute"),
        new(Guests, "Invite guests without an account to watch in a room", "execute"),
        new(Offline, "Save films onto a device to watch without a connection", "execute"),
        new(ProfileRead, "Read your own history, preferences and notifications", "read", Self: true),
        new(ProfileWrite, "Change your own preferences, history, lists and notifications", "write", Self: true),
        new(Watch, "Watch films and open rooms", "execute"),
        new(Wish, "Wait on films that are not out yet", "write"),
    ];

    /// <summary>Whether <paramref name="id"/> is one of Cinema's declared actions.</summary>
    public static bool IsDeclared(string id) => All.Any(a => a.Id == id);

    /// <summary>
    /// The manifest the provider reads at <c>/.well-known/tks-actions.json</c>: every action, sorted, at
    /// the one scope an application has, with no requirements, since Cinema has no service account there.
    /// </summary>
    public static ActionManifest Manifest() => new(
        1,
        Namespace,
        typeof(CinemaActions).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.0.0",
        [.. All.OrderBy(a => a.LocalId, StringComparer.Ordinal)
            .Select(a => new ManifestAction(a.LocalId, a.Title, a.Effect, "cluster", a.Self ? true : null))],
        []);
}

/// <summary>The action manifest, in the format the provider reads (schema version 1).</summary>
public sealed record ActionManifest(
    int SchemaVersion, string Component, string Version, IReadOnlyList<ManifestAction> Actions,
    IReadOnlyList<ManifestRequirement> Requires);

/// <summary>One action as the manifest declares it. <c>self</c> is written only when true, null being left out.</summary>
public sealed record ManifestAction(string Id, string Title, string Effect, string Scope, bool? Self);

/// <summary>An action of another namespace performed as a service account; an application declares none.</summary>
public sealed record ManifestRequirement(string Action, string Scope, string Why);
