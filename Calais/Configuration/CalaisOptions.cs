using System;
using System.Collections.Generic;

namespace Calais.Configuration
{
    /// <summary>
    /// Global options for Calais query processing
    /// </summary>
    public class CalaisOptions
    {
        /// <summary>
        /// Default page size when not specified in query
        /// </summary>
        public int DefaultPageSize { get; set; } = 10;

        /// <summary>
        /// Maximum allowed page size
        /// </summary>
        public int MaxPageSize { get; set; } = 100;

        /// <summary>
        /// Default language for full-text search vectors
        /// </summary>
        public string DefaultVectorLanguage { get; set; } = "english";

        /// <summary>
        /// Whether to throw exceptions on invalid field names (false = silently ignore)
        /// </summary>
        public bool ThrowOnInvalidFields { get; set; } = false;

        /// <summary>
        /// Entity configurations
        /// </summary>
        internal Dictionary<Type, EntityConfiguration> EntityConfigurations { get; } = new Dictionary<Type, EntityConfiguration>();

        /// <summary>
        /// Configure an entity type for Calais processing
        /// </summary>
        public EntityConfigurationBuilder<TEntity> Entity<TEntity>() where TEntity : class
        {
            var entityType = typeof(TEntity);
            if (!EntityConfigurations.TryGetValue(entityType, out var config))
            {
                config = new EntityConfiguration(entityType);
                EntityConfigurations[entityType] = config;
            }
            return new EntityConfigurationBuilder<TEntity>(config, this);
        }

        /// <summary>
        /// Gets the configuration for an entity type, or null if not configured
        /// </summary>
        internal EntityConfiguration? GetEntityConfiguration<TEntity>() where TEntity : class
        {
            EntityConfigurations.TryGetValue(typeof(TEntity), out var config);
            return config;
        }

        /// <summary>
        /// Gets the configuration for an entity type, or null if not configured
        /// </summary>
        internal EntityConfiguration? GetEntityConfiguration(Type entityType)
        {
            EntityConfigurations.TryGetValue(entityType, out var config);
            return config;
        }
    }
}
