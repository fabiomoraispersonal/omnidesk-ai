using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// Minimal request-routed HTTP stub for OpenAI Assistants v2.
/// Routes are matched by (method, path-startsWith). Each route can return a status + JSON body.
/// Recorded requests are exposed for assertions (URL, body, headers).
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly List<Route> _routes = new();
    public List<Recorded> Requests { get; } = new();

    public StubHttpMessageHandler Map(HttpMethod method, string pathStartsWith,
        HttpStatusCode status = HttpStatusCode.OK, object? body = null,
        Func<HttpRequestMessage, object?>? bodyFactory = null)
    {
        _routes.Add(new Route(method, pathStartsWith, status, body, bodyFactory));
        return this;
    }

    public StubHttpMessageHandler MapSequential(HttpMethod method, string pathStartsWith,
        params (HttpStatusCode status, object? body)[] sequence)
    {
        var queue = new Queue<(HttpStatusCode, object?)>(sequence);
        _routes.Add(new Route(method, pathStartsWith, HttpStatusCode.OK, null,
            _ =>
            {
                if (queue.Count == 0) throw new InvalidOperationException($"Sequential map exhausted for {method} {pathStartsWith}");
                var (s, b) = queue.Dequeue();
                return (s, b);
            }));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        var bodyText = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        Requests.Add(new Recorded(request.Method, path, bodyText,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));

        foreach (var route in _routes)
        {
            if (route.Method != request.Method) continue;
            if (!path.StartsWith(route.PathStartsWith, StringComparison.Ordinal)) continue;

            HttpStatusCode status = route.Status;
            object? body = route.Body;
            if (route.BodyFactory is not null)
            {
                var dyn = route.BodyFactory(request);
                if (dyn is ValueTuple<HttpStatusCode, object?> tup) { status = tup.Item1; body = tup.Item2; }
                else { body = dyn; }
            }
            var response = new HttpResponseMessage(status);
            if (body is not null)
            {
                response.Content = new StringContent(JsonSerializer.Serialize(body),
                    System.Text.Encoding.UTF8, "application/json");
            }
            return response;
        }
        throw new InvalidOperationException($"No stub route for {request.Method} {path}");
    }

    public record Recorded(HttpMethod Method, string Path, string? Body, Dictionary<string, string> Headers);

    private record Route(
        HttpMethod Method,
        string PathStartsWith,
        HttpStatusCode Status,
        object? Body,
        Func<HttpRequestMessage, object?>? BodyFactory);
}
