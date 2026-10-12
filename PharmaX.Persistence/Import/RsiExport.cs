using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace PharmaX.Persistence.Import;

/// <summary>
/// The folder the RSI export scripts write: four Excel workbooks, one
/// per area, and a CSV copy of every sheet in them.
/// </summary>
/// <remarks>
/// The workbooks are read first, because they are what verify_exports
/// checks against the database's own totals. They are streamed a row at a
/// time rather than opened whole: the sales workbook is a million cells,
/// and loading it in one piece takes more memory than a pharmacy PC can
/// spare. A sheet no workbook holds is read from its CSV copy instead.
/// Where an export was run more than once, the newest workbook wins.
/// </remarks>
internal sealed partial class RsiExport
{
    private readonly string _folder;
    private readonly Dictionary<string, (string File, string Part)> _sheets =
        new(StringComparer.OrdinalIgnoreCase);

    public RsiExport(string folder)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"There is no folder {folder}.");

        _folder = folder;

        // Products_Stock_2026-10-05.xlsx and _2026-10-06.xlsx: the date in
        // the name orders them, so the last one read for a sheet is the
        // newest.
        foreach (var file in Directory.GetFiles(folder, "*.xlsx")
                     .Where(f => !Path.GetFileName(f).StartsWith('~'))
                     .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            try
            {
                foreach (var (sheet, part) in SheetsOf(file))
                    _sheets[sheet] = (file, part);
            }
            catch (InvalidDataException)
            {
                // Not a workbook after all - a half-written export.
            }
        }
    }

    /// Where a sheet will be read from, for the report.
    public string SourceOf(string sheet) =>
        _sheets.TryGetValue(sheet, out var s)
            ? $"{Path.GetFileName(s.File)} [{sheet}]"
            : File.Exists(CsvPath(sheet)) ? Path.GetFileName(CsvPath(sheet)) : "missing";

    public bool Has(string sheet) =>
        _sheets.ContainsKey(sheet) || File.Exists(CsvPath(sheet));

    public IReadOnlyList<RsiRow> Rows(string sheet)
    {
        if (_sheets.TryGetValue(sheet, out var s)) return ReadSheet(s.File, s.Part);
        if (File.Exists(CsvPath(sheet))) return ReadCsv(CsvPath(sheet));

        throw new FileNotFoundException(
            $"The export has no {sheet} sheet: neither a workbook holding it "
            + $"nor {sheet}.csv is in {_folder}.");
    }

    private string CsvPath(string sheet) => Path.Combine(_folder, sheet + ".csv");

    // ---- Workbooks ---------------------------------------------------------

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rels = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static IEnumerable<(string Sheet, string Part)> SheetsOf(string file)
    {
        using var zip = ZipFile.OpenRead(file);

        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (rels is null) yield break;

        using (var reader = XmlReader.Create(rels.Open()))
            while (reader.ReadToFollowing("Relationship"))
                targets[reader.GetAttribute("Id") ?? ""] = reader.GetAttribute("Target") ?? "";

        var workbook = zip.GetEntry("xl/workbook.xml");
        if (workbook is null) yield break;

        var found = new List<(string, string)>();

        using (var reader = XmlReader.Create(workbook.Open()))
            while (reader.ReadToFollowing("sheet", Main))
            {
                var name = reader.GetAttribute("name");
                var id = reader.GetAttribute("id", Rels);

                if (name is not null && id is not null
                    && targets.TryGetValue(id, out var target))
                    found.Add((name, "xl/" + target.TrimStart('/').Replace("xl/", "", StringComparison.Ordinal)));
            }

        foreach (var sheet in found) yield return sheet;
    }

    private static List<RsiRow> ReadSheet(string file, string part)
    {
        using var zip = ZipFile.OpenRead(file);
        var shared = SharedStrings(zip);
        var entry = zip.GetEntry(part)
                    ?? throw new InvalidDataException($"{file} has no {part}.");

        var rows = new List<RsiRow>();
        string[]? header = null;

        using var reader = XmlReader.Create(entry.Open());

        while (reader.ReadToFollowing("row", Main))
        {
            var cells = new SortedDictionary<int, string>();

            using (var row = reader.ReadSubtree())
            {
                while (row.ReadToFollowing("c", Main))
                {
                    var column = ColumnIndex(row.GetAttribute("r"));
                    var type = row.GetAttribute("t");
                    string? value = null;

                    using (var cell = row.ReadSubtree())
                    {
                        while (cell.Read())
                        {
                            if (cell.NodeType != XmlNodeType.Element) continue;

                            if (cell.LocalName is "v" or "t")
                            {
                                value = cell.ReadElementContentAsString();
                                break;
                            }
                        }
                    }

                    if (type == "s" && int.TryParse(value, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var index)
                        && index < shared.Count)
                        value = shared[index];

                    if (column >= 0) cells[column] = value ?? "";
                }
            }

            var width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
            var values = new string[width];
            foreach (var (column, value) in cells) values[column] = value;

            if (header is null) header = values.Select(v => (v ?? "").Trim()).ToArray();
            else rows.Add(new RsiRow(header, values));
        }

        return rows;
    }

    private static List<string> SharedStrings(ZipArchive zip)
    {
        var strings = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return strings;

        using var reader = XmlReader.Create(entry.Open());

        while (reader.ReadToFollowing("si", Main))
        {
            var text = new StringBuilder();

            using (var si = reader.ReadSubtree())
                while (si.ReadToFollowing("t", Main))
                    text.Append(si.ReadElementContentAsString());

            strings.Add(text.ToString());
        }

        return strings;
    }

    /// "AB12" is column 27, counting from zero.
    private static int ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference)) return -1;

        var index = 0;
        foreach (var ch in reference)
        {
            if (!char.IsLetter(ch)) break;
            index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        }

        return index - 1;
    }

    // ---- CSV ---------------------------------------------------------------

    /// <remarks>
    /// The quoting PowerShell's Export-Csv writes: every field in double
    /// quotes, a quote inside one doubled. A quoted field may run over a
    /// line break, so the file is read as one stream rather than by line.
    /// </remarks>
    private static List<RsiRow> ReadCsv(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (quoted)
            {
                if (ch != '"') { field.Append(ch); continue; }
                if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; continue; }
                quoted = false;
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { fields.Add(field.ToString()); field.Clear(); }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                fields.Add(field.ToString());
                field.Clear();
                if (fields.Count > 1 || fields[0].Length > 0) records.Add(fields.ToArray());
                fields.Clear();
            }
            else if (ch != '﻿') field.Append(ch);
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields.ToArray());
        }

        if (records.Count == 0) return new List<RsiRow>();

        var header = records[0].Select(h => h.Trim()).ToArray();
        return records.Skip(1).Select(r => new RsiRow(header, r)).ToList();
    }
}

