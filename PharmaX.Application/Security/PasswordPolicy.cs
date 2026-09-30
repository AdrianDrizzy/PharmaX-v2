using PharmaX.Application.Results;

namespace PharmaX.Application.Security;

/// <summary>
/// What a password has to be, wherever one is set: a new user's first,
/// an administrator's reset, and the user's own change.
/// </summary>
/// <remarks>
/// The rules were length alone, and held in three places, so "abcdefgh"
/// and "password" were both accepted (ADM-OBS-03).
///
/// These follow NIST SP 800-63B rather than the older "one capital, one
/// digit, one symbol" recipe. Composition rules make people write
/// "Password1!", which meets every one of them and is among the first
/// guesses any attacker tries. What actually stops a guess is refusing
/// the passwords people really choose: the common ones, the obvious
/// runs along the keyboard, and the user's own name.
/// </remarks>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 128;

    /// <summary>
    /// The passwords people choose first, and the words of this trade and
    /// this country they reach for next. Compared after the decoration is
    /// stripped, so "Passw0rd!" and "Pharmacy2026" are caught as well.
    /// </summary>
    private static readonly HashSet<string> Common = new(StringComparer.Ordinal)
    {
        "password", "passwords", "passcode", "letmein", "welcome", "changeme",
        "default", "secret", "login", "admin", "administrator", "root",
        "user", "guest", "test", "testing", "qwerty", "qwertyuiop",
        "asdfghjkl", "zxcvbnm", "iloveyou", "trustno", "sunshine",
        "princess", "football", "baseball", "soccer", "dragon", "monkey",
        "shadow", "master", "superman", "batman", "computer", "internet",
        "whatever", "freedom", "blessed", "blessing", "godisgood", "jesus",
        "abc", "abcd", "abcde", "abcdef", "summer", "winter", "spring",
        "autumn", "january", "february", "september", "october",
        "november", "december", "pharmax", "pharmacy", "pharmacist",
        "chemist", "medicine", "cashier", "manager", "dispensary",
        "zimbabwe", "harare", "bulawayo", "ecocash", "cimas", "psmas"
    };

    private static readonly string[] Runs =
    {
        "abcdefghijklmnopqrstuvwxyz",
        "01234567890",
        "qwertyuiopasdfghjklzxcvbnm",
        "1qaz2wsx3edc4rfv5tgb6yhn7ujm8ik9ol0p"
    };

    /// <summary>
    /// Why a password cannot be used, or null where it can.
    /// </summary>
    /// <param name="username">
    /// The account it is for. A password that contains its own username
    /// is the second guess after "password".
    /// </param>
    public static Error? Refuse(string? password, string? username = null)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < MinimumLength)
            return new Error("SEC-004",
                "A password must be at least eight characters.");

        if (password.Length > MaximumLength)
            return new Error("SEC-006",
                $"A password can be at most {MaximumLength} characters.");

        var lower = password.ToLowerInvariant();

        if (lower.Distinct().Count() < 4)
            return new Error("SEC-006",
                "That password repeats too few characters to be hard to "
                + "guess. Use a phrase of several words.");

        if (Runs.Any(run => run.Contains(lower, StringComparison.Ordinal)
                            || Reverse(run).Contains(lower, StringComparison.Ordinal)))
            return new Error("SEC-006",
                "A run along the alphabet, the digits or the keyboard is "
                + "one of the first things tried. Use a phrase of several "
                + "words.");

        if (Common.Contains(Undecorated(password)))
            return new Error("SEC-006",
                "That is one of the most commonly used passwords, and one "
                + "of the first tried. Use a phrase of several words.");

        if (!string.IsNullOrWhiteSpace(username)
            && username.Trim().Length >= 3
            && lower.Contains(username.Trim().ToLowerInvariant(),
                              StringComparison.Ordinal))
            return new Error("SEC-006",
                "A password cannot contain the username it signs in with.");

        return null;
    }

    /// <summary>
    /// The word underneath: digits and symbols taken off both ends, and
    /// the usual letter swaps undone.
    /// </summary>
    private static string Undecorated(string password)
    {
        var core = password.Trim()
            .TrimStart(Decoration)
            .TrimEnd(Decoration)
            .ToLowerInvariant();

        return new string(core.Select(c => c switch
        {
            '0' => 'o',
            '1' or '!' or '|' => 'i',
            '3' => 'e',
            '4' or '@' => 'a',
            '5' or '$' => 's',
            '7' or '+' => 't',
            _ => c
        }).ToArray());
    }

    private static readonly char[] Decoration =
        "0123456789!@#$%^&*()-_=+[]{};:'\",.<>/?\\|`~ ".ToCharArray();

    private static string Reverse(string value)
    {
        var chars = value.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }
}
