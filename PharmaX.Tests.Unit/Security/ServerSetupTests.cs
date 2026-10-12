using FluentAssertions;
using PharmaX.Desktop.Setup;
using Xunit;

namespace PharmaX.Tests.Unit.Security;

public sealed class ServerSetupTests
{
    /// <remarks>
    /// The password is inlined into msiexec's arguments, the CREATE USER
    /// statement and the tills' connection strings, so it must never hold
    /// a quote, a semicolon or a space - and it is read off a screen and
    /// typed at every till, so no 0/O or 1/l either.
    /// </remarks>
    [Fact]
    public void A_new_password_is_safe_to_inline_and_to_type()
    {
        var passwords = Enumerable.Range(0, 200)
            .Select(_ => ServerSetup.NewPassword())
            .ToList();

        passwords.Should().OnlyContain(p =>
            System.Text.RegularExpressions.Regex.IsMatch(p,
                "^[a-hjkmnp-z2-9]{4}(-[a-hjkmnp-z2-9]{4}){3}$"));

        passwords.Should().OnlyHaveUniqueItems();
    }
}
