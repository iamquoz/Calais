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
using Microsoft.EntityFrameworkCore.Query;

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

		var expressionBuilder = GetExpressionBuilder(source);
		foreach (var filter in query.Filters)
		{
			if (filter.IsOrGroup)
			{
				var sanitizedFilter = RemoveCustomFiltersFromOrGroup<TEntity>(filter);
				if (sanitizedFilter == null)
					continue;

				var orFilterExpression = expressionBuilder.BuildFilterExpression<TEntity>([
					sanitizedFilter,
				]);
				if (orFilterExpression != null)
				{
					if (source.Provider is IAsyncQueryProvider)
						orFilterExpression = CollectionAnyOptimizer.Combine(orFilterExpression);
					source = source.Where(orFilterExpression);
				}

				continue;
			}

			if (_customMethodInvoker.TryApplyFilter(source, filter, out var customFilteredSource))
			{
				source = customFilteredSource;
				expressionBuilder = GetExpressionBuilder(source);
				continue;
			}

			var filterExpression = expressionBuilder.BuildFilterExpression<TEntity>([filter]);
			if (filterExpression != null)
				source = source.Where(filterExpression);
		}

		return source;
	}

	private ExpressionTreeBuilder GetExpressionBuilder(IQueryable source) =>
		source.Provider is IAsyncQueryProvider
			? new ExpressionTreeBuilder(_options, source.Expression)
			: _expressionBuilder;

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
		where TEntity : class =>
		IsUnpaged(query)
			? source
			: ApplyPagination(
				source,
				query.Page.GetValueOrDefault(),
				query.PageSize.GetValueOrDefault()
			);

	private bool IsUnpaged(CalaisQuery query) =>
		_options.MaxPageSize == -1 && query.PageSize == null;

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
		var (_, size, offset) = GetPage(page, pageSize);
		if (offset > int.MaxValue)
			throw new ArgumentOutOfRangeException(
				nameof(page),
				page,
				"The page offset exceeds the range supported by Queryable.Skip."
			);

		return source.Skip((int)offset).Take(size);
	}

	private (int Page, int PageSize, long Offset) GetPage(int page, int pageSize)
	{
		page = Math.Max(1, page);
		pageSize = pageSize > 0 ? pageSize : _options.DefaultPageSize;
		if (_options.MaxPageSize != -1)
			pageSize = Math.Min(pageSize, _options.MaxPageSize);
		return (page, pageSize, (long)(page - 1) * pageSize);
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

		if (IsUnpaged(query))
		{
			var allItems = await source.ToListAsync(cancellationToken);
			return new PagedResult<TEntity>
			{
				Items = allItems,
				Page = 1,
				PageSize = allItems.Count,
				TotalCount = allItems.Count,
			};
		}

		var (page, pageSize, offset) = GetPage(
			query.Page.GetValueOrDefault(),
			query.PageSize.GetValueOrDefault()
		);
		var totalCount = await source.CountAsync(cancellationToken);
		var items =
			offset < totalCount
				? await source.Skip((int)offset).Take(pageSize).ToListAsync(cancellationToken)
				: [];

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
