using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using PharmaX.Application.Reporting;

namespace PharmaX.Assistant;

/// <summary>
/// Everything the model is shown and everything it is allowed to say,
/// built from the report catalogue and this pharmacy's own lists, and
/// the reading of its answer back into a suggestion.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from the inference engine so that it can be tested without
/// a model. What made a two-billion-parameter model usable in the spike
/// (22 October 2026) all lives here:
/// </para>
/// <list type="bullet">
/// <item>A description beside every report name.</item>
/// <item>A grammar that admits only the reports offered, each with its
/// own filters, and only values that exist - this pharmacy's departments
/// and staff rather than whatever the model makes up.</item>
/// <item>A handful of worked examples.</item>
/// <item>Custom dates only when the request has a month or a number in it,
/// so "this year" cannot come back as October.</item>
/// </list>
/// <para>
/// The instructions do not mention today's date, so the model's reading
/// of them can be kept on disk and reused; the date goes with each
/// question instead.
/// </para>
/// </remarks>
public sealed partial class ReportPrompt
{
    /// <summary>
    /// Past this a list is shortened in the instructions. The grammar
    /// still holds every value, so a name the request gives is matched
    /// even when it was not shown.
    /// </summary>
    private const int ListedChoices = 40;

    /// <summary>A department list longer than this is not offered at all.</summary>
    private const int MaxChoices = 400;

    /// <summary>Requests are short; anything longer is cut.</summary>
    private const int MaxRequestLength = 300;

    private static readonly JsonSerializerOptions Json = new()
    {
        // "Café" stays "Café" rather than becoming an escape the model
        // would have to spell out.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ReportSuggestionContext _context;
    private readonly Dictionary<string, IReadOnlyList<Askable>> _askable;

    public ReportPrompt(ReportSuggestionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _askable = context.Presets.ToDictionary(
            p => p.Key,
            p => (IReadOnlyList<Askable>)AskableFor(p.Key),
            StringComparer.Ordinal);

        Instructions = BuildInstructions();
        Grammar = BuildGrammar(allowCustom: true);
        NamedPeriodGrammar = BuildGrammar(allowCustom: false);
    }

    /// <summary>The standing instructions, the same for every question.</summary>
    public string Instructions { get; }

    /// <summary>The answer's grammar where the request may name its own dates.</summary>
    public string Grammar { get; }

    /// <summary>The answer's grammar where it may only name a period.</summary>
    public string NamedPeriodGrammar { get; }

    /// <summary>The instructions as the first turn of a Qwen chat.</summary>
    public string SystemTurn => $"<|im_start|>system\n{Instructions}<|im_end|>\n";

    /// <summary>
    /// One question as a Qwen chat turn, with thinking switched off the
    /// way Qwen's own template does it: an empty think block to start
    /// the answer.
    /// </summary>
    /// <remarks>
    /// The date goes in only where the request names a month or a number
    /// - the only time the model writes dates itself, and needs the year.
    /// With it in front of every question, the model answered "yesterday"
    /// with last week four times in 56; a named period is worked out by
    /// <see cref="Periods"/> and needs no date from the model at all.
    /// </remarks>
    public string Turn(string request)
    {
        var dated = NamesDates(request)
            ? "Today is " + _context.Today.ToString("dddd d MMMM yyyy", CultureInfo.InvariantCulture) + ".\n"
            : "";

        return $"<|im_start|>user\n{dated}{Clean(request)}<|im_end|>\n"
               + "<|im_start|>assistant\n<think>\n\n</think>\n\n";
    }

    public string GrammarFor(string request) =>
        NamesDates(request) ? Grammar : NamedPeriodGrammar;

    /// <summary>True where the request holds a month or a number to build dates from.</summary>
    public static bool NamesDates(string request) =>
        DateWords().IsMatch(request ?? "");

    /// <summary>
    /// The model's answer as a suggestion: the report, its dates worked
    /// out here rather than by the model, and each filter as the value
    /// the screen sends. Anything not offered is dropped.
    /// </summary>
    public ReportSuggestion Read(string answer)
    {
        try
        {
            using var doc = JsonDocument.Parse(answer);
            var root = doc.RootElement;

            var key = root.GetProperty("report").GetString();
            if (key is null || !_askable.TryGetValue(key, out var askable))
                return ReportSuggestion.NotAReport;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var filter in root.GetProperty("filters").EnumerateObject())
            {
                var slicer = askable.FirstOrDefault(a =>
                    string.Equals(a.Slicer.Key, filter.Name, StringComparison.Ordinal));

                if (slicer is null || values.ContainsKey(filter.Name)) continue;

                var said = filter.Value.ValueKind == JsonValueKind.String
                    ? filter.Value.GetString()
                    : filter.Value.GetRawText();

                if (slicer.ValueOf(said) is string value)
                    values[filter.Name] = value;
            }

            var period = root.GetProperty("period").GetString() ?? "none";
            var from = root.TryGetProperty("from", out var f) ? f.GetString() : null;
            var to = root.TryGetProperty("to", out var t) ? t.GetString() : null;
            var range = Periods.Resolve(period, from, to, _context.Today);

            return new ReportSuggestion(key, range?.From, range?.To, values);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException
                                       or InvalidOperationException)
        {
            // Cut off before it finished, which the grammar otherwise rules out.
            return ReportSuggestion.Unclear;
        }
    }

