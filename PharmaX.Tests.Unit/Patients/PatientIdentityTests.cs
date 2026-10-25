using FluentAssertions;
using PharmaX.Application.Patients;

namespace PharmaX.Tests.Unit.Patients;

/// <summary>
/// How many people the patient files are: the same phone number and
/// address is one person, however it was typed.
/// </summary>
public class PatientIdentityTests
{
    [Theory]
    [InlineData("0776415663", "0776415663")]
    [InlineData("077 641 5663", "0776415663")]
    [InlineData("+263 77 641 5663", "0776415663")]
    [InlineData("00263776415663", "0776415663")]
    [InlineData("776415663", "0776415663")]
    [InlineData("0774719478/", "0774719478")]
    [InlineData("09 123456", "09123456")]
    public void Phone_numbers_are_compared_as_digits_in_the_local_form(
        string typed, string expected) =>
        PatientIdentity.Phone(typed).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("07")]
    [InlineData("BYO")]
    [InlineData("MAT NORTH")]
    public void A_placeholder_is_not_a_phone_number(string? typed) =>
        PatientIdentity.Phone(typed).Should().BeNull();

    [Theory]
    [InlineData("8, Thorngrove, Bulawayo", "8, thorngrove, bulawayo")]
    [InlineData("  8 ,  THORNGROVE,Bulawayo. ", "8, thorngrove, bulawayo")]
    [InlineData("14  James Court, Cbd, Bulawayo", "14 james court, cbd, bulawayo")]
    public void Addresses_are_compared_ignoring_case_and_spacing(
        string typed, string expected) =>
        PatientIdentity.Address(typed).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData(" , ,")]
    public void A_blank_address_is_no_address(string? typed) =>
        PatientIdentity.Address(typed).Should().BeNull();

    [Fact]
    public void The_same_number_and_address_typed_differently_is_one_person() =>
        PatientIdentity.CountPeople(new (string?, string?)[]
        {
            ("0776415663", "8, Thorngrove, Bulawayo"),
            ("+263 77 641 5663", "8 , THORNGROVE , bulawayo"),
            ("077-641-5663", "8,Thorngrove,Bulawayo")
        }).Should().Be(1);

    [Fact]
    public void The_same_number_at_another_address_is_another_person() =>
        PatientIdentity.CountPeople(new (string?, string?)[]
        {
            ("0776415663", "8, Thorngrove, Bulawayo"),
            ("0776415663", "9, Thorngrove, Bulawayo")
        }).Should().Be(2, "a shared family phone is not one patient");

    /// <remarks>
    /// Most imported files have no number, and an address alone is shared
    /// by a whole street: merging the blanks would count dozens as one.
    /// </remarks>
    [Fact]
    public void Files_without_a_number_or_an_address_are_each_their_own_person() =>
        PatientIdentity.CountPeople(new (string?, string?)[]
        {
            (null, "Makokoba, Makokoba, Bulawayo"),
            (null, "Makokoba, Makokoba, Bulawayo"),
            ("0", "Makokoba, Makokoba, Bulawayo"),
            ("0776415663", null),
            ("0776415663", "  "),
            (null, null)
        }).Should().Be(6);

    [Fact]
    public void No_files_is_no_people() =>
        PatientIdentity.CountPeople(Array.Empty<(string?, string?)>())
            .Should().Be(0);
}
