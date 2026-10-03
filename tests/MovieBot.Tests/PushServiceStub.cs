using System.Collections.Concurrent;
using System.Net;

namespace TheKrystalShip.MovieBot.Tests;

/// <summary>
/// A browser's push service, as the tests answer it: it takes every push, except at an endpoint
/// whose path says <c>gone</c>, which answers 410 as a service does for a subscription the browser
/// gave up. It keeps what it was sent.
/// </summary>
public sealed class PushServiceStub : HttpMessageHandler
{
    public ConcurrentQueue<(Uri Endpoint, string? Authorization, string? Encoding)> Received { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Received.Enqueue((request.RequestUri!, request.Headers.Authorization?.ToString(),
            request.Content?.Headers.ContentEncoding.FirstOrDefault()));
        var gone = request.RequestUri!.AbsolutePath.Contains("gone", StringComparison.Ordinal);
        return Task.FromResult(new HttpResponseMessage(gone ? HttpStatusCode.Gone : HttpStatusCode.Created));
    }

    public int SentTo(string endpoint) => Received.Count(r => r.Endpoint.ToString() == endpoint);
}
