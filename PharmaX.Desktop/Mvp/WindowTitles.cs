using PharmaX.Application.Abstractions;

namespace PharmaX.Desktop.Mvp;

/// <summary>
/// Puts the pharmacy's trading name where the screens say "PharmaX",
/// when the pharmacy has asked for that at the System Console.
/// </summary>
/// <remarks>
/// Sixty-odd screens set their titles to "PharmaX - something", in
/// designers and in code, some of them more than once while open. Rather
/// than teach each one, PhxFormBase passes every title through Brand, so
/// a title written anywhere comes out right and one written tomorrow
/// does too. Static for the same reason Format is: a form's title is set
/// in InitializeComponent, before any container could hand it a service.
/// </remarks>
public static class WindowTitles
{
    private static PharmacyProfile? _profile;

    /// Called once at start-up, before the first window opens.
    public static void Use(PharmacyProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (_profile is not null) _profile.Changed -= OnChanged;

        _profile = profile;
        profile.Changed += OnChanged;
    }

    public static string Brand(string? text) => _profile?.Brand(text) ?? text ?? "";

    /// The caption for message boxes: the trading name or "PharmaX".
    public static string Caption => Brand(PharmacyDetails.ProductName);

    /// <summary>
    /// Re-titles every open window after the details change, so the
    /// shell behind the System Console changes the moment Save is
    /// pressed rather than at the next sign-in.
    /// </summary>
    private static void OnChanged(object? sender, EventArgs e)
    {
        foreach (var form in System.Windows.Forms.Application.OpenForms
                     .OfType<PhxFormBase>().ToList())
        {
            if (form.IsDisposed) continue;

            if (form.InvokeRequired) form.BeginInvoke(form.RefreshBrand);
            else form.RefreshBrand();
        }
    }
}
