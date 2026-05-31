using System.Linq;
using Calais.Models;

namespace Calais.Tests.TestEntities;

internal sealed class UserCustomSortMethods : ICalaisCustomSortMethods
{
    public IQueryable<User> is_banned(IQueryable<User> source, CalaisSortContext context)
    {
        return context.Direction == SortDirection.Desc
            ? Apply(source, context.UseThenBy, descending: true)
            : Apply(source, context.UseThenBy, descending: false);
    }

    private static IQueryable<User> Apply(IQueryable<User> source, bool useThenBy, bool descending)
    {
        if (useThenBy && source is IOrderedQueryable<User> ordered)
        {
            return descending
                ? ordered.ThenByDescending(u => u.LockoutEnd != null)
                : ordered.ThenBy(u => u.LockoutEnd != null);
        }

        return descending
            ? source.OrderByDescending(u => u.LockoutEnd != null)
            : source.OrderBy(u => u.LockoutEnd != null);
    }
}
