using System.Text.Json.Serialization;

namespace TheKrystalShip.MovieBot.Catalogue;

/// <summary>The lists the queries hand SQLite as JSON arrays, serialized without reflection.</summary>
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(long[]))]
internal partial class StoreJson : JsonSerializerContext;
