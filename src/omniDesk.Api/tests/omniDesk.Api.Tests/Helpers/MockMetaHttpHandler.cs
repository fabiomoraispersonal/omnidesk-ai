using System.Net;
using System.Reflection;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// HttpMessageHandler programável para testes do <c>WhatsAppMetaClient</c>.
/// Spec 008 contracts/whatsapp-meta-graph.md §8.
///
/// Uso:
/// <code>
/// var mock = new MockMetaHttpHandler();
/// mock.OnPost("123/messages", req =&gt; mock.JsonResponse(HttpStatusCode.OK, MetaResponseFixtures.LoadSendText200()));
/// var http = new HttpClient(mock) { BaseAddress = new Uri("https://graph.facebook.com/v19.0/") };
/// </code>
/// </summary>
public sealed class MockMetaHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _handlers = new(StringComparer.Ordinal);
    public List<HttpRequestMessage> RequestsCaptured { get; } = new();

    public MockMetaHttpHandler OnPost(string pathSuffix, Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handlers[$"POST:{pathSuffix}"] = handler;
        return this;
    }

    public MockMetaHttpHandler OnGet(string pathSuffix, Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handlers[$"GET:{pathSuffix}"] = handler;
        return this;
    }

    /// <summary>Default handler para todas as URLs ao mesmo tempo (sobrescreve OnPost/OnGet).</summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? Default { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Buffer the content first so the test can inspect it later.
        // (HttpClient disposes the message after SendAsync returns.)
        var captured = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var h in request.Headers) captured.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (request.Content is not null)
        {
            var bytes = request.Content.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
            captured.Content = new ByteArrayContent(bytes);
            foreach (var h in request.Content.Headers) captured.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        RequestsCaptured.Add(captured);

        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var method = request.Method.Method;

        // Match by suffix (longest first) so tests can register either "messages" or "123/messages".
        var match = _handlers
            .Where(kv => kv.Key.StartsWith($"{method}:", StringComparison.Ordinal)
                && path.EndsWith(kv.Key.Substring(method.Length + 1), StringComparison.Ordinal))
            .OrderByDescending(kv => kv.Key.Length)
            .FirstOrDefault();

        if (match.Value is not null)
            return Task.FromResult(match.Value(request));

        if (Default is not null)
            return Task.FromResult(Default(request));

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"MockMetaHttpHandler: no handler for {method} {path}")
        });
    }

    public HttpResponseMessage JsonResponse(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

    public HttpResponseMessage BinaryResponse(HttpStatusCode status, byte[] body, string contentType) =>
        new(status)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType) } }
        };
}

/// <summary>
/// Loaders dos cassetes JSON/binários de respostas Meta usados nos testes.
/// </summary>
public static class MetaResponseFixtures
{
    public static string LoadSendText200()              => Load("send-text-200.json");
    public static string LoadSendText401TokenRevoked()  => Load("send-text-401-token-revoked.json");
    public static string LoadSendTemplate200()          => Load("send-template-200.json");
    public static string LoadSubmitTemplate200()        => Load("submit-template-200.json");
    public static string LoadSubmitTemplate400()        => Load("submit-template-400-name-invalid.json");
    public static string LoadTemplateStatusApproved()   => Load("template-status-approved.json");
    public static string LoadTemplateStatusRejected()   => Load("template-status-rejected.json");
    public static string LoadMediaMeta200()             => Load("media-meta-200.json");
    public static byte[] LoadMediaBytes200()            => LoadBytes("media-bytes-200.bin");

    private static string Load(string name) => File.ReadAllText(Path.Combine(Dir, name));
    private static byte[] LoadBytes(string name) => File.ReadAllBytes(Path.Combine(Dir, name));

    private static string Dir => _dir ??= Locate();
    private static string? _dir;

    private static string Locate()
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath)!);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Helpers", "Fixtures", "WhatsApp", "MetaResponses");
            if (Directory.Exists(candidate)) return candidate;

            var rooted = Path.Combine(dir.FullName, "tests", "omniDesk.Api.Tests", "Helpers", "Fixtures", "WhatsApp", "MetaResponses");
            if (Directory.Exists(rooted)) return rooted;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate Helpers/Fixtures/WhatsApp/MetaResponses/. Ensure JSON/bin fixtures are copied to test output.");
    }
}
