using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Calais.Configuration;
using Calais.Core;
using Calais.Exceptions;
using Calais.Models;
using Microsoft.EntityFrameworkCore;

namespace Calais;

/// <summary>
/// Main processor for applying CalaisQuery to IQueryable sources
/// </summary>
public class CalaisProcessor
{
    private readonly CalaisOptions _options;
    private readonly ExpressionTreeBuilder _expressionBuilder;
    private readonly SortExpressionBuilder _sortBuilder;
    private readonly CustomMethodInvoker _customMethodInvoker;

    /// <summary>
    /// Initializes a new instance of the <see cref="CalaisProcessor"/> class.
    /// </summary>
    /// <param name="options">The Calais processing options.</param>
    public CalaisProcessor(CalaisOptions options)
        : this(options, EmptyServiceProvider.Instance, [], []) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CalaisProcessor"/> class with custom method services.
    /// </summary>
    /// <param name="options">The Calais processing options.</param>
    /// <param name="services">The scoped service provider.</param>
    /// <param name="customFilterMethods">The custom filter method providers.</param>
    /// <param name="customSortMethods">The custom sort method providers.</param>
    public CalaisProcessor(
        CalaisOptions options,
        IServiceProvider services,
        IEnumerable<ICalaisCustomFilterMethods>? customFilterMethods,
        IEnumerable<ICalaisCustomSortMethods>? customSortMethods
    )
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _customMethodInvoker = new CustomMethodInvoker(
            options,
            services ?? throw new ArgumentNullException(nameof(services)),
            customFilterMethods,
            customSortMethods
        );
        _expressionBuilder = new ExpressionTreeBuilder(options);
        _sortBuilder = new SortExpressionBuilder(options, _customMethodInvoker);
    }

    /// <summary>
    /// Applies filters from the query to the source
    /// </summary>
    public IQueryable<TEntity> ApplyFilters<TEntity>(IQueryable<TEntity> source, CalaisQuery query)
        where TEntity : class
    {
        if (query.Filters == null || query.Filters.Count == 0)
            return source;

        foreach (var filter in query.Filters)
        {
            if (filter.IsOrGroup)
            {
                var sanitizedFilter = RemoveCustomFiltersFromOrGroup<TEntity>(filter);
                if (sanitizedFilter == null)
                    continue;

                var orFilterExpression = _expressionBuilder.BuildFilterExpression<TEntity>(
                    [sanitizedFilter]
                );
                if (orFilterExpression != null)
                    source = source.Where(orFilterExpression);

                continue;
            }

            if (_customMethodInvoker.TryApplyFilter(source, filter, out var customFilteredSource))
            {
                source = customFilteredSource;
                continue;
            }

            var filterExpression = _expressionBuilder.BuildFilterExpression<TEntity>([filter]);
            if (filterExpression != null)
                source = source.Where(filterExpression);
        }

        return source;
    }

    /// <summary>
    /// Applies sorting from the query to the source
    /// </summary>
    public IQueryable<TEntity> ApplySorting<TEntity>(IQueryable<TEntity> source, CalaisQuery query)
        where TEntity : class
    {
        return _sortBuilder.ApplySorting(source, query.Sorts);
    }

    /// <summary>
    /// Applies pagination to the source (separate from filtering/sorting)
    /// </summary>
    public IQueryable<TEntity> ApplyPagination<TEntity>(
        IQueryable<TEntity> source,
        CalaisQuery query
    )
        where TEntity : class
    {
        var page = query.Page ?? 1;
        var pageSize = Math.Min(query.PageSize ?? _options.DefaultPageSize, _options.MaxPageSize);

        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = _options.DefaultPageSize;

        return source.Skip((page - 1) * pageSize).Take(pageSize);
    }

    /// <summary>
    /// Applies pagination with custom page and pageSize values
    /// </summary>
    public IQueryable<TEntity> ApplyPagination<TEntity>(
        IQueryable<TEntity> source,
        int page,
        int pageSize
    )
        where TEntity : class
    {
        pageSize = Math.Min(pageSize, _options.MaxPageSize);
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = _options.DefaultPageSize;

        return source.Skip((page - 1) * pageSize).Take(pageSize);
    }

    /// <summary>
    /// Applies all query operations: filters, sorting, and pagination
    /// </summary>
    public IQueryable<TEntity> Apply<TEntity>(IQueryable<TEntity> source, CalaisQuery query)
        where TEntity : class
    {
        source = ApplyFilters(source, query);
        source = ApplySorting(source, query);
        source = ApplyPagination(source, query);
        return source;
    }

    /// <summary>
    /// Applies filters and sorting without pagination
    /// </summary>
    public IQueryable<TEntity> ApplyWithoutPagination<TEntity>(
        IQueryable<TEntity> source,
        CalaisQuery query
    )
        where TEntity : class
    {
        source = ApplyFilters(source, query);
        source = ApplySorting(source, query);
        return source;
    }

    /// <summary>
    /// Applies the query and returns a paged result with total count
    /// </summary>
    public async Task<PagedResult<TEntity>> ApplyAsync<TEntity>(
        IQueryable<TEntity> source,
        CalaisQuery query,
        CancellationToken cancellationToken = default
    )
        where TEntity : class
    {
        source = ApplyFilters(source, query);
        source = ApplySorting(source, query);

        var totalCount = await source.CountAsync(cancellationToken);

        var page = query.Page ?? 1;
        var pageSize = Math.Min(query.PageSize ?? _options.DefaultPageSize, _options.MaxPageSize);
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = _options.DefaultPageSize;

        var items = await ApplyPagination(source, page, pageSize).ToListAsync(cancellationToken);

        return new PagedResult<TEntity>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    /// <summary>
    /// Gets the total count after applying filters (useful when pagination is separate)
    /// </summary>
    public async Task<int> GetFilteredCountAsync<TEntity>(
        IQueryable<TEntity> source,
        CalaisQuery query,
        CancellationToken cancellationToken = default
    )
        where TEntity : class
    {
        source = ApplyFilters(source, query);
        return await source.CountAsync(cancellationToken);
    }

    private FilterDescriptor? RemoveCustomFiltersFromOrGroup<TEntity>(FilterDescriptor filter)
        where TEntity : class
    {
        if (!filter.IsOrGroup)
        {
            if (!_customMethodInvoker.HasCustomFilter<TEntity>(filter, out var invalid))
                return filter;

            if (_options.ThrowOnInvalidFields)
            {
                throw new ExpressionBuildException(
                    invalid
                        ? $"Custom filter method '{filter.Field}' cannot be used in an OR group because it is incompatible or ambiguous."
                        : $"Custom filter method '{filter.Field}' cannot be used in an OR group."
                );
            }

            return null;
        }

        var sanitizedOrFilters = new List<FilterDescriptor>();
        foreach (var orFilter in filter.Or!)
        {
            var sanitized = RemoveCustomFiltersFromOrGroup<TEntity>(orFilter);
            if (sanitized != null)
                sanitizedOrFilters.Add(sanitized);
        }

        if (sanitizedOrFilters.Count == 0)
            return null;

        return new FilterDescriptor { Or = sanitizedOrFilters };
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new EmptyServiceProvider();

        public object? GetService(Type serviceType) => null;
    }
}
