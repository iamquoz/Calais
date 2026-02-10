using System;
using System.Linq;
using Calais.Models;
using Microsoft.Extensions.DependencyInjection;
using Calais.Configuration;

namespace Calais
{
    /// <summary>
    /// Extension methods for IQueryable to simplify CalaisQuery application
    /// </summary>
    public static class CalaisExtensions
    {
        /// <summary>
        /// Applies a CalaisQuery to the queryable using the provided processor
        /// </summary>
        public static IQueryable<TEntity> ApplyCalaisQuery<TEntity>(
            this IQueryable<TEntity> source,
            CalaisQuery query,
            CalaisProcessor processor) where TEntity : class
        {
            return processor.Apply(source, query);
        }

        /// <summary>
        /// Applies only filters from a CalaisQuery
        /// </summary>
        public static IQueryable<TEntity> ApplyCalaisFilters<TEntity>(
            this IQueryable<TEntity> source,
            CalaisQuery query,
            CalaisProcessor processor) where TEntity : class
        {
            return processor.ApplyFilters(source, query);
        }

        /// <summary>
        /// Applies only sorting from a CalaisQuery
        /// </summary>
        public static IQueryable<TEntity> ApplyCalaisSorting<TEntity>(
            this IQueryable<TEntity> source,
            CalaisQuery query,
            CalaisProcessor processor) where TEntity : class
        {
            return processor.ApplySorting(source, query);
        }

        /// <summary>
        /// Applies only pagination from a CalaisQuery
        /// </summary>
        public static IQueryable<TEntity> ApplyCalaisPagination<TEntity>(
            this IQueryable<TEntity> source,
            CalaisQuery query,
            CalaisProcessor processor) where TEntity : class
        {
            return processor.ApplyPagination(source, query);
        }

        /// <summary>
        /// Applies pagination with explicit parameters
        /// </summary>
        public static IQueryable<TEntity> ApplyCalaisPagination<TEntity>(
            this IQueryable<TEntity> source,
            int page,
            int pageSize,
            CalaisProcessor processor) where TEntity : class
        {
            return processor.ApplyPagination(source, page, pageSize);
        }
    }

    /// <summary>
    /// Extension methods for dependency injection
    /// </summary>
    public static class CalaisServiceCollectionExtensions
    {
        /// <summary>
        /// Adds Calais services to the service collection
        /// </summary>
        public static IServiceCollection AddCalais(
            this IServiceCollection services,
            Action<CalaisBuilder>? configure = null)
        {
            var builder = new CalaisBuilder();
            configure?.Invoke(builder);

            services.AddSingleton(builder.Options);
            services.AddSingleton(builder.Build());

            return services;
        }
    }
}
