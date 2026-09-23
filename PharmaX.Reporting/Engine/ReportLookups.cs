using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Security;
using PharmaX.Persistence;

namespace PharmaX.Reporting.Engine;

/// <summary>
/// The choices behind the slicers whose values are not known until the
/// database is read: departments, till sessions and staff.
/// </summary>
/// <remarks>
/// The catalogue declares these with an empty option list because it is
/// a static table and cannot know what a given pharmacy has on file. The
/// reports screen used to take that emptiness as "let them type it",
/// which meant an operator filtering by department had to know a
/// department's name exactly, and one filtering by operator or till
/// session had to type a GUID. Both were unusable in practice, so both
/// filters went unused.
/// </remarks>
public sealed class ReportLookups : IReportLookups
{
    private readonly PharmaXDbContext _db;

    public ReportLookups(PharmaXDbContext db) => _db = db;

    /// <summary>
    /// The last fifty sessions. A till that has been trading for a year
    /// has hundreds, and nobody scrolls to one from March.
    /// </summary>
    private const int RecentSessions = 50;

    public async Task<IReadOnlyList<SlicerOptionDto>> OptionsForAsync(
        string slicerKey, CancellationToken ct = default) =>
        slicerKey switch
        {
            "department" => await DepartmentsAsync(ct).ConfigureAwait(false),
            "category" => await CategoriesAsync(ct).ConfigureAwait(false),
            "session" => await SessionsAsync(ct).ConfigureAwait(false),
            "operator" => await StaffAsync(null, ct).ConfigureAwait(false),

            "pharmacist" => await StaffAsync(Role.Standard.Pharmacist, ct)
                .ConfigureAwait(false),

            _ => Array.Empty<SlicerOptionDto>()
        };

    /// <remarks>
    /// The identifier is turned into text here rather than in the query.
    /// Translated into SQL, SQLite renders a GUID in upper case and
    /// MySQL does not, and a filter value whose spelling depends on the
    /// database underneath it is a trap for anything that compares one.
    /// </remarks>
    private async Task<IReadOnlyList<SlicerOptionDto>> DepartmentsAsync(
        CancellationToken ct)
    {
        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return departments
            .Select(d => new SlicerOptionDto(d.Id.ToString(), d.Name))
            .ToList();
    }

    private async Task<IReadOnlyList<SlicerOptionDto>> CategoriesAsync(
        CancellationToken ct)
    {
        var categories = await _db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return categories
            .Select(c => new SlicerOptionDto(c.Id.ToString(), c.Name))
            .ToList();
    }

    /// <summary>
    /// Newest first, and labelled by when it was opened rather than by
    /// its identifier: "Till 1, 20/09/2026 08:14" is a thing a manager
    /// can recognise and a GUID is not.
    /// </summary>
    private async Task<IReadOnlyList<SlicerOptionDto>> SessionsAsync(
        CancellationToken ct)
    {
        var sessions = await _db.TillSessions
            .AsNoTracking()
            .OrderByDescending(s => s.OpenedAtUtc)
            .Take(RecentSessions)
            .Select(s => new { s.Id, s.TillNumber, s.OpenedAtUtc, s.Status })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return sessions
            .Select(s => new SlicerOptionDto(
                s.Id.ToString(),
                $"Till {s.TillNumber}, "
                + $"{s.OpenedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}"
                + (s.Status == Domain.Tills.TillSessionStatus.Open
                    ? " (open)" : "")))
            .ToList();
    }

    /// <summary>
    /// Active staff, optionally only those holding one role.
    /// </summary>
    /// <remarks>
    /// The role filter runs in memory because roles are stored as a
    /// primitive collection in one column and the aggregate's own
    /// IsInRole is what decides membership - matching on the column text
    /// would make "Pharmacist" match "Pharmacist Assistant". A pharmacy
    /// has a few dozen staff, so the whole list is a cheap read.
    /// </remarks>
    private async Task<IReadOnlyList<SlicerOptionDto>> StaffAsync(
        string? role, CancellationToken ct)
    {
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return users
            .Where(u => role is null || u.IsInRole(role))
            .OrderBy(u => u.DisplayName, StringComparer.CurrentCulture)
            .Select(u => new SlicerOptionDto(u.Id.ToString(), u.DisplayName))
            .ToList();
    }
}
