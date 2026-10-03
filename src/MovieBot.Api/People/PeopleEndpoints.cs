using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// What the signed-in person keeps for themselves. Whoever the token names is who is asked about:
/// there is no user id on the route, so nobody reads or writes anybody else's.
/// </summary>
public static class PeopleEndpoints
{
    public static void MapPeople(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/preferences", (HttpContext context, PreferenceStore store) =>
            Person(context) is { } person
                ? Results.Ok(store.For(person.UserId))
                : NoPerson());

        app.MapPut("/api/me/preferences", (ViewerPreferences body, HttpContext context, PreferenceStore store) =>
            Person(context) is { } person
                ? Results.Ok(store.Set(person.UserId, body))
                : NoPerson());
    }

    private static RoomTokenPayload? Person(HttpContext context) =>
        context.Items.TryGetValue("room-token", out var token) ? token as RoomTokenPayload : null;

    /// <summary>The service key names nobody, so there is nobody's preferences to answer with.</summary>
    private static IResult NoPerson() =>
        Results.Json(new ErrorReply("Preferences belong to a person; sign in as one."),
            ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden);
}
