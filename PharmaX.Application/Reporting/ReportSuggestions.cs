namespace PharmaX.Application.Reporting;

/// <summary>
/// What the reports screen offers the assistant to choose from: the
/// reports this user may run, each one's filters with their choices
/// filled in, and the day "today" means.
/// </summary>
/// <remarks>
/// Built from the same permission-filtered list the screen shows, so the
/// assistant can only ever suggest a report the operator could have
/// picked themselves. The runner demands the permission again on preview.
/// </remarks>
public sealed record ReportSuggestionContext(
    IReadOnlyList<ReportPresetDto> Presets,
    IReadOnlyDictionary<string, IReadOnlyList<SlicerDto>> Slicers,
    DateOnly Today);

/// <summary>
/// The assistant's reading of a request: which report, over which days,
/// with which filters. Only a suggestion - the screen fills itself in and
/// the operator checks it and presses Preview.
/// </summary>
/// <param name="PresetKey">Null where the request was not for a report.</param>
/// <param name="From">Null where the request named no period.</param>
/// <param name="To">Null where the request named no period.</param>
/// <param name="Slicers">
/// Slicer key to the value the screen sends for it: an option's Value,
/// never its label, so a department arrives as the id the report filters on.
/// </param>
/// <param name="Understood">
/// False where the answer could not be read at all, as against read as
/// "that is not a request for a report".
/// </param>
public sealed record ReportSuggestion(
    string? PresetKey,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyDictionary<string, string> Slicers,
    bool Understood = true)
{
    public static ReportSuggestion NotAReport { get; } =
        new(null, null, null, new Dictionary<string, string>());

    public static ReportSuggestion Unclear { get; } =
        new(null, null, null, new Dictionary<string, string>(), Understood: false);
}

/// <summary>
/// Turns a request in plain words into a report on the reports screen,
/// using a small language model that runs on this computer.
/// </summary>
/// <remarks>
/// Nothing leaves the machine. The model is shown the report list and the
/// request and can only answer with one of the listed reports, its
/// filters and a period: it has no way to write free text, so it cannot
/// be talked into answering a question about medicines.
/// </remarks>
public interface IReportSuggester
{
    /// <summary>False where no model is installed here: the screen hides the box.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// True once the model is loaded and has read the report list, after
    /// which a question takes seconds rather than a minute.
    /// </summary>
    bool IsReady { get; }

    /// <summary>Loads the model and reads the report list ahead of the first question.</summary>
    Task WarmUpAsync(ReportSuggestionContext context, CancellationToken ct = default);

    Task<ReportSuggestion> SuggestAsync(string request, ReportSuggestionContext context,
        CancellationToken ct = default);
}
