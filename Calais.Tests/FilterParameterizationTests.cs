using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Calais.Models;
using Calais.Tests.TestEntities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Calais.Tests;

public class FilterParameterizationTests
{
	[Theory]
	[InlineData("name", "==")]
	[InlineData("comments.text", "==")]
	[InlineData("jsonbColumn.color", "==")]
	[InlineData("tags", "@=")]
	[InlineData("posts.contentVector", "==")]
	[InlineData("passwordHash", "~=")]
	[InlineData("email", "_=")]
	public void ChangingSearchTextReusesCompilationAndBindsTheNewValue(string field, string op)
	{
		int compilations = 0;
		using var db = CreateContext(() => compilations++);
		var processor = new CalaisBuilder().Build();
		string? firstSql = null;
		foreach (var term in new[] { "first", "second" })
		{
			var filter = new FilterDescriptor
			{
				Field = field,
				Operator = op,
				Values = [term],
				IsJson = field.StartsWith("jsonbColumn"),
				IsVector = field.EndsWith("Vector"),
			};
			using var command = processor
				.ApplyFilters(db.Users.TagWith(field), new() { Filters = [filter] })
				.CreateDbCommand();
			firstSql ??= command.CommandText;
			command.CommandText.Should().Be(firstSql);
			command.Parameters.Count.Should().Be(1);
			command.Parameters[0].Value.Should().Be(op == "_=" ? term + "%" : term);
		}
		compilations.Should().Be(1);
	}

	[Theory]
	[InlineData("age", ">=", "5", "12")]
	[InlineData("comments", "len>", "5", "12")]
	[InlineData("status", ">", "1", "2")]
	public void NumericJsonValuesKeepTheirParameterType(
		string field,
		string op,
		string first,
		string second
	)
	{
		int compilations = 0;
		using var db = CreateContext(() => compilations++);
		var processor = new CalaisBuilder().Build();
		foreach (var number in new[] { first, second })
		{
			var query = new CalaisQuery
			{
				Filters =
				[
					new()
					{
						Field = field,
						Operator = op,
						Values = [JsonSerializer.Deserialize<JsonElement>(number)],
					},
				],
			};
			using var command = processor
				.ApplyFilters(db.Users.TagWith(field), query)
				.CreateDbCommand();
			command.Parameters.Count.Should().Be(1);
			command.Parameters[0].Value.Should().Be(int.Parse(number));
		}
		compilations.Should().Be(1);
	}

	[Theory]
	[InlineData("==")]
	[InlineData("!=")]
	public void DifferentListSizesUseOneArrayParameter(string op)
	{
		int compilations = 0;
		using var db = CreateContext(() => compilations++);
		var processor = new CalaisBuilder().Build();
		string? firstSql = null;
		foreach (var ages in new[] { new[] { 10, 11 }, new[] { 11, 12, 13, 13 } })
		{
			var filter = new FilterDescriptor
			{
				Field = "age",
				Operator = op,
				Values = ages.Select(i => (object)i.ToString()).ToList(),
			};
			using var command = processor
				.ApplyFilters(db.Users.TagWith(op), new() { Filters = [filter] })
				.CreateDbCommand();
			firstSql ??= command.CommandText;
			command.CommandText.Should().Be(firstSql);
			command.Parameters.Count.Should().Be(1);
			command.Parameters[0].Value.Should().BeOfType<int[]>().Which.Should().Equal(ages);
		}
		compilations.Should().Be(1);
	}

	[Fact]
	public void DeferredQueriesKeepIndependentNullableDateBounds()
	{
		int compilations = 0;
		using var db = CreateContext(() => compilations++);
		var processor = new CalaisBuilder().Build();
		var source = db.Users.TagWith(nameof(DeferredQueriesKeepIndependentNullableDateBounds));
		IQueryable<User> Between(string start, string end) =>
			processor.ApplyFilters(
				source,
				new()
				{
					Filters =
					[
						new()
						{
							Field = "birthDate",
							Operator = ">=",
							Values = [start],
						},
						new()
						{
							Field = "birthDate",
							Operator = "<",
							Values = [end],
						},
					],
				}
			);

		var january = Between("2000-01-01", "2000-02-01");
		var february = Between("2000-02-01", "2000-03-01");
		using var second = february.CreateDbCommand();
		using var first = january.CreateDbCommand();
		first.CommandText.Should().Be(second.CommandText);
		first.Parameters.Count.Should().Be(2);
		second.Parameters.Count.Should().Be(2);
		first.Parameters[0].Value.Should().Be(new DateOnly(2000, 1, 1));
		first.Parameters[1].Value.Should().Be(new DateOnly(2000, 2, 1));
		second.Parameters[0].Value.Should().Be(new DateOnly(2000, 2, 1));
		second.Parameters[1].Value.Should().Be(new DateOnly(2000, 3, 1));
		compilations.Should().Be(1);
	}

	[Theory]
	[InlineData("==", false, "ab")]
	[InlineData("!=", false, "nc")]
	[InlineData("==", true, "nab")]
	[InlineData("!=", true, "c")]
	public void MembershipPreservesNullsAndBindsASnapshot(
		string op,
		bool includeNull,
		string expected
	)
	{
		var users = new[]
		{
			new User { Name = "n" },
			new User { Name = "a", Email = "a" },
			new User { Name = "b", Email = "b" },
			new User { Name = "c", Email = "c" },
		}.AsQueryable();
		var values = new List<object> { "a", "b" };
		if (includeNull)
			values.Add(null!);
		var query = new CalaisQuery
		{
			Filters =
			[
				new()
				{
					Field = "email",
					Operator = op,
					Values = values,
				},
			],
		};
		var filtered = new CalaisBuilder().Build().ApplyFilters(users, query);
		values.Clear();
		string.Concat(filtered.Select(u => u.Name)).Should().Be(expected);
	}

	private static TestDbContext CreateContext(Action onCompilation) =>
		new(
			new DbContextOptionsBuilder<TestDbContext>()
				.UseNpgsql()
				.LogTo(_ => onCompilation(), [CoreEventId.QueryCompilationStarting], LogLevel.Debug)
				.Options
		);
}
