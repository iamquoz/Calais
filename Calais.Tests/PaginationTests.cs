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
    public class PaginationTests
    {
        private readonly PostgreSqlFixture _fixture;
        private readonly CalaisProcessor _processor;

        public PaginationTests(PostgreSqlFixture fixture)
        {
            _fixture = fixture;
            _processor = new CalaisBuilder()
                .WithDefaultPageSize(10)
                .WithMaxPageSize(100)
                .Build();
        }

        [Fact]
        public async Task Pagination_FirstPage_ReturnsCorrectItems()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Page = 1,
                PageSize = 2,
                Sorts = [new SortDescriptor { Field = "name", Direction = "asc" }]
            };

            var result = await _processor.Apply(context.Users, query)
                .ToListAsync();

            result.Should().HaveCount(2);
            result[0].Name.Should().Be("alice");
            result[1].Name.Should().Be("bob");
        }

        [Fact]
        public async Task Pagination_SecondPage_SkipsFirstPage()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Page = 2,
                PageSize = 2,
                Sorts = [new SortDescriptor { Field = "name", Direction = "asc" }]
            };

            var result = await _processor.Apply(context.Users, query)
                .ToListAsync();

            result.Should().HaveCount(2);
            result[0].Name.Should().Be("charlie");
            result[1].Name.Should().Be("diana");
        }

        [Fact]
        public async Task Pagination_SeparateFromFilter_CanBeAppliedIndependently()
        {
            await using var context = _fixture.CreateContext();

            var filterQuery = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "age",
		                Operator = ">=",
		                Values = [25]
	                }
                ]
            };

            // Apply filters without pagination
            var filteredQuery = _processor.ApplyWithoutPagination(context.Users, filterQuery);
            var totalCount = await filteredQuery.CountAsync();

            // Apply pagination separately
            var pagedResult = await _processor.ApplyPagination(filteredQuery, 1, 2)
                .ToListAsync();

            totalCount.Should().Be(4); // alice(25), bob(30), charlie(35), eve(40)
            pagedResult.Should().HaveCount(2);
        }

        [Fact]
        public async Task ApplyAsync_ReturnsPagedResult_WithTotalCount()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Page = 2,
                PageSize = 2,
                Sorts = [new SortDescriptor { Field = "name", Direction = "asc" }]
            };

            var result = await _processor.ApplyAsync(context.Users, query);

            result.Page.Should().Be(2);
            result.PageSize.Should().Be(2);
            result.TotalCount.Should().Be(5);
            result.TotalPages.Should().Be(3);
            result.Items.Should().HaveCount(2);
            result.HasPreviousPage.Should().BeTrue();
            result.HasNextPage.Should().BeTrue();
        }

        [Fact]
        public async Task Pagination_RespectsMaxPageSize()
        {
            var processor = new CalaisBuilder()
                .WithMaxPageSize(3)
                .Build();

            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Page = 1,
                PageSize = 1000 // Exceeds max
            };

            var result = await processor.Apply(context.Users, query)
                .ToListAsync();

            result.Should().HaveCount(3); // Limited to MaxPageSize
        }
    }
}
