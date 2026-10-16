using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using PharmaX.Application.Import;

namespace PharmaX.Persistence.Import;

/// <summary>
/// Every table in the files an import is given, whatever wrote them:
/// delimited text (comma, semicolon, tab or pipe), Excel workbooks and
/// JSON. A folder stands for every file in it.
/// </summary>
/// <remarks>
/// Only the header of each table is read up front - enough to tell what
/// it holds. Its rows are read when the import asks for them, a workbook
/// streamed a row at a time: a year of till sales is a million cells, and
/// loading every file whole takes more memory than a pharmacy PC can
/// spare.
///
/// A table found twice - saved as CSV and again inside a workbook - is
/// read once, from the delimited text where there is a copy: that holds
/// codes and dates exactly as written, where a workbook may have made a
/// barcode a number or read 06/10 as June. Otherwise the newest wins.
/// </remarks>
internal sealed partial class ImportSource
{
    private ImportSource(IReadOnlyList<SourceTable> tables, IReadOnlyList<string> unread)
    {
        Tables = tables;
        Unread = unread;
    }

    public IReadOnlyList<SourceTable> Tables { get; }

    /// Files that were not read, and why.
    public IReadOnlyList<string> Unread { get; }

    public static ImportSource Read(IEnumerable<string> paths)
    {
        var files = new List<string>();
        var unread = new List<string>();

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                files.AddRange(Directory.GetFiles(path)
                    .Where(f => !Path.GetFileName(f).StartsWith('~'))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            else if (File.Exists(path)) files.Add(path);
            else unread.Add($"{path}: not found");
        }

        var found = new List<SourceTable>();

        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();

            if (!ImportFiles.Extensions.Contains(extension))
            {
                unread.Add($"{Path.GetFileName(file)}: not a data file");
                continue;
            }

            try
            {
                found.AddRange(extension switch
                {
                    ".xlsx" or ".xlsm" => Workbook(file),
                    ".json" => Json(file),
                    _ => new[] { Delimited(file) }
                });
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException
                                           or JsonException or XmlException)
            {
                unread.Add($"{Path.GetFileName(file)}: could not be read ({ex.Message})");
            }
        }

        // Copies of one export: the same table in the same folder - as a
        // CSV, inside today's workbook, inside yesterday's with a column
        // fewer. The same name from another folder - a second branch's
        // patient list - is a table of its own.
        var tables = new List<SourceTable>();

        foreach (var copies in found.Where(t => t.Columns.Count > 0)
                     .GroupBy(t => Path.GetDirectoryName(t.File) + "|" + t.Name,
                              StringComparer.OrdinalIgnoreCase))
        {
            var kept = copies.OrderBy(t => t.IsWorkbook).ThenByDescending(t => t.Written).First();
            tables.Add(kept);

            unread.AddRange(copies.Where(t => t != kept)
                .Select(t => $"{t.Source}: the same table as {kept.Source}, read once from there"));
        }

