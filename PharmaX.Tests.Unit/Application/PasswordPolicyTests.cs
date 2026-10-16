using FluentAssertions;
using PharmaX.Application.Security;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// The one password rule, applied wherever a password is set
/// (ADM-OBS-03): four characters or more.
/// </summary>
public class PasswordPolicyTests
{
    [Theory]
    [InlineData("1234")]
    [InlineData("admin")]
    [InlineData("password")]
    [InlineData("ChangeMe#1")]
    [InlineData("correct horse battery staple")]
    public void A_password_of_four_characters_or_more_is_accepted(string password)
    {
        PasswordPolicy.Refuse(password).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    public void Too_short_is_refused(string password)
    {
        PasswordPolicy.Refuse(password)!.Code.Should().Be("SEC-004");
    }

    [Fact]
    public void Too_long_is_refused()
    {
        PasswordPolicy.Refuse(new string('x', PasswordPolicy.MaximumLength + 1))!
            .Code.Should().Be("SEC-006");
    }
}
