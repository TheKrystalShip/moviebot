using TheKrystalShip.MovieBot.Api.Auth;
using TheKrystalShip.MovieBot.Core;
using TheKrystalShip.Notifications;

namespace TheKrystalShip.MovieBot.Api.Notifications;

/// <summary>
/// Cinema's side of the shared notification library: the kinds it tells, where the library keeps its
/// file, and the routes the site's bell and settings read.
/// </summary>
public static class NotificationSetup
{
    /// <summary>The title every push carries.</summary>
    public const string PushTitle = "Cinema";

    /// <summary>The path of the anonymous route a push button posts to; Cinema's pushes carry no buttons.</summary>
    public const string ActionsPath = "/api/push/actions";

    /// <summary>
    /// Every kind Cinema tells. An invite and a plan are addressed to people on purpose, so they are
    /// always kept and pushed; the digest of titles to watch together is kept only for a person who
    /// switched it on.
    /// </summary>
    public static IReadOnlyList<NotificationKind> Kinds { get; } =
    [
        new(NotificationKinds.Available, "A title I want is out", "bell-ring", "teal")
        {
            Description = "It can be fetched now.",
        },
        new(NotificationKinds.Ready, "Something I asked for is ready", "circle-check", "success")
        {
            Description = "Includes each episode of a season.",
        },
        new(NotificationKinds.Episode, "New episodes of shows I follow", "tv", "teal"),
        new(NotificationKinds.Together, "Titles to watch together", "users", "teal")
        {
            Description = "A title I want is ready and others want it too.",
            EnabledByDefault = false,
        },
        new(NotificationKinds.Invite, "Invitations to a room", "user-plus", "teal") { Switchable = false },
        new(NotificationKinds.Plan, "Plans to watch together", "calendar-clock", "teal") { Switchable = false },
    ];

    /// <summary>
    /// Registers the library. <paramref name="stateDirectory"/> names the directory it keeps its file in, beside the API's database; the subject a push service
    /// reaches this host by and the address of the button route come from the public address.
    /// </summary>
    public static IServiceCollection AddCinemaNotifications(
        this IServiceCollection services, ConfigurationManager configuration, Func<string?> stateDirectory) =>
        // The options are read when the library is first asked for, not while the builder is still being
        // assembled: a configuration source added after this line must still count.
        services.AddNotifications(options =>
        {
            var publicUrl = configuration["Api:PublicBaseUrl"] is { Length: > 0 } url ? url.TrimEnd('/') : null;
            options.StateDirectory = stateDirectory();
            options.PushSubject = publicUrl ?? "mailto:moviebot@localhost";
            options.PushTitle = PushTitle;
            options.ActionBaseUrl = publicUrl is null ? null : publicUrl + ActionsPath;
            options.Kinds = Kinds;
        });

    /// <summary>
    /// Maps the signed-in routes under <c>/api/me</c>, for a person holding <c>profile.read</c> (the
    /// action every person holds for their own records, as reads and writes of them alike), and the
    /// anonymous button route.
    /// </summary>
    public static void MapCinemaNotifications(this WebApplication app)
    {
        app.MapGroup("/api/me")
            .RequireAction(CinemaActions.ProfileRead)
            .MapNotifications(http => CinemaPerson.Of(http)?.UserId);
        app.MapNotificationActions(ActionsPath);
    }
}
