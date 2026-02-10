using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Calais.Models
{
    /// <summary>
    /// Represents a complete query request with pagination, sorting, and filtering
    /// </summary>
    public class CalaisQuery
    {
        [JsonPropertyName("page")]
        public int? Page { get; set; }

        [JsonPropertyName("pageSize")]
        public int? PageSize { get; set; }

        [JsonPropertyName("sorts")]
        public List<SortDescriptor>? Sorts { get; set; }

        [JsonPropertyName("filters")]
        public List<FilterDescriptor>? Filters { get; set; }
    }

    /// <summary>
    /// Describes a sort operation
    /// </summary>
    public class SortDescriptor
    {
        [JsonPropertyName("field")]
        public string Field { get; set; } = string.Empty;

        [JsonPropertyName("direction")]
        public string Direction { get; set; } = "asc";

        [JsonPropertyName("json")]
        public bool IsJson { get; set; }

        public SortDirection GetDirection() =>
            Direction?.ToLowerInvariant() == "desc" ? SortDirection.Desc : SortDirection.Asc;
    }

    /// <summary>
    /// Describes a filter operation, can be nested with OR conditions
    /// </summary>
    public class FilterDescriptor
    {
        [JsonPropertyName("field")]
        public string? Field { get; set; }

        [JsonPropertyName("operator")]
        public string? Operator { get; set; }

        [JsonPropertyName("values")]
        public List<object>? Values { get; set; }

        [JsonPropertyName("vector")]
        public bool IsVector { get; set; }

        [JsonPropertyName("json")]
        public bool IsJson { get; set; }

        [JsonPropertyName("or")]
        public List<FilterDescriptor>? Or { get; set; }

        public bool IsOrGroup => Or != null && Or.Count > 0;
    }
}
