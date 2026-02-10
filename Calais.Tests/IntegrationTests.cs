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
    public class IntegrationTests
    {
        private readonly PostgreSqlFixture _fixture;
        private readonly CalaisProcessor _processor;

        public IntegrationTests(PostgreSqlFixture fixture)
        {
            _fixture = fixture;
            _processor = new CalaisBuilder()
                .ConfigureEntity<User>(e =>
                {
                    e.Ignore(u => u.PasswordHash, sorts: true, filter: true);
                    e.AddSort("is_banned", u => u.LockoutEnd != null);
                })
                .WithDefaultVectorLanguage("english")
                .WithDefaultPageSize(10)
                .WithMaxPageSize(100)
                .Build();
        }

        [Fact]
        public async Task FullExample_FromRequirements_WorksCorrectly()
        {
            await using var context = _fixture.CreateContext();

            // Simplified version of the example from requirements
            // (without vector search which requires raw SQL in tests)
            var json = @"{
                ""page"": 1,
                ""pageSize"": 10,
                ""sorts"": [
                    { ""field"": ""name"", ""direction"": ""asc"" },
                    { ""field"": ""age"", ""direction"": ""desc"" }
                ],
                ""filters"": [
                    {
                        ""field"": ""name"",
                        ""operator"": ""=="",
                        ""values"": [""alice"", ""bob"", ""charlie""]
                    },
                    {
                        ""field"": ""age"",
                        ""operator"": "">="",
                        ""values"": [20]
                    },
                    {
                        ""field"": ""age"",
                        ""operator"": ""<="",
                        ""values"": [35]
                    }
                ]
            }";

            var query = JsonSerializer.Deserialize<CalaisQuery>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var result = await _processor.ApplyAsync(
                context.Users
                    .Include(u => u.Posts)
                    .Include(u => u.Comments),
                query!);

            // Should get alice (25), bob (30), charlie (35) - all within age range
            result.TotalCount.Should().Be(3);
            result.Items.Should().HaveCount(3);

            // Verify sorting - name asc
            result.Items[0].Name.Should().Be("alice");
            result.Items[1].Name.Should().Be("bob");
            result.Items[2].Name.Should().Be("charlie");

            // Verify all within age range
            result.Items.All(u => u.Age >= 20 && u.Age <= 35).Should().BeTrue();
        }

        [Fact]
        public async Task ExcludedIds_WithOrGroup_WorksCorrectly()
        {
            await using var context = _fixture.CreateContext();

            var json = @"{
                ""page"": 1,
                ""pageSize"": 10,
                ""filters"": [
                    {
                        ""field"": ""id"",
                        ""operator"": ""!="",
                        ""values"": [""cf9175ce-ded3-4447-9725-e6f018430de9"", ""a435a45f-a07f-4219-97e5-e2f6d534c3b0""]
                    },
                    {
                        ""or"": [
                            {
                                ""field"": ""name"",
                                ""operator"": ""@="",
                                ""values"": [""ar""]
                            },
                            {
                                ""field"": ""age"",
                                ""operator"": "">="",
                                ""values"": [35]
                            }
                        ]
                    }
                ]
            }";

            var query = JsonSerializer.Deserialize<CalaisQuery>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var result = await _processor.ApplyAsync(context.Users, query!);

            // Excludes alice and bob (by id)
            // Then matches: charlie (name contains 'ar'), eve (age >= 35)
            result.Items.Should().HaveCount(2);
            result.Items.Select(u => u.Name).Should().BeEquivalentTo(new[] { "charlie", "eve" });
        }

        [Fact]
        public async Task NestedCommentsFilter_WithComplexConditions()
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
                context.Users.Include(u => u.Comments),
                query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // Users who have at least one comment containing "good"
            result.Should().HaveCountGreaterThan(0);
            result.All(u => u.Comments.Any(c => c.Text.Contains("good"))).Should().BeTrue();
        }

        [Fact]
        public async Task Pagination_SeperateFromFiltering_AllowsFlexibleUsage()
        {
            await using var context = _fixture.CreateContext();

            // First get filtered data without pagination
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
                ],
                Sorts = [new SortDescriptor { Field = "name", Direction = "asc" }]
            };

            var filteredQuery = _processor.ApplyWithoutPagination(context.Users, filterQuery);

            // Get total count
            var totalCount = await filteredQuery.CountAsync();
            totalCount.Should().Be(4); // alice(25), bob(30), charlie(35), eve(40)

            // Now paginate page 1
            var page1 = await _processor.ApplyPagination(filteredQuery, 1, 2).ToListAsync(TestContext.Current.CancellationToken);
            page1.Should().HaveCount(2);
            page1[0].Name.Should().Be("alice");
            page1[1].Name.Should().Be("bob");

            // Now paginate page 2
            var page2 = await _processor.ApplyPagination(filteredQuery, 2, 2).ToListAsync(TestContext.Current.CancellationToken);
            page2.Should().HaveCount(2);
            page2[0].Name.Should().Be("charlie");
            page2[1].Name.Should().Be("eve");
        }

        [Fact]
        public async Task JsonbColumn_FilterAndSort_WorksWithJsonDocument()
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
		                Values = ["tagged"]
	                }
                ],
                Sorts = [new SortDescriptor { Field = "name", Direction = "asc" }]
            };

            var result = await _processor.Apply(
                context.Users.Where(u => u.JsonbColumn != null),
                query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(2);
            result.Select(u => u.Name).Should().BeEquivalentTo(new[] { "alice", "charlie" });
        }

        [Fact]
        public async Task CustomSort_IsBanned_SortsCorrectly()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Sorts =
                [
	                new SortDescriptor { Field = "is_banned", Direction = "desc" },
	                new SortDescriptor { Field = "name", Direction = "asc" }
                ]
            };

            var result = await _processor.ApplySorting(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // Bob is banned (LockoutEnd != null), should be first
            result.First().Name.Should().Be("bob");
        }

        [Fact]
        public async Task PasswordHash_IsIgnored_NotFilterable()
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

            // Should return all users because passwordHash filter is ignored
            var result = await _processor.ApplyFilters(context.Users, query)
                .ToListAsync(TestContext.Current.CancellationToken);

            result.Should().HaveCount(5);
        }

        [Fact]
        public async Task LengthOperator_FiltersByCollectionCount()
        {
            await using var context = _fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "posts",
		                Operator = "len>=",
		                Values = [1]
	                }
                ]
            };

            var result = await _processor.ApplyFilters(
                context.Users.Include(u => u.Posts),
                query)
                .ToListAsync(TestContext.Current.CancellationToken);

            // alice, bob, charlie each have 1 post
            result.Should().HaveCount(3);
            result.All(u => u.Posts.Count >= 1).Should().BeTrue();
        }
    }
}
