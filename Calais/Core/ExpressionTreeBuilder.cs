using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Calais.Configuration;
using Calais.Exceptions;
using Calais.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using NpgsqlTypes;

namespace Calais.Core;

/// <summary>
/// Builds expression trees for filtering and sorting based on CalaisQuery
/// </summary>
public class ExpressionTreeBuilder(CalaisOptions options)
{
	private readonly Expression? _querySource;
	private IModel? _queryModel;

	internal ExpressionTreeBuilder(CalaisOptions options, Expression querySource)
		: this(options)
	{
		_querySource = querySource;
	}

	private static readonly ConditionalWeakTable<Type, Type[]> DerivedTypes = new();
	private static readonly ConditionalWeakTable<
		Type,
		Func<object, Expression>
	> ParameterFactories = new();
	private static readonly MethodInfo CaptureParameterMethod = (
		(Func<object, Expression>)CaptureParameter<object>
	).Method.GetGenericMethodDefinition();
	private static readonly MethodInfo StringContainsMethod = typeof(string).GetMethod(
		nameof(string.Contains),
		[typeof(string)]
	)!;
	private static readonly MethodInfo StringStartsWithMethod = typeof(string).GetMethod(
		nameof(string.StartsWith),
		[typeof(string)]
	)!;
	private static readonly MethodInfo StringEndsWithMethod = typeof(string).GetMethod(
		nameof(string.EndsWith),
		[typeof(string)]
	)!;
	private static readonly MethodInfo StringToLowerMethod = typeof(string).GetMethod(
		nameof(string.ToLower),
		Type.EmptyTypes
	)!;
	private static readonly MethodInfo RegexIsMatchMethod = typeof(Regex).GetMethod(
		name: nameof(Regex.IsMatch),
		[typeof(string), typeof(string)]
	)!;
	private static readonly MethodInfo RegexIsMatchIgnoreCaseMethod = typeof(Regex).GetMethod(
		name: nameof(Regex.IsMatch),
		[typeof(string), typeof(string), typeof(RegexOptions)]
	)!;
	private static readonly MethodInfo EnumerableCountMethod = typeof(Enumerable)
		.GetMethods()
		.First(m => m.Name == nameof(Enumerable.Count) && m.GetParameters().Length == 1);
	private static readonly MethodInfo EnumerableAnyWithoutPredicateMethod = (
		(Func<IEnumerable<object>, bool>)Enumerable.Any
	).Method.GetGenericMethodDefinition();
	private static readonly MethodInfo EnumerableAnyMethod = typeof(Enumerable)
		.GetMethods()
		.First(m => m.Name == nameof(Enumerable.Any) && m.GetParameters().Length == 2);
	private static readonly MethodInfo EnumerableContainsMethod = typeof(Enumerable)
		.GetMethods()
		.First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2);

	private static readonly MethodInfo TsVectorMatchesMethod = (
		(Func<NpgsqlTsVector, NpgsqlTsQuery, bool>)NpgsqlFullTextSearchLinqExtensions.Matches
	).Method;
	private static readonly MethodInfo TsQueryOrMethod = (
		(Func<NpgsqlTsQuery, NpgsqlTsQuery, NpgsqlTsQuery>)NpgsqlFullTextSearchLinqExtensions.Or
	).Method;
	private static readonly MemberExpression EfFunctions = Expression.Property(
		null,
		typeof(EF),
		nameof(EF.Functions)
	);
	private static readonly MethodInfo ToTsQueryMethod = (
		(Func<DbFunctions, string, string, NpgsqlTsQuery>)
			NpgsqlFullTextSearchDbFunctionsExtensions.ToTsQuery
	).Method;

	/// <summary>
	/// Builds a filter expression from filter descriptors
	/// </summary>
	public Expression<Func<TEntity, bool>>? BuildFilterExpression<TEntity>(
		List<FilterDescriptor>? filters
	)
		where TEntity : class
	{
		if (filters == null || filters.Count == 0)
			return null;

		var parameter = Expression.Parameter(typeof(TEntity), "x");
		var entityConfig = options.GetEntityConfiguration<TEntity>();

		Expression? combinedExpression = null;

		foreach (var filter in filters)
		{
			var filterExpression = BuildSingleFilterExpression<TEntity>(
				filter,
				parameter,
				entityConfig
			);
			if (filterExpression != null)
			{
				combinedExpression =
					combinedExpression == null
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
		EntityConfiguration? entityConfig
	)
		where TEntity : class
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
		if (
			entityConfig != null
			&& entityConfig.Properties.TryGetValue(filter.Field!, out var propConfig)
			&& !propConfig.IsFilterable
		)
		{
			return options.ThrowOnInvalidFields
				? throw new PropertyNotFilterableException(filter.Field!)
				: null;
		}

		return BuildPropertyFilterExpression<TEntity>(filter, parameter);
	}

	private Expression? BuildOrGroupExpression<TEntity>(
		List<FilterDescriptor> orFilters,
		ParameterExpression parameter,
		EntityConfiguration? entityConfig
	)
		where TEntity : class
	{
		Expression? orExpression = null;

		foreach (var filter in orFilters)
		{
			var filterExpr = BuildSingleFilterExpression<TEntity>(filter, parameter, entityConfig);
			if (filterExpr != null)
			{
				orExpression =
					orExpression == null ? filterExpr : Expression.OrElse(orExpression, filterExpr);
			}
		}

		return orExpression;
	}

	private Expression? BuildPropertyFilterExpression<TEntity>(
		FilterDescriptor filter,
		ParameterExpression parameter
	)
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

		return BuildValuesExpression(property, filter.Operator ?? "==", filter.Values);
	}

	private Expression? BuildNestedFilterExpression<TEntity>(
		FilterDescriptor filter,
		ParameterExpression parameter,
		EntityConfiguration? entityConfig
	)
		where TEntity : class
	{
		return BuildPathFilterExpression(
			typeof(TEntity),
			parameter,
			filter.Field!.Split('.'),
			0,
			filter
		);
	}

	private Expression? BuildNestedOrDirectFilter(
		FilterDescriptor filter,
		ParameterExpression param,
		Type entityType
	)
	{
		return BuildPathFilterExpression(entityType, param, filter.Field!.Split('.'), 0, filter);
	}

	private Expression? BuildPathFilterExpression(
		Type currentType,
		Expression currentExpression,
		string[] pathParts,
		int partIndex,
		FilterDescriptor filter,
		PropertyInfo? property = null
	)
	{
		property ??= currentType.GetProperty(
			pathParts[partIndex],
			BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
		);

		if (property == null)
			return BuildDerivedTypePathFilterExpression(
				currentType,
				currentExpression,
				pathParts,
				partIndex,
				filter
			);

		var propertyExpression = Expression.Property(currentExpression, property);

		if (partIndex == pathParts.Length - 1)
		{
			return filter.IsVector
				? BuildVectorMatchExpression(propertyExpression, filter.Values!)
				: BuildValuesExpression(
					propertyExpression,
					filter.Operator ?? "==",
					filter.Values!
				);
		}

		var propertyType = property.PropertyType;
		if (TryGetEnumerableElementType(propertyType, out var elementType))
		{
			var innerParameter = Expression.Parameter(elementType, $"i{partIndex}");
			var innerExpression = BuildPathFilterExpression(
				elementType,
				innerParameter,
				pathParts,
				partIndex + 1,
				filter
			);

			if (innerExpression == null)
				return null;

			var anyMethod = EnumerableAnyMethod.MakeGenericMethod(elementType);
			return Expression.Call(
				anyMethod,
				propertyExpression,
				Expression.Lambda(innerExpression, innerParameter)
			);
		}

		return BuildPathFilterExpression(
			propertyType,
			propertyExpression,
			pathParts,
			partIndex + 1,
			filter
		);
	}

	private Expression? BuildDerivedTypePathFilterExpression(
		Type currentType,
		Expression currentExpression,
		string[] pathParts,
		int partIndex,
		FilterDescriptor filter
	)
	{
		var baseEntity = (_queryModel ??= FindQueryModel(_querySource))?.FindEntityType(
			currentType
		);
		var propertyName = pathParts[partIndex];
		var candidates =
			baseEntity == null
				? GetDerivedTypesWithProperty(currentType, propertyName)
				: GetMappedDerivedTypesWithProperty(baseEntity, propertyName);
		Expression? result = null;
		var foundProperty = false;
		foreach (var (derivedType, property) in candidates)
		{
			foundProperty = true;
			var typeCheck = Expression.TypeIs(currentExpression, derivedType);
			var convertedExpression = Expression.Convert(currentExpression, derivedType);
			var derivedExpression = BuildPathFilterExpression(
				derivedType,
				convertedExpression,
				pathParts,
				partIndex,
				filter,
				property
			);

			if (derivedExpression == null)
				continue;

			var guardedExpression = Expression.AndAlso(typeCheck, derivedExpression);
			result =
				result == null ? guardedExpression : Expression.OrElse(result, guardedExpression);
		}

		if (!foundProperty)
		{
			if (baseEntity != null)
			{
				// A valid filter confined to unmapped subclasses cannot match a database row.
				var clrFilter = new ExpressionTreeBuilder(
					options
				).BuildDerivedTypePathFilterExpression(
					currentType,
					currentExpression,
					pathParts,
					partIndex,
					filter
				);
				return clrFilter == null ? null : Expression.Constant(false);
			}
			if (options.ThrowOnInvalidFields)
				throw new PropertyNotFoundException(propertyName, currentType);
		}

		return result;
	}

	private static IModel? FindQueryModel(Expression? source)
	{
		if (source is EntityQueryRootExpression root)
			return root.EntityType.Model;
		if (source is MethodCallExpression call)
			foreach (var argument in call.Arguments)
				if (FindQueryModel(argument) is { } model)
					return model;
		return null;
	}

	private static IEnumerable<(
		Type DerivedType,
		PropertyInfo Property
	)> GetMappedDerivedTypesWithProperty(IEntityType baseEntity, string propertyName)
	{
		foreach (var entity in baseEntity.GetDerivedTypes())
		{
			var property = entity.ClrType.GetProperty(
				propertyName,
				BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
			);
			if (
				property != null
				&& entity.FindMember(property.Name) is { PropertyInfo: { } mappedProperty } member
				&& member.DeclaringType == entity
			)
				yield return (entity.ClrType, mappedProperty);
		}
	}

	private static IEnumerable<(
		Type DerivedType,
		PropertyInfo Property
	)> GetDerivedTypesWithProperty(Type baseType, string propertyName)
	{
		if (baseType.IsSealed)
			yield break;

		// Emitted assemblies can acquire new subclasses between requests.
		var types = baseType.Assembly.IsDynamic
			? FindDerivedTypes(baseType)
			: DerivedTypes.GetValue(baseType, FindDerivedTypes);
		foreach (var type in types)
		{
			var property = type.GetProperty(
				propertyName,
				BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
			);
			if (property == null)
				continue;

			// The declaring type's branch already covers descendants inheriting this property.
			if (property.DeclaringType != type && types.Contains(property.DeclaringType))
				continue;

			yield return (type, property);
		}
	}

	private static Type[] FindDerivedTypes(Type baseType) =>
		baseType
			.Assembly.GetTypes()
			.Where(t => t != baseType && baseType.IsAssignableFrom(t))
			.ToArray();

	private static bool TryGetEnumerableElementType(Type type, out Type elementType)
	{
		elementType = null!;

		if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
			return false;

		if (type.IsArray)
		{
			elementType = type.GetElementType()!;
			return true;
		}

		if (type.IsGenericType)
		{
			elementType = type.GetGenericArguments()[0];
			return true;
		}

		var enumerableInterface = type.GetInterfaces()
			.FirstOrDefault(i =>
				i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
			);

		if (enumerableInterface == null)
			return false;

		elementType = enumerableInterface.GetGenericArguments()[0];
		return true;
	}

	private Expression? BuildVectorExpression<TEntity>(
		FilterDescriptor filter,
		ParameterExpression parameter,
		EntityConfiguration? entityConfig
	)
		where TEntity : class
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

		var language = Expression.Constant(options.DefaultVectorLanguage);
		Expression? combinedQuery = null;
		foreach (var value in values)
		{
			var tsQueryValue = value?.ToString();
			if (string.IsNullOrEmpty(tsQueryValue))
				continue;

			var query = Expression.Call(
				ToTsQueryMethod,
				EfFunctions,
				language,
				FilterValue(tsQueryValue, typeof(string))
			);

			combinedQuery =
				combinedQuery == null
					? query
					: Expression.Call(TsQueryOrMethod, combinedQuery, query);
		}

		return combinedQuery == null
			? null
			: Expression.Call(TsVectorMatchesMethod, vectorProperty, combinedQuery);
	}

	private Expression? BuildJsonFilterExpression<TEntity>(
		FilterDescriptor filter,
		ParameterExpression parameter
	)
	{
		var parts = filter.Field!.Split('.');
		if (parts.Length < 2)
			return options.ThrowOnInvalidFields
				? throw new InvalidJsonPathException(filter.Field!)
				: null;

		var propType = typeof(TEntity);
		Expression jsonExpr = parameter;
		int index;
		// Get the JsonDocument, JsonElement or JsonNode property
		for (index = 0; index < parts.Length; index++)
		{
			var prop = propType.GetProperty(
				parts[index],
				BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
			);
			if (prop == null)
				return options.ThrowOnInvalidFields
					? throw new PropertyNotFoundException(parts[index], propType)
					: null;

			jsonExpr = Expression.Property(jsonExpr, prop);
			propType = prop.PropertyType;
			if (
				propType.IsAssignableTo(typeof(JsonDocument))
				|| propType.IsAssignableTo(typeof(JsonElement))
				|| propType.IsAssignableTo(typeof(JsonNode))
			)
			{
				break;
			}
		}

		Expression stringExpr;
		if (propType.IsAssignableTo(typeof(JsonNode)))
		{
			// Navigate nested JSON properties or indexes
			var getPropertyMethod = typeof(JsonNode).GetMethod("get_Item", [typeof(string)])!;
			var getIndexMethod = typeof(JsonNode).GetMethod("get_Item", [typeof(int)])!;

			for (index++; index < parts.Length; index++)
			{
				if (int.TryParse(parts[index], out var i))
				{
					jsonExpr = Expression.Call(jsonExpr, getIndexMethod, Expression.Constant(i));
				}
				else
				{
					jsonExpr = Expression.Call(
						jsonExpr,
						getPropertyMethod,
						Expression.Constant(parts[i])
					);
				}
			}

			// Get string value for comparison
			var toStringMethod = typeof(JsonNode).GetMethod("ToString")!;
			stringExpr = Expression.Call(jsonExpr, toStringMethod);
		}
		else
		{
			// Navigate through JSON path: JsonDocument.RootElement.GetProperty("path")
			if (propType.IsAssignableTo(typeof(JsonDocument)))
			{
				// Access RootElement
				var rootElementProp = typeof(JsonDocument).GetProperty("RootElement")!;
				jsonExpr = Expression.Property(jsonExpr, rootElementProp);
			}

			// Navigate nested JSON properties
			var getPropertyMethod = typeof(JsonElement).GetMethod("GetProperty", [typeof(string)])!;
			for (index++; index < parts.Length; index++)
			{
				jsonExpr = Expression.Call(
					jsonExpr,
					getPropertyMethod,
					Expression.Constant(parts[index])
				);
			}

			// Get string value for comparison
			var getStringMethod = typeof(JsonElement).GetMethod("GetString")!;
			stringExpr = Expression.Call(jsonExpr, getStringMethod);
		}

		return BuildValuesExpression(stringExpr, filter.Operator ?? "==", filter.Values!);
	}

	private Expression? BuildLengthExpression<TEntity>(
		FilterDescriptor filter,
		ParameterExpression parameter
	)
	{
		var propInfo = typeof(TEntity).GetProperty(
			filter.Field!,
			BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
		);
		if (propInfo == null)
		{
			if (options.ThrowOnInvalidFields)
				throw new PropertyNotFoundException(filter.Field!, typeof(TEntity));
			return null;
		}

		if (filter.Values == null || filter.Values.Count == 0)
			return null;

		var op = filter.Operator![3..];
		var propExpr = Expression.Property(parameter, propInfo);
		if (propInfo.PropertyType == typeof(string))
			return BuildValuesExpression(
				Expression.Property(propExpr, "Length"),
				op,
				filter.Values
			);

		if (!typeof(IEnumerable).IsAssignableFrom(propInfo.PropertyType))
			return null;

		var elementType = propInfo.PropertyType.IsGenericType
			? propInfo.PropertyType.GetGenericArguments()[0]
			: typeof(object);
		var count = Expression.Call(EnumerableCountMethod.MakeGenericMethod(elementType), propExpr);
		return BuildValuesExpression(count, op, filter.Values);
	}

	private static Expression? BuildValuesExpression(
		Expression property,
		string op,
		List<object> values
	)
	{
		var type = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
		// For these keys Enumerable.Contains has the same equality semantics as ==.
		if (
			values.Count > 1
			&& op is "==" or "!="
			&& (
				type == typeof(string)
				|| type == typeof(Guid)
				|| Type.GetTypeCode(type) is >= TypeCode.Boolean and <= TypeCode.UInt64
			)
			&& !values.Contains(null!)
		)
		{
			var keys = Array.CreateInstance(property.Type, values.Count);
			for (var i = 0; i < values.Count; i++)
			{
				var key = ConvertValue(values[i], property.Type);
				if (!property.Type.IsInstanceOfType(key))
					throw new ArgumentException($"Invalid filter value for {property.Type}.");
				keys.SetValue(key, i);
			}

			var contains = Expression.Call(
				EnumerableContainsMethod.MakeGenericMethod(property.Type),
				FilterValue(keys, keys.GetType()),
				property
			);
			return op == "!=" ? Expression.Not(contains) : contains;
		}

		var useAnd = op.TrimEnd('*') is "!=" or "!@=" or "!_=" or "!_-=";
		Expression? result = null;
		foreach (var value in values)
		{
			var comparison = BuildComparisonExpression(property, op, value);
			if (comparison != null)
			{
				result =
					result == null ? comparison
					: useAnd ? Expression.AndAlso(result, comparison)
					: Expression.OrElse(result, comparison);
			}
		}
		return result;
	}

	private static Expression? BuildComparisonExpression(
		Expression property,
		string op,
		object value
	)
	{
		var isIgnoreCase = op.EndsWith('*');
		var baseOp = isIgnoreCase ? op.TrimEnd('*') : op;

		// Check if property is an array/collection for contains operators
		if (baseOp is "@=" or "!@=")
		{
			var arrayExpr = TryBuildArrayContainsExpression(
				property,
				value,
				baseOp == "!@=",
				isIgnoreCase
			);
			if (arrayExpr != null)
				return arrayExpr;
		}

		var convertedValue = ConvertValue(value, property.Type);
		if (
			convertedValue == null
			&& property.Type.IsValueType
			&& Nullable.GetUnderlyingType(property.Type) == null
		)
			return null;

		if (
			property is MethodCallExpression { Method.Name: nameof(Enumerable.Count) } count
			&& ((baseOp == ">" && convertedValue is 0) || (baseOp == ">=" && convertedValue is 1))
		)
			return Expression.Call(
				EnumerableAnyWithoutPredicateMethod.MakeGenericMethod(
					count.Method.GetGenericArguments()
				),
				count.Arguments[0]
			);

		var valueExpr = FilterValue(convertedValue, property.Type);

		Expression left = property;
		Expression right = valueExpr;

		if (isIgnoreCase && property.Type == typeof(string))
		{
			left = Expression.Call(property, StringToLowerMethod);
			right = Expression.Call(valueExpr, StringToLowerMethod);
		}

		// For enum types with comparison operators, convert to underlying type
		var propertyType = Nullable.GetUnderlyingType(property.Type) ?? property.Type;
		if (propertyType.IsEnum && baseOp is ">" or "<" or ">=" or "<=")
		{
			var underlyingType = Enum.GetUnderlyingType(propertyType);
			left = Expression.Convert(property, underlyingType);
			right = FilterValue(Convert.ChangeType(convertedValue, underlyingType), underlyingType);
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
			"~=" when property.Type == typeof(string) => isIgnoreCase
				? Expression.Call(
					RegexIsMatchIgnoreCaseMethod,
					property,
					valueExpr,
					Expression.Constant(RegexOptions.IgnoreCase)
				)
				: Expression.Call(RegexIsMatchMethod, property, valueExpr),
			_ => null,
		};
	}

	private static Expression? TryBuildArrayContainsExpression(
		Expression property,
		object value,
		bool negate,
		bool ignoreCase
	)
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
		if (
			convertedValue == null
			&& elementType.IsValueType
			&& Nullable.GetUnderlyingType(elementType) == null
		)
			return null;

		var valueExpr = FilterValue(convertedValue, elementType);

		// Build Enumerable.Contains(array, value)
		var containsMethod = EnumerableContainsMethod.MakeGenericMethod(elementType);
		Expression containsCall = Expression.Call(containsMethod, property, valueExpr);

		if (negate)
		{
			containsCall = Expression.Not(containsCall);
		}

		return containsCall;
	}

	private static Expression BuildStringMethodCall(
		Expression property,
		Expression value,
		MethodInfo method,
		bool negate
	)
	{
		var call = Expression.Call(property, method, value);
		if (negate)
		{
			return Expression.Not(call);
		}
		return call;
	}

	private static Expression FilterValue(object? value, Type type)
	{
		if (value == null || !type.IsInstanceOfType(value))
			return Expression.Constant(value, type);

		var capture = ParameterFactories.GetValue(type, static t =>
			CaptureParameterMethod.MakeGenericMethod(t).CreateDelegate<Func<object, Expression>>()
		);
		return capture(value);
	}

	private static Expression CaptureParameter<T>(object value)
	{
		// A typed closure lets EF read the parameter directly, including nullable values and arrays.
		var parameter = (T)value;
		Expression<Func<T>> captured = () => parameter;
		return captured.Body;
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

		if (underlyingType == typeof(DateOnly) && value is string dateOnlyStr)
		{
			return DateOnly.Parse(dateOnlyStr);
		}

		if (underlyingType == typeof(TimeOnly) && value is string timeOnlyStr)
		{
			return TimeOnly.Parse(timeOnlyStr);
		}

		if (underlyingType == typeof(TimeSpan) && value is string timeSpanStr)
		{
			return TimeSpan.Parse(timeSpanStr);
		}

		if (underlyingType == typeof(DateTime) && value is string dateStr)
		{
			return DateTime.Parse(dateStr);
		}

		if (underlyingType == typeof(DateTimeOffset) && value is string dtoStr)
		{
			return DateTimeOffset.Parse(dtoStr);
		}

		if (underlyingType.IsEnum)
		{
			if (value is string enumStr)
			{
				return Enum.Parse(underlyingType, enumStr, true);
			}
			// Handle integer values for enums
			if (value is int or long or short or byte or sbyte or uint or ulong or ushort)
			{
				return Enum.ToObject(underlyingType, value);
			}
		}

		return Convert.ChangeType(value, underlyingType);
	}

	private static object? ConvertJsonElement(JsonElement element, Type targetType)
	{
		// Handle enums from JSON - can be string name or integer value
		if (targetType.IsEnum)
		{
			return element.ValueKind switch
			{
				JsonValueKind.String => Enum.Parse(targetType, element.GetString()!, true),
				JsonValueKind.Number => Enum.ToObject(targetType, element.GetInt32()),
				_ => element.GetRawText(),
			};
		}

		return element.ValueKind switch
		{
			JsonValueKind.String when targetType == typeof(string) => element.GetString(),
			JsonValueKind.String when targetType == typeof(Guid) => element.GetGuid(),
			JsonValueKind.String when targetType == typeof(DateTime) => element.GetDateTime(),
			JsonValueKind.String when targetType == typeof(DateTimeOffset) =>
				element.GetDateTimeOffset(),
			JsonValueKind.String when targetType == typeof(DateOnly) => DateOnly.Parse(
				element.GetString()!
			),
			JsonValueKind.String when targetType == typeof(TimeOnly) => TimeOnly.Parse(
				element.GetString()!
			),
			JsonValueKind.String when targetType == typeof(TimeSpan) => TimeSpan.Parse(
				element.GetString()!
			),
			JsonValueKind.Number when targetType == typeof(int) => element.GetInt32(),
			JsonValueKind.Number when targetType == typeof(long) => element.GetInt64(),
			JsonValueKind.Number when targetType == typeof(double) => element.GetDouble(),
			JsonValueKind.Number when targetType == typeof(decimal) => element.GetDecimal(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			_ => element.GetRawText(),
		};
	}

	private static MemberExpression? GetPropertyExpression(
		Type type,
		string propertyName,
		Expression parameter
	)
	{
		var propInfo = type.GetProperty(
			propertyName,
			BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
		);
		return propInfo != null ? Expression.Property(parameter, propInfo) : null;
	}
}
