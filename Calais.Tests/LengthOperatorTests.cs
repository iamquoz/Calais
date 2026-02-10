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
    public class LengthOperatorTests
    {
        private readonly PostgreSqlFixture _fixture;
        private readonly CalaisProcessor _processor;

        public LengthOperatorTests(PostgreSqlFixture fixture)
        {
            _fixture = fixture;
            _processor = new CalaisBuilder().Build();
        }

        [Fact]
        public async Task Filter_LengthGreaterThanOrEqual_FiltersCollectionSize()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "comments",
		                Operator = "len>=",
		                Values = [1]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(
                context.Users.Include(u => u.Comments), query)
                .ToListAsync();

            // Users with at least 1 comment
            result.Should().HaveCountGreaterThan(0);
            result.All(u => u.Comments.Count >= 1).Should().BeTrue();
        }

        [Fact]
        public async Task Filter_LengthEquals_FiltersExactCount()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "posts",
		                Operator = "len==",
		                Values = [1]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(
                context.Users.Include(u => u.Posts), query)
                .ToListAsync();

            result.All(u => u.Posts.Count == 1).Should().BeTrue();
        }

        [Fact]
        public async Task Filter_LengthGreaterThan_FiltersCorrectly()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "comments",
		                Operator = "len>",
		                Values = [0]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(
                context.Users.Include(u => u.Comments), query)
                .ToListAsync();

            result.All(u => u.Comments.Count > 0).Should().BeTrue();
        }
    }
}
