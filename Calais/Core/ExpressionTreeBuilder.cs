using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Calais.Configuration;
using Calais.Exceptions;
using Calais.Models;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Calais.Core
{
    /// <summary>
    /// Builds expression trees for filtering and sorting based on CalaisQuery
    /// </summary>
    public class ExpressionTreeBuilder(CalaisOptions options)
	{
		private static readonly MethodInfo StringContainsMethod = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;
		private static readonly MethodInfo StringStartsWithMethod = typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) })!;
		private static readonly MethodInfo StringEndsWithMethod = typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string) })!;
		private static readonly MethodInfo StringToLowerMethod = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
		private static readonly MethodInfo EnumerableCountMethod = typeof(Enumerable).GetMethods()
			.First(m => m.Name == nameof(Enumerable.Count) && m.GetParameters().Length == 1);
		private static readonly MethodInfo EnumerableAnyMethod = typeof(Enumerable).GetMethods()
			.First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2);
		private static readonly MethodInfo EnumerableContainsMethod = typeof(Enumerable).GetMethods()
			.First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

		// NpgsqlFullTextSearchLinqExtensions.Matches(NpgsqlTsVector, NpgsqlTsQuery) extension method
        private static readonly MethodInfo TsVectorMatchesMethod = GetTsVectorMatchesMethod();

        // NpgsqlFullTextSearchDbFunctionsExtensions.ToTsQuery(DbFunctions, string config, string query)
        private static readonly MethodInfo ToTsQueryMethod = GetToTsQueryMethod();

        private static MethodInfo GetTsVectorMatchesMethod()
        {
            try
            {
                var assembly = Assembly.Load("Npgsql.EntityFrameworkCore.PostgreSQL");
                var extensionType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "NpgsqlFullTextSearchLinqExtensions");

                if (extensionType != null)
                {
                    // Matches(NpgsqlTsVector, NpgsqlTsQuery)
                    var method = extensionType.GetMethod("Matches", 
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        [typeof(NpgsqlTsVector), typeof(NpgsqlTsQuery)],
                        null);
                    if (method != null)
                        return method;
                }
            }
            catch
            {
                // Ignore assembly loading errors
            }

            return null!;
        }

        private static MethodInfo GetToTsQueryMethod()
        {
            try
            {
                var assembly = Assembly.Load("Npgsql.EntityFrameworkCore.PostgreSQL");
                var extensionType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "NpgsqlFullTextSearchDbFunctionsExtensions");

                if (extensionType != null)
                {
                    // ToTsQuery(DbFunctions, string, string)
                    var method = extensionType.GetMethod("ToTsQuery", 
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        [typeof(DbFunctions), typeof(string), typeof(string)],
                        null);
                    if (method != null)
                        return method;
                }
            }
            catch
            {
                // Ignore assembly loading errors
            }

            return null!;
        }

        /// <summary>
        /// Builds a filter expression from filter descriptors
        /// </summary>
        public Expression<Func<TEntity, bool>>? BuildFilterExpression<TEntity>(
            List<FilterDescriptor>? filters) where TEntity : class
        {
            if (filters == null || filters.Count == 0)
                return null;

            var parameter = Expression.Parameter(typeof(TEntity), "x");
            var entityConfig = options.GetEntityConfiguration<TEntity>();

            Expression? combinedExpression = null;

            foreach (var filter in filters)
            {
                var filterExpression = BuildSingleFilterExpression<TEntity>(filter, parameter, entityConfig);
                if (filterExpression != null)
                {
                    combinedExpression = combinedExpression == null
                        ? filterExpression
                        : Expression.AndAlso(combinedExpression, filterExpression);
                }
            }

            return combinedExpression == null 
	            ? null 
	            : Expression.Lambda<Func<TEntity, bool>>(combinedExpression, parameter);
        }

        private Expression? BuildSingleFilterExpression<TEntity>(
            FilterDescriptor filter,
            ParameterExpression parameter,
            EntityConfiguration? entityConfig) where TEntity : class
        {
            // Handle OR groups
            if (filter.IsOrGroup)
            {
                return BuildOrGroupExpression<TEntity>(filter.Or!, parameter, entityConfig);
            }

            if (string.IsNullOrEmpty(filter.Field))
                return null;

            // Handle vector (full-text search) fields
            if (filter.IsVector)
            {
                return BuildVectorExpression<TEntity>(filter, parameter, entityConfig);
            }

            // Handle JSON fields
            if (filter.IsJson)
            {
                return BuildJsonFilterExpression<TEntity>(filter, parameter);
            }

            // Handle length operators
            if (filter.Operator?.StartsWith("len") == true)
            {
                return BuildLengthExpression<TEntity>(filter, parameter);
            }

            // Handle nested paths (e.g., "comments.text")
            if (filter.Field!.Contains("."))
            {
                return BuildNestedFilterExpression<TEntity>(filter, parameter, entityConfig);
            }

            // Check if property is filterable
            if (entityConfig != null && 
                entityConfig.Properties.TryGetValue(filter.Field!, out var propConfig) && 
                !propConfig.IsFilterable)
            {
	            return options.ThrowOnInvalidFields 
		            ? throw new PropertyNotFilterableException(filter.Field!) 
		            : null;
            }

            // Check for custom filter
            if (entityConfig?.CustomFilters.TryGetValue(filter.Field!, out var customFilter) == true)
            {
                return RebindExpression(customFilter, parameter);
            }

            return BuildPropertyFilterExpression<TEntity>(filter, parameter);
        }

        private Expression? BuildOrGroupExpression<TEntity>(
            List<FilterDescriptor> orFilters,
            ParameterExpression parameter,
            EntityConfiguration? entityConfig) where TEntity : class
        {
            Expression? orExpression = null;

            foreach (var filter in orFilters)
            {
                var filterExpr = BuildSingleFilterExpression<TEntity>(filter, parameter, entityConfig);
                if (filterExpr != null)
                {
                    orExpression = orExpression == null
                        ? filterExpr
                        : Expression.OrElse(orExpression, filterExpr);
                }
            }

            return orExpression;
        }

        private Expression? BuildPropertyFilterExpression<TEntity>(
            FilterDescriptor filter,
            ParameterExpression parameter)
        {
            var property = GetPropertyExpression(typeof(TEntity), filter.Field!, parameter);
            if (property == null)
            {
	            return options.ThrowOnInvalidFields 
		            ? throw new PropertyNotFoundException(filter.Field!, typeof(TEntity)) 
		            : null;
            }

            if (filter.Values == null || filter.Values.Count == 0)
                return null;

            // For != operator with multiple values, use AND (must not match any)
            // For other operators with multiple values, use OR (must match at least one)
            var useAnd = filter.Operator is "!=" or "!=*";
            
            Expression? valueExpression = null;
            foreach (var value in filter.Values)
            {
                var singleValueExpr = BuildComparisonExpression(property, filter.Operator ?? "==", value);
                if (singleValueExpr != null)
                {
                    if (valueExpression == null)
                    {
                        valueExpression = singleValueExpr;
                    }
                    else if (useAnd)
                    {
                        valueExpression = Expression.AndAlso(valueExpression, singleValueExpr);
                    }
                    else
                    {
                        valueExpression = Expression.OrElse(valueExpression, singleValueExpr);
                    }
                }
            }

            return valueExpression;
        }

        private Expression? BuildNestedFilterExpression<TEntity>(
            FilterDescriptor filter,
            ParameterExpression parameter,
            EntityConfiguration? entityConfig) where TEntity : class
        {
            var parts = filter.Field!.Split('.');
            var currentType = typeof(TEntity);
            Expression currentExpr = parameter;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                var propInfo = currentType.GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (propInfo == null)
                {
	                return options.ThrowOnInvalidFields 
		                ? throw new PropertyNotFoundException(parts[i], currentType) 
		                : null;
                }

                currentExpr = Expression.Property(currentExpr, propInfo);
                var propType = propInfo.PropertyType;

                // Check if it's a collection
                if (propType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(propType))
                {
                    var elementType = propType.IsGenericType
                        ? propType.GetGenericArguments()[0]
                        : propType.GetElementType();

                    if (elementType == null)
                        return null;

                    // Build inner expression for Any()
                    var remainingPath = string.Join(".", parts.Skip(i + 1));
                    var innerFilter = new FilterDescriptor
                    {
                        Field = remainingPath,
                        Operator = filter.Operator,
                        Values = filter.Values,
                        IsVector = filter.IsVector,
                        IsJson = filter.IsJson
                    };

                    var innerParam = Expression.Parameter(elementType, "inner");
                    var innerExpr = BuildNestedOrDirectFilter(innerFilter, innerParam, elementType);

                    if (innerExpr == null)
                        return null;

                    var anyMethod = EnumerableAnyMethod.MakeGenericMethod(elementType);
                    var lambda = Expression.Lambda(innerExpr, innerParam);
                    return Expression.Call(anyMethod, currentExpr, lambda);
                }

                currentType = propType;
            }

            // Final property
            var finalProp = currentType.GetProperty(parts[^1], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (finalProp == null)
            {
                if (options.ThrowOnInvalidFields)
                    throw new PropertyNotFoundException(parts[^1], currentType);
                return null;
            }

            var finalExpr = Expression.Property(currentExpr, finalProp);
            return BuildValuesOrExpression(finalExpr, filter.Operator ?? "==", filter.Values!);
        }

        private Expression? BuildNestedOrDirectFilter(FilterDescriptor filter, ParameterExpression param, Type entityType)
        {
            if (filter.Field!.Contains("."))
            {
                var parts = filter.Field.Split('.');
                Expression current = param;
                var currentType = entityType;

                for (int i = 0; i < parts.Length - 1; i++)
                {
                    var prop = currentType.GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop == null) return null;

                    current = Expression.Property(current, prop);
                    var propType = prop.PropertyType;

                    if (propType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(propType))
                    {
                        var elemType = propType.IsGenericType ? propType.GetGenericArguments()[0] : propType.GetElementType();
                        if (elemType == null) return null;

                        var remaining = string.Join(".", parts.Skip(i + 1));
                        var innerFilter = new FilterDescriptor
                        {
                            Field = remaining,
                            Operator = filter.Operator,
                            Values = filter.Values,
                            IsVector = filter.IsVector,
                            IsJson = filter.IsJson
                        };
                        var innerParam = Expression.Parameter(elemType, "i" + i);
                        var innerExpr = BuildNestedOrDirectFilter(innerFilter, innerParam, elemType);
                        if (innerExpr == null) return null;

                        var anyMethod = EnumerableAnyMethod.MakeGenericMethod(elemType);
                        return Expression.Call(anyMethod, current, Expression.Lambda(innerExpr, innerParam));
                    }
                    currentType = propType;
                }

                var finalProp = currentType.GetProperty(parts[^1], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (finalProp == null) return null;

                var finalExpr = Expression.Property(current, finalProp);

                return filter.IsVector 
	                ? BuildVectorMatchExpression(finalExpr, filter.Values!) 
	                : BuildValuesOrExpression(finalExpr, filter.Operator ?? "==", filter.Values!);
            }

            var directProp = entityType.GetProperty(filter.Field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (directProp == null) return null;

            var directExpr = Expression.Property(param, directProp);

            return filter.IsVector 
	            ? BuildVectorMatchExpression(directExpr, filter.Values!) 
	            : BuildValuesOrExpression(directExpr, filter.Operator ?? "==", filter.Values!);
        }

        private Expression? BuildVectorExpression<TEntity>(
            FilterDescriptor filter,
            ParameterExpression parameter,
            EntityConfiguration? entityConfig) where TEntity : class
        {
            if (filter.Field!.Contains("."))
            {
                return BuildNestedFilterExpression<TEntity>(filter, parameter, entityConfig);
            }

            var property = GetPropertyExpression(typeof(TEntity), filter.Field!, parameter);
            if (property == null)
            {
                if (options.ThrowOnInvalidFields)
                    throw new PropertyNotFoundException(filter.Field!, typeof(TEntity));
                return null;
            }

            return BuildVectorMatchExpression(property, filter.Values!);
        }

        private Expression? BuildVectorMatchExpression(Expression vectorProperty, List<object> values)
        {
            if (values == null || values.Count == 0)
                return null;

            // Use pattern: Vector.Matches(EF.Functions.ToTsQuery("language", "query"))
            // This is properly translated by Npgsql EF Core provider
            
            if (TsVectorMatchesMethod == null || ToTsQueryMethod == null)
                return null;

            var language = options.DefaultVectorLanguage;
            
            // EF.Functions property access
            var efFunctionsProperty = typeof(EF).GetProperty(nameof(EF.Functions), BindingFlags.Public | BindingFlags.Static)!;
            var efFunctionsExpr = Expression.Property(null, efFunctionsProperty);

            Expression? orExpr = null;
            foreach (var value in values)
            {
                var tsQueryValue = value?.ToString();
                if (string.IsNullOrEmpty(tsQueryValue))
                    continue;

                // Create: EF.Functions.ToTsQuery(language, tsQueryValue)
                var toTsQueryExpr = Expression.Call(
                    ToTsQueryMethod,
                    efFunctionsExpr,
                    Expression.Constant(language),
                    Expression.Constant(tsQueryValue));

                // Create: NpgsqlFullTextSearchLinqExtensions.Matches(vectorProperty, toTsQueryExpr)
                // Since Matches is a static extension method, we use Expression.Call with the static method
                var matchExpr = Expression.Call(TsVectorMatchesMethod, vectorProperty, toTsQueryExpr);

                orExpr = orExpr == null ? (Expression)matchExpr : Expression.OrElse(orExpr, matchExpr);
            }

            return orExpr;
        }

        private Expression? BuildJsonFilterExpression<TEntity>(
            FilterDescriptor filter,
            ParameterExpression parameter)
        {
            var parts = filter.Field!.Split('.');
            if (parts.Length < 2)
            {
                if (options.ThrowOnInvalidFields)
                    throw new InvalidJsonPathException(filter.Field!);
                return null;
            }

            // Get the JsonDocument property
            var jsonProp = typeof(TEntity).GetProperty(parts[0], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (jsonProp == null)
            {
                if (options.ThrowOnInvalidFields)
                    throw new PropertyNotFoundException(parts[0], typeof(TEntity));
                return null;
            }

            Expression jsonExpr = Expression.Property(parameter, jsonProp);

            // Navigate through JSON path: JsonDocument.RootElement.GetProperty("path")
            if (jsonProp.PropertyType == typeof(JsonDocument))
            {
                // Access RootElement
                var rootElementProp = typeof(JsonDocument).GetProperty("RootElement")!;
                jsonExpr = Expression.Property(jsonExpr, rootElementProp);
            }

            // Navigate nested JSON properties
            var getPropertyMethod = typeof(JsonElement).GetMethod("GetProperty", new[] { typeof(string) })!;
            for (int i = 1; i < parts.Length; i++)
            {
                jsonExpr = Expression.Call(jsonExpr, getPropertyMethod, Expression.Constant(parts[i]));
            }

            // Get string value for comparison
            var getStringMethod = typeof(JsonElement).GetMethod("GetString")!;
            var stringExpr = Expression.Call(jsonExpr, getStringMethod);

            return BuildValuesOrExpression(stringExpr, filter.Operator ?? "==", filter.Values!);
        }

        private Expression? BuildLengthExpression<TEntity>(
            FilterDescriptor filter,
            ParameterExpression parameter)
        {
            var propInfo = typeof(TEntity).GetProperty(filter.Field!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propInfo == null)
            {
                if (options.ThrowOnInvalidFields)
                    throw new PropertyNotFoundException(filter.Field!, typeof(TEntity));
                return null;
            }

            var propExpr = Expression.Property(parameter, propInfo);
            Expression lengthExpr;

            if (propInfo.PropertyType == typeof(string))
            {
                lengthExpr = Expression.Property(propExpr, "Length");
            }
            else if (typeof(IEnumerable).IsAssignableFrom(propInfo.PropertyType) && propInfo.PropertyType != typeof(string))
            {
                var elementType = propInfo.PropertyType.IsGenericType
                    ? propInfo.PropertyType.GetGenericArguments()[0]
                    : typeof(object);
                var countMethod = EnumerableCountMethod.MakeGenericMethod(elementType);
                lengthExpr = Expression.Call(countMethod, propExpr);
            }
            else
            {
                return null;
            }

            var op = filter.Operator!.Substring(3); // Remove "len" prefix
            if (filter.Values == null || filter.Values.Count == 0)
                return null;

            var value = Convert.ToInt32(filter.Values[0]);
            var valueExpr = Expression.Constant(value);

            return op switch
            {
                "==" => Expression.Equal(lengthExpr, valueExpr),
                "!=" => Expression.NotEqual(lengthExpr, valueExpr),
                ">" => Expression.GreaterThan(lengthExpr, valueExpr),
                "<" => Expression.LessThan(lengthExpr, valueExpr),
                ">=" => Expression.GreaterThanOrEqual(lengthExpr, valueExpr),
                "<=" => Expression.LessThanOrEqual(lengthExpr, valueExpr),
                _ => null
            };
        }

        private Expression? BuildValuesOrExpression(Expression property, string op, List<object> values)
        {
            Expression? result = null;
            foreach (var value in values)
            {
                var comparison = BuildComparisonExpression(property, op, value);
                if (comparison != null)
                {
                    result = result == null ? comparison : Expression.OrElse(result, comparison);
                }
            }
            return result;
        }

        private Expression? BuildComparisonExpression(Expression property, string op, object value)
        {
            var isIgnoreCase = op.EndsWith("*");
            var baseOp = isIgnoreCase ? op.TrimEnd('*') : op;

            // Check if property is an array/collection for contains operators
            if (baseOp is "@=" or "!@=")
            {
                var arrayExpr = TryBuildArrayContainsExpression(property, value, baseOp == "!@=", isIgnoreCase);
                if (arrayExpr != null)
                    return arrayExpr;
            }

            var convertedValue = ConvertValue(value, property.Type);
            if (convertedValue == null && property.Type.IsValueType && Nullable.GetUnderlyingType(property.Type) == null)
                return null;

            var valueExpr = Expression.Constant(convertedValue, property.Type);

            Expression left = property;
            Expression right = valueExpr;

            if (isIgnoreCase && property.Type == typeof(string))
            {
                left = Expression.Call(property, StringToLowerMethod);
                right = Expression.Call(valueExpr, StringToLowerMethod);
            }

            return baseOp switch
            {
                "==" => Expression.Equal(left, right),
                "!=" => Expression.NotEqual(left, right),
                ">" => Expression.GreaterThan(left, right),
                "<" => Expression.LessThan(left, right),
                ">=" => Expression.GreaterThanOrEqual(left, right),
                "<=" => Expression.LessThanOrEqual(left, right),
                "@=" => BuildStringMethodCall(left, right, StringContainsMethod, false),
                "_=" => BuildStringMethodCall(left, right, StringStartsWithMethod, false),
                "_-=" => BuildStringMethodCall(left, right, StringEndsWithMethod, false),
                "!@=" => BuildStringMethodCall(left, right, StringContainsMethod, true),
                "!_=" => BuildStringMethodCall(left, right, StringStartsWithMethod, true),
                "!_-=" => BuildStringMethodCall(left, right, StringEndsWithMethod, true),
                _ => null
            };
        }

        private static Expression? TryBuildArrayContainsExpression(Expression property, object value, bool negate, bool ignoreCase)
        {
            var propertyType = property.Type;

            // Skip if it's a string (handled by string.Contains)
            if (propertyType == typeof(string))
                return null;

            // Check if it's an array or implements IEnumerable<T>
            Type? elementType = null;
            if (propertyType.IsArray)
            {
                elementType = propertyType.GetElementType();
            }
            else if (propertyType.IsGenericType && typeof(IEnumerable).IsAssignableFrom(propertyType))
            {
                elementType = propertyType.GetGenericArguments().FirstOrDefault();
            }

            if (elementType == null)
                return null;

            // Convert the value to the element type
            var convertedValue = ConvertValue(value, elementType);
            if (convertedValue == null && elementType.IsValueType && Nullable.GetUnderlyingType(elementType) == null)
                return null;

            var valueExpr = Expression.Constant(convertedValue, elementType);

            // Build Enumerable.Contains(array, value)
            var containsMethod = EnumerableContainsMethod.MakeGenericMethod(elementType);
            Expression containsCall = Expression.Call(containsMethod, property, valueExpr);

            if (negate)
            {
                containsCall = Expression.Not(containsCall);
            }

            return containsCall;
        }

        private static Expression BuildStringMethodCall(Expression property, Expression value, MethodInfo method, bool negate)
        {
            var call = Expression.Call(property, method, value);
            if (negate)
            {
                return Expression.Not(call);
            }
            return call;
        }

        private static object? ConvertValue(object value, Type targetType)
        {
            if (value == null)
                return null;

            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (value is JsonElement jsonElement)
            {
                return ConvertJsonElement(jsonElement, underlyingType);
            }

            if (underlyingType == typeof(Guid) && value is string guidStr)
            {
                return Guid.Parse(guidStr);
            }

            if (underlyingType == typeof(DateTime) && value is string dateStr)
            {
                return DateTime.Parse(dateStr);
            }

            if (underlyingType == typeof(DateTimeOffset) && value is string dtoStr)
            {
                return DateTimeOffset.Parse(dtoStr);
            }

            if (underlyingType.IsEnum && value is string enumStr)
            {
                return Enum.Parse(underlyingType, enumStr, true);
            }

            return Convert.ChangeType(value, underlyingType);
        }

        private static object? ConvertJsonElement(JsonElement element, Type targetType)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String when targetType == typeof(string) => element.GetString(),
                JsonValueKind.String when targetType == typeof(Guid) => element.GetGuid(),
                JsonValueKind.String when targetType == typeof(DateTime) => element.GetDateTime(),
                JsonValueKind.String when targetType == typeof(DateTimeOffset) => element.GetDateTimeOffset(),
                JsonValueKind.Number when targetType == typeof(int) => element.GetInt32(),
                JsonValueKind.Number when targetType == typeof(long) => element.GetInt64(),
                JsonValueKind.Number when targetType == typeof(double) => element.GetDouble(),
                JsonValueKind.Number when targetType == typeof(decimal) => element.GetDecimal(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => element.GetRawText()
            };
        }

        private static MemberExpression? GetPropertyExpression(Type type, string propertyName, Expression parameter)
        {
            var propInfo = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return propInfo != null ? Expression.Property(parameter, propInfo) : null;
        }

        private static Expression RebindExpression(LambdaExpression expression, ParameterExpression newParameter)
        {
            var replacer = new ParameterReplacer(expression.Parameters[0], newParameter);
            return replacer.Visit(expression.Body);
        }

        private class ParameterReplacer(ParameterExpression oldParameter, ParameterExpression newParameter)
	        : ExpressionVisitor
        {
	        protected override Expression VisitParameter(ParameterExpression node)
            {
                return node == oldParameter ? newParameter : base.VisitParameter(node);
            }
        }
    }
}
