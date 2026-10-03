using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Bot.Api;

/// <summary>The serializer for what the bot sends Discord itself, outside the client library.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Presence.VoiceStatusRequest))]
public partial class BotJsonContext : JsonSerializerContext;