    private string BuildInstructions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You pick the PharmaX report that answers a pharmacy staff member's request.");
        sb.AppendLine("Weeks start on Monday.");
        sb.AppendLine();
        sb.AppendLine("Answer with JSON only, in this shape:");
        sb.AppendLine("{\"report\": \"<key>\", \"filters\": {}, \"period\": \"<period>\"}");
        sb.AppendLine();
        sb.AppendLine("report: the key of the one report below that best answers the request. "
            + "Use \"none\" when the request is not asking for a report, for example a question "
            + "about medicines or doses, a task such as printing a label, or small talk.");
        sb.AppendLine("filters: only the filters the request actually mentions, using that report's "
            + "filter names. Use {} when it mentions none.");
        sb.AppendLine("period: " + string.Join(", ", Periods.Named.Take(Periods.Named.Count - 1))
            + ", custom or none. Use custom for a named month or explicit dates and add \"from\" "
            + "and \"to\" as YYYY-MM-DD. Use none when the request gives no period.");
        sb.AppendLine();
        sb.AppendLine("Reports (key | name | group | filters):");

        foreach (var preset in _context.Presets)
        {
            var name = string.IsNullOrWhiteSpace(preset.Description)
                ? preset.DisplayName
                : $"{preset.DisplayName}: {preset.Description}";
            var filters = _askable[preset.Key].Select(a => a.Slicer.Key).ToList();

            var listed = filters.Count == 0 ? "-" : string.Join(", ", filters);
            sb.AppendLine(CultureInfo.InvariantCulture, $"{preset.Key} | {name} | {preset.Group} | {listed}");
        }

