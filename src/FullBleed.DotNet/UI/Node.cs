using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace FullBleed.DotNet.UI;

/// <summary>An immutable child in a static print document.</summary>
public abstract class Node
{
    private protected Node() { }

    /// <summary>Serialize this subtree. Text and attribute values are escaped.</summary>
    public string ToHtml()
    {
        var output = new StringBuilder();
        var pending = new Stack<(Node? Node, string? ClosingTag)>();
        pending.Push((this, null));
        while (pending.TryPop(out var frame))
        {
            if (frame.ClosingTag is not null)
            {
                output.Append("</").Append(frame.ClosingTag).Append('>');
                continue;
            }
            switch (frame.Node)
            {
                case TextNode text:
                    Escape(output, text.Value);
                    break;
                case FragmentNode fragment:
                    PushChildren(fragment.Children);
                    break;
                case Element element:
                    output.Append('<').Append(element.Tag);
                    foreach (var attribute in element.Attributes)
                    {
                        output.Append(' ').Append(attribute.Key).Append("=\"");
                        Escape(output, attribute.Value);
                        output.Append('"');
                    }
                    if (element.InlineStyle.ToCss() is { Length: > 0 } css)
                    {
                        output.Append(" style=\"");
                        Escape(output, css);
                        output.Append('"');
                    }
                    output.Append('>');
                    if (!element.IsVoid)
                    {
                        pending.Push((null, element.Tag));
                        PushChildren(element.Children);
                    }
                    break;
            }
        }
        return output.ToString();

        void PushChildren(IReadOnlyList<Node> children)
        {
            for (var i = children.Count - 1; i >= 0; i--)
            {
                pending.Push((children[i], null));
            }
        }
    }

    [return: NotNullIfNotNull(nameof(value))]
    public static implicit operator Node?(string? value) => value is null ? null : new TextNode(value);
    public static implicit operator Node(int value) => new TextNode(value.ToString(CultureInfo.InvariantCulture));
    public static implicit operator Node(uint value) => new TextNode(value.ToString(CultureInfo.InvariantCulture));
    public static implicit operator Node(long value) => new TextNode(value.ToString(CultureInfo.InvariantCulture));
    public static implicit operator Node(ulong value) => new TextNode(value.ToString(CultureInfo.InvariantCulture));
    public static implicit operator Node(decimal value) => new TextNode(value.ToString(CultureInfo.InvariantCulture));
    public static implicit operator Node(float value) => float.IsFinite(value)
        ? new TextNode(value.ToString("R", CultureInfo.InvariantCulture))
        : throw new ArgumentOutOfRangeException(nameof(value), "Document numbers must be finite.");
    public static implicit operator Node(double value) => double.IsFinite(value)
        ? new TextNode(value.ToString("R", CultureInfo.InvariantCulture))
        : throw new ArgumentOutOfRangeException(nameof(value), "Document numbers must be finite.");

    internal static Node[] Snapshot(IEnumerable<Node?> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        return children.Where(child => child is not null).Cast<Node>().ToArray();
    }

    internal static string Escape(string value)
    {
        var output = new StringBuilder();
        Escape(output, value);
        return output.ToString();
    }

    private static void Escape(StringBuilder output, string value)
    {
        foreach (var c in value)
        {
            output.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#x27;",
                _ => c.ToString(),
            });
        }
    }
}

internal sealed class TextNode(string value) : Node
{
    internal string Value { get; } = value;
}

internal sealed class FragmentNode(IEnumerable<Node?> children) : Node
{
    internal IReadOnlyList<Node> Children { get; } = Array.AsReadOnly(Snapshot(children));
}
