using System;

namespace Calais.Exceptions;

/// <summary>
/// Base exception for all Calais-related errors
/// </summary>
public class CalaisException : Exception
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CalaisException"/> class.
	/// </summary>
	public CalaisException() { }

	/// <summary>
	/// Initializes a new instance of the <see cref="CalaisException"/> class with a message.
	/// </summary>
	/// <param name="message">The exception message.</param>
	public CalaisException(string message)
		: base(message) { }

	/// <summary>
	/// Initializes a new instance of the <see cref="CalaisException"/> class with a message and inner exception.
	/// </summary>
	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The exception that caused this exception.</param>
	public CalaisException(string message, Exception innerException)
		: base(message, innerException) { }
}

/// <summary>
/// Thrown when a property specified in a filter or sort is not found on the entity
/// </summary>
public class PropertyNotFoundException : CalaisException
{
	/// <summary>
	/// The property name that could not be found.
	/// </summary>
	public string PropertyName { get; }

	/// <summary>
	/// The entity type that was searched.
	/// </summary>
	public Type EntityType { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="PropertyNotFoundException"/> class.
	/// </summary>
	/// <param name="propertyName">The missing property name.</param>
	/// <param name="entityType">The entity type that was searched.</param>
	public PropertyNotFoundException(string propertyName, Type entityType)
		: base($"Property '{propertyName}' not found on type '{entityType.Name}'")
	{
		PropertyName = propertyName;
		EntityType = entityType;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="PropertyNotFoundException"/> class with an inner exception.
	/// </summary>
	/// <param name="propertyName">The missing property name.</param>
	/// <param name="entityType">The entity type that was searched.</param>
	/// <param name="innerException">The exception that caused this exception.</param>
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
	/// <summary>
	/// The property name that cannot be filtered.
	/// </summary>
	public string PropertyName { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="PropertyNotFilterableException"/> class.
	/// </summary>
	/// <param name="propertyName">The property name that cannot be filtered.</param>
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
	/// <summary>
	/// The property name that cannot be sorted.
	/// </summary>
	public string PropertyName { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="PropertyNotSortableException"/> class.
	/// </summary>
	/// <param name="propertyName">The property name that cannot be sorted.</param>
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
	/// <summary>
	/// The invalid JSON path.
	/// </summary>
	public string Path { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="InvalidJsonPathException"/> class.
	/// </summary>
	/// <param name="path">The invalid JSON path.</param>
	public InvalidJsonPathException(string path)
		: base($"Invalid JSON path '{path}'. JSON paths require at least column.property format")
	{
		Path = path;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="InvalidJsonPathException"/> class with a custom message.
	/// </summary>
	/// <param name="path">The invalid JSON path.</param>
	/// <param name="message">The exception message.</param>
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
	/// <summary>
	/// The invalid filter operator.
	/// </summary>
	public string Operator { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="InvalidFilterOperatorException"/> class.
	/// </summary>
	/// <param name="operatorValue">The invalid filter operator.</param>
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
	/// <summary>
	/// The value that could not be converted.
	/// </summary>
	public object? Value { get; }

	/// <summary>
	/// The target type for the conversion.
	/// </summary>
	public Type TargetType { get; }

	/// <summary>
	/// Initializes a new instance of the <see cref="ValueConversionException"/> class.
	/// </summary>
	/// <param name="value">The value that could not be converted.</param>
	/// <param name="targetType">The target type for the conversion.</param>
	public ValueConversionException(object? value, Type targetType)
		: base($"Cannot convert value '{value}' to type '{targetType.Name}'")
	{
		Value = value;
		TargetType = targetType;
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="ValueConversionException"/> class with an inner exception.
	/// </summary>
	/// <param name="value">The value that could not be converted.</param>
	/// <param name="targetType">The target type for the conversion.</param>
	/// <param name="innerException">The exception that caused this exception.</param>
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
	/// <summary>
	/// Initializes a new instance of the <see cref="ExpressionBuildException"/> class.
	/// </summary>
	/// <param name="message">The exception message.</param>
	public ExpressionBuildException(string message)
		: base(message) { }

	/// <summary>
	/// Initializes a new instance of the <see cref="ExpressionBuildException"/> class with an inner exception.
	/// </summary>
	/// <param name="message">The exception message.</param>
	/// <param name="innerException">The exception that caused this exception.</param>
	public ExpressionBuildException(string message, Exception innerException)
		: base(message, innerException) { }
}
