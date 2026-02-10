using System;
using System.Collections.Generic;
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
    public class BasicFilterTests
    {
        private readonly PostgreSqlFixture _fixture;
        private readonly CalaisProcessor _processor;

        public BasicFilterTests(PostgreSqlFixture fixture)
        {
            _fixture = fixture;
            _processor = new CalaisBuilder()
                .ConfigureEntity<User>(e => e.Ignore(u => u.PasswordHash, sorts: true, filter: true))
                .Build();
        }

        [Fact]
        public async Task Filter_Equals_SingleValue_ReturnsMatchingRecords()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "name",
		                Operator = "==",
		                Values = ["alice"]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            result.Should().HaveCount(1);
            result[0].Name.Should().Be("alice");
        }

        [Fact]
        public async Task Filter_Equals_MultipleValues_TreatsAsOr()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "name",
		                Operator = "==",
		                Values = ["alice", "bob"]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            result.Should().HaveCount(2);
            result.Select(u => u.Name).Should().BeEquivalentTo("alice", "bob");
        }

        [Fact]
        public async Task Filter_NotEquals_ExcludesMatchingRecords()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "id",
		                Operator = "!=",
		                Values =
		                [
			                "cf9175ce-ded3-4447-9725-e6f018430de9",
			                "a435a45f-a07f-4219-97e5-e2f6d534c3b0"
		                ]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            result.Should().HaveCount(3);
            result.Select(u => u.Name).Should().NotContain(["alice", "bob"]);
        }

        [Fact]
        public async Task Filter_GreaterThanOrEqual_AndLessThanOrEqual_CreatesRange()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "age",
		                Operator = ">=",
		                Values = [20]
	                },

	                new FilterDescriptor
	                {
		                Field = "age",
		                Operator = "<=",
		                Values = [35]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            result.Should().HaveCount(4);
            result.All(u => u.Age >= 20 && u.Age <= 35).Should().BeTrue();
        }

        [Fact]
        public async Task Filter_Contains_MatchesSubstring()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "name",
		                Operator = "@=",
		                Values = ["li"]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            result.Should().HaveCount(2); // alice and charlie
            result.All(u => u.Name.Contains("li")).Should().BeTrue();
        }

        [Fact]
        public async Task Filter_StartsWith_MatchesPrefix()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "name",
		                Operator = "_=",
		                Values = ["a"]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            result.Should().HaveCount(1);
            result[0].Name.Should().Be("alice");
        }

        [Fact]
        public async Task Filter_IgnoreCase_MatchesCaseInsensitive()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "name",
		                Operator = "==*",
		                Values = ["ALICE"]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            result.Should().HaveCount(1);
            result[0].Name.Should().Be("alice");
        }

        [Fact]
        public async Task IgnoredField_IsNotFilterable()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "passwordHash",
		                Operator = "==",
		                Values = ["hash1"]
	                }
                ]
            };

            // With default options (ThrowOnInvalidFields = false), ignored field is silently skipped
            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            // Should return all users since the filter is ignored
            result.Should().HaveCount(5);
        }

        [Fact]
        public async Task Filter_ArrayContains_SingleValue_ReturnsMatchingRecords()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "tags",
                        Operator = "@=",
                        Values = ["admin"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            result.Should().HaveCount(2); // alice and charlie have "admin" tag
            result.Select(u => u.Name).Should().BeEquivalentTo("alice", "charlie");
        }

        [Fact]
        public async Task Filter_ArrayContains_MultipleValues_TreatsAsOr()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "tags",
                        Operator = "@=",
                        Values = ["admin", "tester"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            // alice (admin), bob (tester), charlie (admin), diana (tester)
            result.Should().HaveCount(4);
            result.Select(u => u.Name).Should().BeEquivalentTo("alice", "bob", "charlie", "diana");
        }

        [Fact]
        public async Task Filter_ArrayNotContains_SingleValue_ExcludesMatchingRecords()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "tags",
                        Operator = "!@=",
                        Values = ["developer"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            // charlie (admin, manager), diana (tester), eve (empty)
            result.Should().HaveCount(3);
            result.Select(u => u.Name).Should().BeEquivalentTo("charlie", "diana", "eve");
        }

        [Fact]
        public async Task Filter_ArrayContains_NoMatch_ReturnsEmpty()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "tags",
                        Operator = "@=",
                        Values = ["nonexistent"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task Filter_ArrayContains_CombinedWithOtherFilters()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "tags",
                        Operator = "@=",
                        Values = ["developer"]
                    },
                    new FilterDescriptor
                    {
                        Field = "age",
                        Operator = ">=",
                        Values = [30]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            // Only bob is a developer AND age >= 30
            result.Should().HaveCount(1);
            result[0].Name.Should().Be("bob");
        }
    }
}