/// <summary>One row of an export sheet, read by column name.</summary>
internal sealed partial class RsiRow
{
    private static readonly string[] DateFormats =
    {
        "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm:ss", "dd/MM/yyyy", "d/M/yyyy",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"
    };

    private readonly Dictionary<string, string> _values;

    public RsiRow(string[] header, string?[] values)
    {
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Length; i++)
            if (header[i].Length > 0)
                _values[header[i]] = i < values.Length ? (values[i] ?? "").Trim() : "";
    }

    public IReadOnlyDictionary<string, string> Values => _values;

    public string Text(string column) => _values.GetValueOrDefault(column, "");

    /// The text, or null where it is blank or one of the old system's
    /// placeholders: ".", "-", "_".
    public string? Clean(string column)
    {
        var value = Text(column);
        return value.Length == 0 || Junk().IsMatch(value) ? null : value;
    }

    public decimal Number(string column) =>
        decimal.TryParse(Text(column), NumberStyles.Float, CultureInfo.InvariantCulture,
                         out var value)
            ? value
            : 0m;

    public bool Flag(string column) =>
        string.Equals(Text(column), "True", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A date as the workbook holds it - an Excel serial number - or as
    /// the CSV writes it, day first.
    /// </summary>
    public DateTime? Date(string column)
    {
        var value = Text(column);
        if (value.Length == 0) return null;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                            out var serial) && serial is > 0 and < 2958466)
            return DateTime.FromOADate(serial);

        return DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
                                      DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// A date with a time of day held in a column of its own.
    public DateTime When(string dateColumn, string timeColumn)
    {
        var day = Date(dateColumn)?.Date ?? DateTime.MinValue;

        return TimeSpan.TryParse(Text(timeColumn), CultureInfo.InvariantCulture, out var time)
            ? day + time
            : day;
    }

    [GeneratedRegex(@"^[\.\-_\s]+$")]
    private static partial Regex Junk();
}
