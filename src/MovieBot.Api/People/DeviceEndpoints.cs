using TheKrystalShip.MovieBot.Core;

namespace TheKrystalShip.MovieBot.Api.People;

/// <summary>
/// The devices a person is signed in on. Whoever the token names is who is asked about, and ending
/// a session is refusing its token's id from then on; an id that is somebody else's is answered as
/// an id that does not exist.
/// </summary>
public static class DeviceEndpoints
{
    public static void MapDevices(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/sessions", (HttpContext context, DeviceSessions devices) =>
            Person(context) is { } person
                ? Results.Ok(devices.For(person.UserId, person.TokenId))
                : NoPerson());

        app.MapDelete("/api/me/sessions/{id}", (string id, HttpContext context, DeviceSessions devices) =>
            Person(context) is not { } person
                ? NoPerson()
                : devices.Revoke(person.UserId, id) ? Results.NoContent() : Results.NotFound());

        app.MapDelete("/api/me/sessions", (HttpContext context, DeviceSessions devices) =>
        {
            if (Person(context) is not { } person) return NoPerson();

            devices.RevokeOthers(person.UserId, person.TokenId);
            return Results.NoContent();
        });
    }

    private static RoomTokenPayload? Person(HttpContext context) =>
        context.Items.TryGetValue("room-token", out var token) ? token as RoomTokenPayload : null;

    /// <summary>The service key names nobody, so there are no devices to answer with.</summary>
    private static IResult NoPerson() =>
        Results.Json(new ErrorReply("Devices belong to a person; sign in as one."),
            ApiJsonContext.Default.ErrorReply, statusCode: StatusCodes.Status403Forbidden);
}