        return new ImportSource(tables.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList(), unread);
    }

    /// "Products_Stock_2026-10-06" and "Products_Stock" are one table.
    private static string TableName(string name) =>
        DatedSuffix().Replace(name.Trim(), "");

    [GeneratedRegex(@"[\s_\-]*(\(\d+\)|\d{4}-?\d{2}-?\d{2}([\s_T-]?\d{4,6})?)$")]
    private static partial Regex DatedSuffix();

    // ---- Delimited text ----------------------------------------------------

    /// <remarks>
    /// A file separated by anything but commas is taken to write its
    /// decimals with a comma, as Excel does on a South African or European
    /// PC: "12,50" is twelve rand fifty, not one thousand two hundred.
    /// </remarks>
    private static SourceTable Delimited(string file)
    {
        var written = File.GetLastWriteTimeUtc(file);
        var encoding = EncodingOf(file);
        var delimiter = Delimiter(file, encoding);
        using var first = Records(file, delimiter, encoding).GetEnumerator();
        var header = first.MoveNext() ? first.Current : Array.Empty<string?>();

        return new SourceTable(TableName(Path.GetFileNameWithoutExtension(file)),
            file, isWorkbook: false, written, header,
            () => Records(file, delimiter, encoding).Skip(1), decimalComma: delimiter != ',');
    }

    /// <summary>
    /// UTF-8 where the file is valid UTF-8, otherwise the Western code page
    /// Excel saves "CSV (Comma delimited)" in - so "Zoë" stays Zoë. A byte
    /// order mark, where there is one, wins over both.
    /// </summary>
    private static Encoding EncodingOf(string file)
    {
        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(File.ReadAllBytes(file));
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1;
        }
    }

    /// The separator the header line uses most: comma, semicolon, tab or pipe.
    private static char Delimiter(string file, Encoding encoding)
    {
        using var reader = new StreamReader(file, encoding, detectEncodingFromByteOrderMarks: true);
        var line = reader.ReadLine() ?? "";

        return new[] { ',', ';', '\t', '|' }
            .OrderByDescending(d => line.Count(c => c == d))
            .First();
    }

    /// <remarks>
    /// Quoted fields may hold the separator, a doubled quote, or a line
    /// break, so the file is read as one stream rather than by line.
    /// </remarks>
    private static IEnumerable<string?[]> Records(string file, char delimiter, Encoding encoding)
    {
        using var reader = new StreamReader(file, encoding, detectEncodingFromByteOrderMarks: true);
        var fields = new List<string?>();
        var field = new StringBuilder();
        var quoted = false;
        int read;

        while ((read = reader.Read()) >= 0)
        {
            var ch = (char)read;

            if (quoted)
            {
                if (ch != '"') { field.Append(ch); continue; }
                if (reader.Peek() == '"') { field.Append('"'); reader.Read(); continue; }
                quoted = false;
            }
            else if (ch == '"' && field.Length == 0) quoted = true;
            else if (ch == delimiter) { fields.Add(field.ToString()); field.Clear(); }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && reader.Peek() == '\n') reader.Read();
                fields.Add(field.ToString());
                field.Clear();
                if (fields.Count > 1 || fields[0]!.Length > 0) yield return fields.ToArray();
                fields.Clear();
            }
            else if (ch != '﻿') field.Append(ch);
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields.ToArray();
        }
    }

    // ---- JSON --------------------------------------------------------------

    /// <summary>
    /// An array of objects is one table; an object whose properties are
    /// arrays of objects is a table per property.
    /// </summary>
    private static IEnumerable<SourceTable> Json(string file)
    {
        var written = File.GetLastWriteTimeUtc(file);
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var root = document.RootElement;
        var name = TableName(Path.GetFileNameWithoutExtension(file));

        var arrays = root.ValueKind == JsonValueKind.Array
            ? new[] { (Name: name, Array: root.Clone()) }
            : root.ValueKind == JsonValueKind.Object
                ? root.EnumerateObject()
                      .Where(p => p.Value.ValueKind == JsonValueKind.Array)
                      .Select(p => (Name: TableName(p.Name), Array: p.Value.Clone()))
                      .ToArray()
                : Array.Empty<(string, JsonElement)>();

        foreach (var (table, array) in arrays)
        {
            var objects = array.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object).ToList();
            var header = objects.SelectMany(o => o.EnumerateObject().Select(p => p.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var rows = objects.Select(o => header.Select(h =>
                    o.TryGetProperty(h, out var v) ? JsonText(v) : null).ToArray())
                .ToList();

            yield return new SourceTable(table, file, isWorkbook: false, written,
                header, () => rows);
        }
    }

    private static string? JsonText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        _ => value.GetRawText()
    };

    // ---- Workbooks ---------------------------------------------------------

    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rels = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static IEnumerable<SourceTable> Workbook(string file)
    {
        var written = File.GetLastWriteTimeUtc(file);

        foreach (var (sheet, part) in SheetsOf(file))
        {
            using var first = SheetRecords(file, part).GetEnumerator();
            var header = first.MoveNext() ? first.Current : Array.Empty<string?>();

            yield return new SourceTable(TableName(sheet), file, isWorkbook: true,
                written, header, () => SheetRecords(file, part).Skip(1));
        }
    }

    private static List<(string Sheet, string Part)> SheetsOf(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        var found = new List<(string, string)>();

        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        var workbook = zip.GetEntry("xl/workbook.xml");
        if (rels is null || workbook is null) return found;

        using (var reader = XmlReader.Create(rels.Open()))
            while (reader.ReadToFollowing("Relationship"))
                targets[reader.GetAttribute("Id") ?? ""] = reader.GetAttribute("Target") ?? "";

        using (var reader = XmlReader.Create(workbook.Open()))
            while (reader.ReadToFollowing("sheet", Main))
            {
                var name = reader.GetAttribute("name");
                var id = reader.GetAttribute("id", Rels);

                if (name is not null && id is not null && targets.TryGetValue(id, out var target))
                    found.Add((name, "xl/" + target.TrimStart('/')
                        .Replace("xl/", "", StringComparison.Ordinal)));
            }

        return found;
    }

    private static IEnumerable<string?[]> SheetRecords(string file, string part)
    {
        using var zip = ZipFile.OpenRead(file);
        var shared = SharedStrings(zip);
        var entry = zip.GetEntry(part) ?? throw new InvalidDataException($"{file} has no {part}.");

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
                        while (cell.Read())
                        {
                            if (cell.NodeType != XmlNodeType.Element || cell.LocalName is not ("v" or "t"))
                                continue;

                            value = cell.ReadElementContentAsString();
                            break;
                        }

                    if (type == "s" && int.TryParse(value, NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out var index) && index < shared.Count)
                        value = shared[index];
                    else if (type == "b")
                        value = value == "1" ? "True" : "False";

                    if (column >= 0) cells[column] = value ?? "";
                }
            }

            var values = new string?[cells.Count == 0 ? 0 : cells.Keys.Max() + 1];
            foreach (var (column, value) in cells) values[column] = value;
            yield return values;
        }
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
}

