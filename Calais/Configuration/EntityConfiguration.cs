using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Calais.Models;

namespace Calais.Configuration;

/// <summary>
/// Configuration for a specific property of an entity
/// </summary>
public class PropertyConfiguration
{
	/// <summary>
	/// The configured property name.
	/// </summary>
	public string PropertyName { get; set; } = string.Empty;

	/// <summary>
	/// The optional query alias for the property.
	/// </summary>
	public string? Alias { get; set; }

	/// <summary>
	/// Whether the property can be sorted.
	/// </summary>
	public bool IsSortable { get; set; } = true;

	/// <summary>
	/// Whether the property can be filtered.
	/// </summary>
	public bool IsFilterable { get; set; } = true;

	/// <summary>
	/// Whether the property is configured as a full-text search vector.
	/// </summary>
	public bool IsVector { get; set; }

	/// <summary>
	/// The language configuration for full-text search vector queries.
	/// </summary>
	public string? VectorLanguage { get; set; }
}

/// <summary>
/// Configuration for a default sort field.
/// </summary>
public sealed class DefaultSortConfiguration
{
	/// <summary>
	/// Initializes a new instance of the <see cref="DefaultSortConfiguration"/> class.
	/// </summary>
	/// <param name="field">The field path to sort by.</param>
	/// <param name="direction">The default sort direction.</param>
	public DefaultSortConfiguration(string field, SortDirection direction)
	{
		Field = field;
		Direction = direction;
	}

	/// <summary>
	/// The field path to sort by.
	/// </summary>
	public string Field { get; }

	/// <summary>
	/// The default sort direction.
	/// </summary>
	public SortDirection Direction { get; }
}

/// <summary>
/// Configuration for an entity type
/// </summary>
public class EntityConfiguration
{
	/// <summary>
	/// The entity type this configuration applies to.
	/// </summary>
	public Type EntityType { get; }

