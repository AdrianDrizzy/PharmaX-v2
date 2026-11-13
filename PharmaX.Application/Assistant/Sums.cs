using System.Globalization;
using System.Text.RegularExpressions;

namespace PharmaX.Application.Assistant;

/// <summary>
/// "What is 230 + 15%?", "15% of 480", "(12 x 35.50) / 3": worked out
/// exactly, in decimals, rather than guessed at by the model.
/// </summary>
public static partial class Sums
{
    /// <summary>"230 + 15% = 264.50"; null where the question is not a sum.</summary>
    public static string? Answer(string question)
    {
        var text = Lead().Replace((question ?? "").Trim().TrimEnd('?', '.', '=', ' '), "");
        text = Words().Replace(text, m => m.Value.ToLowerInvariant() switch
        {
            "plus" or "add" => "+",
            "minus" or "less" => "-",
            "of" or "times" or "multiplied by" or "x" or "×" => "*",
            "divided by" or "÷" => "/",
            _ when m.Value.StartsWith('R') || m.Value.StartsWith('r') => "",
            _ => m.Value
        });

        if (!Allowed().IsMatch(text) || !text.Any(char.IsDigit) || !text.Any(c => "+-*/%".Contains(c)))
            return null;

        decimal value;
        try
        {
            var parser = new Parser(text.Replace(" ", "", StringComparison.Ordinal));
            value = parser.Whole();
        }
        catch (Exception ex) when (ex is FormatException or DivideByZeroException or OverflowException)
        {
            return ex is DivideByZeroException ? "That divides by zero, which has no answer." : null;
        }

        return $"{question!.Trim().TrimEnd('?', '=', ' ')} = **{value.ToString("#,##0.####", CultureInfo.CurrentCulture)}**";
    }

    /// <summary>
    /// Numbers, + - * / ( ) and a percent sign. "230 + 15%" adds 15% of
    /// 230, as a till does; "15% * 480" is 0.15 x 480.
    /// </summary>
    private sealed class Parser
    {
        private readonly string _text;
        private int _at;

        public Parser(string text) => _text = text;

        public decimal Whole()
        {
            var value = Expression();
            if (_at != _text.Length) throw new FormatException();
            return value;
        }

        private decimal Expression()
        {
            var value = Term(out _);

            while (_at < _text.Length && _text[_at] is '+' or '-')
            {
                var plus = _text[_at++] == '+';
                var right = Term(out var percent);
                if (percent) right = value * right;
                value = plus ? value + right : value - right;
            }

            return value;
        }

        private decimal Term(out bool percent)
        {
            var value = Factor(out percent);

            while (_at < _text.Length && _text[_at] is '*' or '/')
            {
                var times = _text[_at++] == '*';
                var right = Factor(out _);
                value = times ? value * right : value / right;
                percent = false;
            }

            return value;
        }

        private decimal Factor(out bool percent)
        {
            percent = false;
            if (_at >= _text.Length) throw new FormatException();

            decimal value;
            if (_text[_at] == '-')
            {
                _at++;
                value = -Factor(out percent);
                return value;
            }

            if (_text[_at] == '(')
            {
                _at++;
                value = Expression();
                if (_at >= _text.Length || _text[_at++] != ')') throw new FormatException();
            }
            else
            {
                var start = _at;
                while (_at < _text.Length && (char.IsDigit(_text[_at]) || _text[_at] == '.')) _at++;
                value = decimal.Parse(_text[start.._at], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            }

            if (_at < _text.Length && _text[_at] == '%')
            {
                _at++;
                percent = true;
                value /= 100m;
            }

            return value;
        }
    }

    [GeneratedRegex(@"^(please\s+)?(what\s+is|what's|whats|calculate|work\s+out|how\s+much\s+is|compute)\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Lead();

    // "R230" is 230; "12x3" and "12 x 3" are 12 * 3.
    [GeneratedRegex(@"\b(multiplied by|divided by|plus|add|minus|less|times|of)\b|(?<=[\d\s)])x(?=[\s\d(])|[×÷]|\bR\s?(?=\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Words();

    // No comma: "12,5" is twelve and a half here and 125 elsewhere.
    [GeneratedRegex(@"^[\d\s\.\+\-\*/\(\)%]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Allowed();
}
