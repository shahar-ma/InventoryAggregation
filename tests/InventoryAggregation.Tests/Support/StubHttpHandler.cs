using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace InventoryAggregation.Tests.Support;

internal sealed record RequestSnapshot(Uri Uri, IReadOnlyDictionary<string, string> Headers);

/// <summary>Scriptable primary <see cref="HttpMessageHandler"/>: the responder gets (request, 1-based call number, token).</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> responder)
    : HttpMessageHandler
{
    private readonly ConcurrentQueue<RequestSnapshot> _requests = new();
    private int _callCount;

    public int CallCount => Volatile.Read(ref _callCount);

    public IReadOnlyCollection<RequestSnapshot> Requests => _requests;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var callNumber = Interlocked.Increment(ref _callCount);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        _requests.Enqueue(new RequestSnapshot(request.RequestUri!, headers));
        return responder(request, callNumber, cancellationToken);
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>Call n gets steps[n-1]; once the script runs out, the last step repeats.</summary>
    public static Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> Script(
        params Func<CancellationToken, Task<HttpResponseMessage>>[] steps) =>
        (_, callNumber, token) => steps[Math.Min(callNumber, steps.Length) - 1](token);
}

/// <summary>Building blocks for <see cref="StubHttpHandler.Script"/>.</summary>
internal static class Steps
{
    public static Func<CancellationToken, Task<HttpResponseMessage>> Respond(Func<HttpResponseMessage> make) =>
        _ => Task.FromResult(make());

    public static Func<CancellationToken, Task<HttpResponseMessage>> Ok(string? body = null) =>
        Respond(() => StubHttpHandler.Json(body ?? TestData.SinglePageJson));

    public static Func<CancellationToken, Task<HttpResponseMessage>> Status(HttpStatusCode code) =>
        Respond(() => new HttpResponseMessage(code));

    public static Func<CancellationToken, Task<HttpResponseMessage>> Throw(Exception exception) =>
        _ => throw exception;

    /// <summary>Never answers; only the pipeline's timeout (or the caller) can end it.</summary>
    public static Func<CancellationToken, Task<HttpResponseMessage>> Hang() =>
        async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        };
}
