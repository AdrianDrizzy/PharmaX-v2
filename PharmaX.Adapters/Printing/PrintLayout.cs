using System.Drawing.Printing;
using System.Globalization;
using System.Text.Json;
using PharmaX.Application.Abstractions;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// Paper size and type scale for one kind of printout: labels, receipts
/// or claim forms. Saved per workstation so each till can match the
/// stock loaded in its printer.
/// </summary>
/// <remarks>
/// Padding values are in hundredths of an inch (WinForms margins).
/// -1 on every side means "use the document's built-in default".
/// </remarks>
public sealed record DocumentLayout(
    string PresetId,
    decimal WidthMm,
    decimal HeightMm,
    int FontScalePercent = 100,
    int LineSpacingPercent = 100,
    int PaddingTop = -1,
    int PaddingRight = -1,
    int PaddingBottom = -1,
    int PaddingLeft = -1)
{
    /// Current label stock: 77 mm by 39 mm.
    public static DocumentLayout DefaultLabel { get; } =
        new("label-77x39", 77m, 39m);

    /// Current receipt roll: 75 mm wide; height is dynamic.
    public static DocumentLayout DefaultReceipt { get; } =
        new("receipt-75", 75m, 0m);

    /// Claim form on A4.
    public static DocumentLayout DefaultClaim { get; } =
        new("claim-a4", 210m, 297m);

    public int WidthHundredths => MmToHundredths(WidthMm);

    public int HeightHundredths =>
        HeightMm <= 0m ? 0 : MmToHundredths(HeightMm);

    public float FontScale => Math.Clamp(FontScalePercent, 50, 200) / 100f;

    public float LineSpacingScale =>
        Math.Clamp(LineSpacingPercent, 50, 200) / 100f;

    public bool HasCustomPadding =>
        PaddingTop >= 0 || PaddingRight >= 0
        || PaddingBottom >= 0 || PaddingLeft >= 0;

    public static int MmToHundredths(decimal mm) =>
        (int)Math.Round(mm / 25.4m * 100m, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Margins for the page: each -1 side becomes <paramref name="fallback"/>.
    /// </summary>
    public Margins ToMargins(int fallback) =>
        new(PaddingLeft >= 0 ? PaddingLeft : fallback,
            PaddingRight >= 0 ? PaddingRight : fallback,
            PaddingTop >= 0 ? PaddingTop : fallback,
            PaddingBottom >= 0 ? PaddingBottom : fallback);

    public DocumentLayout WithCustom(decimal widthMm, decimal heightMm,
        int fontScale, int lineSpacing,
        int padTop, int padRight, int padBottom, int padLeft) =>
        this with
        {
            PresetId = "custom",
            WidthMm = widthMm,
            HeightMm = heightMm,
            FontScalePercent = fontScale,
            LineSpacingPercent = lineSpacing,
            PaddingTop = padTop,
            PaddingRight = padRight,
            PaddingBottom = padBottom,
            PaddingLeft = padLeft
        };
}

/// <summary>
/// Named stock sizes the print-sizes screen offers.
/// </summary>
public static class PrintPresets
{
    public static IReadOnlyList<(string Id, string Name, DocumentLayout Layout)>
        Labels { get; } =
    [
        ("label-77x39", "Current — 77 × 39 mm", DocumentLayout.DefaultLabel),
        ("label-50x25", "50 × 25 mm", new("label-50x25", 50m, 25m)),
        ("label-50x30", "50 × 30 mm", new("label-50x30", 50m, 30m)),
        ("label-62x29", "62 × 29 mm", new("label-62x29", 62m, 29m)),
        ("label-100x50", "100 × 50 mm", new("label-100x50", 100m, 50m)),
        ("custom", "Custom…", DocumentLayout.DefaultLabel with { PresetId = "custom" })
    ];

    public static IReadOnlyList<(string Id, string Name, DocumentLayout Layout)>
        Receipts { get; } =
    [
        ("receipt-57", "57 mm roll", new("receipt-57", 57m, 0m)),
        ("receipt-75", "Current — 75 mm roll", DocumentLayout.DefaultReceipt),
        ("receipt-80", "80 mm roll", new("receipt-80", 80m, 0m)),
        ("custom", "Custom…", DocumentLayout.DefaultReceipt with { PresetId = "custom" })
    ];

    public static IReadOnlyList<(string Id, string Name, DocumentLayout Layout)>
        Claims { get; } =
    [
        ("claim-a4", "Current — A4 (210 × 297 mm)", DocumentLayout.DefaultClaim),
        ("claim-letter", "Letter (216 × 279 mm)", new("claim-letter", 216m, 279m)),
        ("custom", "Custom…", DocumentLayout.DefaultClaim with { PresetId = "custom" })
    ];
}

/// <summary>
/// The layouts this computer prints with. Documents read these at print
/// time; Printer setup / Print sizes write them when the operator saves.
/// </summary>
public static class PrintLayouts
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static DocumentLayout Label { get; set; } = DocumentLayout.DefaultLabel;
    public static DocumentLayout Receipt { get; set; } = DocumentLayout.DefaultReceipt;
    public static DocumentLayout Claim { get; set; } = DocumentLayout.DefaultClaim;

    public static async Task LoadAsync(IWorkstationSettings settings,
                                       CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Label = await ReadAsync(settings, WorkstationSettingKey.LabelLayout,
                                DocumentLayout.DefaultLabel, ct)
            .ConfigureAwait(false);
        Receipt = await ReadAsync(settings, WorkstationSettingKey.ReceiptLayout,
                                  DocumentLayout.DefaultReceipt, ct)
            .ConfigureAwait(false);
        Claim = await ReadAsync(settings, WorkstationSettingKey.ClaimLayout,
                                DocumentLayout.DefaultClaim, ct)
            .ConfigureAwait(false);
    }

    public static async Task SaveAsync(IWorkstationSettings settings,
                                       DocumentLayout label,
                                       DocumentLayout receipt,
                                       DocumentLayout claim,
                                       CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Label = label;
        Receipt = receipt;
        Claim = claim;

        await settings.SetAsync(WorkstationSettingKey.LabelLayout,
            JsonSerializer.Serialize(label, Json), ct).ConfigureAwait(false);
        await settings.SetAsync(WorkstationSettingKey.ReceiptLayout,
            JsonSerializer.Serialize(receipt, Json), ct).ConfigureAwait(false);
        await settings.SetAsync(WorkstationSettingKey.ClaimLayout,
            JsonSerializer.Serialize(claim, Json), ct).ConfigureAwait(false);
    }

    private static async Task<DocumentLayout> ReadAsync(
        IWorkstationSettings settings, string key, DocumentLayout fallback,
        CancellationToken ct)
    {
        var json = await settings.GetAsync(key, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return fallback;

        try
        {
            return JsonSerializer.Deserialize<DocumentLayout>(json, Json)
                   ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public static string Describe(DocumentLayout layout) =>
        layout.HeightMm <= 0m
            ? string.Format(CultureInfo.CurrentCulture,
                "{0:0.##} mm wide", layout.WidthMm)
            : string.Format(CultureInfo.CurrentCulture,
                "{0:0.##} × {1:0.##} mm", layout.WidthMm, layout.HeightMm);
}
