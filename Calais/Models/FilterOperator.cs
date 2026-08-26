namespace Calais.Models;

/// <summary>
/// Supported filter operators matching Sieve-style syntax
/// </summary>
public static class FilterOperator
{
	/// <summary>
	/// Equals comparison.
	/// </summary>
	public new const string Equals = "==";

	/// <summary>
	/// Not equals comparison.
	/// </summary>
	public const string NotEquals = "!=";

	/// <summary>
	/// Greater-than comparison.
	/// </summary>
	public const string GreaterThan = ">";

	/// <summary>
	/// Less-than comparison.
	/// </summary>
	public const string LessThan = "<";

	/// <summary>
	/// Greater-than-or-equal comparison.
	/// </summary>
	public const string GreaterThanOrEqual = ">=";

	/// <summary>
	/// Less-than-or-equal comparison.
	/// </summary>
	public const string LessThanOrEqual = "<=";

	/// <summary>
	/// String or collection contains comparison.
	/// </summary>
	public const string Contains = "@=";

	/// <summary>
	/// String starts-with comparison.
	/// </summary>
	public const string StartsWith = "_=";

	/// <summary>
	/// String ends-with comparison.
	/// </summary>
	public const string EndsWith = "_-=";

	/// <summary>
	/// String or collection does-not-contain comparison.
	/// </summary>
	public const string DoesNotContain = "!@=";

	/// <summary>
	/// String does-not-start-with comparison.
	/// </summary>
	public const string DoesNotStartWith = "!_=";

	/// <summary>
	/// String does-not-end-with comparison.
	/// </summary>
	public const string DoesNotEndWith = "!_-=";

	/// <summary>
	/// Case-insensitive equals comparison.
	/// </summary>
	public const string EqualsIgnoreCase = "==*";

	/// <summary>
	/// Case-insensitive not-equals comparison.
	/// </summary>
	public const string NotEqualsIgnoreCase = "!=*";

	/// <summary>
	/// Case-insensitive contains comparison.
	/// </summary>
	public const string ContainsIgnoreCase = "@=*";

	/// <summary>
	/// Regular-expression match comparison.
	/// </summary>
	public const string RegexMatch = "~=";

	/// <summary>
	/// Case-insensitive regular-expression match comparison.
	/// </summary>
	public const string RegexMatchIgnoreCase = "~=*";

	/// <summary>
	/// Case-insensitive starts-with comparison.
	/// </summary>
	public const string StartsWithIgnoreCase = "_=*";

	/// <summary>
	/// Case-insensitive ends-with comparison.
	/// </summary>
	public const string EndsWithIgnoreCase = "_-=*";

	/// <summary>
	/// Case-insensitive does-not-contain comparison.
	/// </summary>
	public const string DoesNotContainIgnoreCase = "!@=*";

	/// <summary>
	/// Case-insensitive does-not-start-with comparison.
	/// </summary>
	public const string DoesNotStartWithIgnoreCase = "!_=*";

	/// <summary>
	/// Case-insensitive does-not-end-with comparison.
	/// </summary>
	public const string DoesNotEndWithIgnoreCase = "!_-=*";

	/// <summary>
	/// Length equals comparison.
	/// </summary>
	public const string LengthEquals = "len==";

	/// <summary>
	/// Length not-equals comparison.
	/// </summary>
	public const string LengthNotEquals = "len!=";

	/// <summary>
	/// Length greater-than comparison.
	/// </summary>
	public const string LengthGreaterThan = "len>";

	/// <summary>
	/// Length less-than comparison.
	/// </summary>
	public const string LengthLessThan = "len<";

	/// <summary>
	/// Length greater-than-or-equal comparison.
	/// </summary>
	public const string LengthGreaterThanOrEqual = "len>=";

	/// <summary>
	/// Length less-than-or-equal comparison.
	/// </summary>
	public const string LengthLessThanOrEqual = "len<=";
}
