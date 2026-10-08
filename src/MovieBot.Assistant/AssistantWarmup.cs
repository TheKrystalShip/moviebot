using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TheKrystalShip.Llm.Backends;
using TheKrystalShip.Llm.Backends.LlamaCpp;
using TheKrystalShip.Llm.Interfaces;
using TheKrystalShip.Llm.Models;

namespace TheKrystalShip.MovieBot.Assistant;

/// <summary>
/// Puts the instructions and the catalog of the turns that come through the model once the service
/// starts, and leaves the same request on disk for a model unit of its own to replay when it starts.
/// </summary>
/// <remarks>
/// <para>
/// <b>A warm-up only warms the shape it sends.</b> The card compiles its pipelines the first time a
/// graph runs, and the server keeps the computed prefix of the last request. Measured on hotbox: a
/// repeat of the warm-up's own request took 241 ms, and a first request carrying a different prompt
/// and catalog took 1,093 ms. So what warms the model has to be what this service sends, and this
/// service is the only thing that knows it. It warms a room's turn where the bot relays rooms here,
/// because a room is somebody standing in a voice channel waiting for the film to move, and a
/// person's turn everywhere else: on a model server shared with other assistants, a shape no turn
/// repeats only takes a slot another assistant was keeping warm.
/// </para>
/// <para>
/// <b>Both halves, because either can restart alone.</b> This service restarting leaves the model's
/// cache holding whatever came last, which is warmed here. The model restarting leaves this running
/// and its first request cold, which is what the file is for: the unit's start-up replays it before it
/// reports ready.
/// </para>
/// </remarks>
public sealed class AssistantWarmup(
    IOptions<AssistantOptions> options,
    IOptions<LlmBackendOptions> backend,
    IOptions<LlamaCppOptions> llamaCpp,
    IServiceProvider services,
    ILogger<AssistantWarmup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // The turns that come: a room's when the bot relays rooms here, a person's otherwise.
            var rooms = options.Value.Enabled;
            var prompt = services.GetRequiredService<PromptPack>().Read(rooms ? PromptPack.RoomFile : PromptPack.SiteFile);
            var catalog = services.GetRequiredService<ToolCatalog>();
            var tools = rooms ? catalog.Room : catalog.Site;
            var messages = Request(prompt);

            await WriteReplayAsync(messages, tools, stoppingToken);

            var model = services.GetRequiredService<ILlmClient>();
            var started = Stopwatch.GetTimestamp();
            var result = await model.ChatAsync(messages, tools, think: false, stoppingToken);

            if (result.IsFailure)
                logger.LogWarning("Assistant: the model did not answer the warm-up: {Error}", result.Error);
            else
                logger.LogInformation(
                    "Assistant: warmed the model for {Shape} turns with {Tools} tools in {Elapsed:0}ms",
                    rooms ? "room" : "person", tools.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Everything the warm-up would have saved is paid by the first request instead, which is a
            // slower answer rather than a broken service.
            logger.LogWarning(ex, "Assistant: could not warm the model");
        }
    }

    /// <summary>
    /// A turn shaped like a real one: the instructions, a room holding nothing, and a request. The
    /// prefix the cache keeps is the instructions and the catalog, which is all of it that recurs.
    /// </summary>
    public static IReadOnlyList<LlmMessage> Request(SystemPrompt prompt) =>
    [
        LlmMessage.System(prompt.Text),
        LlmMessage.System("What is true right now.\nSpeaking: someone.\nRoom: no film is loaded.\nThe library is empty."),
        LlmMessage.User("someone: what can you do?"),
    ];

    private async Task WriteReplayAsync(
        IReadOnlyList<LlmMessage> messages, IReadOnlyList<LlmToolDefinition> tools, CancellationToken ct)
    {
        var body = LlamaCppRequestBuilder.Build(backend.Value, llamaCpp.Value, messages, tools, stream: false, think: false);
        var path = options.Value.ResolveWarmupPath();
        var temporary = path + ".tmp";

        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(body, LlmWireJsonContext.Default.LlamaCppChatRequest), ct);
        File.Move(temporary, path, overwrite: true);
    }
}
