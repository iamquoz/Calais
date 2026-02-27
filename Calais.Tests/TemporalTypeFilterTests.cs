using System.Linq;
using System.Threading.Tasks;
using Calais.Models;
using Calais.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Calais.Tests
{
    [Collection("PostgreSql")]
    public class TemporalTypeFilterTests
    {
        private readonly PostgreSqlFixture _fixture;
        private readonly CalaisProcessor _processor;

        public TemporalTypeFilterTests(PostgreSqlFixture fixture)
        {
            _fixture = fixture;
            _processor = new CalaisBuilder().Build();
        }

        // ──────────────────────────────────────────────
        //  DateOnly filters
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Filter_DateOnly_Equals_ReturnsSingleMatch()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "birthDate",
                        Operator = "==",
                        Values = ["1999-03-15"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(1);
            result[0].Name.Should().Be("alice");
        }

        [Fact]
        public async Task Filter_DateOnly_GreaterThan_ReturnsLaterDates()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "birthDate",
                        Operator = ">",
                        Values = ["2000-01-01"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // Only diana (2002-05-10) is after 2000-01-01
            result.Should().HaveCount(1);
            result[0].Name.Should().Be("diana");
        }

        [Fact]
        public async Task Filter_DateOnly_LessThanOrEqual_ReturnsEarlierOrEqualDates()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "birthDate",
                        Operator = "<=",
                        Values = ["1994-07-22"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // charlie (1989-12-01) and bob (1994-07-22)
            result.Should().HaveCount(2);
            result.Select(u => u.Name).Should().BeEquivalentTo(["bob", "charlie"]);
        }

        [Fact]
        public async Task Filter_DateOnly_NotEquals_ExcludesMatch()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "birthDate",
                        Operator = "!=",
                        Values = ["1999-03-15"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // bob, charlie, diana have non-null birthDate != alice's date; eve has null (excluded by !=)
            result.Select(u => u.Name).Should().NotContain("alice");
        }

        // ──────────────────────────────────────────────
        //  TimeOnly filters
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Filter_TimeOnly_Equals_ReturnsSingleMatch()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "preferredContactTime",
                        Operator = "==",
                        Values = ["14:30"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(1);
            result[0].Name.Should().Be("bob");
        }

        [Fact]
        public async Task Filter_TimeOnly_GreaterThanOrEqual_ReturnsLaterTimes()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "preferredContactTime",
                        Operator = ">=",
                        Values = ["14:00"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // bob (14:30) and charlie (18:00)
            result.Should().HaveCount(2);
            result.Select(u => u.Name).Should().BeEquivalentTo(["bob", "charlie"]);
        }

        [Fact]
        public async Task Filter_TimeOnly_LessThan_ReturnsEarlierTimes()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "preferredContactTime",
                        Operator = "<",
                        Values = ["09:00"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // diana (08:00) is the only one before 09:00
            result.Should().HaveCount(1);
            result[0].Name.Should().Be("diana");
        }

        // ──────────────────────────────────────────────
        //  TimeSpan filters
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Filter_TimeSpan_Equals_ReturnsSingleMatch()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "sessionDuration",
                        Operator = "==",
                        Values = ["01:30:00"] // 90 minutes
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(1);
            result[0].Name.Should().Be("bob");
        }

        [Fact]
        public async Task Filter_TimeSpan_GreaterThan_ReturnsLongerDurations()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "sessionDuration",
                        Operator = ">",
                        Values = ["01:30:00"] // > 90 minutes
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // alice (2h) and charlie (3h)
            result.Should().HaveCount(2);
            result.Select(u => u.Name).Should().BeEquivalentTo(["alice", "charlie"]);
        }

        [Fact]
        public async Task Filter_TimeSpan_LessThanOrEqual_ReturnsShorterOrEqualDurations()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "sessionDuration",
                        Operator = "<=",
                        Values = ["01:30:00"] // <= 90 minutes
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // bob (90min) and diana (45min)
            result.Should().HaveCount(2);
            result.Select(u => u.Name).Should().BeEquivalentTo(["bob", "diana"]);
        }

        // ──────────────────────────────────────────────
        //  Sorting on temporal types
        // ──────────────────────────────────────────────

        [Fact]
        public async Task Sort_DateOnly_Ascending_OrdersCorrectly()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "birthDate",
                        Operator = "!=",
                        Values = ["0001-01-01"] // exclude nulls implicitly via having a value
                    }
                ],
                Sorts = [new SortDescriptor { Field = "birthDate", Direction = "asc" }]
            };

            // Filter to only users with BirthDate set, then sort
            var allWithBirthDate = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // Apply sorting separately on users that have birthdate
            var sortQuery = new CalaisQuery
            {
                Sorts = [new SortDescriptor { Field = "birthDate", Direction = "asc" }]
            };

            var result = await _processor.ApplySorting(
                    context.Users.Where(u => u.BirthDate != null), sortQuery)
                .ToListAsync(TestContext.Current.CancellationToken);

            // charlie (1989) < bob (1994) < alice (1999) < diana (2002)
            result.Should().HaveCount(4);
            result[0].Name.Should().Be("charlie");
            result[1].Name.Should().Be("bob");
            result[2].Name.Should().Be("alice");
            result[3].Name.Should().Be("diana");
        }

        [Fact]
        public async Task Sort_TimeOnly_Descending_OrdersCorrectly()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Sorts = [new SortDescriptor { Field = "preferredContactTime", Direction = "desc" }]
            };

            var result = await _processor.ApplySorting(
                    context.Users.Where(u => u.PreferredContactTime != null), query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // charlie (18:00) > bob (14:30) > alice (09:00) > diana (08:00)
            result.Should().HaveCount(4);
            result[0].Name.Should().Be("charlie");
            result[1].Name.Should().Be("bob");
            result[2].Name.Should().Be("alice");
            result[3].Name.Should().Be("diana");
        }

        [Fact]
        public async Task Sort_TimeSpan_Ascending_OrdersCorrectly()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Sorts = [new SortDescriptor { Field = "sessionDuration", Direction = "asc" }]
            };

            var result = await _processor.ApplySorting(
                    context.Users.Where(u => u.SessionDuration != null), query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // diana (45m) < bob (90m) < alice (2h) < charlie (3h)
            result.Should().HaveCount(4);
            result[0].Name.Should().Be("diana");
            result[1].Name.Should().Be("bob");
            result[2].Name.Should().Be("alice");
            result[3].Name.Should().Be("charlie");
        }
    }
}
