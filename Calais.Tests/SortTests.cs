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
    public class SortTests
    {
        private readonly PostgreSqlFixture _fixture;
        private readonly CalaisProcessor _processor;

        public SortTests(PostgreSqlFixture fixture)
        {
            _fixture = fixture;
            _processor = new CalaisBuilder()
                .ConfigureEntity<User>(e => 
                    e.AddSort("is_banned", u => u.LockoutEnd != null))
                .Build();
        }

        [Fact]
        public async Task Sort_SingleField_Ascending()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Sorts = [new SortDescriptor { Field = "name", Direction = "asc" }]
            };

            var result = await _processor.ApplySorting(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().BeInAscendingOrder(u => u.Name);
        }

        [Fact]
        public async Task Sort_SingleField_Descending()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Sorts = [new SortDescriptor { Field = "age", Direction = "desc" }]
            };

            var result = await _processor.ApplySorting(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().BeInDescendingOrder(u => u.Age);
        }

        [Fact]
        public async Task Sort_MultipleFields_AppliedInOrder()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Sorts =
                [
	                new SortDescriptor { Field = "age", Direction = "asc" },
	                new SortDescriptor { Field = "name", Direction = "desc" }
                ]
            };

            var result = await _processor.ApplySorting(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // Primary sort by age ascending
            var ages = result.Select(u => u.Age).ToList();
            ages.Should().BeInAscendingOrder();
        }

        [Fact]
        public async Task Sort_CustomSort_AppliesExpression()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Sorts = [new SortDescriptor { Field = "is_banned", Direction = "desc" }]
            };

            var result = await _processor.ApplySorting(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // Banned users (LockoutEnd != null) should come first when desc
            result.Should().HaveCount(5);
            // Bob has LockoutEnd set, so should be first
            result.First().LockoutEnd.Should().NotBeNull();
        }
    }
}
