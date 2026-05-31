using System;
using System.Collections.Generic;
using System.Linq;
using Calais.Configuration;
using Calais.Exceptions;
using Calais.Models;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Calais.Tests;

public class CustomMethodAndDefaultSortTests
{
    [Fact]
    public void CustomFilter_UsesContextServicesAndValues()
    {
        var services = new ServiceCollection()
            .AddSingleton(new AgeOffsetService(5))
            .BuildServiceProvider();
        var processor = new CalaisProcessor(
            new CalaisOptions(),
            services,
            [new ContextFilterMethods()],
            null
        );
        var query = new CalaisQuery
        {
            Filters =
            [
                new FilterDescriptor
                {
                    Field = "minimumAgeFromContext",
                    Operator = ">=",
                    Values = [30],
                },
            ],
        };

        var result = processor.ApplyFilters(SampleUsers().AsQueryable(), query).ToList();

        result.Select(u => u.Name).Should().Equal("charlie");
    }

    [Fact]
    public void CustomFilter_CanUseScopedConstructorInjection()
    {
        var services = new ServiceCollection()
            .AddScoped(_ => new AgeLimitService(30))
            .AddScoped<ICalaisCustomFilterMethods, InjectedFilterMethods>()
            .AddCalais()
            .BuildServiceProvider();

        using var scope = services.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<CalaisProcessor>();
        var query = new CalaisQuery
        {
            Filters =
            [
                new FilterDescriptor
                {
                    Field = "minimumInjectedAge",
                    Operator = ">=",
                    Values = [],
                },
            ],
        };

        var result = processor.ApplyFilters(SampleUsers().AsQueryable(), query).ToList();

        result.Select(u => u.Name).Should().Equal("bob", "charlie");
    }

    [Fact]
    public void CustomSort_ReceivesUseThenByForSecondarySort()
    {
        var methods = new RecordingSortMethods();
        var processor = new CalaisProcessor(
            new CalaisOptions(),
            new EmptyServiceProvider(),
            null,
            [methods]
        );
        var query = new CalaisQuery
        {
            Sorts =
            [
                new SortDescriptor { Field = "Age", Direction = "asc" },
                new SortDescriptor { Field = "nameLength", Direction = "desc" },
            ],
        };

        _ = processor.ApplySorting(SampleUsers().AsQueryable(), query).ToList();

        methods.LastUseThenBy.Should().BeTrue();
    }

    [Fact]
    public void CustomSort_SupportsCompatibleGenericMethods()
    {
        var processor = new CalaisProcessor(
            new CalaisOptions(),
            new EmptyServiceProvider(),
            null,
            [new GenericSortMethods()]
        );
        var query = new CalaisQuery
        {
            Sorts = [new SortDescriptor { Field = "genericName", Direction = "desc" }],
        };

        var result = processor.ApplySorting(SampleUsers().AsQueryable(), query).ToList();

        result.Select(u => u.Name).Should().Equal("charlie", "bob", "alice");
    }

    [Fact]
    public void DefaultSorts_AreUsedWhenRequestHasNoSorts()
    {
        var processor = new CalaisBuilder()
            .ConfigureEntity<Book>(e =>
                e.AddDefaultSort(b => b.Name).ThenBy(b => b.Author.Name, SortDirection.Desc)
            )
            .Build();
        var query = new CalaisQuery();

        var result = processor.ApplySorting(SampleBooks().AsQueryable(), query).ToList();

        result
            .Select(b => $"{b.Name}:{b.Author.Name}")
            .Should()
            .Equal("Alpha:Zed", "Alpha:Ada", "Beta:Bob");
    }

    [Fact]
    public void DefaultSorts_AppendMissingDefaultsAfterRequestSorts()
    {
        var processor = new CalaisBuilder()
            .ConfigureEntity<Book>(e =>
                e.AddDefaultSort(b => b.Name).ThenBy(b => b.Author.Name, SortDirection.Desc)
            )
            .Build();
        var query = new CalaisQuery
        {
            Sorts = [new SortDescriptor { Field = "author.name", Direction = "asc" }],
        };

        var result = processor.ApplySorting(SampleBooks().AsQueryable(), query).ToList();

        result
            .Select(b => $"{b.Author.Name}:{b.Name}")
            .Should()
            .Equal("Ada:Alpha", "Bob:Beta", "Zed:Alpha");
    }

