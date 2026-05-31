using System;
using System.Collections.Generic;
using Calais.Models;

namespace Calais;

/// <summary>
/// Marker interface for services that provide custom filter methods.
/// </summary>
public interface ICalaisCustomFilterMethods { }

/// <summary>
/// Marker interface for services that provide custom sort methods.
/// </summary>
public interface ICalaisCustomSortMethods { }

/// <summary>
/// Context passed to custom filter methods.
/// </summary>
public sealed class CalaisFilterContext
{
	/// <summary>
	/// Creates a custom filter context.
	/// </summary>
	public CalaisFilterContext(FilterDescriptor descriptor, IServiceProvider services)
	{
		Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
		Services = services ?? throw new ArgumentNullException(nameof(services));
		Operator = descriptor.Operator ?? FilterOperator.Equals;
		Values = descriptor.Values ?? [];
	}

	/// <summary>
	/// The requested filter operator.
	/// </summary>
	public string Operator { get; }

	/// <summary>
	/// The requested filter values.
	/// </summary>
	public IReadOnlyList<object> Values { get; }

	/// <summary>
	/// The original filter descriptor.
	/// </summary>
	public FilterDescriptor Descriptor { get; }

	/// <summary>
	/// The scoped service provider available to custom methods.
	/// </summary>
	public IServiceProvider Services { get; }
}

/// <summary>
/// Context passed to custom sort methods.
/// </summary>
public sealed class CalaisSortContext
{
	/// <summary>
	/// Creates a custom sort context.
	/// </summary>
	public CalaisSortContext(SortDescriptor descriptor, bool useThenBy, IServiceProvider services)
	{
		Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
		Services = services ?? throw new ArgumentNullException(nameof(services));
		Direction = descriptor.GetDirection();
		UseThenBy = useThenBy;
	}

	/// <summary>
	/// The requested sort direction.
	/// </summary>
	public SortDirection Direction { get; }

	/// <summary>
	/// Whether this sort should be applied as a secondary sort.
	/// </summary>
	public bool UseThenBy { get; }

	/// <summary>
	/// The original sort descriptor.
	/// </summary>
	public SortDescriptor Descriptor { get; }

	/// <summary>
	/// The scoped service provider available to custom methods.
	/// </summary>
	public IServiceProvider Services { get; }
}
