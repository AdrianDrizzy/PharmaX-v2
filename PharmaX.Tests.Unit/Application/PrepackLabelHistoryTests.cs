using FluentAssertions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// Pre-pack stickers are remembered newest first, and the list does not
/// grow without limit.
/// </summary>
public class PrepackLabelHistoryTests
{
    [Fact]
    public async Task A_sticker_is_listed_newest_first()
    {
        var settings = new MemorySettings();
        var clock = new FixedClock();
        var history = new PrepackLabelHistory(settings, clock);

        await history.RememberAsync(Sticker("First"), 2);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        await history.RememberAsync(Sticker("Second"), 5);

        var list = await history.ListAsync();

        list.Select(s => s.Medicine).Should().Equal("Second", "First");
        list[0].Copies.Should().Be(5);
        list[0].Directions.Should().Be("Take 2 tablets three times a day");
        list[0].BatchNumber.Should().Be("546777");
    }

    [Fact]
    public async Task The_list_keeps_the_newest_two_hundred()
    {
        var history = new PrepackLabelHistory(new MemorySettings(), new FixedClock());

        for (var i = 0; i < PrepackLabelHistory.Most + 1; i++)
            await history.RememberAsync(Sticker($"Pack {i}"), 1);

        var list = await history.ListAsync();

        list.Should().HaveCount(PrepackLabelHistory.Most);
        list[0].Medicine.Should().Be($"Pack {PrepackLabelHistory.Most}");
        list.Should().NotContain(s => s.Medicine == "Pack 0");
    }

    [Fact]
    public void The_supplier_follows_the_medicine_and_comes_back_off()
    {
        PrepackMedicine.Line("Paracetamol 500mg", "Afromed")
            .Should().Be("Paracetamol 500mg (Afromed)");
        PrepackMedicine.Line("Paracetamol 500mg", "  ")
            .Should().Be("Paracetamol 500mg");

        var suppliers = new[] { "Afromed", "Other" };

        PrepackMedicine.Read("Paracetamol 500mg (afromed)", suppliers)
            .Should().Be(("Paracetamol 500mg", "Afromed"));
        PrepackMedicine.Read("Paracetamol 500mg (NewCo)", suppliers)
            .Should().Be(("Paracetamol 500mg", "NewCo"));
        PrepackMedicine.Read("Paracetamol 500mg", suppliers)
            .Should().Be(("Paracetamol 500mg", null));
        PrepackMedicine.Read("Vitamin B (complex)", suppliers)
            .Should().Be(("Vitamin B", "complex"));
    }

    [Fact]
    public async Task A_blank_list_is_empty_and_a_broken_one_is_ignored()
    {
        var settings = new MemorySettings();
        var history = new PrepackLabelHistory(settings, new FixedClock());

        (await history.ListAsync()).Should().BeEmpty();

        await settings.SetAsync(PharmacySettingKey.PrepackLabels, "not json");

        (await history.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_older_pre_pack_list_still_loads()
    {
        var settings = new MemorySettings();

        await settings.SetAsync(PharmacySettingKey.PrepackLabels,
            """
            [{"Id":"11111111-1111-1111-1111-111111111111","Medicine":"Old","Quantity":1,"Directions":"Take","Warning":null,"Expiry":null,"BatchNumber":null,"Price":null,"PackedOn":"2026-10-20","Copies":1,"PrintedAtUtc":"2026-10-20T10:00:00Z"}]
            """);

        var list = await new PrepackLabelHistory(settings, new FixedClock()).ListAsync();

        list.Should().ContainSingle();
        list[0].Medicine.Should().Be("Old");
        list[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task A_deactivated_pre_pack_stays_on_the_list()
    {
        var history = new PrepackLabelHistory(new MemorySettings(), new FixedClock());
        await history.RememberAsync(Sticker("Paracetamol"), 1);

        var id = (await history.ListAsync())[0].Id;
        await history.DeactivateAsync(id);

        var row = (await history.ListAsync())[0];
        row.Medicine.Should().Be("Paracetamol");
        row.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task A_template_keeps_what_was_left_off_and_can_be_deactivated()
    {
        var history = new TemplateLabelHistory(new MemorySettings(), new FixedClock());
        var sticker = Sticker("Paracetamol") with
        {
            Omit = new LabelOmissions(Address: true)
        };

        await history.RememberAsync(sticker, 3);

        var row = (await history.ListAsync())[0];
        row.Copies.Should().Be(3);
        row.Omit.Address.Should().BeTrue();
        row.Sticker().Omit!.Address.Should().BeTrue();
        row.IsActive.Should().BeTrue();

        await history.DeactivateAsync(row.Id);

        (await history.ListAsync())[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task A_pre_pack_sticker_can_be_deleted_for_good()
    {
        var history = new PrepackLabelHistory(new MemorySettings(), new FixedClock());
        await history.RememberAsync(Sticker("Keep"), 1);
        await history.RememberAsync(Sticker("Made in error"), 1);

        var wrong = (await history.ListAsync()).Single(s => s.Medicine == "Made in error");
        await history.DeleteAsync(wrong.Id);
        await history.DeleteAsync(Guid.NewGuid()); // nothing there: no harm

        (await history.ListAsync()).Select(s => s.Medicine).Should().Equal("Keep");
    }

    [Fact]
    public async Task A_template_label_can_be_deleted_whether_active_or_not()
    {
        var templates = new TemplateLabelHistory(new MemorySettings(), new FixedClock());
        await templates.RememberAsync(Sticker("Old"), 1);
        await templates.RememberAsync(Sticker("Current"), 1);

        var old = (await templates.ListAsync()).Single(s => s.Medicine == "Old");
        await templates.DeactivateAsync(old.Id);
        await templates.DeleteAsync(old.Id);

        (await templates.ListAsync()).Select(s => s.Medicine).Should().Equal("Current");
    }

    private static LabelDto Sticker(string medicine) => new(
        PatientName: "",
        ProductDescription: medicine,
        Quantity: 40m,
        Directions: "Take 2 tablets three times a day",
        BatchNumber: "546777",
        Expiry: new DateOnly(2026, 12, 31),
        PharmacistName: "",
        ScriptNumber: "Pre-pack",
        DispensedOn: new DateOnly(2026, 10, 20),
        Cautions: new[] { "May cause drowsiness" },
        Price: 1m);

    private sealed class MemorySettings : IPharmacySettings
    {
        private readonly Dictionary<string, string?> _values = new();

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string? value, CancellationToken ct = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; set; } =
            new(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);

        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }
}