    [Fact]
    public void DefaultSorts_DoNotDuplicateRequestedDefaultField()
    {
        var processor = new CalaisBuilder()
            .ConfigureEntity<Book>(e =>
                e.AddDefaultSort(b => b.Name).ThenBy(b => b.Author.Name, SortDirection.Desc)
            )
            .Build();
        var query = new CalaisQuery
        {
            Sorts = [new SortDescriptor { Field = "AUTHOR.NAME", Direction = "asc" }],
        };

        var result = processor.ApplySorting(SampleBooks().AsQueryable(), query).ToList();

        result.Select(b => b.Author.Name).Should().Equal("Ada", "Bob", "Zed");
    }

    [Fact]
    public void NoDefaultSorts_PreservesCurrentNoSortBehavior()
    {
        var processor = new CalaisBuilder().Build();
        var source = SampleBooks().AsQueryable();

        var result = processor.ApplySorting(source, new CalaisQuery()).ToList();

        result.Should().Equal(source);
    }

    [Fact]
    public void DefaultSort_RejectsCollectionNavigation()
    {
        var act = () =>
            new CalaisBuilder()
                .ConfigureEntity<Library>(e => e.AddDefaultSort(l => l.Books.Count))
                .Build();

        act.Should().Throw<ArgumentException>().WithMessage("*collection navigation*");
    }

    [Fact]
    public void StrictMode_ThrowsWhenCustomFilterAppearsInOrGroup()
    {
        var processor = new CalaisProcessor(
            new CalaisBuilder().ThrowOnInvalidFields().Options,
            new EmptyServiceProvider(),
            [new ContextFilterMethods()],
            null
        );
        var query = new CalaisQuery
        {
            Filters =
            [
                new FilterDescriptor
                {
                    Or =
                    [
                        new FilterDescriptor { Field = "minimumAgeFromContext", Values = [20] },
                        new FilterDescriptor
                        {
                            Field = "Name",
                            Operator = "==",
                            Values = ["alice"],
                        },
                    ],
                },
            ],
        };

        var act = () => processor.ApplyFilters(SampleUsers().AsQueryable(), query);

        act.Should().Throw<ExpressionBuildException>().WithMessage("*OR group*");
    }

    [Fact]
    public void NonStrictMode_IgnoresCustomFilterInsideOrGroup()
    {
        var processor = new CalaisProcessor(
            new CalaisOptions(),
            new EmptyServiceProvider(),
            [new ContextFilterMethods()],
            null
        );
        var query = new CalaisQuery
        {
            Filters =
            [
                new FilterDescriptor
                {
                    Or =
                    [
                        new FilterDescriptor { Field = "minimumAgeFromContext", Values = [20] },
                        new FilterDescriptor
                        {
                            Field = "Name",
                            Operator = "==",
                            Values = ["alice"],
                        },
                    ],
                },
            ],
        };

        var result = processor.ApplyFilters(SampleUsers().AsQueryable(), query).ToList();

        result.Select(u => u.Name).Should().Equal("alice");
    }

    [Fact]
    public void StrictMode_ThrowsForAmbiguousCustomSortMethods()
    {
        var processor = new CalaisProcessor(
            new CalaisBuilder().ThrowOnInvalidFields().Options,
            new EmptyServiceProvider(),
            null,
            [new AmbiguousSortMethods(), new AmbiguousSortMethods()]
        );
        var query = new CalaisQuery
        {
            Sorts = [new SortDescriptor { Field = "ambiguous", Direction = "asc" }],
        };

        var act = () => processor.ApplySorting(SampleUsers().AsQueryable(), query);

        act.Should().Throw<ExpressionBuildException>().WithMessage("*ambiguous*");
    }

