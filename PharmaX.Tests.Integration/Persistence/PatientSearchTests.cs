using FluentAssertions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Security;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// Finding a patient from what the counter actually types: the surname
/// and then the first name, a few letters of each, the telephone number
/// however it is spaced, or the address - and a product the same way.
/// </summary>
public class PatientSearchTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public PatientSearchTests(PharmaXFixture fixture) => _fixture = fixture;

    /// <remarks>
    /// The counter types the surname first. A patient whose surname
    /// starts with what was typed comes ahead of one who only has those
    /// letters in a first name - even one who sorts before them.
    /// </remarks>
    [Fact]
    public async Task A_surname_starting_with_the_first_word_comes_first()
    {
        await SignInAsync();

        var surnamed = await GivenAsync("Xanders", "Tapiwa");
        var firstNamed = await GivenAsync("Abbotsford", "Xandra");

        var found = await SearchAsync("xan");

        found.Select(p => p.Id).Should().ContainInOrder(surnamed, firstNamed);

        (await SearchAsync("xan tap")).Should().ContainSingle()
            .Which.Id.Should().Be(surnamed, "the first name narrows it");
    }

    [Fact]
    public async Task A_few_letters_of_the_surname_and_the_name_are_enough()
    {
        await SignInAsync();

        var id = await GivenAsync("Qillaymore", "Reshmarie");

        (await SearchAsync("qill resh")).Should().ContainSingle()
            .Which.Id.Should().Be(id);

        (await SearchAsync("Qillaymore, Reshmarie")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_telephone_number_finds_the_patient_however_it_is_spaced()
    {
        await SignInAsync();

        var id = await GivenAsync("Telfordson", "Nyasha",
                                  telephone: "+263 77 918 2736");

        (await SearchAsync("0779 182 736")).Should().ContainSingle()
            .Which.Id.Should().Be(id);

        (await SearchAsync("779182736")).Should().ContainSingle();
    }

    [Fact]
    public async Task The_address_is_kept_and_searched()
    {
        await SignInAsync();

        var id = await GivenAsync("Addressby", "Rufaro",
            address: "14 Kwamenkrumah Avenue\nHarare");

        var found = (await SearchAsync("kwamenkrumah")).Should().ContainSingle()
            .Subject;

        found.Id.Should().Be(id);
        found.Address.Should().Be("14 Kwamenkrumah Avenue\nHarare");

        // Left out of an edit, it stays; sent blank, it is cleared.
        await UpdateAsync(id, Edit("Addressby", "Rufaro") with { Address = null });
        (await GetAsync(id)).Address.Should().Be("14 Kwamenkrumah Avenue\nHarare");

        await UpdateAsync(id, Edit("Addressby", "Rufaro") with { Address = "" });
        (await GetAsync(id)).Address.Should().BeNull();
    }

    /// <remarks>
    /// The number a patient gives identifies them as surely as their
    /// name. A new file for somebody whose number is already on one is
    /// asked about before it is opened - asked, because a family often
    /// shares a number.
    /// </remarks>
    [Fact]
    public async Task A_new_file_with_a_number_already_on_file_is_questioned()
    {
        await SignInAsync();

        var existing = await GivenAsync("Dupliphone", "Tatenda",
                                        telephone: "0712 345 678");

        using var scope = _fixture.NewScope();
        var patients = PharmaXFixture.Resolve<IPatientService>(scope);

        var same = await patients.PossibleDuplicatesAsync(
            Edit("Somebody", "Else") with { Telephone = "+263 71 234 5678" });

        same.IsSuccess.Should().BeTrue(same.Error.Message);
        same.Value.Select(p => p.Id).Should().Contain(existing);
    }

    [Fact]
    public async Task An_id_number_of_digits_alone_is_still_found()
    {
        await SignInAsync();

        var id = await GivenAsync("Idnumberton", "Kuda",
                                  idNumber: "8001015009087");

        (await SearchAsync("8001015009087")).Should().ContainSingle()
            .Which.Id.Should().Be(id);
    }

    [Fact]
    public async Task A_product_is_found_word_by_word_and_by_its_generic_name()
    {
        await SignInAsync();

        using (var scope = _fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);

            var created = await products.CreateAsync(new ProductEditDto(
                Code: "AMZ500", Description: "Amoxizzle 500mg capsules 21s",
                GenericName: "Zzamoxicillin", RetailPrice: 10m, CostPrice: 5m,
                VatRatePercent: 15m, TaxCode: "A", Schedule: 0,
                IsBatchTracked: false, ReorderLevel: 0m, PackSize: null));

            created.IsSuccess.Should().BeTrue(created.Error.Message);
        }

        (await ProductsAsync("amoxiz 500")).Should().ContainSingle()
            .Which.Code.Should().Be("AMZ500");

        (await ProductsAsync("zzamoxicillin")).Should().ContainSingle();
        (await ProductsAsync("amoxiz 250")).Should().BeEmpty();
    }

    // ---- Steps -----------------------------------------------------------

    private static PatientEditDto Edit(string surname, string firstNames) =>
        new(Surname: surname, FirstNames: firstNames, IdNumber: null,
            DateOfBirth: new DateOnly(1990, 1, 1), Telephone: null,
            Email: null, WeightKg: null, IsPregnant: false,
            IsBreastfeeding: false, ClinicalNotes: null,
            Allergies: Array.Empty<AllergyDto>(),
            ChronicConditions: Array.Empty<string>());

    private async Task<Guid> GivenAsync(string surname, string firstNames,
        string? telephone = null, string? address = null,
        string? idNumber = null)
    {
        using var scope = _fixture.NewScope();
        var patients = PharmaXFixture.Resolve<IPatientService>(scope);

        var created = await patients.CreateAsync(Edit(surname, firstNames) with
        {
            Telephone = telephone,
            Address = address,
            IdNumber = idNumber
        });

        created.IsSuccess.Should().BeTrue(created.Error.Message);
        return created.Value;
    }

    private async Task UpdateAsync(Guid id, PatientEditDto edit)
    {
        using var scope = _fixture.NewScope();
        var patients = PharmaXFixture.Resolve<IPatientService>(scope);

        var updated = await patients.UpdateAsync(id, edit);
        updated.IsSuccess.Should().BeTrue(updated.Error.Message);
    }

    private async Task<PatientDto> GetAsync(Guid id)
    {
        using var scope = _fixture.NewScope();
        var patients = PharmaXFixture.Resolve<IPatientService>(scope);

        var fetched = await patients.GetAsync(id);
        fetched.IsSuccess.Should().BeTrue(fetched.Error.Message);
        return fetched.Value;
    }

    private async Task<IReadOnlyList<PatientListItemDto>> SearchAsync(string term)
    {
        using var scope = _fixture.NewScope();
        var patients = PharmaXFixture.Resolve<IPatientService>(scope);

        var found = await patients.SearchAsync(term);
        found.IsSuccess.Should().BeTrue(found.Error.Message);
        return found.Value;
    }

    private async Task<IReadOnlyList<ProductListItemDto>> ProductsAsync(
        string term)
    {
        using var scope = _fixture.NewScope();
        var products = PharmaXFixture.Resolve<IProductService>(scope);

        var found = await products.SearchAsync(term);
        found.IsSuccess.Should().BeTrue(found.Error.Message);
        return found.Value.Items;
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }
}
