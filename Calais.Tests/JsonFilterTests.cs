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
    public class JsonFilterTests
    {
        private readonly PostgreSqlFixture _fixture;
        private readonly CalaisProcessor _processor;

        public JsonFilterTests(PostgreSqlFixture fixture)
        {
            _fixture = fixture;
            _processor = new CalaisBuilder().Build();
        }

        [Fact]
        public async Task Filter_JsonProperty_Contains_MatchesValue()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "jsonbColumn.randomData",
		                IsJson = true,
		                Operator = "@=",
		                Values = ["tagged"]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            // alice and charlie have "tagged" in randomData
            result.Should().HaveCount(2);
            result.Select(u => u.Name).Should().BeEquivalentTo(new[] { "alice", "charlie" });
        }

        [Fact]
        public async Task Filter_JsonProperty_Equals_MatchesExactValue()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "jsonbColumn.randomData",
		                IsJson = true,
		                Operator = "==",
		                Values = ["other"]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            result.Should().HaveCount(1);
            result[0].Name.Should().Be("bob");
        }

        [Fact]
        public async Task Sort_JsonProperty_SortsCorrectly()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Sorts =
                [
	                new SortDescriptor
	                {
		                Field = "jsonbColumn.randomData",
		                IsJson = true,
		                Direction = "asc"
	                }
                ]
            };

            // This tests that JSON sorting can be applied
            // Note: Users without jsonbColumn will have null values
            var result = await _processor.ApplySorting(
                context.Users.Where(u => u.JsonbColumn != null), query)
                .ToListAsync();

            result.Should().HaveCount(3);
        }
    }
}
