using System;
using System.Linq;
using System.Threading.Tasks;
using Calais.Models;
using Calais.Tests.Fixtures;
using Calais.Tests.TestEntities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Calais.Tests;

public class PaginationBoundaryTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(-2)]
	public void InvalidConfiguredPageSizesAreRejected(int size)
	{
		var builder = new CalaisBuilder();
		var setDefault = () => builder.WithDefaultPageSize(size);
		var setMaximum = () => builder.WithMaxPageSize(size);
		setDefault.Should().Throw<ArgumentOutOfRangeException>();
		setMaximum.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void UncappedQueryWithoutSizeKeepsAllMatchesAndSorting()
	{
		var builder = new CalaisBuilder().WithMaxPageSize(-1);
		var setDefault = () => builder.WithDefaultPageSize(-1);
		setDefault.Should().Throw<ArgumentOutOfRangeException>();

		var processor = builder.Build();
		var source = new[]
		{
			new User { Age = 10 },
			new User { Age = 20 },
			new User { Age = 30 },
			new User { Age = 40 },
		}.AsQueryable().Take(3);
		var query = new CalaisQuery
		{
			Page = int.MaxValue,
			Filters =
			[
				new()
				{
					Field = "age",
					Operator = ">",
					Values = [10],
				},
			],
			Sorts = [new() { Field = "age", Direction = "desc" }],
		};
		processor.ApplyPagination(source, query).Should().BeSameAs(source);
		processor.Apply(source, query).Select(u => u.Age).Should().Equal(30, 20);
	}

	[Theory]
	[InlineData(150, 150)]
	[InlineData(1, 1)]
	[InlineData(0, 10)]
	[InlineData(-1, 10)]
	public void UncappedExplicitSizePaginatesBothOverloads(int requestedSize, int effectiveSize)
	{
		var processor = new CalaisBuilder().WithMaxPageSize(-1).Build();
		var rows = Enumerable.Range(1, 400).Select(n => n.ToString()).ToArray();
		var source = rows.AsQueryable();
		var expected = rows[effectiveSize..(2 * effectiveSize)];
		var query = new CalaisQuery { Page = 2, PageSize = requestedSize };
		processor.ApplyPagination(source, query).Should().Equal(expected);
		processor.ApplyPagination(source, 2, requestedSize).Should().Equal(expected);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	[InlineData(100)]
	public void PageSizeCannotExceedConfiguredMaximum(int requestedSize)
	{
		var processor = new CalaisBuilder().WithMaxPageSize(3).Build();
		var rows = Enumerable.Range(1, 12).Select(n => n.ToString()).AsQueryable();
		var query = new CalaisQuery { Page = 2, PageSize = requestedSize };
		processor.ApplyPagination(rows, query).Should().Equal("4", "5", "6");
		processor.ApplyPagination(rows, 2, requestedSize).Should().Equal("4", "5", "6");
	}

	[Fact]
	public void OffsetOutsideQueryableSkipRangeIsRejected()
	{
		var rows = new[] { "first" }.AsQueryable();
		var processor = new CalaisBuilder().Build();
		var apply = () => processor.ApplyPagination(rows, 1_073_741_825, 4);
		apply.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("page");
	}

	[Theory]
	[InlineData(5, int.MaxValue, 1)]
	[InlineData(int.MaxValue, 2, 1_073_741_824)]
	[InlineData(0, int.MaxValue, 0)]
	public void TotalPagesRoundsUpWithoutOverflow(int count, int size, int pages)
	{
		var result = new PagedResult<string>
		{
			Page = 1,
			TotalCount = count,
			PageSize = size,
		};
		result.TotalPages.Should().Be(pages);
		result.HasNextPage.Should().Be(pages > 1);
	}
}

[Collection("PostgreSql")]
public class PaginationTests
{
	private readonly PostgreSqlFixture _fixture;
	private readonly CalaisProcessor _processor;

	public PaginationTests(PostgreSqlFixture fixture)
	{
		_fixture = fixture;
		_processor = new CalaisBuilder().WithDefaultPageSize(10).WithMaxPageSize(100).Build();
	}

	[Fact]
	public async Task Pagination_FirstPage_ReturnsCorrectItems()
	{
		await using var context = _fixture.CreateContext();

		var query = new CalaisQuery
		{
			Page = 1,
			PageSize = 2,
			Sorts = [new SortDescriptor { Field = "name", Direction = "asc" }],
		};

		var result = await _processor
			.Apply(context.Users, query)
			.ToListAsync(TestContext.Current.CancellationToken);

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
			Sorts = [new SortDescriptor { Field = "name", Direction = "asc" }],
		};

		var result = await _processor
			.Apply(context.Users, query)
			.ToListAsync(TestContext.Current.CancellationToken);

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
					Values = [25],
				},
			],
		};

		// Apply filters without pagination
		var filteredQuery = _processor.ApplyWithoutPagination(context.Users, filterQuery);
		var totalCount = await filteredQuery.CountAsync(TestContext.Current.CancellationToken);

		// Apply pagination separately
		var pagedResult = await _processor
			.ApplyPagination(filteredQuery, 1, 2)
			.ToListAsync(TestContext.Current.CancellationToken);

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
			Sorts = [new SortDescriptor { Field = "name", Direction = "asc" }],
		};

		var result = await _processor.ApplyAsync(
			context.Users,
			query,
			TestContext.Current.CancellationToken
		);

		result.Page.Should().Be(2);
		result.PageSize.Should().Be(2);
		result.TotalCount.Should().Be(5);
		result.TotalPages.Should().Be(3);
		result.Items.Should().HaveCount(2);
		result.HasPreviousPage.Should().BeTrue();
		result.HasNextPage.Should().BeTrue();
	}

	[Fact]
	public async Task ApplyAsync_SkipsDataQueryWhenCountRulesOutPage()
	{
		var commands = 0;
		await using var context = new TestDbContext(
			new DbContextOptionsBuilder<TestDbContext>()
				.UseNpgsql(_fixture.ConnectionString)
				.LogTo(_ => commands++, [RelationalEventId.CommandExecuted], LogLevel.Information)
				.Options
		);
		var result = await _processor.ApplyAsync(
			context.Users.OrderBy(u => u.Id),
			new() { Page = 2, PageSize = 5 },
			TestContext.Current.CancellationToken
		);

		result.TotalCount.Should().Be(5);
		result.Items.Should().BeEmpty();
		commands.Should().Be(1);
	}

	[Theory]
	[InlineData(30, 2)]
	[InlineData(100, 0)]
	public async Task ApplyAsync_UncappedQueryWithoutSizeReportsOneResultSet(
		int minimumAge,
		int expectedCount
	)
	{
		await using var context = _fixture.CreateContext();
		var processor = new CalaisBuilder().WithMaxPageSize(-1).Build();
		var query = new CalaisQuery
		{
			Page = 3,
			Filters =
			[
				new()
				{
					Field = "age",
					Operator = ">",
					Values = [minimumAge],
				},
			],
			Sorts = [new() { Field = "age", Direction = "desc" }],
		};
		var result = await processor.ApplyAsync(
			context.Users,
			query,
			TestContext.Current.CancellationToken
		);
		result.Items.Should().HaveCount(expectedCount).And.BeInDescendingOrder(u => u.Age);
		result.Page.Should().Be(1);
		result.PageSize.Should().Be(expectedCount);
		result.TotalCount.Should().Be(expectedCount);
		result.TotalPages.Should().Be(expectedCount == 0 ? 0 : 1);
		result.HasNextPage.Should().BeFalse();
		result.HasPreviousPage.Should().BeFalse();
	}

	[Fact]
	public async Task ApplyAsync_UncappedExplicitSizeReportsRequestedPage()
	{
		await using var context = _fixture.CreateContext();
		var processor = new CalaisBuilder().WithMaxPageSize(-1).Build();
		var result = await processor.ApplyAsync(
			context.Users.OrderBy(u => u.Name),
			new() { Page = 2, PageSize = 2 },
			TestContext.Current.CancellationToken
		);
		result.Items.Select(u => u.Name).Should().Equal("charlie", "diana");
		result.Page.Should().Be(2);
		result.PageSize.Should().Be(2);
		result.TotalCount.Should().Be(5);
		result.TotalPages.Should().Be(3);
	}

	[Fact]
	public async Task Pagination_RespectsMaxPageSize()
	{
		var processor = new CalaisBuilder().WithMaxPageSize(3).Build();

		await using var context = _fixture.CreateContext();

		var query = new CalaisQuery
		{
			Page = 1,
			PageSize = 1000, // Exceeds max
		};

		var result = await processor
			.Apply(context.Users, query)
			.ToListAsync(TestContext.Current.CancellationToken);

		result.Should().HaveCount(3); // Limited to MaxPageSize
	}
}
