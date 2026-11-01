using FullBleed.DotNet;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static class FontCascadeFixtures
{
    internal static void Run(string assets, string outputDirectory, string version)
    {
        var input = File.ReadAllBytes(Path.Combine(assets, "font-cascade-2.5.51.json"));
        using var source = JsonDocument.Parse(input);
        var output = Path.Combine(outputDirectory, "font-cascade");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "inputs.json"), input);
        using var engine = new FullBleedEngine();
        var cases = new List<object>();
        foreach (var specimen in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = specimen.GetProperty("name").GetString()!;
            var html = specimen.GetProperty("html").GetString()!;
            var css = specimen.GetProperty("css").GetString()!;
            var result = engine.RenderPdfWithDiagnostics(html, css);
            if (result.Diagnostics.MissingGlyphs.Count != 0 || !result.Pdf.SequenceEqual(engine.RenderPdf(html, css)))
            {
                throw new InvalidOperationException("Font-cascade render has missing glyphs or changed on repeat.");
            }
            var filename = name + ".pdf";
            var path = Path.Combine(output, filename);
            File.WriteAllBytes(path, result.Pdf);
            var pages = FullBleedEngine.InspectPdf(path).PageCount;
            if (pages != specimen.GetProperty("pages").GetInt32())
            {
                throw new InvalidOperationException("Unexpected font-cascade page count.");
            }
            var original = engine.RenderFinalizedPdfImagePagesToDirectory(path,
                Path.Combine(output, name + "-preview"), dpi: 96, stem: "native");
            if (original.Paths.Count != pages) throw new InvalidOperationException("Missing font-cascade preview.");
            var previews = new List<object>();
            for (var index = 0; index < original.Paths.Count; index++)
            {
                var bytes = File.ReadAllBytes(original.Paths[index]);
                var preview = name + "-" + (index + 1) + ".png";
                File.WriteAllBytes(Path.Combine(output, preview), bytes);
                previews.Add(new { filename = preview, sha256 = Hash(bytes) });
            }
            cases.Add(new { name, filename, pages, pdfSha256 = Hash(result.Pdf), previews, repeatPdfBytesMatch = true });
        }
        if (cases.Count != 13) throw new InvalidOperationException("Incomplete font-cascade fixtures.");
        File.WriteAllText(Path.Combine(output, "renders.json"), JsonSerializer.Serialize(new
        {
            status = "rendered-awaiting-independent-readback",
            packageVersion = version,
            runtime = Environment.Version.ToString(),
            platform = RuntimeInformation.RuntimeIdentifier,
            fixtureSha256 = Hash(input),
            cases,
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
