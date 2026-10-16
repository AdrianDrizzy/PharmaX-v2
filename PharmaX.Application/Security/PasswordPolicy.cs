using PharmaX.Application.Results;

namespace PharmaX.Application.Security;

/// <summary>
/// What a password has to be, wherever one is set: a new user's first,
/// an administrator's reset, and the user's own change.
/// </summary>
/// <remarks>
/// Length alone, and short: four characters will do. The pharmacy asked on
/// 16 October 2026 for passwords staff can remember at a busy counter. The
/// common-word, keyboard-run and username checks were dropped with the
/// longer minimum, because a password the staff forget ends up written on
/// the till.
/// </remarks>
public static class PasswordPolicy
{
    public const int MinimumLength = 4;
    public const int MaximumLength = 128;

    /// <summary>
    /// Why a password cannot be used, or null where it can.
    /// </summary>
    public static Error? Refuse(string? password)
    {
        if (string.IsNullOrWhiteSpace(password)
            || password.Length < MinimumLength)
            return new Error("SEC-004",
                $"A password must be at least {MinimumLength} characters.");

        if (password.Length > MaximumLength)
            return new Error("SEC-006",
                $"A password can be at most {MaximumLength} characters.");

        return null;
    }
}
