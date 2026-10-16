using System.Globalization;
using System.Text;
using PharmaX.Application.Import;

namespace PharmaX.Persistence.Import;

/// <summary>
/// What an import did, written for the person who has to sign it off:
/// what was read and from where, what came in, whether the totals agree
/// with the files' own, and what somebody should look at before the
/// pharmacy trades.
/// </summary>
/// <remarks>
/// Lists carry codes and file numbers, never patient names: the report
/// is saved to the log folder, which every user of the PC can read.
/// </remarks>
internal sealed class ImportReport
{
    private const int Shown = 40;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly bool _dryRun;
    private readonly string _from;
    private readonly List<string> _sources = new();
    private readonly List<string> _notes = new();
    private readonly List<string> _counts = new();
    private readonly List<string> _checks = new();
    private readonly List<string> _reviews = new();
    private bool _failedCheck;

    public ImportReport(bool dryRun, string from)
    {
        _dryRun = dryRun;
        _from = from;
    }

    public void Source(string what, string from, int? rows) =>
        _sources.Add(rows is int n
            ? string.Format(Invariant, "  {0,-34} {1,8:N0}  {2}", what, n, from)
            : string.Format(Invariant, "  {0,-34} {1,8}  {2}", what, "", from));

    public void Note(string text) => _notes.Add("  " + text);

    public void Count(string what, int count) =>
        _counts.Add(string.Format(Invariant, "  {0,-72}{1,10:N0}", what, count));

    public void Figure(string what, decimal amount) =>
        _counts.Add(string.Format(Invariant, "  {0,-72}{1,13:N2}", what, amount));

    /// <summary>
    /// A total the files recorded, or that the import read from them,
    /// against the same total counted in the database afterwards.
    /// </summary>
    public void Check(string what, decimal expected, decimal actual,
                      decimal tolerance = 0.005m)
    {
        var pass = Math.Abs(expected - actual) <= tolerance;
        if (!pass) _failedCheck = true;

        _checks.Add(string.Format(Invariant, "  {0,-4} {1,-50}{2,16:N2}{3,16:N2}",
            pass ? "PASS" : "FAIL", what, expected, actual));
    }

    public void Review(string title, IEnumerable<string> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;

        _reviews.Add($"{title} - {list.Count:N0}");
        _reviews.AddRange(list.Take(Shown).Select(i => "    " + i));

        if (list.Count > Shown)
            _reviews.Add($"    ...and {list.Count - Shown:N0} more");

        _reviews.Add("");
    }

    public ImportResult Fail(string reason) =>
        new(_dryRun, false, Render() + Environment.NewLine + "NOT IMPORTED. " + reason);

    public ImportResult Done() =>
        new(_dryRun, !_failedCheck, Render() + Environment.NewLine + (_failedCheck
            ? "Totals that FAIL above do not agree with the files. Do not trade on this "
              + "database until each one is explained."
            : _dryRun
                ? "Every total agrees. Nothing was kept: run the import for real."
                : "Every total agrees with the files."));

    private string Render()
    {
        var text = new StringBuilder();

        text.AppendLine(_dryRun
            ? "DATA IMPORT - DRY RUN (checked, then rolled back; nothing was kept)"
            : "DATA IMPORT");
        text.AppendLine(string.Create(Invariant, $"{DateTime.Now:dd/MM/yyyy HH:mm} from {_from}"));
        text.AppendLine();

        Section(text, "Read", _sources);
        Section(text, "Notes", _notes);
        Section(text, "Brought in", _counts);

        if (_checks.Count > 0)
        {
            text.AppendLine("Checked against the files' own totals");
            text.AppendLine(string.Format(Invariant, "  {0,-4} {1,-50}{2,16}{3,16}",
                "", "", "Files", "PharmaX"));
            foreach (var line in _checks) text.AppendLine(line);
            text.AppendLine();
        }

        Section(text, "For the pharmacist to look at", _reviews);
        return text.ToString();
    }

    private static void Section(StringBuilder text, string title, List<string> lines)
    {
        if (lines.Count == 0) return;

        text.AppendLine(title);
        foreach (var line in lines) text.AppendLine(line);
        text.AppendLine();
    }
}
