# Calais

A flexible C# query building library for Entity Framework Core with PostgreSQL/Npgsql support. Inspired by [Sieve](https://github.com/Biarity/Sieve), Calais transforms user input into database queries using expression trees.

## Features

- **Expression tree-based filtering and sorting** - Efficient query building that translates to SQL
- **Full-text search support** - Native support for `NpgsqlTsVector` with configurable language
- **JSONB document querying** - Filter and sort on `JsonDocument` properties
- **Nested property navigation** - Support for paths like `comments.text` or `posts.title`
- **Opt-in entities, opt-out fields** - Entity-level configuration with field-level overrides
- **Custom sorts and filters** - Attach custom expressions to field names
- **Separate pagination** - Pagination can be applied independently from filtering/sorting
- **OR group support** - Complex nested conditions with OR logic
- **Length operators** - Filter by collection size with `len>=`, `len==`, etc.

## Installation

Add the project reference or package to your project.

## Quick Start

### 1. Configure the processor

```csharp
var processor = new CalaisBuilder()
    .ConfigureEntity<User>(e =>
    {
        // Ignore sensitive fields
        e.Ignore(u => u.PasswordHash, sorts: true, filter: true);
        
        // Add custom sorts
        e.AddSort("is_banned", u => u.LockoutEnd != null);
        
        // Configure vector fields
        e.AsVector(u => u.SearchVector, language: "english");
    })
    .WithDefaultPageSize(10)
    .WithMaxPageSize(100)
    .WithDefaultVectorLanguage("english")
    .Build();
```

### 2. Create a query

```csharp
var query = new CalaisQuery
{
    Page = 1,
    PageSize = 10,
    Sorts = new List<SortDescriptor>
    {
        new SortDescriptor { Field = "name", Direction = "asc" },
        new SortDescriptor { Field = "age", Direction = "desc" }
    },
    Filters = new List<FilterDescriptor>
    {
        new FilterDescriptor
        {
            Field = "name",
            Operator = "==",
            Values = new List<object> { "alice", "bob" }
        },
        new FilterDescriptor
        {
            Field = "age",
            Operator = ">=",
            Values = new List<object> { 20 }
        }
    }
};
```

### 3. Apply the query

```csharp
// Apply all (filters, sorting, pagination)
var result = await processor.ApplyAsync(dbContext.Users, query);

// Or apply separately
var filtered = processor.ApplyFilters(dbContext.Users, query);
var sorted = processor.ApplySorting(filtered, query);
var paged = processor.ApplyPagination(sorted, query);
```

## Supported Operators

| Operator | Description |
|----------|-------------|
| `==` | Equals |
| `!=` | Not equals |
| `>` | Greater than |
| `<` | Less than |
| `>=` | Greater than or equal |
| `<=` | Less than or equal |
| `@=` | Contains (string) |
| `_=` | Starts with |
| `_-=` | Ends with |
| `!@=` | Does not contain |
| `!_=` | Does not start with |
| `!_-=` | Does not end with |
| `==*` | Equals (case insensitive) |
| `!=*` | Not equals (case insensitive) |
| `@=*` | Contains (case insensitive) |
| `_=*` | Starts with (case insensitive) |
| `_-=*` | Ends with (case insensitive) |
| `len==` | Length equals |
| `len!=` | Length not equals |
| `len>` | Length greater than |
| `len<` | Length less than |
| `len>=` | Length greater than or equal |
| `len<=` | Length less than or equal |

## Multiple Values

When multiple values are provided for a filter:
- For `==` and similar operators: treated as **OR** (matches any)
- For `!=`: treated as **AND** (must not match any)

```json
{
  "field": "name",
  "operator": "==",
  "values": ["alice", "bob"]
}
```
This generates: `name == "alice" OR name == "bob"`

## OR Groups

Use the `or` property to create OR conditions:

```json
{
  "filters": [
    {
      "or": [
        { "field": "name", "operator": "@=", "values": ["admin"] },
        { "field": "age", "operator": ">=", "values": [21] }
      ]
    }
  ]
}
```

## Nested Properties

Filter through navigation properties:

```json
{
  "field": "comments.text",
  "operator": "@=",
  "values": ["good"]
}
```
This generates a query that finds users who have at least one comment containing "good".

