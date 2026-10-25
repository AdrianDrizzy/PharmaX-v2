using System.Text;

namespace PharmaX.Assistant;

/// <summary>
/// Writing and checking the GBNF grammar that holds the model to answers
/// PharmaX can use.
/// </summary>
/// <remarks>
/// The grammar is checked here before it reaches llama.cpp because a
/// grammar it cannot parse does not fail politely there: the native
/// library reports it on standard error and the process dies with an
/// access violation, which on a till is the whole application gone.
/// </remarks>
public static class Gbnf
{
    /// <summary>A GBNF string literal that matches exactly the given text.</summary>
    public static string Literal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var sb = new StringBuilder(text.Length + 2).Append('"');

        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => c.ToString()
            });
        }

        return sb.Append('"').ToString();
    }

    /// <summary>
    /// Throws where a rule name is one llama.cpp will not accept, a rule
    /// is used but never defined, a literal or bracket is left open, or
    /// there is no root.
    /// </summary>
    public static void Validate(string grammar)
    {
        ArgumentNullException.ThrowIfNull(grammar);

        var defined = new HashSet<string>(StringComparer.Ordinal);
        var used = new List<(string Name, int Line)>();
        var lineNumber = 0;

        foreach (var raw in grammar.Split('\n'))
        {
            lineNumber++;
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0) continue;

            var arrow = line.IndexOf("::=", StringComparison.Ordinal);
            if (arrow < 0)
                throw Bad(lineNumber, "has no '::='");

            var name = line[..arrow].Trim();
            if (!IsRuleName(name))
                throw Bad(lineNumber, $"names a rule '{name}', but rule names may hold only letters, digits and dashes");

            if (!defined.Add(name))
                throw Bad(lineNumber, $"defines '{name}' a second time");

            foreach (var reference in References(line[(arrow + 3)..], lineNumber))
                used.Add((reference, lineNumber));
        }

        if (!defined.Contains("root"))
            throw new InvalidOperationException("The assistant's grammar has no root rule.");

        foreach (var (reference, line) in used)
        {
            if (!defined.Contains(reference))
                throw Bad(line, $"uses '{reference}', which is never defined");
        }
    }

    private static List<string> References(string body, int line)
    {
        var refs = new List<string>();
        var depth = 0;
        var i = 0;

        while (i < body.Length)
        {
            var c = body[i];

            if (c == '"')
            {
                i = SkipQuoted(body, i, '"', line);
            }
            else if (c == '[')
            {
                i = SkipQuoted(body, i, ']', line);
            }
            else if (c == '{')
            {
                var close = body.IndexOf('}', i);
                if (close < 0) throw Bad(line, "leaves a '{' open");
                i = close + 1;
            }
            else if (c == '(')
            {
                depth++;
                i++;
            }
            else if (c == ')')
            {
                if (--depth < 0) throw Bad(line, "closes a ')' it never opened");
                i++;
            }
            else if (char.IsLetterOrDigit(c) || c == '-')
            {
                var start = i;
                while (i < body.Length && (char.IsLetterOrDigit(body[i]) || body[i] == '-')) i++;
                refs.Add(body[start..i]);
            }
            else
            {
                i++;
            }
        }

        if (depth != 0) throw Bad(line, "leaves a '(' open");
        return refs;
    }

    /// Past the closing character of a literal or a character class.
    private static int SkipQuoted(string body, int open, char close, int line)
    {
        for (var i = open + 1; i < body.Length; i++)
        {
            if (body[i] == '\\') { i++; continue; }
            if (body[i] == close) return i + 1;
        }

        throw Bad(line, $"leaves a '{body[open]}' open");
    }

    private static bool IsRuleName(string name) =>
        name.Length > 0 && name.All(c => (c is >= 'a' and <= 'z') || (c is >= 'A' and <= 'Z')
                                         || (c is >= '0' and <= '9') || c == '-');

    private static InvalidOperationException Bad(int line, string what) =>
        new($"The assistant's grammar is not valid: line {line} {what}.");
}
