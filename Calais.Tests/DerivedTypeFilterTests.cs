using System;
using System.Collections.Generic;
using System.Linq;
using Calais.Exceptions;
using Calais.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Xunit;

namespace Calais.Tests;

public class DerivedTypeFilterTests
{
	private readonly CalaisProcessor _processor = new CalaisBuilder().Build();

	[Fact]
	public void Filter_NestedDerivedCollectionProperty_FiltersThroughDerivedType()
	{
		var targetLabelId = Guid.NewGuid();
		var otherLabelId = Guid.NewGuid();
		var documents = new List<TestDocument>
		{
			new()
			{
				Id = Guid.NewGuid(),
				Sections =
				[
					new TestTaggedSection
					{
						Id = Guid.NewGuid(),
						Labels = [new TestLabel { Id = targetLabelId }],
					},
				],
			},
			new()
			{
				Id = Guid.NewGuid(),
				Sections =
				[
					new TestTaggedSection
					{
						Id = Guid.NewGuid(),
						Labels = [new TestLabel { Id = otherLabelId }],
					},
				],
			},
			new()
			{
				Id = Guid.NewGuid(),
				Sections =
				[
					new TestPersonalSection { Id = Guid.NewGuid(), OwnerGroupId = targetLabelId },
				],
			},
		};

		var query = new CalaisQuery
		{
			Filters =
			[
				new FilterDescriptor
				{
					Field = "sections.labels.id",
					Operator = "==",
					Values = [targetLabelId],
				},
			],
		};

		var result = _processor.ApplyFilters(documents.AsQueryable(), query).ToList();

		result.Should().ContainSingle();
		result
			.Single()
			.Sections.OfType<TestTaggedSection>()
			.Single()
			.Labels.Single()
			.Id.Should()
			.Be(targetLabelId);
	}

	[Fact]
	public void Filter_NestedDerivedProperty_FiltersAcrossMatchingDerivedTypes()
	{
		var targetOwnerGroupId = Guid.NewGuid();
		var otherOwnerGroupId = Guid.NewGuid();
		var documents = new List<TestDocument>
		{
			new()
			{
				Id = Guid.NewGuid(),
				Sections =
				[
					new TestPersonalSection
					{
						Id = Guid.NewGuid(),
						OwnerGroupId = targetOwnerGroupId,
					},
				],
			},
			new()
			{
				Id = Guid.NewGuid(),
				Sections =
				[
					new TestSharedSection
					{
						Id = Guid.NewGuid(),
						OwnerGroupId = targetOwnerGroupId,
					},
				],
			},
			new()
			{
				Id = Guid.NewGuid(),
				Sections =
				[
					new TestPersonalSection
					{
						Id = Guid.NewGuid(),
						OwnerGroupId = otherOwnerGroupId,
					},
				],
			},
		};

		var query = new CalaisQuery
		{
			Filters =
			[
				new FilterDescriptor
				{
					Field = "sections.ownerGroupId",
					Operator = "==",
					Values = [targetOwnerGroupId],
				},
			],
		};

		var result = _processor.ApplyFilters(documents.AsQueryable(), query).ToList();

		result.Should().HaveCount(2);
		result
			.SelectMany(c => c.Sections)
			.Should()
			.AllSatisfy(section =>
			{
				section.Should().BeAssignableTo<TestOwnedSection>();
				((TestOwnedSection)section).OwnerGroupId.Should().Be(targetOwnerGroupId);
			});
	}

	[Fact]
	public void InheritedPropertyUsesOneComparisonAcrossMappedSubtypes()
	{
		using var db = new DocumentContext();
		var query = new CalaisQuery
		{
			Filters = [new() { Field = "sections.OWNERGROUPID", Values = [Guid.NewGuid()] }],
		};
		using var command = _processor
			.ApplyFilters(db.Set<TestDocument>(), query)
			.CreateDbCommand();
		command.Parameters.Count.Should().Be(1);
		command
			.CommandText.Should()
			.Contain(nameof(TestPersonalSection))
			.And.Contain(nameof(TestSharedSection));
	}

	[Fact]
	public void UnmappedIntermediateUsesMappedSiblingsOnAComposedQuery()
	{
		using var db = new FlattenedDocumentContext();
		db.Model.FindEntityType(typeof(TestOwnedSection)).Should().BeNull();
		var owner = Guid.NewGuid();
		var query = new CalaisQuery
		{
			Filters = [new() { Field = "sections.OWNERGROUPID", Values = [owner] }],
		};
		var source = db.Set<TestDocument>().AsNoTracking().Where(d => d.Id != Guid.Empty);
		var filtered = _processor.ApplyFilters(source, query);
		using var command = filtered.CreateDbCommand();
		command
			.CommandText.Should()
			.Contain(nameof(TestPersonalSection))
			.And.Contain(nameof(TestSharedSection))
			.And.NotContain(nameof(TestOwnedSection))
			.And.NotContain(nameof(UnregisteredOwnedSection));
		command
			.Parameters.Cast<System.Data.Common.DbParameter>()
			.Select(p => p.Value)
			.Should()
			.OnlyContain(value => Equals(value, owner));

		var sections = new TestSection[]
		{
			new TestPersonalSection { OwnerGroupId = owner },
			new TestSharedSection { OwnerGroupId = owner },
			new TestPersonalSection(),
			new UnregisteredOwnedSection { OwnerGroupId = owner },
			new TestTaggedSection(),
		};
		var documents = sections.Select(s => new TestDocument { Sections = [s] }).AsQueryable();
		var predicateQuery = ReplacingExpressionVisitor.Replace(
			source.Expression,
			documents.Expression,
			filtered.Expression
		);
		documents
			.Provider.CreateQuery<TestDocument>(predicateQuery)
			.AsEnumerable()
			.SelectMany(d => d.Sections)
			.Should()
			.Equal(sections.Take(2));
	}