## JSONB Support

Mark filters as JSON to query `JsonDocument` properties:

```json
{
  "field": "jsonbColumn.randomData",
  "json": true,
  "operator": "@=",
  "values": ["tagged"]
}
```

## Full-Text Search

Mark filters as vector for tsquery matching:

```json
{
  "field": "contentVector",
  "vector": true,
  "values": ["test & example"]
}
```

Multiple vector values are combined with OR.

## Separate Pagination

Pagination can be applied independently, addressing [Sieve issue #34](https://github.com/Biarity/Sieve/issues/34):

```csharp
// Get filtered count first
var filteredQuery = processor.ApplyWithoutPagination(dbContext.Users, query);
var totalCount = await filteredQuery.CountAsync();

// Then apply pagination
var page1 = await processor.ApplyPagination(filteredQuery, 1, 10).ToListAsync();
var page2 = await processor.ApplyPagination(filteredQuery, 2, 10).ToListAsync();
```

## Dependency Injection

```csharp
services.AddCalais(builder =>
{
    builder.ConfigureEntity<User>(e => e.Ignore(u => u.PasswordHash));
    builder.WithDefaultPageSize(20);
});
```

Then inject `CalaisProcessor` where needed.

## Example: Complete Query

```json
{
  "page": 5,
  "pageSize": 10,
  "sorts": [
    { "field": "name", "direction": "asc" },
    { "field": "age", "direction": "desc" },
    { "field": "jsonbColumn.randomData", "json": true, "direction": "desc" }
  ],
  "filters": [
    { "field": "id", "operator": "!=", "values": ["guid1", "guid2"] },
    { "field": "name", "operator": "==", "values": ["alice", "bob"] },
    { "field": "age", "operator": ">=", "values": [20] },
    { "field": "age", "operator": "<=", "values": [35] },
    { "field": "comments.text", "operator": "@=", "values": ["good"] },
    {
      "or": [
        { "field": "posts.title", "operator": "@=", "values": ["abc"] },
        { "field": "posts.contentVector", "vector": true, "values": ["test:* & example:*"] },
        { "field": "jsonbColumn.randomData", "json": true, "operator": "@=", "values": ["tagged"] },
        { "field": "comments", "operator": "len>=", "values": [1] }
      ]
    }
  ]
}
```

This query:
- Gets page 5 with 10 items
- Sorts by name (asc), then age (desc), then JSONB field
- Excludes specific IDs
- Matches name equal to alice or bob
- Age between 20 and 35
- Has comments containing "good"
- AND either:
  - Has posts with title containing "abc", OR
  - Has posts matching the tsquery, OR
  - Has JSONB randomData containing "tagged", OR
  - Has at least 1 comment

## Error Handling

Calais provides custom exceptions for different error scenarios. By default, invalid fields are silently ignored. Enable strict mode with `ThrowOnInvalidFields`:

```csharp
var processor = new CalaisBuilder()
    .ThrowOnInvalidFields(true)
    .Build();
```

### Exception Types

| Exception | Description |
|-----------|-------------|
| `CalaisException` | Base exception for all Calais errors |
| `PropertyNotFoundException` | Property not found on entity type |
| `PropertyNotFilterableException` | Property is configured as not filterable |
| `PropertyNotSortableException` | Property is configured as not sortable |
| `InvalidJsonPathException` | JSON path format is invalid (requires `column.property`) |
| `InvalidFilterOperatorException` | Filter operator is not recognized |
| `ValueConversionException` | Value cannot be converted to target property type |
| `ExpressionBuildException` | Generic expression building failure |

### Example: Catching Specific Exceptions

```csharp
try
{
    var result = await processor.ApplyAsync(dbContext.Users, query);
}
catch (PropertyNotFoundException ex)
{
    Console.WriteLine($"Property '{ex.PropertyName}' not found on {ex.EntityType.Name}");
}
catch (PropertyNotFilterableException ex)
{
    Console.WriteLine($"Cannot filter by '{ex.PropertyName}'");
}
catch (CalaisException ex)
{
    // Catch any Calais-related error
    Console.WriteLine($"Query error: {ex.Message}");
}