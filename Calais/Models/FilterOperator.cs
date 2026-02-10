namespace Calais.Models
{
    /// <summary>
    /// Supported filter operators matching Sieve-style syntax
    /// </summary>
    public static class FilterOperator
    {
        public const string Equals = "==";
        public const string NotEquals = "!=";
        public const string GreaterThan = ">";
        public const string LessThan = "<";
        public const string GreaterThanOrEqual = ">=";
        public const string LessThanOrEqual = "<=";
        public const string Contains = "@=";
        public const string StartsWith = "_=";
        public const string EndsWith = "_-=";
        public const string DoesNotContain = "!@=";
        public const string DoesNotStartWith = "!_=";
        public const string DoesNotEndWith = "!_-=";
        public const string EqualsIgnoreCase = "==*";
        public const string NotEqualsIgnoreCase = "!=*";
        public const string ContainsIgnoreCase = "@=*";
        public const string StartsWithIgnoreCase = "_=*";
        public const string EndsWithIgnoreCase = "_-=*";
        public const string DoesNotContainIgnoreCase = "!@=*";
        public const string DoesNotStartWithIgnoreCase = "!_=*";
        public const string DoesNotEndWithIgnoreCase = "!_-=*";
        public const string LengthEquals = "len==";
        public const string LengthNotEquals = "len!=";
        public const string LengthGreaterThan = "len>";
        public const string LengthLessThan = "len<";
        public const string LengthGreaterThanOrEqual = "len>=";
        public const string LengthLessThanOrEqual = "len<=";
    }
}
