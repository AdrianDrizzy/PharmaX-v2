using System.Globalization;
using System.Text;
using PharmaX.Application.Legacy;

namespace PharmaX.Persistence.Import;

/// <summary>
/// What an import did, written for the person who has to sign it off:
/// what came in, whether the totals agree with the export's own, and
/// what somebody should look at before the pharmacy trades.
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
    private readonly string _folder;
    private readonly List<string> _sources = new();
    private readonly List<string> _counts = new();
    private readonly List<string> _checks = new();
    private readonly List<string> _reviews = new();
    private bool _failedCheck;

    public ImportReport(bool dryRun, string folder)
    {
        _dryRun = dryRun;
        _folder = folder;
    }

    public void Source(string sheet, string from) => _sources.Add($"  {sheet,-22} {from}");

    public void Count(string what, int count) =>
        _counts.Add(string.Format(Invariant, "  {0,-72}{1,10:N0}", what, count));

    public void Figure(string what, decimal amount) =>
        _counts.Add(string.Format(Invariant, "  {0,-72}{1,13:N2}", what, amount));

    /// <summary>
    /// A total the export recorded against the same total counted here.
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

        _reviews.Add($"{title} - {list.Count}");
        _reviews.AddRange(list.Take(Shown).Select(i => "    " + i));

        if (list.Count > Shown)
            _reviews.Add($"    ...and {list.Count - Shown} more");

        _reviews.Add("");
    }

    public LegacyImportReport Fail(string reason) =>
        new(_dryRun, false, Render() + Environment.NewLine + "NOT IMPORTED. " + reason);

    public LegacyImportReport Done() =>
        new(_dryRun, !_failedCheck, Render() + Environment.NewLine + (_failedCheck
            ? "Totals that FAIL above do not agree with the export. Do not trade on this "
              + "database until each one is explained."
            : _dryRun
                ? "Every total agrees. Nothing was kept: run the import for real."
                : "Every total agrees with the export."));

    private string Render()
    {
        var text = new StringBuilder();

        text.AppendLine(_dryRun
            ? "RSI IMPORT - DRY RUN (checked, then rolled back; nothing was kept)"
            : "RSI IMPORT");
        text.AppendLine(string.Create(Invariant, $"{DateTime.Now:dd/MM/yyyy HH:mm} from {_folder}"));
        text.AppendLine();

        Section(text, "Read from", _sources);
        Section(text, "Brought in", _counts);

        if (_checks.Count > 0)
        {
            text.AppendLine("Checked against the export's own totals");
            text.AppendLine(string.Format(Invariant, "  {0,-4} {1,-50}{2,16}{3,16}",
                "", "", "Export", "PharmaX"));
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
