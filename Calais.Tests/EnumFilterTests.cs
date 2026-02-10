using System.Linq;
using System.Threading.Tasks;
using Calais.Models;
using Calais.Tests.Fixtures;
using Calais.Tests.TestEntities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Calais.Tests
{
    [Collection("PostgreSql")]
    public class EnumFilterTests
    {
        private readonly PostgreSqlFixture _fixture;
        private readonly CalaisProcessor _processor;

        public EnumFilterTests(PostgreSqlFixture fixture)
        {
            _fixture = fixture;
            _processor = new CalaisBuilder()
                .ConfigureEntity<User>(e => e.Ignore(u => u.PasswordHash, sorts: true, filter: true))
                .Build();
        }

        [Fact]
        public async Task Filter_Enum_Equals_ByName_ReturnsMatchingRecords()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "status",
                        Operator = "==",
                        Values = ["Active"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(2);
            result.Select(u => u.Name).Should().BeEquivalentTo("alice", "charlie");
            result.Should().AllSatisfy(u => u.Status.Should().Be(UserStatus.Active));
        }

        [Fact]
        public async Task Filter_Enum_Equals_ByNameCaseInsensitive_ReturnsMatchingRecords()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "status",
                        Operator = "==",
                        Values = ["active"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(2);
            result.Should().AllSatisfy(u => u.Status.Should().Be(UserStatus.Active));
        }

        [Fact]
        public async Task Filter_Enum_NotEquals_ExcludesMatchingRecords()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "status",
                        Operator = "!=",
                        Values = ["Active"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(3);
            result.Should().AllSatisfy(u => u.Status.Should().NotBe(UserStatus.Active));
        }

        [Fact]
        public async Task Filter_Enum_Equals_MultipleValues_TreatsAsOr()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "status",
                        Operator = "==",
                        Values = ["Active", "Pending"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(3);
            result.Should().AllSatisfy(u => u.Status.Should().BeOneOf(UserStatus.Active, UserStatus.Pending));
        }

        [Fact]
        public async Task Filter_Enum_GreaterThan_ReturnsMatchingRecords()
        {
            await using var context = _fixture.CreateContext();

            // Pending=0, Active=1, Suspended=2, Banned=3
            // Greater than Active (1) means Suspended (2) and Banned (3)
            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "status",
                        Operator = ">",
                        Values = ["Active"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(2);
            result.Select(u => u.Name).Should().BeEquivalentTo("bob", "eve");
            result.Should().AllSatisfy(u => u.Status.Should().BeOneOf(UserStatus.Suspended, UserStatus.Banned));
        }

        [Fact]
        public async Task Filter_Enum_LessThan_ReturnsMatchingRecords()
        {
            await using var context = _fixture.CreateContext();

            // Pending=0, Active=1, Suspended=2, Banned=3
            // Less than Active (1) means Pending (0)
            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "status",
                        Operator = "<",
                        Values = ["Active"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(1);
            result[0].Name.Should().Be("diana");
            result[0].Status.Should().Be(UserStatus.Pending);
        }

        [Fact]
        public async Task Filter_Enum_GreaterThanOrEqual_ReturnsMatchingRecords()
        {
            await using var context = _fixture.CreateContext();

            // Pending=0, Active=1, Suspended=2, Banned=3
            // Greater than or equal to Suspended (2) means Suspended (2) and Banned (3)
            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "status",
                        Operator = ">=",
                        Values = ["Suspended"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(2);
            result.Select(u => u.Name).Should().BeEquivalentTo("bob", "eve");
        }

        [Fact]
        public async Task Filter_Enum_LessThanOrEqual_ReturnsMatchingRecords()
        {
            await using var context = _fixture.CreateContext();

            // Pending=0, Active=1, Suspended=2, Banned=3
            // Less than or equal to Active (1) means Pending (0) and Active (1)
            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "status",
                        Operator = "<=",
                        Values = ["Active"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(3);
            result.Select(u => u.Name).Should().BeEquivalentTo("alice", "charlie", "diana");
        }
    }
}