/// <summary>
/// One table: a CSV file, a sheet of a workbook, an array in a JSON file.
/// </summary>
internal sealed class SourceTable
{
    private readonly Func<IEnumerable<string?[]>> _records;
    private readonly Dictionary<string, int> _columns = new(StringComparer.Ordinal);
    private IReadOnlyList<SourceRow>? _rows;

    public SourceTable(string name, string file, bool isWorkbook, DateTime written,
                       IReadOnlyList<string?> header, Func<IEnumerable<string?[]>> records,
                       bool decimalComma = false)
    {
        Name = name;
        File = file;
        IsWorkbook = isWorkbook;
        Written = written;
        DecimalComma = decimalComma;
        Columns = header.Select(h => (h ?? "").Trim()).ToList();
        _records = records;

        for (var i = 0; i < Columns.Count; i++)
            _columns.TryAdd(Key(Columns[i]), i);
    }

    public string Name { get; }
    public string File { get; }
    public bool IsWorkbook { get; }
    public DateTime Written { get; }
    public IReadOnlyList<string> Columns { get; }

    /// "12,50" is twelve and a half here, not twelve hundred and fifty.
    public bool DecimalComma { get; }

    public string Source => IsWorkbook
        ? $"{Path.GetFileName(File)} [{Name}]"
        : Path.GetFileName(File);

    /// <summary>
    /// The column holding a field, by the first of its names the table
    /// uses, or -1.
    /// </summary>
    public int IndexOf(IEnumerable<string> names)
    {
        foreach (var name in names)
            if (_columns.TryGetValue(name, out var index)) return index;

        return -1;
    }

    public bool Has(IEnumerable<string> names) => IndexOf(names) >= 0;

