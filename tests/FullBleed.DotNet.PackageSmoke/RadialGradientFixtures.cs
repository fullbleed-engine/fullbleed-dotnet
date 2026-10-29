using FullBleed.DotNet;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class RadialGradientFixtures
{
    internal static void Run(string assets, string outputDirectory, string version)
    {
        var bytes = File.ReadAllBytes(Path.Combine(assets, "radial-gradient-endpoints-2.5.43.json"));
        using var source = JsonDocument.Parse(bytes);
        var output = Path.Combine(outputDirectory, "radial-gradient-endpoints");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "inputs.json"), bytes);
        using var engine = new FullBleedEngine();
        var cases = new List<object>();
        foreach (var fixture in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = fixture.GetProperty("name").GetString()!;
            var html = fixture.GetProperty("html").GetString()!;
            var css = fixture.GetProperty("css").GetString()!;
            if (Hash(Encoding.UTF8.GetBytes(html + "\0" + css)) != fixture.GetProperty("inputSha256").GetString())
                throw new InvalidOperationException("Radial-gradient input changed.");
            foreach (var mode in new[] { "direct", "compiled" })
            {
                var directory = mode + "-" + name;
                var folder = Path.Combine(output, directory);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "input.html"), html);
                File.WriteAllText(Path.Combine(folder, "style.css"), css);
                var pdf = Render(engine, html, css, mode);
                if (!pdf.SequenceEqual(Render(engine, html, css, mode)))
                    throw new InvalidOperationException("Radial-gradient PDF changed on repeat.");
                var path = Path.Combine(folder, "document.pdf");
                File.WriteAllBytes(path, pdf);
                var pages = FullBleedEngine.InspectPdf(path).PageCount;
                var previews = engine.RenderFinalizedPdfImagePagesToDirectory(path, folder, dpi: 144, stem: "native");
                if (pages != 1 || previews.Paths.Count != 1)
                    throw new InvalidOperationException("Radial-gradient output must have one PDF page and preview.");
                cases.Add(new
                {
                    name, mode, directory, pages, pdfSha256 = Hash(pdf), repeatPdfBytesMatch = true,
                    previewFiles = previews.Paths.Select(p => Path.GetRelativePath(folder, p).Replace('\\', '/')).ToArray(),
                    previewSha256 = previews.Paths.Select(p => Hash(File.ReadAllBytes(p))).ToArray(),
                });
            }
        }
        if (cases.Count != 24) throw new InvalidOperationException("Incomplete radial-gradient fixture set.");
        File.WriteAllText(Path.Combine(output, "renders.json"), JsonSerializer.Serialize(new
        {
            packageVersion = version, runtime = Environment.Version.ToString(), inputSha256 = Hash(bytes), cases,
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static byte[] Render(FullBleedEngine engine, string html, string css, string mode)
    {
        if (mode == "direct") return engine.RenderPdf(html, css);
        using var compiled = engine.Compile(html, css);
        return compiled.Render();
    }
}
