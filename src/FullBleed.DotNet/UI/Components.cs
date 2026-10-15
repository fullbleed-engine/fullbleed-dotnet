namespace FullBleed.DotNet.UI;

/// <summary>Named elements for static print composition; no framework or renderer is started.</summary>
public static class Components
{
    public static Node Fragment(params Node?[]? children) => Fragment((IEnumerable<Node?>)(children ?? []));
    public static Node Fragment(IEnumerable<Node?> children) => new FragmentNode(children);
    public static Element Text(params Node?[]? children) => Element.Create("span", children);
    public static Element Paragraph(params Node?[]? children) => Element.Create("p", children);
    public static Element Strong(params Node?[]? children) => Element.Create("strong", children);
    public static Element Emphasis(params Node?[]? children) => Element.Create("em", children);
    public static Element Heading(int level, params Node?[]? children) => level is >= 1 and <= 6
        ? Element.Create($"h{level}", children)
        : throw new ArgumentOutOfRangeException(nameof(level), "Heading levels are 1 through 6.");
    public static Element Box(params Node?[]? children) => Element.Create("div", children);
    public static Element Section(params Node?[]? children) => Element.Create("section", children);
    public static Element Header(params Node?[]? children) => Element.Create("header", children);
    public static Element Footer(params Node?[]? children) => Element.Create("footer", children);
    public static Element Stack(params Node?[]? children) => Box(children).WithClass("ui-stack");
    public static Element Stack(IEnumerable<Node?> children) => Stack(Fragment(children));
    public static Element Row(params Node?[]? children) => Box(children).WithClass("ui-row");
    public static Element Row(IEnumerable<Node?> children) => Row(Fragment(children));
    public static Element LayoutGrid(params Node?[]? children) => Box(children).WithClass("ui-layout-grid");
    public static Element Inline(params Node?[]? children) => Text(children).WithClass("ui-inline");
    public static Element Center(params Node?[]? children) => Box(children).WithClass("ui-center");
    public static Element Card(params Node?[]? children) => Box(children).WithClass("ui-card");
    public static Element Table(params Node?[]? children) => Element.Create("table", children).WithClass("ui-table");
    public static Element TableCaption(params Node?[]? children) => Element.Create("caption", children);
    public static Element TableHead(params Node?[]? children) => Element.Create("thead", children);
    public static Element TableBody(params Node?[]? children) => Element.Create("tbody", children);
    public static Element TableBody(IEnumerable<Node?> children) => TableBody(Fragment(children));
    public static Element TableRow(params Node?[]? children) => Element.Create("tr", children);
    public static Element TableCell(params Node?[]? children) => Element.Create("td", children);
    public static Element TableHeader(params Node?[]? children) => Element.Create("th", children);
    public static Element List(params Node?[]? children) => Element.Create("ul", children);
    public static Element List(IEnumerable<Node?> children) => List(Fragment(children));
    public static Element OrderedList(params Node?[]? children) => Element.Create("ol", children);
    public static Element OrderedList(IEnumerable<Node?> children) => OrderedList(Fragment(children));
    public static Element ListItem(params Node?[]? children) => Element.Create("li", children);
    public static Element LineBreak() => Element.Create("br");
    public static Element Divider() => Element.Create("hr");
    public static Element Link(string href, params Node?[]? children)
    {
        ArgumentNullException.ThrowIfNull(href);
        return Element.Create("a", children).WithAttribute("href", href);
    }
    public static Element Image(string source, string alternativeText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(alternativeText);
        return Element.Create("img").WithAttribute("src", source).WithAttribute("alt", alternativeText);
    }

    /// <summary>Small layout defaults only. No theme, assets or fonts are loaded.</summary>
    public static StyleSheet DefaultStyles { get; } = StyleSheet.FromCss("""
        .ui-stack { display: flex; flex-direction: column; gap: 8pt; }
        .ui-row { display: flex; align-items: center; justify-content: space-between; gap: 8pt; }
        .ui-layout-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8pt; }
        .ui-inline { display: inline-flex; align-items: center; gap: 4pt; }
        .ui-center { display: flex; flex-direction: column; align-items: center; text-align: center; }
        .ui-card { border: 1px solid #d3ded8; border-radius: 4pt; padding: 12pt; }
        .ui-table { width: 100%; border-collapse: collapse; }
        """);
}
