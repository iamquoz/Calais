using System;
using Calais.Configuration;

namespace Calais
{
    /// <summary>
    /// Builder for creating and configuring CalaisProcessor instances
    /// </summary>
    public class CalaisBuilder
    {
	    /// <summary>
        /// Sets the default page size
        /// </summary>
        public CalaisBuilder WithDefaultPageSize(int pageSize)
        {
            Options.DefaultPageSize = pageSize;
            return this;
        }

        /// <summary>
        /// Sets the maximum allowed page size
        /// </summary>
        public CalaisBuilder WithMaxPageSize(int maxPageSize)
        {
            Options.MaxPageSize = maxPageSize;
            return this;
        }

        /// <summary>
        /// Sets the default language for full-text search vectors
        /// </summary>
        public CalaisBuilder WithDefaultVectorLanguage(string language)
        {
            Options.DefaultVectorLanguage = language;
            return this;
        }

        /// <summary>
        /// Configures whether to throw exceptions on invalid field names
        /// </summary>
        public CalaisBuilder ThrowOnInvalidFields(bool throwOnInvalid = true)
        {
            Options.ThrowOnInvalidFields = throwOnInvalid;
            return this;
        }

        /// <summary>
        /// Configures an entity type (opt-in for entities, opt-out for fields)
        /// </summary>
        public CalaisBuilder ConfigureEntity<TEntity>(
            Action<EntityConfigurationBuilder<TEntity>> configure) where TEntity : class
        {
            var builder = Options.Entity<TEntity>();
            configure(builder);
            return this;
        }

        /// <summary>
        /// Builds the CalaisProcessor with the configured options
        /// </summary>
        public CalaisProcessor Build()
        {
            return new CalaisProcessor(Options);
        }

        /// <summary>
        /// Gets the configured options (for advanced scenarios)
        /// </summary>
        public CalaisOptions Options { get; } = new CalaisOptions();
    }
}
