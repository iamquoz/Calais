using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Calais.Configuration
{
    /// <summary>
    /// Configuration for a specific property of an entity
    /// </summary>
    public class PropertyConfiguration
    {
        public string PropertyName { get; set; } = string.Empty;
        public string? Alias { get; set; }
        public bool IsSortable { get; set; } = true;
        public bool IsFilterable { get; set; } = true;
        public bool IsVector { get; set; }
        public string? VectorLanguage { get; set; }
        public LambdaExpression? CustomSortExpression { get; set; }
        public LambdaExpression? CustomFilterExpression { get; set; }
    }

    /// <summary>
    /// Configuration for an entity type
    /// </summary>
    public class EntityConfiguration
    {
        public Type EntityType { get; }
        public Dictionary<string, PropertyConfiguration> Properties { get; } = new Dictionary<string, PropertyConfiguration>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, LambdaExpression> CustomSorts { get; } = new Dictionary<string, LambdaExpression>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, LambdaExpression> CustomFilters { get; } = new Dictionary<string, LambdaExpression>(StringComparer.OrdinalIgnoreCase);

        public EntityConfiguration(Type entityType)
        {
            EntityType = entityType;
        }
    }

    /// <summary>
    /// Fluent builder for entity configuration
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured</typeparam>
    public class EntityConfigurationBuilder<TEntity> where TEntity : class
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
            bool filter = true)
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
        /// Adds a custom sort expression for a named sort key
        /// </summary>
        public EntityConfigurationBuilder<TEntity> AddSort<TProperty>(
            string sortKey,
            Expression<Func<TEntity, TProperty>> sortExpression)
        {
            _configuration.CustomSorts[sortKey] = sortExpression;
            return this;
        }

        /// <summary>
        /// Adds a custom filter expression for a named filter key
        /// </summary>
        public EntityConfigurationBuilder<TEntity> AddFilter<TProperty>(
            string filterKey,
            Expression<Func<TEntity, TProperty>> filterExpression)
        {
            _configuration.CustomFilters[filterKey] = filterExpression;
            return this;
        }

        /// <summary>
        /// Configures a property as a full-text search vector with optional language override
        /// </summary>
        public EntityConfigurationBuilder<TEntity> AsVector<TProperty>(
            Expression<Func<TEntity, TProperty>> propertyExpression,
            string? language = null)
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
            string alias)
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

        private static string GetPropertyName<TProperty>(Expression<Func<TEntity, TProperty>> expression)
        {
            if (expression.Body is MemberExpression memberExpression)
            {
                return memberExpression.Member.Name;
            }
            throw new ArgumentException("Expression must be a member expression", nameof(expression));
        }
    }
}
