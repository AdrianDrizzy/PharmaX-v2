using FluentAssertions;
using PharmaX.Application.Security;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// The one password rule, applied wherever a password is set
/// (ADM-OBS-03).
/// </summary>
public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Starting#1")]
    [InlineData("BrandNew#2")]
    [InlineData("tendai drinks rooibos")]
    [InlineData("Luveve-Road-28")]
    [InlineData("correct horse battery staple")]
    public void A_password_people_would_not_guess_is_accepted(string password)
    {
        PasswordPolicy.Refuse(password, "tmoyo").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("Ab#1234")]
    public void Too_short_is_refused(string password)
    {
        PasswordPolicy.Refuse(password)!.Code.Should().Be("SEC-004");
    }

    [Theory]
    [InlineData("abcdefgh")]         // the one the E2E run found accepted
    [InlineData("12345678")]
    [InlineData("87654321")]
    [InlineData("qwertyui")]
    [InlineData("zxcvbnm,")]
    public void A_run_along_the_keyboard_is_refused(string password)
    {
        PasswordPolicy.Refuse(password)!.Code.Should().Be("SEC-006");
    }

    [Theory]
    [InlineData("password")]
    [InlineData("Password1!")]
    [InlineData("P@ssw0rd")]
    [InlineData("Pharmacy2026")]
    [InlineData("ChangeMe#1")]       // the seeded first password, never kept
    [InlineData("Zimbabwe123")]
    public void A_common_password_is_refused_however_it_is_dressed(
        string password)
    {
        PasswordPolicy.Refuse(password)!.Code.Should().Be("SEC-006");
    }

    [Theory]
    [InlineData("aaaaaaaa")]
    [InlineData("abababab")]
    [InlineData("11112222")]
    public void Too_few_different_characters_is_refused(string password)
    {
        PasswordPolicy.Refuse(password)!.Code.Should().Be("SEC-006");
    }

    [Fact]
    public void A_password_containing_the_username_is_refused()
    {
        PasswordPolicy.Refuse("Tmoyo#2026x", "tmoyo")!.Message
            .Should().Contain("username");
    }
}
