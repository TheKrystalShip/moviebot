using System.Security.Cryptography;
using System.Text;

namespace TheKrystalShip.MovieBot.Catalogue.Data;

/// <summary>A short, stable hash of text: the first 16 bytes of its SHA-256, as hex.</summary>
public static class Hash
{
    public static string Of(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
}
