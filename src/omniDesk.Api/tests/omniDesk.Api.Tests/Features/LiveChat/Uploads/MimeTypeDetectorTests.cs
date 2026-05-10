using omniDesk.Api.Features.LiveChat.Uploads;
using Xunit;

namespace omniDesk.Api.Tests.Features.LiveChat.Uploads;

/// <summary>
/// Spec 007 T165 — MimeTypeDetector recognizes the 7 allowlisted MIMEs by their magic
/// bytes; ZIP-based docx/xlsx are detected by inspecting [Content_Types].xml inside
/// the archive.
/// </summary>
public class MimeTypeDetectorTests
{
    private readonly MimeTypeDetector _detector = new();

    [Fact]
    public async Task Detects_jpeg() => await AssertMime(MakeStream([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]), MimeTypeDetector.Jpeg);

    [Fact]
    public async Task Detects_png() => await AssertMime(
        MakeStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D]),
        MimeTypeDetector.Png);

    [Fact]
    public async Task Detects_gif() => await AssertMime(MakeStream([0x47, 0x49, 0x46, 0x38, 0x39, 0x61]), MimeTypeDetector.Gif);

    [Fact]
    public async Task Detects_webp() => await AssertMime(
        MakeStream([0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50]),
        MimeTypeDetector.Webp);

    [Fact]
    public async Task Detects_pdf() => await AssertMime(
        MakeStream([0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37]),
        MimeTypeDetector.Pdf);

    [Fact]
    public async Task Detects_docx_via_content_types_xml()
    {
        var stream = MakeOfficeZip("word/document.xml");
        var mime = await _detector.DetectAsync(stream);
        Assert.Equal(MimeTypeDetector.Docx, mime);
    }

    [Fact]
    public async Task Detects_xlsx_via_content_types_xml()
    {
        var stream = MakeOfficeZip("xl/workbook.xml");
        var mime = await _detector.DetectAsync(stream);
        Assert.Equal(MimeTypeDetector.Xlsx, mime);
    }

    [Fact]
    public async Task Returns_null_for_unknown_bytes()
    {
        var mime = await _detector.DetectAsync(MakeStream([0x00, 0x01, 0x02, 0x03, 0x04, 0x05]));
        Assert.Null(mime);
    }

    [Fact]
    public async Task Returns_null_when_pe32_disguised_as_pdf()
    {
        // MZ header (Windows executable) — definitely not in our allowlist.
        var mime = await _detector.DetectAsync(MakeStream([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]));
        Assert.Null(mime);
    }

    private async Task AssertMime(MemoryStream stream, string expected)
    {
        var mime = await _detector.DetectAsync(stream);
        Xunit.Assert.Equal(expected, mime);
    }

    private static MemoryStream MakeStream(byte[] bytes) => new(bytes);

    private static MemoryStream MakeOfficeZip(string contentTypeMarker)
    {
        var buffer = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("[Content_Types].xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write($"<Types>{contentTypeMarker}</Types>");
        }
        buffer.Position = 0;
        return buffer;
    }
}
