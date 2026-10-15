namespace FullBleed.DotNet.UI;

/// <summary>An immutable, ordered stylesheet. Attach it to the outer Document through Styles.</summary>
public sealed class StyleSheet
{
    private readonly List<string> _fragments = [];

    public StyleSheet(params StyleSheet[] shared)
    {
        ArgumentNullException.ThrowIfNull(shared);
        foreach (var sheet in shared)
        {
            ArgumentNullException.ThrowIfNull(sheet);
            _fragments.Add(sheet.ToCss());
        }
    }

    /// <summary>Add a declaration rule during construction. Repeated selectors retain authored cascade order.</summary>
    public Style this[string selector]
    {
        init => AddRule(selector, value);
    }

    public StyleSheet WithRule(string selector, Style declarations)
    {
        var result = new StyleSheet(this);
        result.AddRule(selector, declarations);
        return result;
    }

    /// <summary>Use authored CSS for nested rules and engine-specific syntax.</summary>
    public static StyleSheet FromCss(string css)
    {
        ArgumentNullException.ThrowIfNull(css);
        var result = new StyleSheet();
        result._fragments.Add(css.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim());
        return result;
    }

    public string ToCss() => string.Join("\n", _fragments.Where(fragment => fragment.Length != 0));

    private void AddRule(string selector, Style declarations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        ArgumentNullException.ThrowIfNull(declarations);
        // Declaration rules have no outer block. Nested rules use FromCss.
        if (CssSyntax.Split(selector, '{').Count != 1)
        {
            throw new ArgumentException("Use a selector without a rule block. For nested rules, use StyleSheet.FromCss.", nameof(selector));
        }
        _fragments.Add($"{selector.Trim()} {{ {declarations.ToCss()} }}");
    }
}
