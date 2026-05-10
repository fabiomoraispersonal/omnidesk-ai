using System.Reflection;

namespace omniDesk.Api.Tests.Helpers;

/// <summary>
/// Loaders para arquivos JSON canônicos de webhooks Meta usados em testes.
/// Spec 008 contracts/whatsapp-webhook.md §6.
///
/// Fixtures vivem em <c>Helpers/Fixtures/WhatsApp/Webhooks/</c>.
/// </summary>
public static class MetaWebhookFixtures
{
    public static string LoadTextMessage()           => Load("webhook-text-message.json");
    public static string LoadImageMessage()          => Load("webhook-image-message.json");
    public static string LoadDocumentMessage()       => Load("webhook-document-message.json");
    public static string LoadAudioMessage()          => Load("webhook-audio-message.json");
    public static string LoadStatusSent()            => Load("webhook-status-sent.json");
    public static string LoadStatusDelivered()       => Load("webhook-status-delivered.json");
    public static string LoadStatusRead()            => Load("webhook-status-read.json");
    public static string LoadStatusFailed()          => Load("webhook-status-failed.json");
    public static string LoadTemplateApproved()      => Load("webhook-template-approved.json");
    public static string LoadTemplateRejected()      => Load("webhook-template-rejected.json");
    public static string LoadUnsupportedSticker()    => Load("webhook-unsupported-sticker.json");
    public static string LoadMalformed()             => Load("webhook-malformed.json");

    public static string Load(string name)
    {
        var dir = LocateFixturesDir();
        var path = Path.Combine(dir, name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Webhook fixture {name} not found at {path}", path);
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Localiza o diretório de fixtures buscando para cima a partir do output dir do test runner
    /// (busca pela pasta <c>Helpers/Fixtures/WhatsApp/Webhooks/</c>).
    /// </summary>
    private static string LocateFixturesDir()
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var dir = new DirectoryInfo(Path.GetDirectoryName(assemblyPath)!);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Helpers", "Fixtures", "WhatsApp", "Webhooks");
            if (Directory.Exists(candidate)) return candidate;

            // tests/ folder sometimes nested 1 level under bin/Debug/...; also try project root.
            var rooted = Path.Combine(dir.FullName, "tests", "omniDesk.Api.Tests", "Helpers", "Fixtures", "WhatsApp", "Webhooks");
            if (Directory.Exists(rooted)) return rooted;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate Helpers/Fixtures/WhatsApp/Webhooks/. Ensure JSON fixtures are copied to test output.");
    }
}
