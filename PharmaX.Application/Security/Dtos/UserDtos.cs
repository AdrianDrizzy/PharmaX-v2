using PharmaX.Domain.Security;

namespace PharmaX.Application.Security.Dtos;

public sealed record UserListItemDto(
    Guid Id, string Username, string DisplayName, string Roles,
    decimal DiscountLimitPercent, bool IsActive, bool IsLockedOut,
    bool MustChangePassword, DateTime? LastLoginAtUtc)
{
    public static UserListItemDto From(User u)
    {
        ArgumentNullException.ThrowIfNull(u);

        return new UserListItemDto(
            u.Id, u.Username, u.DisplayName,
            string.Join(", ", u.Roles), u.DiscountLimitPercent,
            u.IsActive, u.IsLockedOut, u.MustChangePassword,
            u.LastLoginAtUtc);
    }
}

public sealed record UserDto(
    Guid Id, string Username, string DisplayName,
    IReadOnlyList<string> Roles, decimal DiscountLimitPercent,
    string? RegistrationNumber, bool IsActive, bool IsLockedOut,
    bool MustChangePassword, int FailedAttempts,
    DateTime? LastLoginAtUtc, DateTime? PasswordChangedAtUtc)
{
    public static UserDto From(User u)
    {
        ArgumentNullException.ThrowIfNull(u);

        return new UserDto(
            u.Id, u.Username, u.DisplayName, u.Roles,
            u.DiscountLimitPercent, u.RegistrationNumber, u.IsActive,
            u.IsLockedOut, u.MustChangePassword, u.FailedAttempts,
            u.LastLoginAtUtc, u.PasswordChangedAtUtc);
    }
}

/// <summary>
/// What the edit screen sends back. No credential: a password is set
/// through ResetPasswordAsync, which audits it as its own act.
/// </summary>
public sealed record UserEditDto(
    string Username, string DisplayName, IReadOnlyList<string> Roles,
    decimal DiscountLimitPercent, string? RegistrationNumber);
