using System.Net;
using System.Text;

namespace YouTracker.Core.Tests.YouTrack;

/// <summary>Records outgoing requests and replays canned responses in FIFO order.</summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    public sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Body,
        string? Authorization,
        string? Accept
    );

    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

    public List<RecordedRequest> Requests { get; } = new();

    public StubHttpHandler Enqueue(HttpStatusCode status, string body)
    {
        _responses.Enqueue((status, body));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        string? body = null;
        if (request.Content is not null)
            body = await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(
            new RecordedRequest(
                request.Method,
                request.RequestUri!,
                body,
                request.Headers.Authorization?.ToString(),
                request.Headers.Accept.ToString()
            )
        );

        if (_responses.Count == 0)
            throw new InvalidOperationException(
                $"No canned response left for {request.Method} {request.RequestUri}"
            );

        var (status, responseBody) = _responses.Dequeue();
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
    }
}
