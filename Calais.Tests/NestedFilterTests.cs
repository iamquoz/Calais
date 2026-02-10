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
    public class NestedFilterTests
    {
        private readonly PostgreSqlFixture _fixture;
        private readonly CalaisProcessor _processor;

        public NestedFilterTests(PostgreSqlFixture fixture)
        {
            _fixture = fixture;
            _processor = new CalaisBuilder().Build();
        }

        [Fact]
        public async Task Filter_NestedProperty_FiltersThroughNavigation()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "comments.text",
		                Operator = "@=",
		                Values = ["good"]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(
                context.Users.Include(u => u.Comments), query)
                .ToListAsync();

            // Users who have at least one comment containing "good"
            result.Should().HaveCountGreaterThan(0);
            result.All(u => u.Comments.Any(c => c.Text.Contains("good"))).Should().BeTrue();
        }

        [Fact]
        public async Task Filter_NestedPostTitle_FiltersThroughPosts()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "posts.title",
		                Operator = "@=",
		                Values = ["abc"]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(
                context.Users.Include(u => u.Posts), query)
                .ToListAsync();

            result.Should().HaveCountGreaterThan(0);
            result.All(u => u.Posts.Any(p => p.Title.Contains("abc"))).Should().BeTrue();
        }
    }
}
