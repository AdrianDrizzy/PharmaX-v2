using System.Security.Cryptography;
using FluentAssertions;
using PharmaX.Application.Licensing;

namespace PharmaX.Tests.Unit.Application;

public sealed class LicenceCodeTests
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private static readonly LicencePayload Sample = new(MembershipPlan.Quarterly,
        Guid.Parse("6f1c2d3e-4a5b-4c6d-8e9f-0a1b2c3d4e5f"),
        new DateOnly(2027, 2, 18), new DateOnly(2026, 11, 18));

    [Fact]
    public void Issued_code_verifies_to_same_payload() =>
        LicenceCode.Verify(LicenceCode.Issue(Sample, Key), Key).Should().Be(Sample);

    [Fact]
    public void Code_starts_with_PHX_and_is_grouped() =>
        LicenceCode.Issue(Sample, Key).Should()
            .MatchRegex("^PHX-([A-Z2-7]{1,5}-)*[A-Z2-7]{1,5}$");

    [Fact]
    public void Pasted_code_with_whitespace_and_lower_case_verifies()
    {
        var code = LicenceCode.Issue(Sample, Key);

        LicenceCode.Verify("  " + code.ToLowerInvariant().Replace("-", "- \r\n") + " ", Key)
            .Should().Be(Sample);
    }

    [Fact]
    public void Changed_character_is_rejected()
    {
        var code = LicenceCode.Issue(Sample, Key).ToCharArray();
        code[10] = code[10] == 'A' ? 'B' : 'A';

        LicenceCode.Verify(new string(code), Key).Should().BeNull();
    }

    [Fact]
    public void Code_signed_by_another_key_is_rejected() =>
        LicenceCode.Verify(LicenceCode.Issue(Sample,
            ECDsa.Create(ECCurve.NamedCurves.nistP256)), Key).Should().BeNull();

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("PHX-")]
    [InlineData("PHX-AAAAA-AAAAA")]
    public void Garbage_is_rejected(string text) =>
        LicenceCode.Verify(text, Key).Should().BeNull();

    [Fact]
    public void Whole_whatsapp_message_pasted_verifies()
    {
        var message = "Thank you for your payment. Your PharmaX membership runs until "
            + "18 Feb 2027. Paste this code and click Activate:\r\n​"
            + LicenceCode.Issue(Sample, Key) + " ";

        LicenceCode.Verify(message, Key).Should().Be(Sample);
    }

    [Fact]
    public void Pharmacy_id_round_trips() =>
        LicenceCode.ParsePharmacyId(
                LicenceCode.FormatPharmacyId(Sample.PharmacyId).ToLowerInvariant())
            .Should().Be(Sample.PharmacyId);

    [Fact]
    public void Built_in_public_key_loads() =>
        LicenceCode.Verify("PHX-AAAAA").Should().BeNull();

    [Theory]
    // Renewing early carries on from the current end.
    [InlineData("2026-11-18", "2026-12-10", MembershipPlan.Monthly, "2027-01-10")]
    // Lapsed: starts again from today.
    [InlineData("2026-11-18", "2026-10-01", MembershipPlan.Quarterly, "2027-02-18")]
    [InlineData("2026-11-18", null, MembershipPlan.Yearly, "2027-11-18")]
    public void Renewal_extends_from_current_end(string today, string? currentEnd,
        MembershipPlan plan, string expected) =>
        LicenceCode.NextValidUntil(DateOnly.Parse(today),
                currentEnd is null ? null : DateOnly.Parse(currentEnd), plan)
            .Should().Be(DateOnly.Parse(expected));
}
