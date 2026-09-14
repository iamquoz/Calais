using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Calais.Models;
using Calais.Tests.TestEntities;
using FluentAssertions;
using Xunit;

namespace Calais.Tests;

public class MultipleValueFilterTests
{
	[Fact]
	public void NegatedOperators_MultipleValues_MustNotMatchAny()
	{
		var processor = new CalaisBuilder().Build();
		var users = new List<User>
		{
			new()
			{
				Name = "red",
				JsonbColumn = JsonDocument.Parse("""{"color":"red"}"""),
				Comments = [new Comment { Text = "red" }],
			},
			new()
			{
				Name = "blue",
				JsonbColumn = JsonDocument.Parse("""{"color":"blue"}"""),
				Comments = [new Comment { Text = "blue" }],
			},
			new()
			{
				Name = "green",
				JsonbColumn = JsonDocument.Parse("""{"color":"green"}"""),
				Comments = [new Comment { Text = "green" }],
			},
		};

		foreach (
			var filter in new[]
			{
				new FilterDescriptor
				{
					Field = "name",
					Operator = "!=",
					Values = ["red", "blue"],
				},
				new FilterDescriptor
				{
					Field = "name",
					Operator = "!@=",
					Values = ["red", "blue"],
				},
				new FilterDescriptor
				{
					Field = "comments.text",
					Operator = "!=",
					Values = ["red", "blue"],
				},
				new FilterDescriptor
				{
					Field = "jsonbColumn.color",
					IsJson = true,
					Operator = "!=",
					Values = ["red", "blue"],
				},
				new FilterDescriptor
				{
					Field = "name",
					Operator = "len!=",
					Values = [3, 4],
				},
			}
		)
		{
			var result = processor
				.ApplyFilters(users.AsQueryable(), new CalaisQuery { Filters = [filter] })
				.Select(user => user.Name);

			result.Should().Equal("green");
		}
	}
}
