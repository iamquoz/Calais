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
    public class FullTextSearchTests(PostgreSqlFixture fixture)
    {
        private readonly CalaisProcessor _processor = new CalaisBuilder()
            .WithDefaultVectorLanguage("english")
            .Build();

        [Fact]
        public async Task Filter_VectorField_MatchesSingleTerm()
        {
            await using var context = fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "ContentVector",
                        IsVector = true,
                        Values = ["test"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Posts, query)
                .ToListAsync();

            // Should find posts with "test" in their content
            result.Should().HaveCountGreaterThan(0);
            result.All(p => p.Content.Contains("test", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        }

        [Fact]
        public async Task Filter_VectorField_MultipleValues_CombinedAsOr()
        {
            await using var context = fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "ContentVector",
                        IsVector = true,
                        Values = ["test", "different"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Posts, query)
                .ToListAsync();

            // Should find posts containing either "test" or "different"
            result.Should().HaveCountGreaterThan(0);
            result.All(p => 
                p.Content.Contains("test", StringComparison.OrdinalIgnoreCase) || 
                p.Content.Contains("different", StringComparison.OrdinalIgnoreCase)
            ).Should().BeTrue();
        }

        [Fact]
        public async Task Filter_VectorField_NoMatch_ReturnsEmpty()
        {
            await using var context = fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "ContentVector",
                        IsVector = true,
                        Values = ["nonexistentterm12345"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Posts, query)
                .ToListAsync();

            result.Should().BeEmpty();
        }

        [Fact]
        public async Task Filter_VectorField_WithOtherFilters_CombinesCorrectly()
        {
            await using var context = fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "Title",
                        Operator = "@=",
                        Values = ["abc"]
                    },
                    new FilterDescriptor
                    {
                        Field = "ContentVector",
                        IsVector = true,
                        Values = ["test"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Posts, query)
                .ToListAsync();

            // Should find posts with title containing "abc" AND content matching "test"
            result.Should().HaveCountGreaterThan(0);
            result.All(p => 
                p.Title.Contains("abc", StringComparison.OrdinalIgnoreCase) &&
                p.Content.Contains("test", StringComparison.OrdinalIgnoreCase)
            ).Should().BeTrue();
        }

        [Fact]
        public async Task Filter_VectorField_InOrGroup_WorksCorrectly()
        {
            await using var context = fixture.CreateContext();

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
                                Field = "Title",
                                Operator = "==",
                                Values = ["another post"]
                            },
                            new FilterDescriptor
                            {
                                Field = "ContentVector",
                                IsVector = true,
                                Values = ["example"]
                            }
                        ]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(context.Posts, query)
                .ToListAsync();

            // Should find posts with title "another post" OR content matching "example"
            result.Should().HaveCountGreaterThan(0);
        }

        [Fact]
        public async Task Filter_VectorField_EmptyValues_ReturnsAll()
        {
            await using var context = fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "ContentVector",
                        IsVector = true,
                        Values = []
                    }
                ]
            };

            var allPosts = await context.Posts.ToListAsync();
            var result = await _processor.ApplyFilters(context.Posts, query)
                .ToListAsync();

            // Empty values should not filter anything
            result.Should().HaveCount(allPosts.Count);
        }

        [Fact]
        public async Task Filter_VectorField_NullValues_ReturnsAll()
        {
            await using var context = fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "ContentVector",
                        IsVector = true,
                        Values = null
                    }
                ]
            };

            var allPosts = await context.Posts.ToListAsync();
            var result = await _processor.ApplyFilters(context.Posts, query)
                .ToListAsync();

            // Null values should not filter anything
            result.Should().HaveCount(allPosts.Count);
        }

        [Fact]
        public async Task Filter_VectorField_WithPagination_WorksCorrectly()
        {
            await using var context = fixture.CreateContext();

            var query = new CalaisQuery
            {
                Page = 1,
                PageSize = 2,
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "ContentVector",
                        IsVector = true,
                        Values = ["test"]
                    }
                ]
            };

            var result = await _processor.ApplyAsync(context.Posts, query);

            result.Items.Should().HaveCountLessThanOrEqualTo(2);
            result.Items.All(p => p.Content.Contains("test", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        }

        [Fact]
        public async Task Filter_NestedVectorField_ThroughNavigation()
        {
            await using var context = fixture.CreateContext();

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "Posts.ContentVector",
                        IsVector = true,
                        Values = ["test"]
                    }
                ]
            };

            var result = await _processor.ApplyFilters(
                context.Users.Include(u => u.Posts), query)
                .ToListAsync();

            // Should find users who have at least one post matching "test"
            result.Should().HaveCountGreaterThan(0);
            result.All(u => u.Posts.Any(p => 
                p.Content.Contains("test", StringComparison.OrdinalIgnoreCase))
            ).Should().BeTrue();
        }
    }
}
