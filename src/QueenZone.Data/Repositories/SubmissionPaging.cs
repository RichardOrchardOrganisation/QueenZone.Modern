using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace QueenZone.Data;

/// <summary>
/// Newest-first paging shared by the EF submission repositories: page-number clamping, the
/// <c>SubmittedAt DESC, Id ASC</c> order, and the SQLite / SQL Server split.
/// </summary>
/// <remarks>
/// The EF Core SQLite provider cannot <c>ORDER BY</c> a <see cref="DateTimeOffset"/>, so on SQLite
/// (the default <c>QueenZone.Web.Tests</c> suite) the filtered rows are materialised and ordered
/// and paged in memory. Other providers order and page in SQL (<c>OFFSET ... FETCH</c>); that path
/// is covered by <c>tests/QueenZone.SqlServerTests</c> and the SQL-shape tests.
/// </remarks>
internal static class SubmissionPaging
{
    internal const int MaxPageSize = 100;

    /// <summary>Clamps <paramref name="page"/> to 1+ and <paramref name="pageSize"/> to 1–100.</summary>
    internal static (int Skip, int Take) Normalize(int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        return ((page - 1) * pageSize, pageSize);
    }

    /// <summary>The SQL-side page: order newest first, skip, take, then project.</summary>
    internal static IQueryable<TResult> NewestFirstPage<TEntity, TResult>(
        this IQueryable<TEntity> source,
        NewestFirstOrder<TEntity> order,
        Expression<Func<TEntity, TResult>> projection,
        int skip,
        int take) =>
        source
            .OrderByDescending(order.SubmittedAt)
            .ThenBy(order.Id)
            .Skip(skip)
            .Take(take)
            .Select(projection);

    /// <summary>
    /// Projects one newest-first page, in SQL (<see cref="NewestFirstPage{TEntity,TResult}"/>) or,
    /// when <paramref name="pageInSql"/> is false, in memory after materialising every filtered row.
    /// </summary>
    internal static async Task<List<TResult>> ToNewestFirstPageAsync<TEntity, TResult>(
        this IQueryable<TEntity> source,
        NewestFirstOrder<TEntity> order,
        Expression<Func<TEntity, TResult>> projection,
        int skip,
        int take,
        bool pageInSql,
        CancellationToken cancellationToken)
    {
        if (pageInSql)
        {
            return await source.NewestFirstPage(order, projection, skip, take).ToListAsync(cancellationToken);
        }

        var rows = await source.Select(order.WithSortKeys(projection)).ToListAsync(cancellationToken);
        return rows
            .OrderByDescending(row => row.SubmittedAt)
            .ThenBy(row => row.Id)
            .Skip(skip)
            .Take(take)
            .Select(row => row.Item)
            .ToList();
    }

    /// <summary>
    /// Loads one newest-first page of tracked-or-not entities as the query shapes them, keeping any
    /// <c>Include</c> on <paramref name="source"/> (a projection would drop it).
    /// </summary>
    internal static async Task<List<TEntity>> ToNewestFirstEntityPageAsync<TEntity>(
        this IQueryable<TEntity> source,
        NewestFirstOrder<TEntity> order,
        int skip,
        int take,
        bool pageInSql,
        CancellationToken cancellationToken)
    {
        if (pageInSql)
        {
            return await source
                .OrderByDescending(order.SubmittedAt)
                .ThenBy(order.Id)
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        var rows = await source.ToListAsync(cancellationToken);
        return rows
            .OrderByDescending(order.CompiledSubmittedAt)
            .ThenBy(order.CompiledId)
            .Skip(skip)
            .Take(take)
            .ToList();
    }

    /// <summary>
    /// Counts every filtered row, then loads the requested newest-first page of it.
    /// </summary>
    internal static async Task<SubmissionListPage<TResult>> ToNewestFirstListPageAsync<TEntity, TResult>(
        this IQueryable<TEntity> source,
        NewestFirstOrder<TEntity> order,
        Expression<Func<TEntity, TResult>> projection,
        int skip,
        int take,
        bool pageInSql,
        CancellationToken cancellationToken)
    {
        var totalCount = await source.CountAsync(cancellationToken);
        var items = await source.ToNewestFirstPageAsync(order, projection, skip, take, pageInSql, cancellationToken);
        return new SubmissionListPage<TResult>(items, totalCount);
    }
}

/// <summary>
/// The <c>SubmittedAt DESC, Id ASC</c> order for one submission entity. Articles pass
/// <c>SubmittedAt ?? DateTimeOffset.MinValue</c> so unsubmitted drafts sort last.
/// </summary>
internal sealed class NewestFirstOrder<TEntity>(
    Expression<Func<TEntity, DateTimeOffset>> submittedAt,
    Expression<Func<TEntity, Guid>> id)
{
    public Expression<Func<TEntity, DateTimeOffset>> SubmittedAt { get; } = submittedAt;

    public Expression<Func<TEntity, Guid>> Id { get; } = id;

    public Func<TEntity, DateTimeOffset> CompiledSubmittedAt { get; } = submittedAt.Compile();

    public Func<TEntity, Guid> CompiledId { get; } = id.Compile();

    /// <summary>
    /// Combines the sort keys and <paramref name="projection"/> into one EF-translatable
    /// projection, so the in-memory path can sort rows it has already projected.
    /// </summary>
    internal Expression<Func<TEntity, SortableRow<TResult>>> WithSortKeys<TResult>(
        Expression<Func<TEntity, TResult>> projection)
    {
        var entity = projection.Parameters[0];
        var body = Expression.MemberInit(
            Expression.New(typeof(SortableRow<TResult>)),
            Expression.Bind(
                typeof(SortableRow<TResult>).GetProperty(nameof(SortableRow<TResult>.SubmittedAt))!,
                ReplacingExpressionVisitor.Replace(SubmittedAt.Parameters[0], entity, SubmittedAt.Body)),
            Expression.Bind(
                typeof(SortableRow<TResult>).GetProperty(nameof(SortableRow<TResult>.Id))!,
                ReplacingExpressionVisitor.Replace(Id.Parameters[0], entity, Id.Body)),
            Expression.Bind(
                typeof(SortableRow<TResult>).GetProperty(nameof(SortableRow<TResult>.Item))!,
                projection.Body));
        return Expression.Lambda<Func<TEntity, SortableRow<TResult>>>(body, entity);
    }
}

internal sealed class SortableRow<TResult>
{
    public DateTimeOffset SubmittedAt { get; init; }

    public Guid Id { get; init; }

    public TResult Item { get; init; } = default!;
}
