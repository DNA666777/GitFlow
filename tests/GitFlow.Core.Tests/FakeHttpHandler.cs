using System.Net;
using System.Text;

namespace GitFlow.Core.Tests;

/// <summary>Simulated HTTP server: records requests (with their body) and answers from a queue. No network.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body, string ContentType)> _responses = new();

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public FakeHttpHandler Respond(HttpStatusCode status, string body, string contentType = "application/json")
    {
        _responses.Enqueue((status, body, contentType));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        var (status, text, type) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.NotFound, "{}", "application/json");
        return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, type) };
    }
}
