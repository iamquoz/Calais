using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Calais.Configuration;
using Calais.Core;
using Calais.Models;
using Microsoft.EntityFrameworkCore;

namespace Calais
{
    /// <summary>
    /// Main processor for applying CalaisQuery to IQueryable sources
    /// </summary>
    public class CalaisProcessor
    {
        private readonly CalaisOptions _options;
        private readonly ExpressionTreeBuilder _expressionBuilder;
        private readonly SortExpressionBuilder _sortBuilder;

        public CalaisProcessor(CalaisOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _expressionBuilder = new ExpressionTreeBuilder(options);
            _sortBuilder = new SortExpressionBuilder(options);
        }

        /// <summary>
        /// Applies filters from the query to the source
        /// </summary>
        public IQueryable<TEntity> ApplyFilters<TEntity>(
            IQueryable<TEntity> source,
            CalaisQuery query) where TEntity : class
        {
            if (query.Filters == null || query.Filters.Count == 0)
                return source;

            var filterExpression = _expressionBuilder.BuildFilterExpression<TEntity>(query.Filters);
            return filterExpression != null ? source.Where(filterExpression) : source;
        }

        /// <summary>
        /// Applies sorting from the query to the source
        /// </summary>
        public IQueryable<TEntity> ApplySorting<TEntity>(
            IQueryable<TEntity> source,
            CalaisQuery query) where TEntity : class
        {
            return _sortBuilder.ApplySorting(source, query.Sorts);
        }

        /// <summary>
        /// Applies pagination to the source (separate from filtering/sorting)
        /// </summary>
        public IQueryable<TEntity> ApplyPagination<TEntity>(
            IQueryable<TEntity> source,
            CalaisQuery query) where TEntity : class
        {
            var page = query.Page ?? 1;
            var pageSize = Math.Min(query.PageSize ?? _options.DefaultPageSize, _options.MaxPageSize);

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = _options.DefaultPageSize;

            return source.Skip((page - 1) * pageSize).Take(pageSize);
        }

        /// <summary>
        /// Applies pagination with custom page and pageSize values
        /// </summary>
        public IQueryable<TEntity> ApplyPagination<TEntity>(
            IQueryable<TEntity> source,
            int page,
            int pageSize) where TEntity : class
        {
            pageSize = Math.Min(pageSize, _options.MaxPageSize);
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = _options.DefaultPageSize;

            return source.Skip((page - 1) * pageSize).Take(pageSize);
        }

        /// <summary>
        /// Applies all query operations: filters, sorting, and pagination
        /// </summary>
        public IQueryable<TEntity> Apply<TEntity>(
            IQueryable<TEntity> source,
            CalaisQuery query) where TEntity : class
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
            CalaisQuery query) where TEntity : class
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
            CancellationToken cancellationToken = default) where TEntity : class
        {
            source = ApplyFilters(source, query);
            source = ApplySorting(source, query);

            var totalCount = await source.CountAsync(cancellationToken);

            var page = query.Page ?? 1;
            var pageSize = Math.Min(query.PageSize ?? _options.DefaultPageSize, _options.MaxPageSize);
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = _options.DefaultPageSize;

            var items = await ApplyPagination(source, page, pageSize)
                .ToListAsync(cancellationToken);

            return new PagedResult<TEntity>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount
            };
        }

        /// <summary>
        /// Gets the total count after applying filters (useful when pagination is separate)
        /// </summary>
        public async Task<int> GetFilteredCountAsync<TEntity>(
            IQueryable<TEntity> source,
            CalaisQuery query,
            CancellationToken cancellationToken = default) where TEntity : class
        {
            source = ApplyFilters(source, query);
            return await source.CountAsync(cancellationToken);
        }
    }
}