    [Fact]
    public void StrictMode_ThrowsForIncompatibleCustomFilterMethods()
    {
        var processor = new CalaisProcessor(
            new CalaisBuilder().ThrowOnInvalidFields().Options,
            new EmptyServiceProvider(),
            [new IncompatibleFilterMethods()],
            null
        );
        var query = new CalaisQuery
        {
            Filters = [new FilterDescriptor { Field = "badFilter", Values = [20] }],
        };

        var act = () => processor.ApplyFilters(SampleUsers().AsQueryable(), query);

        act.Should().Throw<ExpressionBuildException>().WithMessage("*incompatible*");
    }

    private static List<TestUser> SampleUsers() =>
        [
            new TestUser { Name = "alice", Age = 25 },
            new TestUser { Name = "bob", Age = 30 },
            new TestUser { Name = "charlie", Age = 35 },
        ];

    private static List<Book> SampleBooks() =>
        [
            new Book
            {
                Name = "Alpha",
                Author = new Author { Name = "Ada" },
            },
            new Book
            {
                Name = "Beta",
                Author = new Author { Name = "Bob" },
            },
            new Book
            {
                Name = "Alpha",
                Author = new Author { Name = "Zed" },
            },
        ];

    private class TestUser : NamedEntity
    {
        public int Age { get; set; }
    }

    private abstract class NamedEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private class Book
    {
        public string Name { get; set; } = string.Empty;
        public Author Author { get; set; } = new();
    }

    private class Author
    {
        public string Name { get; set; } = string.Empty;
    }

    private class Library
    {
        public List<Book> Books { get; set; } = [];
    }

    private sealed class AgeOffsetService(int offset)
    {
        public int Offset { get; } = offset;
    }

    private sealed class AgeLimitService(int minimumAge)
    {
        public int MinimumAge { get; } = minimumAge;
    }

    private sealed class ContextFilterMethods : ICalaisCustomFilterMethods
    {
        public IQueryable<TestUser> minimumAgeFromContext(
            IQueryable<TestUser> source,
            CalaisFilterContext context
        )
        {
            var offset =
                ((AgeOffsetService?)context.Services.GetService(typeof(AgeOffsetService)))?.Offset
                ?? 0;
            var minimumAge = Convert.ToInt32(context.Values[0]) + offset;
            return source.Where(u => u.Age >= minimumAge);
        }
    }

    private sealed class InjectedFilterMethods(AgeLimitService ageLimitService)
        : ICalaisCustomFilterMethods
    {
        public IQueryable<TestUser> minimumInjectedAge(
            IQueryable<TestUser> source,
            CalaisFilterContext context
        )
        {
            return source.Where(u => u.Age >= ageLimitService.MinimumAge);
        }
    }

    private sealed class RecordingSortMethods : ICalaisCustomSortMethods
    {
        public bool LastUseThenBy { get; private set; }

        public IQueryable<TestUser> nameLength(
            IQueryable<TestUser> source,
            CalaisSortContext context
        )
        {
            LastUseThenBy = context.UseThenBy;
            if (context.UseThenBy && source is IOrderedQueryable<TestUser> ordered)
            {
                return context.Direction == SortDirection.Desc
                    ? ordered.ThenByDescending(u => u.Name.Length)
                    : ordered.ThenBy(u => u.Name.Length);
            }

            return context.Direction == SortDirection.Desc
                ? source.OrderByDescending(u => u.Name.Length)
                : source.OrderBy(u => u.Name.Length);
        }
    }

    private sealed class GenericSortMethods : ICalaisCustomSortMethods
    {
        public IQueryable<T> genericName<T>(IQueryable<T> source, CalaisSortContext context)
            where T : NamedEntity
        {
            return context.Direction == SortDirection.Desc
                ? source.OrderByDescending(e => e.Name)
                : source.OrderBy(e => e.Name);
        }
    }

    private sealed class AmbiguousSortMethods : ICalaisCustomSortMethods
    {
        public IQueryable<TestUser> ambiguous(
            IQueryable<TestUser> source,
            CalaisSortContext context
        )
        {
            return source.OrderBy(u => u.Name);
        }
    }

    private sealed class IncompatibleFilterMethods : ICalaisCustomFilterMethods
    {
        public IQueryable<TestUser> badFilter(IQueryable<TestUser> source)
        {
            return source;
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
