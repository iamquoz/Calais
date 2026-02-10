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
    public class OrGroupFilterTests
    {
        private readonly PostgreSqlFixture _fixture;
        private readonly CalaisProcessor _processor;

        public OrGroupFilterTests(PostgreSqlFixture fixture)
        {
            _fixture = fixture;
            _processor = new CalaisBuilder().Build();
        }

        [Fact]
        public async Task Filter_OrGroup_MatchesAnyCondition()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Or =
		                [
			                new FilterDescriptor
			                {
				                Field = "name",
				                Operator = "==",
				                Values = ["alice"]
			                },

			                new FilterDescriptor
			                {
				                Field = "age",
				                Operator = ">",
				                Values = [35]
			                }
		                ]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // alice (name match) or eve (age > 35)
            result.Should().HaveCount(2);
            result.Select(u => u.Name).Should().BeEquivalentTo(new[] { "alice", "eve" });
        }

        [Fact]
        public async Task Filter_OrGroupCombinedWithAnd_AppliesCorrectly()
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
		                Values = [25]
	                },

	                new FilterDescriptor
	                {
		                Or =
		                [
			                new FilterDescriptor
			                {
				                Field = "name",
				                Operator = "==",
				                Values = ["alice"]
			                },

			                new FilterDescriptor
			                {
				                Field = "name",
				                Operator = "==",
				                Values = ["charlie"]
			                }
		                ]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // age >= 25 AND (name == alice OR name == charlie)
            result.Should().HaveCount(2);
            result.All(u => u.Age >= 25).Should().BeTrue();
            result.Select(u => u.Name).Should().BeEquivalentTo(new[] { "alice", "charlie" });
        }
    }
}
