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

namespace Calais.Tests;

public class CollectionLengthTranslationTests
{
	[Theory]
	[InlineData("len>", "0", "EXISTS", 1, 2)]
	[InlineData("len>=", "1", "EXISTS", 1, 2)]
	[InlineData("len>", "1", "count(*)", 2)]
	[InlineData("len==", "2", "count(*)", 2)]
	public void CollectionLength_PreservesMatchesAndUsesExistenceForPositiveBounds(
		string op,
		string threshold,
		string sqlFragment,
		params int[] expectedAges
	)
	{
		using var db = new TestDbContext(
			new DbContextOptionsBuilder<TestDbContext>().UseNpgsql().Options
		);
		var processor = new CalaisBuilder().Build();
		var query = new CalaisQuery
		{
			Filters =
			[
				new()
				{
					Field = "comments",
					Operator = op,
					Values = [threshold],
				},
			],
		};

		processor.ApplyFilters(db.Users, query).ToQueryString().Should().Contain(sqlFragment);
		var users = new User[]
		{
			new() { Age = 0 },
			new() { Age = 1, Comments = [new()] },
			new() { Age = 2, Comments = [new(), new()] },
		}.AsQueryable();
		processor.ApplyFilters(users, query).Select(u => u.Age).Should().Equal(expectedAges);
	}
}

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
					Values = [1],
				},
			],
		};

		var result = await _processor
			.ApplyFilters(context.Users.Include(u => u.Comments), query)
			.ToListAsync(TestContext.Current.CancellationToken);

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
					Values = [1],
				},
			],
		};

		var result = await _processor
			.ApplyFilters(context.Users.Include(u => u.Posts), query)
			.ToListAsync(TestContext.Current.CancellationToken);

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
					Values = [0],
				},
			],
		};

		var result = await _processor
			.ApplyFilters(context.Users.Include(u => u.Comments), query)
			.ToListAsync(TestContext.Current.CancellationToken);

		result.All(u => u.Comments.Count > 0).Should().BeTrue();
	}
}
