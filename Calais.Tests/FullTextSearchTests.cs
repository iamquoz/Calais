using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Calais.Models;
using Calais.Tests.Fixtures;
using Calais.Tests.TestEntities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Calais.Tests;

public class FullTextSearchTranslationTests
{
	[Theory]
	[InlineData(false, 0)]
	[InlineData(false, 1)]
	[InlineData(false, 3)]
	[InlineData(true, 0)]
	[InlineData(true, 1)]
	[InlineData(true, 3)]
	public void MatchesEachVectorOnceAndBindsEachSearchExpression(bool nested, int count)
	{
		using var db = new TestDbContext(
			new DbContextOptionsBuilder<TestDbContext>().UseNpgsql().Options
		);
		var processor = new CalaisBuilder().Build();
		var terms = new[] { "cat:* & !dog", "red <-> fox", "rat | mouse" }.Take(count).ToArray();
		var query = new CalaisQuery
		{
			Filters =
			[
				new()
				{
					Field = nested ? "posts.contentVector" : "contentVector",
					IsVector = true,
					Values = [null!, .. terms, ""],
				},
			],
		};
		using var command = nested
			? processor.ApplyFilters(db.Users, query).Select(u => u.Id).CreateDbCommand()
			: processor.ApplyFilters(db.Posts, query).Select(p => p.Id).CreateDbCommand();
		var sql = command.CommandText;
		Regex.Count(sql, "@@").Should().Be(count == 0 ? 0 : 1);
		Regex.Count(sql, @"\|\|").Should().Be(Math.Max(0, count - 1));
		Regex.Count(sql, "EXISTS").Should().Be(nested && count > 0 ? 1 : 0);
		command.Parameters.Cast<DbParameter>().Select(p => p.Value).Should().Equal(terms);
	}
}

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
					Values = ["test"],
				},
			],
		};

		var result = await _processor
			.ApplyFilters(context.Posts, query)
			.ToListAsync(TestContext.Current.CancellationToken);

		// Should find posts with "test" in their content
		result.Should().HaveCountGreaterThan(0);
		result
			.All(p => p.Content.Contains("test", StringComparison.OrdinalIgnoreCase))
			.Should()
			.BeTrue();
	}

	[Theory]
	[InlineData("test", "different")]
	[InlineData("test:* & !different", "another <-> post")]
	[InlineData("the", "different")]
	public async Task Filter_VectorField_MultipleValues_CombinedAsOr(string first, string second)
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
					Values = [first, second],
				},
			],
		};

		var result = await _processor
			.ApplyFilters(context.Posts, query)
			.Select(p => p.Id)
			.ToListAsync(TestContext.Current.CancellationToken);
		var expected = await context
			.Posts.Where(p =>
				p.ContentVector.Matches(EF.Functions.ToTsQuery("english", first))
				|| p.ContentVector.Matches(EF.Functions.ToTsQuery("english", second))
			)
			.Select(p => p.Id)
			.ToListAsync(TestContext.Current.CancellationToken);
		result.Should().BeEquivalentTo(expected);
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
					Values = ["nonexistentterm12345"],
				},
			],
		};

		var result = await _processor
			.ApplyFilters(context.Posts, query)
			.ToListAsync(TestContext.Current.CancellationToken);

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
					Values = ["abc"],
				},
				new FilterDescriptor
				{
					Field = "ContentVector",
					IsVector = true,
					Values = ["test"],
				},
			],
		};

		var result = await _processor
			.ApplyFilters(context.Posts, query)
			.ToListAsync(TestContext.Current.CancellationToken);

		// Should find posts with title containing "abc" AND content matching "test"
		result.Should().HaveCountGreaterThan(0);
		result
			.All(p =>
				p.Title.Contains("abc", StringComparison.OrdinalIgnoreCase)
				&& p.Content.Contains("test", StringComparison.OrdinalIgnoreCase)
			)
			.Should()
			.BeTrue();
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
							Values = ["another post"],
						},
						new FilterDescriptor
						{
							Field = "ContentVector",
							IsVector = true,
							Values = ["example"],
						},
					],
				},
			],
		};

		var result = await _processor
			.ApplyFilters(context.Posts, query)
			.ToListAsync(TestContext.Current.CancellationToken);

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
					Values = [],
				},
			],
		};

		var allPosts = await context.Posts.ToListAsync(TestContext.Current.CancellationToken);
		var result = await _processor
			.ApplyFilters(context.Posts, query)
			.ToListAsync(TestContext.Current.CancellationToken);

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
					Values = null,
				},
			],
		};

		var allPosts = await context.Posts.ToListAsync(TestContext.Current.CancellationToken);
		var result = await _processor
			.ApplyFilters(context.Posts, query)
			.ToListAsync(TestContext.Current.CancellationToken);

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
					Values = ["test"],
				},
			],
		};

		var result = await _processor.ApplyAsync(
			context.Posts,
			query,
			TestContext.Current.CancellationToken
		);

		result.Items.Should().HaveCountLessThanOrEqualTo(2);
		result
			.Items.All(p => p.Content.Contains("test", StringComparison.OrdinalIgnoreCase))
			.Should()
			.BeTrue();
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
					Values = ["test"],
				},
			],
		};

		var result = await _processor
			.ApplyFilters(context.Users.Include(u => u.Posts), query)
			.ToListAsync(TestContext.Current.CancellationToken);

		// Should find users who have at least one post matching "test"
		result.Should().HaveCountGreaterThan(0);
		result
			.All(u =>
				u.Posts.Any(p => p.Content.Contains("test", StringComparison.OrdinalIgnoreCase))
			)
			.Should()
			.BeTrue();
	}
}
