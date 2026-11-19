using System.Linq;
using System.Text.RegularExpressions;
using Calais.Models;
using Calais.Tests.TestEntities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Xunit;

namespace Calais.Tests;

public class CollectionOrFilterTests
{
	[Theory]
	[InlineData("posts.content", 1, "content")]
	[InlineData("posts.comments.text", 2, "post comment")]
	[InlineData("comments.text", 2, "user comment")]
	public void CombinesOnlyAlternativesOnTheSameCollection(
		string otherPath,
		int expectedExists,
		string otherMatch
	)
	{
		using var db = new TestDbContext(
			new DbContextOptionsBuilder<TestDbContext>().UseNpgsql().Options
		);
		IQueryable<User> root = db.Users;
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
							Field = "posts.title",
							Operator = "==",
							Values = ["match"],
						},
						new FilterDescriptor
						{
							Field = otherPath,
							Operator = "==",
							Values = ["match"],
						},
					],
				},
			],
		};
		var filtered = new CalaisBuilder().Build().ApplyFilters(root, query);
		Regex.Matches(filtered.ToQueryString(), @"\bEXISTS\s*\(").Count.Should().Be(expectedExists);

		var comment = new Comment { Text = "match" };
		var users = new User[]
		{
			new() { Name = "title", Posts = [new() { Title = "match" }] },
			new() { Name = "content", Posts = [new() { Content = "match" }] },
			new() { Name = "post comment", Posts = [new() { Comments = [comment] }] },
			new() { Name = "user comment", Comments = [comment] },
			new()
			{
				Name = "both",
				Posts = [new() { Title = "match" }, new() { Comments = [comment] }],
				Comments = [comment],
			},
			new() { Name = "empty" },
		}.AsQueryable();
		var predicateQuery = ReplacingExpressionVisitor.Replace(
			root.Expression,
			users.Expression,
			filtered.Expression
		);
		var matches = users.Provider.CreateQuery<User>(predicateQuery).Select(u => u.Name);
		matches.Should().Equal("title", otherMatch, "both");
	}
}