        var slicers = _askable.Values.SelectMany(a => a)
            .GroupBy(a => a.Slicer.Key, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        if (slicers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Filter values:");
            foreach (var slicer in slicers)
                sb.AppendLine(CultureInfo.InvariantCulture, $"{slicer.Slicer.Key} ({slicer.Slicer.Label.ToLowerInvariant()}): {slicer.Describe()}");
        }

        sb.AppendLine();
        sb.AppendLine("Examples:");
        foreach (var (request, answer) in Examples())
            sb.AppendLine(CultureInfo.InvariantCulture, $"Request: {request}\nAnswer: {answer}");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Worked examples, each only where its report is offered and the
    /// values it uses exist, so an example never names a department this
    /// pharmacy does not have.
    /// </summary>
    private IEnumerable<(string Request, string Answer)> Examples()
    {
        if (Choice("pos.transactions.all", "tender", "Account") is string account)
            yield return ("Account sales from last month",
                Answer("pos.transactions.all", "last_month", ("tender", Str(account))));

        if (FirstChoice("inv.stock.on-hand", "department") is string department)
            yield return ($"Stock on hand in the {department} department",
                Answer("inv.stock.on-hand", "none", ("department", Str(department))));

        if (_askable.ContainsKey("inv.price.changes"))
            yield return ("Price changes this year", Answer("inv.price.changes", "this_year"));

        if (_askable.ContainsKey("pos.sales.daily"))
        {
            var year = _context.Today.Year;
            yield return ($"Sales for each day of March {year}",
                Answer("pos.sales.daily", "custom", from: $"{year}-03-01", to: $"{year}-03-31"));
        }

        if (Offers("inv.stock.expiring", "withinDays"))
            yield return ("Batches expiring in the next 2 weeks",
                Answer("inv.stock.expiring", "none", ("withinDays", "14")));

        if (FirstChoice("dsp.scripts.reversed", "pharmacist") is string pharmacist)
            yield return ($"Reversed scripts by {pharmacist} this month",
                Answer("dsp.scripts.reversed", "this_month", ("pharmacist", Str(pharmacist))));

        yield return ("What can I take for a headache?", Answer("none", "none"));
    }

    private bool Offers(string preset, string slicer) =>
        _askable.TryGetValue(preset, out var a) && a.Any(s => s.Slicer.Key == slicer);

    private string? Choice(string preset, string slicer, string said) =>
        _askable.TryGetValue(preset, out var a)
            ? a.FirstOrDefault(s => s.Slicer.Key == slicer)?.Choices
               .Select(c => c.Said)
               .FirstOrDefault(c => string.Equals(c, said, StringComparison.OrdinalIgnoreCase))
            : null;

    private string? FirstChoice(string preset, string slicer) =>
        _askable.TryGetValue(preset, out var a)
            ? a.FirstOrDefault(s => s.Slicer.Key == slicer)?.Choices.FirstOrDefault()?.Said
            : null;

    private static string Answer(string report, string period,
        params (string Key, string JsonValue)[] filters) =>
        Answer(report, period, null, null, filters);

    private static string Answer(string report, string period, string? from, string? to,
        params (string Key, string JsonValue)[] filters)
    {
        var filterText = string.Join(", ", filters.Select(f => $"{Str(f.Key)}: {f.JsonValue}"));
        var dates = period == Periods.Custom ? $", \"from\": {Str(from!)}, \"to\": {Str(to!)}" : "";
        return $"{{\"report\": {Str(report)}, \"filters\": {{{filterText}}}, \"period\": {Str(period)}{dates}}}";
    }

    /// A JSON string, quotes included, exactly as the grammar spells it.
    private static string Str(string text) => JsonSerializer.Serialize(text, Json);

    /// <summary>
    /// One alternative per report, each admitting only that report's own
    /// filters and their values, so whatever comes back is something
    /// PharmaX could run. Each filter appears at most once and in the
    /// catalogue's order: s{i}x{j} is filter j followed optionally by any
    /// later one. Rule names hold only letters, digits and dashes, which
    /// is all llama.cpp accepts.
    /// </summary>
    private string BuildGrammar(bool allowCustom)
    {
        var rules = new StringBuilder();
        var reports = new List<string>();

        for (var i = 0; i < _context.Presets.Count; i++)
        {
            var preset = _context.Presets[i];
            var askable = _askable[preset.Key];

            reports.Add($"r{i}");
            rules.AppendLine(CultureInfo.InvariantCulture, $"r{i} ::= {Gbnf.Literal(Str(preset.Key))} \",\" ws {Gbnf.Literal("\"filters\":")} ws f{i}");

            if (askable.Count == 0)
            {
                rules.AppendLine(CultureInfo.InvariantCulture, $"f{i} ::= \"{{}}\"");
                continue;
            }

            rules.AppendLine($"f{i} ::= \"{{\" ( "
                + string.Join(" | ", Enumerable.Range(0, askable.Count).Select(j => $"s{i}x{j}"))
                + " )? \"}\"");

            for (var j = 0; j < askable.Count; j++)
            {
                var pair = $"{Gbnf.Literal(Str(askable[j].Slicer.Key) + ":")} ws {askable[j].GrammarValue()}";
                var later = Enumerable.Range(j + 1, askable.Count - j - 1).Select(k => $"s{i}x{k}").ToList();

                rules.AppendLine(later.Count == 0
                    ? $"s{i}x{j} ::= {pair}"
                    : $"s{i}x{j} ::= {pair} ( \",\" ws ( {string.Join(" | ", later)} ) )?");
            }
        }

        reports.Add("rnone");
        rules.AppendLine(CultureInfo.InvariantCulture, $"rnone ::= {Gbnf.Literal(Str("none"))} \",\" ws {Gbnf.Literal("\"filters\":")} ws \"{{}}\"");

        var periods = Periods.Named.Select(p => Gbnf.Literal(Str(p))).ToList();
        if (allowCustom)
            periods.Add($"{Gbnf.Literal(Str(Periods.Custom))} \",\" ws {Gbnf.Literal("\"from\":")} ws date \",\" ws {Gbnf.Literal("\"to\":")} ws date");

        var head = new StringBuilder();
        head.AppendLine(CultureInfo.InvariantCulture, $"root ::= \"{{\" ws {Gbnf.Literal("\"report\":")} ws ( {string.Join(" | ", reports)} ) \",\" ws {Gbnf.Literal("\"period\":")} ws period ws \"}}\"");
        head.AppendLine(CultureInfo.InvariantCulture, $"period ::= {string.Join(" | ", periods)}");
        head.AppendLine("date ::= \"\\\"\" \"20\" [0-9] [0-9] \"-\" [01] [0-9] \"-\" [0-3] [0-9] \"\\\"\"");
        head.AppendLine("num ::= [1-9] [0-9]{0,3}");
        head.AppendLine("bool ::= \"\\\"true\\\"\" | \"\\\"false\\\"\"");
        head.AppendLine("ws ::= \" \"?");
        return head.ToString() + rules;
    }

    /// <summary>
    /// The filters the model may set on a report. A till session is left
    /// to the operator - fifty "Till 1, 20/09/2026 08:14" entries are not
    /// something to ask for in words - and so is free text, which a model
    /// fills with whatever it likes.
    /// </summary>
    private List<Askable> AskableFor(string presetKey)
    {
        if (!_context.Slicers.TryGetValue(presetKey, out var slicers))
            return new List<Askable>();

        var askable = new List<Askable>();

        foreach (var slicer in slicers)
        {
            if (slicer.Key == "session") continue;

            if (slicer.Kind is "flag" or "number")
            {
                askable.Add(new Askable(slicer, Array.Empty<Spoken>()));
                continue;
            }

            if (slicer.Options.Count == 0 || slicer.Options.Count > MaxChoices) continue;

            var choices = slicer.Options
                .Where(o => !string.IsNullOrWhiteSpace(o.Value) && !string.IsNullOrWhiteSpace(o.Label))
                .Select(o => new Spoken(SaidAs(o), o.Value, o.Label))
                .DistinctBy(c => c.Said, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (choices.Count > 0)
                askable.Add(new Askable(slicer, choices));
        }

        return askable;
    }

    /// <summary>
    /// What the model writes for an option: the value where it reads as a
    /// word ("MedicalAid", "B"), the label where the value is an id that
    /// nobody would say - a department, a member of staff.
    /// </summary>
    private static string SaidAs(SlicerOptionDto option) =>
        Guid.TryParse(option.Value, out _) ? option.Label.Trim() : option.Value;

    private static string Clean(string request)
    {
        // The request is read with the chat's control tokens switched on,
        // so one typed into it would end the turn early.
        var text = (request ?? "").Replace("<|", "< |", StringComparison.Ordinal).Trim();
        return text.Length > MaxRequestLength ? text[..MaxRequestLength] : text;
    }

    [GeneratedRegex(@"\d|\b(jan(uary)?|feb(ruary)?|mar(ch)?|apr(il)?|may|june?|july?|aug(ust)?|sep(t|tember)?|oct(ober)?|nov(ember)?|dec(ember)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DateWords();

    /// <summary>An option as the model writes it, and what the screen sends for it.</summary>
    private sealed record Spoken(string Said, string Value, string Label);

    private sealed record Askable(SlicerDto Slicer, IReadOnlyList<Spoken> Choices)
    {
        public string Describe() => Slicer.Kind switch
        {
            "flag" => "true or false",
            "number" => "a number",
            _ => Listed()
        };

        private string Listed()
        {
            var shown = Choices.Take(ListedChoices).Select(c =>
            {
                if (Guid.TryParse(c.Value, out _)) return c.Said;

                // "A - standard rated" reads as "A = standard rated".
                var label = c.Label.StartsWith(c.Said + " - ", StringComparison.Ordinal)
                    ? c.Label[(c.Said.Length + 3)..]
                    : c.Label;

                return string.Equals(c.Said, label, StringComparison.OrdinalIgnoreCase)
                    ? c.Said
                    : $"{c.Said} = {label.ToLowerInvariant()}";
            });

            var more = Choices.Count > ListedChoices
                ? $", and {Choices.Count - ListedChoices} more"
                : "";

            return string.Join(", ", shown) + more;
        }

        public string GrammarValue() => Slicer.Kind switch
        {
            "flag" => "bool",
            "number" => "num",
            _ => "( " + string.Join(" | ", Choices.Select(c => Gbnf.Literal(Str(c.Said)))) + " )"
        };

        /// <summary>The value to send for what the model wrote, or null to leave the filter alone.</summary>
        public string? ValueOf(string? said)
        {
            if (said is null) return null;

            return Slicer.Kind switch
            {
                "flag" => said == "true" ? "true" : null,
                "number" => int.TryParse(said, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
                            && n > 0 ? n.ToString(CultureInfo.InvariantCulture) : null,
                _ => Choices.FirstOrDefault(c =>
                         string.Equals(c.Said, said, StringComparison.OrdinalIgnoreCase))?.Value
            };
        }
    }
}
