using System;

namespace Calais.Exceptions
{
    /// <summary>
    /// Base exception for all Calais-related errors
    /// </summary>
    public class CalaisException : Exception
    {
        public CalaisException()
        {
        }

        public CalaisException(string message) : base(message)
        {
        }

        public CalaisException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when a property specified in a filter or sort is not found on the entity
    /// </summary>
    public class PropertyNotFoundException : CalaisException
    {
        public string PropertyName { get; }
        public Type EntityType { get; }

        public PropertyNotFoundException(string propertyName, Type entityType)
            : base($"Property '{propertyName}' not found on type '{entityType.Name}'")
        {
            PropertyName = propertyName;
            EntityType = entityType;
        }

        public PropertyNotFoundException(string propertyName, Type entityType, Exception innerException)
            : base($"Property '{propertyName}' not found on type '{entityType.Name}'", innerException)
        {
            PropertyName = propertyName;
            EntityType = entityType;
        }
    }

    /// <summary>
    /// Thrown when a property is not allowed for filtering
    /// </summary>
    public class PropertyNotFilterableException : CalaisException
    {
        public string PropertyName { get; }

        public PropertyNotFilterableException(string propertyName)
            : base($"Property '{propertyName}' is not filterable")
        {
            PropertyName = propertyName;
        }
    }

    /// <summary>
    /// Thrown when a property is not allowed for sorting
    /// </summary>
    public class PropertyNotSortableException : CalaisException
    {
        public string PropertyName { get; }

        public PropertyNotSortableException(string propertyName)
            : base($"Property '{propertyName}' is not sortable")
        {
            PropertyName = propertyName;
        }
    }

    /// <summary>
    /// Thrown when a JSON path format is invalid
    /// </summary>
    public class InvalidJsonPathException : CalaisException
    {
        public string Path { get; }

        public InvalidJsonPathException(string path)
            : base($"Invalid JSON path '{path}'. JSON paths require at least column.property format")
        {
            Path = path;
        }

        public InvalidJsonPathException(string path, string message)
            : base(message)
        {
            Path = path;
        }
    }

    /// <summary>
    /// Thrown when a filter operator is not recognized or supported
    /// </summary>
    public class InvalidFilterOperatorException : CalaisException
    {
        public string Operator { get; }

        public InvalidFilterOperatorException(string operatorValue)
            : base($"Invalid or unsupported filter operator: '{operatorValue}'")
        {
            Operator = operatorValue;
        }
    }

    /// <summary>
    /// Thrown when a filter value cannot be converted to the target property type
    /// </summary>
    public class ValueConversionException : CalaisException
    {
        public object? Value { get; }
        public Type TargetType { get; }

        public ValueConversionException(object? value, Type targetType)
            : base($"Cannot convert value '{value}' to type '{targetType.Name}'")
        {
            Value = value;
            TargetType = targetType;
        }

        public ValueConversionException(object? value, Type targetType, Exception innerException)
            : base($"Cannot convert value '{value}' to type '{targetType.Name}'", innerException)
        {
            Value = value;
            TargetType = targetType;
        }
    }

    /// <summary>
    /// Thrown when an expression cannot be built from the provided filter or sort descriptor
    /// </summary>
    public class ExpressionBuildException : CalaisException
    {
        public ExpressionBuildException(string message) : base(message)
        {
        }

        public ExpressionBuildException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