	[Fact]
	public void InheritedNavigationUsesOnlySubtypesThatMapIt()
	{
		using var db = new FlattenedDocumentContext();
		var query = new CalaisQuery
		{
			Filters = [new() { Field = "sections.ownerLabel.id", Values = [Guid.NewGuid()] }],
		};
		var sql = _processor.ApplyFilters(db.Set<TestDocument>(), query).ToQueryString();
		sql.Should().Contain(nameof(TestSharedSection)).And.NotContain(nameof(TestPersonalSection));
	}

	[Fact]
	public void FieldExclusiveToAnUnmappedSubclassCannotMatchRows()
	{
		using var db = new FlattenedDocumentContext();
		var query = new CalaisQuery
		{
			Filters = [new() { Field = "sections.localMarker", Values = [1] }],
		};
		var sql = _processor.ApplyFilters(db.Set<TestDocument>(), query).ToQueryString();
		sql.Should().ContainEquivalentOf("FALSE").And.NotContain(nameof(UnregisteredOwnedSection));
	}

	[Fact]
	public void HiddenAndSiblingPropertiesStillContributeTheirOwnMatches()
	{
		var inherited = new TestPersonalSection();
		((TestOwnedSection)inherited).Code = "match";
		var sections = new TestSection[]
		{
			inherited,
			new TestPersonalSection { Code = "match" },
			new TestTaggedSection { Code = "match" },
			new TestSharedSection(),
		};
		var documents = sections.Select(s => new TestDocument { Sections = [s] }).AsQueryable();
		var query = new CalaisQuery
		{
			Filters = [new() { Field = "sections.code", Values = ["match"] }],
		};
		_processor
			.ApplyFilters(documents, query)
			.AsEnumerable()
			.SelectMany(d => d.Sections)
			.Should()
			.Equal(sections.Take(3));
	}

	[Fact]
	public void UnknownDerivedFieldRespectsEachProcessorsInvalidFieldPolicy()
	{
		var documents = new[] { new TestDocument() }.AsQueryable();
		var query = new CalaisQuery
		{
			Filters = [new() { Field = "sections.missing", Values = [1] }],
		};
		_processor.ApplyFilters(documents, query).Should().Equal(documents);
		var strict = new CalaisBuilder().ThrowOnInvalidFields(true).Build();
		var apply = () => strict.ApplyFilters(documents, query);
		apply.Should().Throw<PropertyNotFoundException>().Where(e => e.PropertyName == "missing");
	}

	private sealed class DocumentContext : DbContext
	{
		protected override void OnConfiguring(DbContextOptionsBuilder options) =>
			options.UseNpgsql();

		protected override void OnModelCreating(ModelBuilder model)
		{
			model.Entity<TestDocument>().HasMany(d => d.Sections).WithOne();
			model.Entity<TestOwnedSection>().Ignore(s => s.Code);
			model.Entity<TestPersonalSection>().Ignore(s => s.Code);
			model.Entity<TestSharedSection>();
			model.Entity<TestTaggedSection>();
		}
	}

	private sealed class FlattenedDocumentContext : DbContext
	{
		protected override void OnConfiguring(DbContextOptionsBuilder options) =>
			options.UseNpgsql();

		protected override void OnModelCreating(ModelBuilder model)
		{
			model.Entity<TestDocument>().HasMany(d => d.Sections).WithOne();
			model.Entity<TestPersonalSection>().HasBaseType<TestSection>().Ignore(s => s.Code);
			model.Entity<TestPersonalSection>().Ignore(s => s.OwnerLabel);
			model.Entity<TestSharedSection>().HasBaseType<TestSection>();
			model.Entity<TestTaggedSection>();
		}
	}

	private class TestDocument
	{
		public Guid Id { get; set; }
		public List<TestSection> Sections { get; set; } = [];
	}

	private abstract class TestSection
	{
		public Guid Id { get; set; }
	}

	private abstract class TestOwnedSection : TestSection
	{
		public Guid OwnerGroupId { get; set; }
		public TestLabel? OwnerLabel { get; set; }
		public string? Code { get; set; }
	}

	private class TestPersonalSection : TestOwnedSection
	{
		public new string? Code { get; set; }
	}

	private class TestSharedSection : TestOwnedSection;

	private class UnregisteredOwnedSection : TestOwnedSection
	{
		public int LocalMarker { get; set; }
	}

	private class TestTaggedSection : TestSection
	{
		public List<TestLabel> Labels { get; set; } = [];
		public string? Code { get; set; }
	}

	private class TestLabel
	{
		public Guid Id { get; set; }
	}
}
