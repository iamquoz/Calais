using System.Collections.Generic;

namespace Calais.Models;

/// <summary>
/// Represents a paginated result set
/// </summary>
/// <typeparam name="T">The entity type</typeparam>
public class PagedResult<T>
{
	/// <summary>
	/// The items on the current page.
	/// </summary>
	public List<T> Items { get; set; } = new List<T>();

	/// <summary>
	/// The current one-based page number.
	/// </summary>
	public int Page { get; set; }

	/// <summary>
	/// The number of items requested per page.
	/// </summary>
	public int PageSize { get; set; }

	/// <summary>
	/// The total number of items after filtering.
	/// </summary>
	public int TotalCount { get; set; }

	/// <summary>
	/// The total number of available pages.
	/// </summary>
	public int TotalPages => PageSize > 0 ? (TotalCount + PageSize - 1) / PageSize : 0;

	/// <summary>
	/// Whether a previous page exists.
	/// </summary>
	public bool HasPreviousPage => Page > 1;

	/// <summary>
	/// Whether a next page exists.
	/// </summary>
	public bool HasNextPage => Page < TotalPages;
}
