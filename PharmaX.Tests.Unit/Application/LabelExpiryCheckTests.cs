using FluentAssertions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;

namespace PharmaX.Tests.Unit.Application;

public sealed class LabelExpiryCheckTests
{
    private static LabelDto Label(DateOnly? expiry, LabelOmissions? omit = null) =>
        new("Ada", "Amoxicillin", 20m, "tds", null, expiry, "Pharm", "42",
            new DateOnly(2026, 11, 21), Array.Empty<string>(), Omit: omit);

    [Fact]
    public void Missing_lists_labels_without_an_expiry() =>
        LabelExpiryCheck.Missing(new[] { Label(null), Label(new DateOnly(2027, 1, 1)) })
            .Should().ContainSingle().Which.Expiry.Should().BeNull();

    [Fact]
    public void Missing_ignores_a_template_label_that_leaves_the_expiry_off() =>
        LabelExpiryCheck.Missing(new[] { Label(null, new LabelOmissions(Expiry: true)) })
            .Should().BeEmpty();
}
