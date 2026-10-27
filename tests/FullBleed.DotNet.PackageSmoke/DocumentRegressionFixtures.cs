using FullBleed.DotNet;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

internal static class DocumentRegressionFixtures
{
    internal static void Run(string assets, string outputDirectory, string version)
    {
        var output = Path.Combine(outputDirectory, "document-regressions");
        Directory.CreateDirectory(output);
        Previews(assets, output, version);
        Quotes(assets, output, version);
    }

    private static void Previews(string assets, string output, string version)
    {
        var source = Path.Combine(assets, "pdf-preview-subsets-2.5.39.json");
        var sourceBytes = File.ReadAllBytes(source);
        using var fixtures = JsonDocument.Parse(sourceBytes);
        var root = fixtures.RootElement;
        var archivePath = Path.Combine(assets, root.GetProperty("archive").GetString()!);
        if (HashFile(archivePath) != root.GetProperty("archiveSha256").GetString())
        {
            throw new InvalidOperationException("The imported-PDF fixture archive changed.");
        }
        var inputs = Path.Combine(output, "inputs");
        Directory.CreateDirectory(inputs);
        using (var archive = ZipFile.OpenRead(archivePath))
        {
            foreach (var member in root.GetProperty("members").EnumerateObject())
            {
                var path = Path.GetFullPath(Path.Combine(inputs, member.Name));
                if (!path.StartsWith(Path.GetFullPath(inputs) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The fixture archive contains an invalid path.");
                }
                var entry = archive.GetEntry(member.Name) ?? throw new InvalidOperationException("Missing PDF fixture.");
                using var stream = entry.Open();
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                var data = bytes.ToArray();
                if (Hash(data) != member.Value.GetProperty("sha256").GetString() ||
                    data.Length != member.Value.GetProperty("bytes").GetInt32())
                {
                    throw new InvalidOperationException("The fixture archive contents changed.");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, data);
            }
        }
        File.WriteAllBytes(Path.Combine(output, "preview-inputs.json"), sourceBytes);
        using var engine = new FullBleedEngine();
        var cases = new List<object>();
        foreach (var fixture in root.GetProperty("cases").EnumerateArray())
        {
            var name = fixture.GetProperty("name").GetString()!;
            var directory = "previews/" + name;
            var folder = Path.Combine(output, directory);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "document.pdf");
            var original = Path.Combine(inputs, "pdfs", fixture.GetProperty("file").GetString()!);
            File.Copy(original, path);
            var digest = HashFile(path);
            if (digest != fixture.GetProperty("pdfSha256").GetString())
            {
                throw new InvalidOperationException("The imported PDF bytes changed.");
            }
            var dpi = fixture.GetProperty("dpi").GetUInt32();
            var pages = FullBleedEngine.InspectPdf(path).PageCount;
            var actual = engine.RenderFinalizedPdfImagePagesToDirectory(path, folder, dpi, "preview");
            var repeated = engine.RenderFinalizedPdfImagePagesToDirectory(path, Path.Combine(folder, "repeat"), dpi, "preview");
            var hashes = actual.Paths.Select(HashFile).ToArray();
            if (pages != fixture.GetProperty("pages").GetInt32() || actual.Paths.Count != pages ||
                !hashes.SequenceEqual(repeated.Paths.Select(HashFile)) || HashFile(path) != digest)
            {
                throw new InvalidOperationException("Imported PDF preview page count, repeatability, or source preservation failed.");
            }
            cases.Add(new
            {
                name, directory, pages, dpi, pdfSha256 = digest,
                previewFiles = actual.Paths.Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/')).ToArray(),
                previewsSha256 = hashes, repeatPreviewBytesIdentical = true, sourceUnchanged = true,
            });
        }
        File.WriteAllText(Path.Combine(output, "previews.json"), JsonSerializer.Serialize(new
        {
            schema = "fullbleed.dotnet.preview_subsets.renders.v1", packageVersion = version,
            sourceSha256 = Hash(sourceBytes), runtime = Environment.Version.ToString(), cases,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Quotes(string assets, string output, string version)
    {
        var source = Path.Combine(assets, "generated-quotes-2.5.38.json");
        var sourceBytes = File.ReadAllBytes(source);
        using var fixtures = JsonDocument.Parse(sourceBytes);
        var root = fixtures.RootElement;
        var font = Path.Combine(assets, "fonts", "Inter-Variable.ttf");
        if (HashFile(font) != root.GetProperty("fontSha256").GetString())
        {
            throw new InvalidOperationException("The quotation fixtures require their exact reference font.");
        }
        File.WriteAllBytes(Path.Combine(output, "quote-inputs.json"), sourceBytes);
        using var engine = new FullBleedEngine(new FullBleedEngineOptions
        {
            DocumentLanguage = root.GetProperty("documentLanguage").GetString(),
            DocumentTitle = root.GetProperty("documentTitle").GetString(),
            Assets = [FullBleedAsset.FromPath(font, FullBleedAssetKind.Font)],
        });
        var cases = new List<object>();
        foreach (var mode in new[] { "direct", "compiled", "reflow" })
        {
            foreach (var fixture in root.GetProperty("cases").EnumerateArray())
            {
                var name = fixture.GetProperty("name").GetString()!;
                var html = fixture.GetProperty("html").GetString()!;
                var css = fixture.GetProperty("css").GetString()!;
                if (Hash(System.Text.Encoding.UTF8.GetBytes(html)) != fixture.GetProperty("htmlSha256").GetString() ||
                    Hash(System.Text.Encoding.UTF8.GetBytes(css)) != fixture.GetProperty("cssSha256").GetString())
                {
                    throw new InvalidOperationException("The authored quotation strings changed.");
                }
                var directory = "quotes/" + mode + "-" + name;
                var folder = Path.Combine(output, directory);
                Directory.CreateDirectory(folder);
                byte[] pdf;
                var authored = html;
                if (mode == "direct")
                {
                    var result = engine.RenderPdfWithDiagnostics(html, css);
                    pdf = result.Pdf;
                    if (result.Diagnostics.MissingGlyphs.Count != 0 || !pdf.SequenceEqual(engine.RenderPdf(html, css)))
                    {
                        throw new InvalidOperationException("Quotation direct rendering failed glyph or repeatability checks.");
                    }
                }
                else if (mode == "compiled")
                {
                    using var compiled = engine.Compile(html, css);
                    pdf = compiled.Render();
                    if (!pdf.SequenceEqual(compiled.Render()))
                    {
                        throw new InvalidOperationException("Compiled quotations were not repeatable.");
                    }
                }
                else
                {
                    if (!html.Contains("Record Ada", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Missing explicit record field in the quotation fixture.");
                    }
                    authored = html.Replace("Record Ada", "Record {{name}}", StringComparison.Ordinal);
                    using var compiled = engine.Compile(authored, css);
                    var records = new[] { "Ada", "Bea" };
                    pdf = compiled.RenderReflowBindings(records, map => map.Bind("name", record => record));
                    if (!pdf.SequenceEqual(compiled.RenderReflowBindings(records, map => map.Bind("name", record => record))))
                    {
                        throw new InvalidOperationException("Reflow quotation records were not repeatable.");
                    }
                }
                File.WriteAllText(Path.Combine(folder, "input.html"), authored);
                File.WriteAllText(Path.Combine(folder, "style.css"), css);
                var path = Path.Combine(folder, "document.pdf");
                File.WriteAllBytes(path, pdf);
                var pages = FullBleedEngine.InspectPdf(path).PageCount;
                var previews = engine.RenderFinalizedPdfImagePagesToDirectory(path, folder, dpi: 96, stem: "preview");
                if (pages != (mode == "reflow" ? 2 : 1) || previews.Paths.Count != pages)
                {
                    throw new InvalidOperationException("Unexpected quotation page count.");
                }
                cases.Add(new
                {
                    name, mode, directory, pages, pdfSha256 = Hash(pdf),
                    htmlSha256 = HashFile(Path.Combine(folder, "input.html")),
                    cssSha256 = HashFile(Path.Combine(folder, "style.css")),
                    previewFiles = previews.Paths.Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/')).ToArray(),
                    previewsSha256 = previews.Paths.Select(HashFile).ToArray(), repeatPdfBytesIdentical = true,
                });
            }
        }
        File.WriteAllText(Path.Combine(output, "quotes.json"), JsonSerializer.Serialize(new
        {
            schema = "fullbleed.dotnet.generated_quotes.renders.v1", packageVersion = version,
            sourceSha256 = Hash(sourceBytes), fontSha256 = HashFile(font),
            runtime = Environment.Version.ToString(), cases,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string HashFile(string path) => Hash(File.ReadAllBytes(path));
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
