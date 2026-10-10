using TheKrystalShip.MovieBot.Api.Auth;

namespace TheKrystalShip.MovieBot.Api.Sessions;

/// <summary>
/// The room's door on a route: a person reaches a room over HTTP only as they reach it over the hub.
/// </summary>
/// <remarks>
/// <para>
/// <b>Touching a room is entering it.</b> Reading where a room is, changing it and asking somebody
/// into it each go through <see cref="SessionStore.Enter"/>, which lets the person in when the room
/// admits them and makes the room for them when there is none — so a room a person makes over HTTP is
/// theirs as much as one they make by opening it. A room private to them is a 403, and the route
/// never runs.
/// </para>
/// <para>
/// <b>A guest is refused</b>, on a route that would ever authenticate one.
/// </para>
/// <para>
/// <b>Machine work passes.</b> The service key names nobody, and what it does to a room — loading a
/// download that somebody in it asked for — is that room's own business arriving.
/// </para>
/// </remarks>
public static class RoomEntry
{
    public static TBuilder RequireRoomEntry<TBuilder>(this TBuilder builder, string roomParameter = "sessionId")
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            // A guest enters no room by a route: their link opens one room, to watch, over the hub.
            if (CinemaPerson.Of(http) is null && GuestViewer.Of(http.User) is not null) return Private;
            if (CinemaPerson.Of(http) is { } person
                && http.GetRouteValue(roomParameter) is string room
                && !http.RequestServices.GetRequiredService<SessionStore>().Enter(room, person.UserId))
                return Private;
            return await next(context);
        });

    /// <summary>What a person a room is private to is answered.</summary>
    public static IResult Private { get; } = Results.Json(new ErrorReply("This room is private."),
        ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden);
}