    public IReadOnlyList<SourceRow> Rows => _rows ??= _records()
        .Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v)))
        .Select(r => new SourceRow(this, r))
        .ToList();

    /// "Product Code", "PRODUCT_CODE" and "productCode" are one column.
    public static string Key(string column) =>
        string.Concat(column.Where(char.IsLetterOrDigit)).ToLowerInvariant();
}

/// <summary>One row of a table, read by field.</summary>
internal sealed partial class SourceRow
{
    private static readonly string[] DateFormats =
    {
        "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy", "d/M/yyyy",
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK",
        "yyyy-MM-dd", "yyyy/MM/dd", "dd-MM-yyyy", "dd.MM.yyyy", "d MMM yyyy", "dd-MMM-yyyy"
    };

    private readonly SourceTable _table;
    private readonly string?[] _values;

    public SourceRow(SourceTable table, string?[] values)
    {
        _table = table;
        _values = values;
    }

    private string Raw(int index) =>
        index >= 0 && index < _values.Length ? (_values[index] ?? "").Trim() : "";

    /// The field's value, or "" where the table has no such column.
    public string Text(string[] field) => Raw(_table.IndexOf(field));

    /// <summary>
    /// The value, or null where it is blank or a placeholder the old
    /// system typed for "nothing": ".", "-", "_".
    /// </summary>
    public string? Clean(string[] field)
    {
        var value = Text(field);
        return value.Length == 0 || Junk().IsMatch(value) ? null : value;
    }

    /// The first of several fields that holds something: a mobile, then a landline.
    public string? First(params string[][] fields) =>
        fields.Select(Clean).FirstOrDefault(v => v is not null);

    /// <remarks>
    /// Spaces between thousands are dropped: "1 250,00" is how a South
    /// African till writes it. Where a figure has both a comma and a dot,
    /// the last of them is the decimal point. A lone comma is the decimal
    /// point in a table that writes decimals that way, and a thousands
    /// mark anywhere else.
    /// </remarks>
    public decimal Number(string[] field)
    {
        var text = Text(field).Replace(" ", "", StringComparison.Ordinal)
                              .Replace(" ", "", StringComparison.Ordinal);

        var comma = text.LastIndexOf(',');
        var dot = text.LastIndexOf('.');
        var decimalComma = comma >= 0 && (dot >= 0
            ? comma > dot
            : _table.DecimalComma && text.Count(c => c == ',') == 1);

        text = decimalComma
            ? text.Replace(".", "", StringComparison.Ordinal).Replace(',', '.')
            : text.Replace(",", "", StringComparison.Ordinal);

        return decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowCurrencySymbol,
                   CultureInfo.InvariantCulture, out var value)
            ? value
            : 0m;
    }

    public bool Flag(string[] field) =>
        Text(field).ToUpperInvariant() is "TRUE" or "1" or "YES" or "Y" or "T" or "-1";

    /// <summary>
    /// A date as a workbook holds it - an Excel serial number - or as
    /// text, day first.
    /// </summary>
    public DateTime? Date(string[] field)
    {
        var value = Text(field);
        if (value.Length == 0) return null;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
            return serial is > 0 and < 2958466 ? DateTime.FromOADate(serial) : null;

        return DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces, out var date)
               || DateTime.TryParse(value, CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces, out date)
            ? date
            : null;
    }

    /// <summary>
    /// A date with the time of day in a column of its own, where there is
    /// one. A workbook writes a time as a fraction of a day.
    /// </summary>
    public DateTime? When(string[] dateField, string[] timeField)
    {
        if (Date(dateField) is not DateTime when) return null;

        var time = Text(timeField);

        if (TimeSpan.TryParse(time, CultureInfo.InvariantCulture, out var span) && span < TimeSpan.FromDays(1))
            return when.Date + span;

        if (double.TryParse(time, NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction)
            && fraction is >= 0 and < 1)
            return when.Date + TimeSpan.FromDays(fraction);

        if (DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
            return when.Date + clock.TimeOfDay;

        return when;
    }

    [GeneratedRegex(@"^[\.\-_\s]+$")]
    private static partial Regex Junk();
}
