using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Calais.Configuration;
using Calais.Exceptions;
using Calais.Models;

namespace Calais.Core;

/// <summary>
/// Builds sorting expressions from sort descriptors
/// </summary>
public class SortExpressionBuilder
{
    private readonly CalaisOptions _options;
    private readonly CustomMethodInvoker? _customMethodInvoker;

    /// <summary>
    /// Initializes a new instance of the <see cref="SortExpressionBuilder"/> class.
    /// </summary>
    /// <param name="options">The Calais processing options.</param>
    public SortExpressionBuilder(CalaisOptions options)
        : this(options, null) { }

    internal SortExpressionBuilder(CalaisOptions options, CustomMethodInvoker? customMethodInvoker)
    {
        _options = options;
        _customMethodInvoker = customMethodInvoker;
    }

    /// <summary>
    /// Applies sorting to a queryable based on sort descriptors
    /// </summary>
    public IQueryable<TEntity> ApplySorting<TEntity>(
        IQueryable<TEntity> query,
        List<SortDescriptor>? sorts
    )
        where TEntity : class
    {
        var entityConfig = _options.GetEntityConfiguration<TEntity>();
        var effectiveSorts = BuildEffectiveSorts(sorts, entityConfig);
        if (effectiveSorts.Count == 0)
            return query;

        IOrderedQueryable<TEntity>? orderedQuery = null;
        IQueryable<TEntity> currentQuery = query;
        var hasAppliedSort = false;

        for (int i = 0; i < effectiveSorts.Count; i++)
        {
            var sort = effectiveSorts[i];
            var isFirst = !hasAppliedSort;
            var direction = sort.GetDirection();

            if (
                _customMethodInvoker?.TryApplySort(
                    currentQuery,
                    sort,
                    !isFirst,
                    out var customSortedQuery
                ) == true
            )
            {
                currentQuery = customSortedQuery;
                orderedQuery = customSortedQuery as IOrderedQueryable<TEntity>;
                hasAppliedSort = true;
                continue;
            }

            if (!isFirst && orderedQuery == null)
            {
                throw new ExpressionBuildException(
                    $"Cannot apply secondary sort '{sort.Field}' because the previous custom sort did not return an ordered query."
                );
            }

            if (sort.IsJson)
            {
                orderedQuery = ApplyJsonSort(
                    orderedQuery,
                    isFirst ? currentQuery : null,
                    sort,
                    direction
                );
                currentQuery = orderedQuery ?? currentQuery;
                hasAppliedSort = orderedQuery != null || hasAppliedSort;
                continue;
            }

            // Check if property is sortable
            if (
                entityConfig != null
                && entityConfig.Properties.TryGetValue(sort.Field, out var propConfig)
                && !propConfig.IsSortable
            )
            {
                if (_options.ThrowOnInvalidFields)
                    throw new PropertyNotSortableException(sort.Field);
                continue;
            }

            // Handle nested sorts
            if (sort.Field.Contains("."))
            {
                orderedQuery = ApplyNestedSort(
                    orderedQuery,
                    isFirst ? currentQuery : null,
                    sort,
                    direction,
                    isFirst
                );
                currentQuery = orderedQuery ?? currentQuery;
                hasAppliedSort = orderedQuery != null || hasAppliedSort;
                continue;
            }

            orderedQuery = ApplyPropertySort(
                orderedQuery,
                isFirst ? currentQuery : null,
                sort,
                direction,
                isFirst
            );
            currentQuery = orderedQuery ?? currentQuery;
            hasAppliedSort = orderedQuery != null || hasAppliedSort;
        }

        return currentQuery;
    }

