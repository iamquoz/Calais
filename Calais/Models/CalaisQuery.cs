using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Calais.Models;

/// <summary>
/// Represents a complete query request with pagination, sorting, and filtering
/// </summary>
public class CalaisQuery
{
	/// <summary>
	/// The one-based page number to return.
	/// </summary>
	[JsonPropertyName("page")]
	public int? Page { get; set; }

	/// <summary>
	/// The number of items to return per page.
	/// </summary>
	[JsonPropertyName("pageSize")]
	public int? PageSize { get; set; }

	/// <summary>
	/// The sort operations to apply.
	/// </summary>
	[JsonPropertyName("sorts")]
	public List<SortDescriptor>? Sorts { get; set; }

	/// <summary>
	/// The filter operations to apply.
	/// </summary>
	[JsonPropertyName("filters")]
	public List<FilterDescriptor>? Filters { get; set; }
}

/// <summary>
/// Describes a sort operation
/// </summary>
public class SortDescriptor
{
	/// <summary>
	/// The field or custom sort method name to sort by.
	/// </summary>
	[JsonPropertyName("field")]
	public string Field { get; set; } = string.Empty;

	/// <summary>
	/// The sort direction, either asc or desc.
	/// </summary>
	[JsonPropertyName("direction")]
	public string Direction { get; set; } = "asc";

	/// <summary>
	/// Whether the field should be interpreted as a JSON path.
	/// </summary>
	[JsonPropertyName("json")]
	public bool IsJson { get; set; }

	/// <summary>
	/// Gets the parsed sort direction.
	/// </summary>
	public SortDirection GetDirection() =>
		Direction?.ToLowerInvariant() == "desc" ? SortDirection.Desc : SortDirection.Asc;
}

/// <summary>
/// Describes a filter operation, can be nested with OR conditions
/// </summary>
public class FilterDescriptor
{
	/// <summary>
	/// The field or custom filter method name to filter by.
	/// </summary>
	[JsonPropertyName("field")]
	public string? Field { get; set; }

	/// <summary>
	/// The filter operator to apply.
	/// </summary>
	[JsonPropertyName("operator")]
	public string? Operator { get; set; }

	/// <summary>
	/// The values to compare against.
	/// </summary>
	[JsonPropertyName("values")]
	public List<object>? Values { get; set; }

	/// <summary>
	/// Whether the field should be filtered as a full-text search vector.
	/// </summary>
	[JsonPropertyName("vector")]
	public bool IsVector { get; set; }

	/// <summary>
	/// Whether the field should be interpreted as a JSON path.
	/// </summary>
	[JsonPropertyName("json")]
	public bool IsJson { get; set; }

	/// <summary>
	/// Child filters that should be combined with OR.
	/// </summary>
	[JsonPropertyName("or")]
	public List<FilterDescriptor>? Or { get; set; }

	/// <summary>
	/// Gets whether this descriptor represents an OR group.
	/// </summary>
	public bool IsOrGroup => Or != null && Or.Count > 0;
}
