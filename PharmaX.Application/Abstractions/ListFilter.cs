using PharmaX.Domain.Common;

namespace PharmaX.Application.Abstractions;

/// <summary>The orders a maintenance list can be put in.</summary>
public enum ListSort
{
    /// The default. Each list keeps its own best-match ranking within it,
    /// so "amox" still puts Amoxicillin ahead of Co-Amoxiclav.
    NameAToZ,
    NameZToA,
    NewestAdded,
    OldestAdded,
    RecentlyModified,

    /// Products only.
    PriceHighest,
    PriceLowest
}

/// <summary>
/// The Sort and date range a maintenance list is searched with, applied in
/// the query so that every page, the count and an export agree.
/// </summary>
/// <param name="ByModified">The range is on when a record was last changed rather than added.</param>
/// <param name="FromUtc">Inclusive; null for no lower bound.</param>
/// <param name="ToUtc">Exclusive; null for no upper bound.</param>
public sealed record ListFilter(ListSort Sort = ListSort.NameAToZ,
    bool ByModified = false, DateTime? FromUtc = null, DateTime? ToUtc = null);

public static class ListFilterQuery
{
    /// <summary>The records added - or last changed - in the filter's range.</summary>
    public static IQueryable<T> Within<T>(this IQueryable<T> query, ListFilter? filter)
        where T : class, IAuditable
    {
        if (filter?.FromUtc is DateTime from)
            query = filter.ByModified
                ? query.Where(e => e.UpdatedAtUtc >= from)
                : query.Where(e => e.CreatedAtUtc >= from);

        if (filter?.ToUtc is DateTime to)
            query = filter.ByModified
                ? query.Where(e => e.UpdatedAtUtc < to)
                : query.Where(e => e.CreatedAtUtc < to);

        return query;
    }

    /// <summary>
    /// Ordered by date for the date sorts; null for the rest, which each
    /// list orders by its own name and ranking.
    /// </summary>
    public static IOrderedQueryable<T>? ByDate<T>(this IQueryable<T> query, ListFilter? filter)
        where T : class, IAuditable => filter?.Sort switch
    {
        ListSort.NewestAdded => query.OrderByDescending(e => e.CreatedAtUtc),
        ListSort.OldestAdded => query.OrderBy(e => e.CreatedAtUtc),
        ListSort.RecentlyModified => query.OrderByDescending(e => e.UpdatedAtUtc),
        _ => null
    };
}
