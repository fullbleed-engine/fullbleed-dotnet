using FullBleed.DotNet;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

internal static class GeneratedCounterFixtures
{
    internal static void Run(string assets, string outputDirectory, string version)
    {
        var input = File.ReadAllBytes(Path.Combine(assets, "generated-counters-2.5.49.json"));
        using var source = JsonDocument.Parse(input);
        var font = File.ReadAllBytes(Path.Combine(assets, "fonts", "Inter-Variable.ttf"));
        if (Hash(font) != source.RootElement.GetProperty("font").GetProperty("sha256").GetString())
        {
            throw new InvalidOperationException("Generated-counter font input changed.");
        }
        var output = Path.Combine(outputDirectory, "generated-counters");
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, "inputs.json"), input);
        using var engine = new FullBleedEngine(new FullBleedEngineOptions
        {
            Assets = [FullBleedAsset.FromBytes(font, FullBleedAssetKind.Font, "Inter-Variable.ttf")],
        });
        var modes = new[] { "direct", "compiled", "fixed", "reflow", "compact" };
        var cases = new List<object>();
        foreach (var specimen in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            var name = specimen.GetProperty("name").GetString()!;
            foreach (var mode in modes)
            {
                var names = mode is "direct" or "compiled" ? new[] { "Ada" } : new[] { "Ada", "Bea" };
                var html = specimen.GetProperty("html").GetString()!;
                if (names.Length == 1) html = html.Replace("{{name}}", "Ada", StringComparison.Ordinal);
                var css = specimen.GetProperty("css").GetString()!;
                var pdf = Render(engine, html, css, mode, names);
                if (!pdf.SequenceEqual(Render(engine, html, css, mode, names)))
                {
                    throw new InvalidOperationException("Generated-counter PDF changed on repeat.");
                }
                var filename = name + "-" + mode + ".pdf";
                var path = Path.Combine(output, filename);
                File.WriteAllBytes(path, pdf);
                var pages = FullBleedEngine.InspectPdf(path).PageCount;
                if (pages != specimen.GetProperty("pagesPerRecord").GetInt32() * names.Length)
                {
                    throw new InvalidOperationException("Unexpected generated-counter page count.");
                }
                var previews = new List<object>();
                if (name == "block-after-increment")
                {
                    var original = engine.RenderFinalizedPdfImagePagesToDirectory(path,
                        Path.Combine(output, name + "-" + mode + "-preview"), dpi: 96, stem: "native");
                    if (original.Paths.Count != pages) throw new InvalidOperationException("Missing counter preview.");
                    for (var index = 0; index < original.Paths.Count; index++)
                    {
                        var bytes = File.ReadAllBytes(original.Paths[index]);
                        var preview = name + "-" + mode + "-native-" + (index + 1) + ".png";
                        File.WriteAllBytes(Path.Combine(output, preview), bytes);
                        previews.Add(new { filename = preview, sha256 = Hash(bytes) });
                    }
                }
                cases.Add(new { name, mode, names, filename, pages, pdfSha256 = Hash(pdf), previews, repeatPdfBytesMatch = true });
            }
        }
        if (cases.Count != 295) throw new InvalidOperationException("Incomplete generated-counter fixtures.");
        File.WriteAllText(Path.Combine(output, "renders.json"), JsonSerializer.Serialize(new
        {
            status = "rendered-awaiting-independent-readback",
            packageVersion = version,
            runtime = Environment.Version.ToString(),
            platform = RuntimeInformation.RuntimeIdentifier,
            fixtureSha256 = Hash(input),
            modes,
            cases,
        }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static byte[] Render(FullBleedEngine engine, string html, string css, string mode, string[] names)
    {
        if (mode == "direct")
        {
            var result = engine.RenderPdfWithDiagnostics(html, css);
            if (result.Diagnostics.MissingGlyphs.Count != 0) throw new InvalidOperationException("Missing counter glyph.");
            return result.Pdf;
        }
        using var compiled = engine.Compile(html, css);
        if (mode == "compiled") return compiled.Render();
        if (mode == "fixed") return compiled.RenderBindings(names, map => map.Bind("name", value => value));
        return compiled.RenderReflowBindings(names, map => map.Bind("name", value => value),
            mode == "compact" ? CompiledFlowCompression.Compact : CompiledFlowCompression.Throughput);
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