    private static List<SortDescriptor> BuildEffectiveSorts(
        List<SortDescriptor>? requestedSorts,
        EntityConfiguration? entityConfig
    )
    {
        var effectiveSorts = requestedSorts?.ToList() ?? [];
        if (entityConfig?.DefaultSorts.Count is null or 0)
            return effectiveSorts;

        foreach (var defaultSort in entityConfig.DefaultSorts)
        {
            if (
                effectiveSorts.Any(sort =>
                    string.Equals(sort.Field, defaultSort.Field, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                continue;
            }

            effectiveSorts.Add(
                new SortDescriptor
                {
                    Field = defaultSort.Field,
                    Direction = defaultSort.Direction == SortDirection.Desc ? "desc" : "asc",
                }
            );
        }

        return effectiveSorts;
    }

    private IOrderedQueryable<TEntity>? ApplyPropertySort<TEntity>(
        IOrderedQueryable<TEntity>? orderedQuery,
        IQueryable<TEntity>? query,
        SortDescriptor sort,
        SortDirection direction,
        bool isFirst
    )
        where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var property = typeof(TEntity).GetProperty(
            sort.Field,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
        );

        if (property == null)
        {
            if (_options.ThrowOnInvalidFields)
                throw new PropertyNotFoundException(sort.Field, typeof(TEntity));
            return orderedQuery;
        }

        var propertyAccess = Expression.Property(parameter, property);
        var lambda = Expression.Lambda(propertyAccess, parameter);

        return ApplyOrderBy(orderedQuery, query, lambda, direction, isFirst);
    }

    private IOrderedQueryable<TEntity>? ApplyNestedSort<TEntity>(
        IOrderedQueryable<TEntity>? orderedQuery,
        IQueryable<TEntity>? query,
        SortDescriptor sort,
        SortDirection direction,
        bool isFirst
    )
        where TEntity : class
    {
        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var parts = sort.Field.Split('.');
        Expression current = parameter;
        var currentType = typeof(TEntity);

        foreach (var part in parts)
        {
            var prop = currentType.GetProperty(
                part,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
            );
            if (prop == null)
            {
                if (_options.ThrowOnInvalidFields)
                    throw new PropertyNotFoundException(part, currentType);
                return orderedQuery;
            }
            current = Expression.Property(current, prop);
            currentType = prop.PropertyType;
        }

        var lambda = Expression.Lambda(current, parameter);
        return ApplyOrderBy(orderedQuery, query, lambda, direction, isFirst);
    }

    private IOrderedQueryable<TEntity>? ApplyJsonSort<TEntity>(
        IOrderedQueryable<TEntity>? orderedQuery,
        IQueryable<TEntity>? query,
        SortDescriptor sort,
        SortDirection direction
    )
        where TEntity : class
    {
        var parts = sort.Field.Split('.');
        if (parts.Length < 2)
        {
            if (_options.ThrowOnInvalidFields)
                throw new InvalidJsonPathException(sort.Field);
            return orderedQuery;
        }

        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var jsonProp = typeof(TEntity).GetProperty(
            parts[0],
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
        );

        if (jsonProp == null)
        {
            if (_options.ThrowOnInvalidFields)
                throw new PropertyNotFoundException(parts[0], typeof(TEntity));
            return orderedQuery;
        }

        Expression jsonExpr = Expression.Property(parameter, jsonProp);

        if (jsonProp.PropertyType == typeof(JsonDocument))
        {
            var rootElementProp = typeof(JsonDocument).GetProperty("RootElement")!;
            jsonExpr = Expression.Property(jsonExpr, rootElementProp);
        }

        var getPropertyMethod = typeof(JsonElement).GetMethod(
            "GetProperty",
            new[] { typeof(string) }
        )!;
        for (int i = 1; i < parts.Length; i++)
        {
            jsonExpr = Expression.Call(jsonExpr, getPropertyMethod, Expression.Constant(parts[i]));
        }

        var getStringMethod = typeof(JsonElement).GetMethod("GetString")!;
        var stringExpr = Expression.Call(jsonExpr, getStringMethod);

        var lambda = Expression.Lambda(stringExpr, parameter);
        var isFirst = query != null;
        return ApplyOrderBy(orderedQuery, query, lambda, direction, isFirst);
    }

    private static IOrderedQueryable<TEntity>? ApplyOrderBy<TEntity>(
        IOrderedQueryable<TEntity>? orderedQuery,
        IQueryable<TEntity>? query,
        LambdaExpression keySelector,
        SortDirection direction,
        bool isFirst
    )
        where TEntity : class
    {
        var sourceQuery = isFirst ? query! : orderedQuery!;
        var methodName = isFirst
            ? (direction == SortDirection.Asc ? "OrderBy" : "OrderByDescending")
            : (direction == SortDirection.Asc ? "ThenBy" : "ThenByDescending");

        var method = typeof(Queryable)
            .GetMethods()
            .First(m => m.Name == methodName && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(TEntity), keySelector.ReturnType);

        return (IOrderedQueryable<TEntity>)method.Invoke(null, [sourceQuery, keySelector])!;
    }
}
