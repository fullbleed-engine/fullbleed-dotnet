using FullBleed.DotNet;
using System.Security.Cryptography;
using System.Text.Json;

internal static class InlineLayoutFixtures
{
    private const string Tail = "to change the paper size, margins, heading scale, table colors, or code treatment. Keep local image references next to the document.";

    internal static void Run(string assets, string outputDirectory, string version)
    {
        var output = Path.Combine(outputDirectory, "inline-layout");
        Directory.CreateDirectory(Path.Combine(output, "fonts"));
        var fontPaths = new[]
        {
            Path.Combine(assets, "fonts", "Inter-Variable.ttf"),
            Path.Combine(assets, "verification-fonts", "NotoSans-Regular.ttf"),
        };
        foreach (var path in fontPaths.Append(Path.Combine(assets, "fonts", "Inter-OFL.txt"))
            .Append(Path.Combine(assets, "verification-fonts", "NotoSans-OFL.txt")))
        {
            File.Copy(path, Path.Combine(output, "fonts", Path.GetFileName(path)));
        }
        using var engine = new FullBleedEngine(new FullBleedEngineOptions
        {
            Assets = fontPaths.Select(path => FullBleedAsset.FromPath(path, FullBleedAssetKind.Font)).ToList(),
        });
        var cases = new List<object>();
        void Render(string name, string mode, string kind, string html, string css, string expected)
        {
            var folder = Path.Combine(output, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "input.html"), html);
            File.WriteAllText(Path.Combine(folder, "style.css"), css);
            byte[] pdf;
            if (mode == "pdf")
            {
                var input = html.Replace("{{filename}}", "print.css", StringComparison.Ordinal);
                var rendered = engine.RenderPdfWithDiagnostics(input, css);
                pdf = rendered.Pdf;
                if (rendered.Diagnostics.MissingGlyphs.Count != 0 || !pdf.SequenceEqual(engine.RenderPdf(input, css)))
                {
                    throw new InvalidOperationException($"Non-repeatable output or missing glyphs: {name}");
                }
            }
            else
            {
                using var compiled = engine.Compile(html, css);
                var values = new[] { "print.css" };
                byte[] Paint() => mode == "fixed"
                    ? compiled.RenderBindings(values, map => map.Bind("filename", value => value))
                    : compiled.RenderReflowBindings(values, map => map.Bind("filename", value => value),
                        mode == "compact" ? CompiledFlowCompression.Compact : CompiledFlowCompression.Throughput);
                pdf = Paint();
                if (!pdf.SequenceEqual(Paint()))
                {
                    throw new InvalidOperationException($"Non-repeatable compiled output: {name}");
                }
            }
            var path = Path.Combine(folder, "document.pdf");
            File.WriteAllBytes(path, pdf);
            if (FullBleedEngine.InspectPdf(path).PageCount != 1)
            {
                throw new InvalidOperationException($"Expected one inline fixture page: {name}");
            }
            var previews = engine.RenderFinalizedPdfImagePagesToDirectory(path, folder, dpi: 96, stem: "preview");
            string? controlSha256 = null;
            if (name.EndsWith("-helvetica", StringComparison.Ordinal) || name.EndsWith("-times", StringComparison.Ordinal))
            {
                var controlPath = Path.Combine(folder, "control.pdf");
                engine.RenderPdfToFile("<p>Edit print.css to change</p>", css, controlPath);
                controlSha256 = HashFile(controlPath);
            }
            cases.Add(new
            {
                name,
                mode,
                kind,
                expectedText = expected,
                htmlSha256 = HashFile(Path.Combine(folder, "input.html")),
                cssSha256 = HashFile(Path.Combine(folder, "style.css")),
                pdfSha256 = HashFile(path),
                previewFiles = previews.Paths.Select(Path.GetFileName).ToArray(),
                previewsSha256 = previews.Paths.Select(HashFile).ToArray(),
                controlSha256,
                repeatPdfBytesIdentical = true,
            });
        }

        var plain = "<p>Edit {{filename}} " + Tail + "</p>";
        var code = "<p>Edit <code>{{filename}}</code> " + Tail + "</p>";
        var span = "<p>Edit <span>{{filename}}</span> " + Tail + "</p>";
        var wrapping = new (string Name, string Html, string Css)[]
        {
            ("plain", plain, ""),
            ("unstyled-span", span, ""),
            ("code", code, ""),
            ("color", code, "code {color:#ab391c}"),
            ("background", code, "code {background:#eee9da}"),
            ("padding", code, "code {padding:1pt 3pt}"),
            ("different-font", code, "code {font-family:'Noto Sans';font-size:9pt}"),
            ("decorated", code, "code {font-family:'Noto Sans';font-size:9pt;padding:1pt 3pt;background:#eee9da}"),
            ("long-inline", "<p>Edit <span>{{filename}} " + Tail + "</span></p>", "span {color:#ab391c}"),
            ("collapsed-spaces", "<p>  Edit <span> {{filename}} </span>  " + Tail.Replace(" ", "  ", StringComparison.Ordinal) + "  </p>", "span {background:#eee9da}"),
            ("helvetica", span, "* {font-family:Helvetica} span {color:#ab391c}"),
            ("times", span, "* {font-family:Times-Roman} span {color:#ab391c}"),
        };
        foreach (var mode in new[] { "pdf", "fixed", "reflow", "compact" })
        {
            foreach (var item in wrapping)
            {
                Render($"{mode}-{item.Name}", mode, "wrapping", item.Html + "<p>After the paragraph.</p>",
                    "@page {size:240pt 420pt;margin:20pt} body {font-family:Inter;font-size:10pt;line-height:15pt} "
                    + "* {margin:0;padding:0} p {margin-bottom:12pt} " + item.Css,
                    "Edit print.css " + Tail + " After the paragraph.");
            }
        }
        foreach (var spacing in new[] { "0", "0.45", "1.5" })
        {
            foreach (var display in new[] { "flex", "inline-block" })
            {
                var html = display == "flex"
                    ? "<div class=\"row\"><span>NORTHSTAR STUDIO / INVOICE NS-1042</span><span>01 / 01</span></div>"
                    : "<div class=\"row\"><span class=\"label\">TOTAL <em>OPERATING</em> ALLOCATION USD</span> END</div>";
                var expected = display == "flex" ? "NORTHSTAR STUDIO / INVOICE NS-1042 01 / 01" : "TOTAL OPERATING ALLOCATION USD END";
                var rule = display == "flex" ? ".row {display:flex;justify-content:space-between;gap:12pt}"
                    : ".label {display:inline-block} em {font-style:normal;color:#b44123}";
                Render($"sizing-{display}-{spacing}", "pdf", "sizing", html,
                    "@page {size:420pt 160pt;margin:20pt} * {margin:0;padding:0} "
                    + "body {font-family:Inter;font-size:8pt;line-height:15pt} "
                    + $".row {{letter-spacing:{spacing}pt}} " + rule, expected);
            }
        }
        var report = new
        {
            schema = "fullbleed.dotnet.inline_layout.v1",
            ok = true,
            packageVersion = version,
            runtime = Environment.Version.ToString(),
            fonts = fontPaths.Select(path => new { path = "fonts/" + Path.GetFileName(path), sha256 = HashFile(path) }).ToArray(),
            cases,
        };
        File.WriteAllText(Path.Combine(output, "renders.json"), JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