	/// <summary>
	/// Property-level configuration by property name.
	/// </summary>
	public Dictionary<string, PropertyConfiguration> Properties { get; } =
		new Dictionary<string, PropertyConfiguration>(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Ordered default sorts for the entity.
	/// </summary>
	public List<DefaultSortConfiguration> DefaultSorts { get; } = [];

	/// <summary>
	/// Initializes a new instance of the <see cref="EntityConfiguration"/> class.
	/// </summary>
	/// <param name="entityType">The entity type this configuration applies to.</param>
	public EntityConfiguration(Type entityType)
	{
		EntityType = entityType;
	}
}

/// <summary>
/// Fluent builder for entity configuration
/// </summary>
/// <typeparam name="TEntity">The entity type being configured</typeparam>
public class EntityConfigurationBuilder<TEntity>
	where TEntity : class
{
	private readonly EntityConfiguration _configuration;
	private readonly CalaisOptions _options;

	internal EntityConfigurationBuilder(EntityConfiguration configuration, CalaisOptions options)
	{
		_configuration = configuration;
		_options = options;
	}

	/// <summary>
	/// Ignores a property for sorting, filtering, or both
	/// </summary>
	public EntityConfigurationBuilder<TEntity> Ignore<TProperty>(
		Expression<Func<TEntity, TProperty>> propertyExpression,
		bool sorts = true,
		bool filter = true
	)
	{
		var propertyName = GetPropertyName(propertyExpression);
		var config = GetOrCreatePropertyConfig(propertyName);

		if (sorts)
			config.IsSortable = false;
		if (filter)
			config.IsFilterable = false;

		return this;
	}

	/// <summary>
	/// Adds the first default sort for this entity.
	/// </summary>
	public DefaultSortConfigurationBuilder<TEntity> AddDefaultSort<TProperty>(
		Expression<Func<TEntity, TProperty>> sortExpression,
		SortDirection direction = SortDirection.Asc
	)
	{
		AddDefaultSortInternal(sortExpression, direction);
		return new DefaultSortConfigurationBuilder<TEntity>(this);
	}

	/// <summary>
	/// Configures a property as a full-text search vector with optional language override
	/// </summary>
	public EntityConfigurationBuilder<TEntity> AsVector<TProperty>(
		Expression<Func<TEntity, TProperty>> propertyExpression,
		string? language = null
	)
	{
		var propertyName = GetPropertyName(propertyExpression);
		var config = GetOrCreatePropertyConfig(propertyName);
		config.IsVector = true;
		config.VectorLanguage = language ?? _options.DefaultVectorLanguage;
		return this;
	}

	/// <summary>
	/// Sets an alias for a property to be used in queries
	/// </summary>
	public EntityConfigurationBuilder<TEntity> HasAlias<TProperty>(
		Expression<Func<TEntity, TProperty>> propertyExpression,
		string alias
	)
	{
		var propertyName = GetPropertyName(propertyExpression);
		var config = GetOrCreatePropertyConfig(propertyName);
		config.Alias = alias;
		return this;
	}

	private PropertyConfiguration GetOrCreatePropertyConfig(string propertyName)
	{
		if (!_configuration.Properties.TryGetValue(propertyName, out var config))
		{
			config = new PropertyConfiguration { PropertyName = propertyName };
			_configuration.Properties[propertyName] = config;
		}
		return config;
	}

	private static string GetPropertyName<TProperty>(
		Expression<Func<TEntity, TProperty>> expression
	)
	{
		if (expression.Body is MemberExpression memberExpression)
		{
			return memberExpression.Member.Name;
		}
		throw new ArgumentException("Expression must be a member expression", nameof(expression));
	}

	internal void AddDefaultSortInternal<TProperty>(
		Expression<Func<TEntity, TProperty>> sortExpression,
		SortDirection direction
	)
	{
		var fieldPath = GetMemberPath(sortExpression);
		_configuration.DefaultSorts.Add(new DefaultSortConfiguration(fieldPath, direction));
	}

	private static string GetMemberPath<TProperty>(Expression<Func<TEntity, TProperty>> expression)
	{
		var members = new Stack<MemberInfo>();
		Expression? current = StripConvert(expression.Body);

		while (current is MemberExpression memberExpression)
		{
			members.Push(memberExpression.Member);
			current = StripConvert(memberExpression.Expression);
		}

		if (current != expression.Parameters[0] || members.Count == 0)
		{
			throw new ArgumentException(
				"Expression must be a member access chain",
				nameof(expression)
			);
		}

		var memberArray = members.ToArray();
		RejectCollectionNavigation(memberArray, expression);
		return string.Join(".", memberArray.Select(member => member.Name));
	}

	private static Expression? StripConvert(Expression? expression)
	{
		while (
			expression is UnaryExpression unaryExpression
			&& (
				unaryExpression.NodeType == ExpressionType.Convert
				|| unaryExpression.NodeType == ExpressionType.ConvertChecked
			)
		)
		{
			expression = unaryExpression.Operand;
		}

		return expression;
	}

	private static void RejectCollectionNavigation<TProperty>(
		IReadOnlyList<MemberInfo> members,
		Expression<Func<TEntity, TProperty>> expression
	)
	{
		for (var i = 0; i < members.Count; i++)
		{
			var memberType = members[i] switch
			{
				PropertyInfo propertyInfo => propertyInfo.PropertyType,
				FieldInfo fieldInfo => fieldInfo.FieldType,
				_ => throw new ArgumentException(
					"Expression must be a property or field access chain",
					nameof(expression)
				),
			};

			if (memberType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(memberType))
			{
				throw new ArgumentException(
					"Default sorts do not support collection navigation",
					nameof(expression)
				);
			}
		}
	}
}

/// <summary>
/// Fluent builder for additional default sorts.
/// </summary>
/// <typeparam name="TEntity">The entity type being configured</typeparam>
public sealed class DefaultSortConfigurationBuilder<TEntity>
	where TEntity : class
{
	private readonly EntityConfigurationBuilder<TEntity> _entityBuilder;

	internal DefaultSortConfigurationBuilder(EntityConfigurationBuilder<TEntity> entityBuilder)
	{
		_entityBuilder = entityBuilder;
	}

	/// <summary>
	/// Adds a secondary default sort.
	/// </summary>
	public DefaultSortConfigurationBuilder<TEntity> ThenBy<TProperty>(
		Expression<Func<TEntity, TProperty>> sortExpression,
		SortDirection direction = SortDirection.Asc
	)
	{
		_entityBuilder.AddDefaultSortInternal(sortExpression, direction);
		return this;
	}
}
