using System.IO.Compression;
using System.Text;

namespace omniDesk.Api.Features.LiveChat.Uploads;

/// <summary>
/// Spec 007 R5 / FR-040 — content-sniffing detector that ignores the client's declared
/// Content-Type and inspects the bytes themselves. Anything not in the allowlist returns
/// null so the upload endpoint can reject with 415 UNSUPPORTED_MIME_TYPE.
///
/// Allowlist (7 MIMEs): jpeg, png, gif, webp, pdf, docx, xlsx.
/// </summary>
public class MimeTypeDetector
{
    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string Gif = "image/gif";
    public const string Webp = "image/webp";
    public const string Pdf = "application/pdf";
    public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static readonly IReadOnlyCollection<string> Allowlist =
        new[] { Jpeg, Png, Gif, Webp, Pdf, Docx, Xlsx };

    public async Task<string?> DetectAsync(Stream stream, CancellationToken ct = default)
    {
        if (!stream.CanRead) return null;
        if (!stream.CanSeek)
            throw new ArgumentException("DetectAsync requires a seekable stream.", nameof(stream));

        stream.Position = 0;
        var head = new byte[12];
        var read = await stream.ReadAsync(head.AsMemory(0, 12), ct);
        stream.Position = 0;
        if (read < 4) return null;

        if (StartsWith(head, [0xFF, 0xD8, 0xFF])) return Jpeg;
        if (StartsWith(head, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return Png;
        if (StartsWith(head, [0x47, 0x49, 0x46, 0x38])) return Gif; // GIF8(7|9)a
        if (read >= 12
            && StartsWith(head, [0x52, 0x49, 0x46, 0x46]) // RIFF
            && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50) // WEBP
            return Webp;
        if (StartsWith(head, [0x25, 0x50, 0x44, 0x46])) return Pdf; // %PDF
        if (StartsWith(head, [0x50, 0x4B, 0x03, 0x04])) // PK\x03\x04 — ZIP container.
            return await DetectOfficeFromZipAsync(stream, ct);

        return null;
    }

    private static async Task<string?> DetectOfficeFromZipAsync(Stream stream, CancellationToken ct)
    {
        // ZipArchive needs a seekable stream; the caller already guarantees that.
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var entry = archive.GetEntry("[Content_Types].xml");
            if (entry is null) return null;

            await using var es = entry.Open();
            using var ms = new MemoryStream();
            await es.CopyToAsync(ms, ct);
            var xml = Encoding.UTF8.GetString(ms.ToArray());

            if (xml.Contains("word/document.xml", StringComparison.OrdinalIgnoreCase))
                return Docx;
            if (xml.Contains("xl/workbook.xml", StringComparison.OrdinalIgnoreCase))
                return Xlsx;
            return null;
        }
        catch (InvalidDataException) { return null; }
        finally { stream.Position = 0; }
    }

    private static bool StartsWith(byte[] buffer, ReadOnlySpan<byte> prefix)
    {
        if (buffer.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
            if (buffer[i] != prefix[i]) return false;
        return true;
    }
}
