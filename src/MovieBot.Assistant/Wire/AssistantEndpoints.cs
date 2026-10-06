using TheKrystalShip.Agent.Service.Commands;
using TheKrystalShip.Agent.Service.Conversations;
using TheKrystalShip.Agent.Service.Memory;
using TheKrystalShip.Agent.Service.Push;
using TheKrystalShip.Agent.Service.Speech;
using TheKrystalShip.Agent.Service.Turns;
using TheKrystalShip.MovieBot.Client.Assistant;

namespace TheKrystalShip.MovieBot.Assistant.Wire;

/// <summary>
/// The assistant wire contract (<c>tks-assistant/docs/wire-contract.md</c>, version 2.1) as this service
/// serves it: every route is <c>TheKrystalShip.Agent.Service</c>'s over this service's seams, plus who the
/// caller is and the room verbs the bot writes into a room's conversation.
/// </summary>
public static class AssistantEndpoints
{
    public static void MapAssistant(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok());

        app.MapGet("/me", (HttpRequest request, Callers callers) =>
            callers.Resolve(request) is { } caller
                ? Results.Json(new MeReply(caller.UserId, caller.DisplayName, caller.Relayed), AssistantJsonContext.Default.MeReply)
                : Refuse());

        // A room verb the bot carried out without the model, written into the room's conversation so a
        // question about it a minute later is answered by an assistant that knows.
        app.MapPost("/rooms/record", async (HttpRequest request, Callers callers, CinemaAssistant assistant) =>
        {
            if (callers.Resolve(request) is not { Relayed: true } caller) return Refuse();

            RecordRequest? body;
            try
            {
                body = await request.ReadFromJsonAsync(AssistantJsonContext.Default.RecordRequest, request.HttpContext.RequestAborted);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                body = null;
            }

            if (body is null)
                return Results.Json(new AssistantError("A record names a room, a tool and an outcome."),
                    AssistantJsonContext.Default.AssistantError, statusCode: StatusCodes.Status400BadRequest);

            assistant.RecordAct(AssistantTurn.InRoom(body.Room, caller.UserId, caller.DisplayName),
                body.Said, body.Tool, body.Arguments, body.Outcome);
            return Results.NoContent();
        });

        // What every TheKrystalShip assistant serves, over CinemaCallers: turns and the event stream every
        // surface of a person shares, their conversations, the confirm channel, the speech surface (which
        // answers that this service neither hears nor speaks), the commands, a person's memory, and their
        // notification devices. A notification's button arrives with no token, so its route is on the app
        // with nothing in front.
        app.MapAssistantTurns();
        app.MapAssistantConversations();
        app.MapAssistantConfirm();
        app.MapAssistantSpeech();
        app.MapAssistantCommands();
        app.MapAssistantMemory();
        app.MapAssistantPush<StagedAction>(open: app);
    }

    private static IResult Refuse() =>
        Results.Json(new AssistantError("Sign in to Krystal Cinema."),
            AssistantJsonContext.Default.AssistantError, statusCode: StatusCodes.Status401Unauthorized);
}
