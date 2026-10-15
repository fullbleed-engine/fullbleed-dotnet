using System.Text;

namespace FullBleed.DotNet.UI;

/// <summary>Immutable CSS declarations. Use an initializer or With/Merge to compose styles.</summary>
public sealed class Style
{
    private readonly List<KeyValuePair<string, string>> _declarations = [];

    public Style() { }

    public Style(Style basis)
    {
        ArgumentNullException.ThrowIfNull(basis);
        _declarations.AddRange(basis._declarations);
    }

    /// <summary>CSS property names accept kebab-case, snake_case and camelCase. Custom-property case is preserved.</summary>
    public string? this[string property]
    {
        get => _declarations.FirstOrDefault(pair => pair.Key == NormalizeName(property)).Value;
        init => Set(property, value);
    }

    public Style With(string property, string value)
    {
        var result = new Style(this);
        result.Set(property, value);
        return result;
    }

    public Style Merge(Style overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var result = new Style(this);
        foreach (var pair in overrides._declarations)
        {
            result.Set(pair.Key, pair.Value);
        }
        return result;
    }

    /// <summary>Parse a declaration list, preserving strings, comments, function and data-URL boundaries.</summary>
    public static Style Parse(string declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        var result = new Style();
        foreach (var declaration in CssSyntax.Split(declarations, ';'))
        {
            if (string.IsNullOrWhiteSpace(declaration))
            {
                continue;
            }
            var colon = declaration.IndexOf(':');
            if (colon <= 0)
            {
                throw new ArgumentException("A style declaration requires a property and colon.", nameof(declarations));
            }
            result.Set(declaration[..colon], declaration[(colon + 1)..]);
        }
        return result;
    }

    public string ToCss() => _declarations.Count == 0 ? "" :
        string.Join("; ", _declarations.Select(pair => $"{pair.Key}: {pair.Value}")) + ";";

    private void Set(string property, string? value)
    {
        var name = NormalizeName(property);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var parts = CssSyntax.Split(value.Trim(), ';');
        if (parts.Count != 1 || string.IsNullOrWhiteSpace(parts[0]))
        {
            throw new ArgumentException("A style value must be one CSS value, without a declaration separator.", nameof(value));
        }
        _declarations.RemoveAll(pair => pair.Key == name);
        _declarations.Add(new(name, parts[0].Trim()));
    }

    private static string NormalizeName(string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        var name = property.Trim();
        if (!name.StartsWith("--", StringComparison.Ordinal))
        {
            var normalized = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                var c = name[i];
                if (char.IsAsciiLetterUpper(c) && i > 0 && (char.IsAsciiLetterLower(name[i - 1]) || char.IsAsciiDigit(name[i - 1])))
                {
                    normalized.Append('-');
                }
                normalized.Append(c == '_' ? '-' : char.ToLowerInvariant(c));
            }
            name = normalized.ToString();
        }
        var custom = name.StartsWith("--", StringComparison.Ordinal);
        var bare = custom ? name[2..] : name.TrimStart('-');
        if (bare.Length == 0 || !custom && !char.IsAsciiLetter(bare[0]) ||
            bare.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
        {
            throw new ArgumentException($"Invalid CSS property: {property}", nameof(property));
        }
        return name;
    }
}

internal static class CssSyntax
{
    internal static List<string> Split(string text, char delimiter)
    {
        var parts = new List<string>();
        var chunk = new StringBuilder();
        var brackets = new Stack<char>();
        var quote = '\0';
        var escaped = false;
        var comment = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (comment)
            {
                if (c == '*' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    comment = false;
                    i++;
                }
                continue;
            }
            if (escaped)
            {
                chunk.Append(c);
                escaped = false;
            }
            else if (c == '\\')
            {
                chunk.Append(c);
                escaped = true;
            }
            else if (quote != '\0')
            {
                chunk.Append(c);
                if (c == quote) quote = '\0';
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                chunk.Append(' ');
                comment = true;
                i++;
            }
            else if (c is '\'' or '"')
            {
                chunk.Append(c);
                quote = c;
            }
            else if (c == delimiter && brackets.Count == 0)
            {
                parts.Add(chunk.ToString());
                chunk.Clear();
            }
            else if (c is '(' or '[' or '{')
            {
                brackets.Push(c switch { '(' => ')', '[' => ']', _ => '}' });
                chunk.Append(c);
            }
            else if (c is ')' or ']' or '}')
            {
                if (!brackets.TryPop(out var expected) || c != expected)
                {
                    throw new ArgumentException("Unbalanced CSS delimiters.", nameof(text));
                }
                chunk.Append(c);
            }
            else
            {
                chunk.Append(c);
            }
        }
        if (quote != '\0' || escaped || comment || brackets.Count != 0)
        {
            throw new ArgumentException("Incomplete CSS string, escape, comment or block.", nameof(text));
        }
        parts.Add(chunk.ToString());
        return parts;
    }
}
