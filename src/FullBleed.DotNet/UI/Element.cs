using System.Collections.ObjectModel;

namespace FullBleed.DotNet.UI;

/// <summary>An immutable element. Custom components are ordinary methods returning elements.</summary>
public sealed class Element : Node
{
    private static readonly HashSet<string> VoidTags = new(
        "area base br col embed hr img input link meta source track wbr".Split(' '), StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BooleanAttributes = new(
        ("allowfullscreen async autofocus autoplay checked controls default defer disabled " +
         "formnovalidate hidden inert ismap itemscope loop multiple muted nomodule novalidate " +
         "open playsinline readonly required reversed selected").Split(' '), StringComparer.OrdinalIgnoreCase);

    private Element(string tag, Node[] children, Dictionary<string, string> attributes, Style style)
    {
        ValidateName(tag, isTag: true);
        Tag = tag;
        IsVoid = VoidTags.Contains(tag);
        if (IsVoid && children.Length != 0)
        {
            throw new ArgumentException($"Void element {tag} cannot have children.", nameof(children));
        }
        Children = Array.AsReadOnly(children);
        Attributes = new ReadOnlyDictionary<string, string>(attributes);
        InlineStyle = style;
    }

    public string Tag { get; }
    public IReadOnlyList<Node> Children { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }
    public string ClassName => Attributes.GetValueOrDefault("class", "");
    public Style InlineStyle { get; }
    internal bool IsVoid { get; }

    /// <summary>Lower-level escape hatch for an engine-supported tag; children remain escaped.</summary>
    public static Element Create(string tag, params Node?[]? children) => Create(tag, (IEnumerable<Node?>)(children ?? []));
    public static Element Create(string tag, IEnumerable<Node?> children) => new(tag, Snapshot(children), new(StringComparer.Ordinal), new Style());

    /// <summary>Add space-separated class names, retaining existing and built-in classes.</summary>
    public Element WithClass(params string?[] names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var classes = new[] { ClassName }.Concat(names)
            .Where(name => name is not null)
            .SelectMany(name => name!.Split([' ', '\t', '\r', '\n', '\f'], StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal);
        return WithAttribute("class", string.Join(" ", classes));
    }

    /// <summary>Compose inline declarations; later properties override earlier ones without mutation.</summary>
    public Element WithStyle(Style style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return new(Tag, Children.ToArray(), new(Attributes, StringComparer.Ordinal), InlineStyle.Merge(style));
    }

    public Element WithStyle(string declarations) => WithStyle(Style.Parse(declarations));

    /// <summary>Set an escaped HTML attribute. Null removes it; class replaces the complete class list.</summary>
    public Element WithAttribute(string name, string? value)
    {
        ValidateName(name, isTag: false);
        name = name.ToLowerInvariant();
        if (name == "style")
        {
            return new(Tag, Children.ToArray(), new(Attributes, StringComparer.Ordinal), value is null ? new Style() : Style.Parse(value));
        }
        var attributes = new Dictionary<string, string>(Attributes, StringComparer.Ordinal);
        attributes.Remove(name);
        if (value is not null)
        {
            attributes.Add(name, value);
        }
        return new(Tag, Children.ToArray(), attributes, InlineStyle);
    }

    /// <summary>Boolean HTML attributes use presence; other attributes retain literal true/false.</summary>
    public Element WithAttribute(string name, bool value) => WithAttribute(name,
        BooleanAttributes.Contains(name) ? value ? "" : null : value ? "true" : "false");

    private static void ValidateName(string name, bool isTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!(char.IsAsciiLetter(name[0]) || !isTag && name[0] is '_' or ':') ||
            name.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-' and not '.' and not ':'))
        {
            throw new ArgumentException($"Invalid {(isTag ? "tag" : "attribute")} name: {name}", nameof(name));
        }
    }
}
