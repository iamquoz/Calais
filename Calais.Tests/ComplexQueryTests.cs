using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    public class ComplexQueryTests
    {
        private readonly PostgreSqlFixture _fixture;
        private readonly CalaisProcessor _processor;

        public ComplexQueryTests(PostgreSqlFixture fixture)
        {
            _fixture = fixture;
            _processor = new CalaisBuilder()
                .ConfigureEntity<User>(e =>
                {
                    e.Ignore(u => u.PasswordHash, sorts: true, filter: true);
                    e.AddSort("is_banned", u => u.LockoutEnd != null);
                })
                .WithDefaultVectorLanguage("english")
                .Build();
        }

        [Fact]
        public async Task CompleteQuery_AppliesAllOperations()
        {
            await using var context = _fixture.CreateContext();

            // This mimics the example from the requirements
            var query = new CalaisQuery
            {
                Page = 1,
                PageSize = 10,
                Sorts =
                [
	                new SortDescriptor { Field = "name", Direction = "asc" },
	                new SortDescriptor { Field = "age", Direction = "desc" }
                ],
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "name",
		                Operator = "==",
		                Values = ["alice", "bob", "charlie"]
	                },

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

            var result = await _processor.ApplyAsync(
                context.Users.Include(u => u.Posts).Include(u => u.Comments), 
                query);

            result.Items.Should().HaveCount(3);
            result.Items.Should().BeInAscendingOrder(u => u.Name);
            result.Items.All(u => u.Age is >= 20 and <= 35).Should().BeTrue();
        }

        [Fact]
        public async Task ComplexOrQuery_CombinesConditionsCorrectly()
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
				                Operator = "@=",
				                Values = ["li"] // matches alice, charlie
			                },

			                new FilterDescriptor
			                {
				                Field = "age",
				                Operator = ">=",
				                Values = [35]
			                }
		                ]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync();

            // age >= 25 AND (name contains 'li' OR age >= 35)
            // alice(25, contains li), charlie(35, contains li AND age >= 35), eve(40, age >= 35)
            result.Should().HaveCount(3);
        }

        [Fact]
        public async Task QueryFromJson_ParsesAndExecutesCorrectly()
        {
            await using var context = _fixture.CreateContext();

            var json = """
                       {
                        "page": 1,
                        "pageSize": 10,
                        "sorts": [
                            { "field": "name", "direction": "asc" }
                        ],
                        "filters": [
                            {
                                "field": "name",
                                "operator": "@=",
                                "values": ["a"]
                            }
                        ]
                        }
                       """;

            var query = JsonSerializer.Deserialize<CalaisQuery>(json, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            query.Should().NotBeNull();

            var result = await _processor.ApplyAsync(context.Users, query!);

            // Names containing 'a': alice, charlie, diana
            result.Items.Should().HaveCount(3);
            result.Items.Should().BeInAscendingOrder(u => u.Name);
        }
    }
}
