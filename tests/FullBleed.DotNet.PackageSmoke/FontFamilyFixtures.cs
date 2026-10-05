using FullBleed.DotNet;
using System.Security.Cryptography;
using System.Text.Json;

internal static class FontFamilyFixtures
{
    internal static void Run(string assets, string outputDirectory, string version)
    {
        var output = Path.Combine(outputDirectory, "font-families");
        Directory.CreateDirectory(Path.Combine(output, "fonts"));
        var source = Path.Combine(assets, "fonts");
        var names = new Dictionary<string, string>
        {
            ["normal"] = "DMSerifDisplay-Regular",
            ["italic"] = "DMSerifDisplay-Italic",
        };
        foreach (var name in names.Values.Append("DMSerifDisplay-OFL"))
        {
            var file = name + (name.EndsWith("OFL", StringComparison.Ordinal) ? ".txt" : ".ttf");
            File.Copy(Path.Combine(source, file), Path.Combine(output, "fonts", file));
        }

        FullBleedEngine CreateEngine(params string[] styles) => new(new FullBleedEngineOptions
        {
            Assets = styles.Select(style => FullBleedAsset.FromPath(
                Path.Combine(source, names[style] + ".ttf"), FullBleedAssetKind.Font)).ToList(),
        });

        var cases = new List<object>();
        void Render(FullBleedEngine engine, string name, string mode, string style, string family,
            string prefix = "", string? control = null)
        {
            var folder = Path.Combine(output, name);
            Directory.CreateDirectory(folder);
            var labels = mode == "pdf" ? new[] { "Alpha" } : new[] { "Alpha", "Bravo" };
            var html = mode == "pdf" ? "<p>Type Alpha with care.</p>" : "<p>Type {{name}} with care.</p>";
            var css = prefix + "@page {size:440pt 140pt;margin:20pt} p {font-family:\"" + family
                + "\";font-size:22pt;font-style:" + style + ";margin:0}";
            File.WriteAllText(Path.Combine(folder, "input.html"), html);
            File.WriteAllText(Path.Combine(folder, "style.css"), css);
            byte[] pdf;
            if (mode == "pdf")
            {
                pdf = engine.RenderPdf(html, css);
                if (!pdf.SequenceEqual(engine.RenderPdf(html, css)))
                {
                    throw new InvalidOperationException($"Font family render did not repeat: {name}");
                }
            }
            else
            {
                using var compiled = engine.Compile(html, css);
                byte[] Paint() => mode == "fixed"
                    ? compiled.RenderBindings(labels, map => map.Bind("name", label => label))
                    : compiled.RenderReflowBindings(labels, map => map.Bind("name", label => label), CompiledFlowCompression.Compact);
                pdf = Paint();
                if (!pdf.SequenceEqual(Paint()))
                {
                    throw new InvalidOperationException($"Compiled font family render did not repeat: {name}");
                }
            }
            var path = Path.Combine(folder, "document.pdf");
            File.WriteAllBytes(path, pdf);
            if (FullBleedEngine.InspectPdf(path).PageCount != labels.Length)
            {
                throw new InvalidOperationException($"Unexpected font family page count: {name}");
            }
            var previews = engine.RenderFinalizedPdfImagePagesToDirectory(path, folder, dpi: 96, stem: "preview");
            cases.Add(new
            {
                name,
                mode,
                style,
                control,
                expectedFace = names[style],
                expectedText = labels.Select(label => $"Type {label} with care.").ToArray(),
                htmlSha256 = HashFile(Path.Combine(folder, "input.html")),
                cssSha256 = HashFile(Path.Combine(folder, "style.css")),
                pdfSha256 = HashFile(path),
                previewFiles = previews.Paths.Select(Path.GetFileName).ToArray(),
                previewsSha256 = previews.Paths.Select(HashFile).ToArray(),
                repeatPdfBytesIdentical = true,
            });
        }

        foreach (var mode in new[] { "pdf", "fixed", "reflow" })
        {
            foreach (var style in names.Keys)
            {
                using var engine = CreateEngine(style);
                Render(engine, $"control-{style}-{mode}", mode, style, names[style]);
            }
        }
        const string mapped = "@font-face {font-family:\"Customer Serif\";src:local(\"DMSerifDisplay-Regular\");font-style:normal}"
            + "@font-face {font-family:\"Customer Serif\";src:local(\"DMSerifDisplay-Italic\");font-style:italic}";
        foreach (var order in new[] { new[] { "italic", "normal" }, new[] { "normal", "italic" } })
        {
            using var engine = CreateEngine(order);
            foreach (var mode in new[] { "pdf", "fixed", "reflow" })
            {
                foreach (var style in names.Keys)
                {
                    Render(engine, $"{order[0]}-first-{style}-{mode}", mode, style, "DM Serif Display",
                        control: $"control-{style}-{mode}");
                }
            }
            foreach (var style in names.Keys)
            {
                Render(engine, $"{order[0]}-first-mapped-{style}", "pdf", style, "Customer Serif", mapped,
                    $"control-{style}-pdf");
            }
        }
        var report = new
        {
            schema = "fullbleed.binding_font_families.v1",
            ok = true,
            packageVersion = version,
            runtime = Environment.Version.ToString(),
            fonts = names.Values.Select(name => new
            {
                path = "fonts/" + name + ".ttf",
                face = name,
                sha256 = HashFile(Path.Combine(source, name + ".ttf")),
            }).ToArray(),
            cases,
        };
        File.WriteAllText(Path.Combine(output, "renders.json"), JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
