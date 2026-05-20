using System;
using System.Collections.Generic;
using System.Linq;
using Calais.Models;
using FluentAssertions;
using Xunit;

namespace Calais.Tests
{
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
                            Labels = [new TestLabel { Id = targetLabelId }]
                        }
                    ]
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Sections =
                    [
                        new TestTaggedSection
                        {
                            Id = Guid.NewGuid(),
                            Labels = [new TestLabel { Id = otherLabelId }]
                        }
                    ]
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Sections =
                    [
                        new TestPersonalSection
                        {
                            Id = Guid.NewGuid(),
                            OwnerGroupId = targetLabelId
                        }
                    ]
                }
            };

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "sections.labels.id",
                        Operator = "==",
                        Values = [targetLabelId]
                    }
                ]
            };

            var result = _processor.ApplyFilters(documents.AsQueryable(), query).ToList();

            result.Should().ContainSingle();
            result.Single().Sections.OfType<TestTaggedSection>()
                .Single().Labels.Single().Id.Should().Be(targetLabelId);
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
                            OwnerGroupId = targetOwnerGroupId
                        }
                    ]
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Sections =
                    [
                        new TestSharedSection
                        {
                            Id = Guid.NewGuid(),
                            OwnerGroupId = targetOwnerGroupId
                        }
                    ]
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Sections =
                    [
                        new TestPersonalSection
                        {
                            Id = Guid.NewGuid(),
                            OwnerGroupId = otherOwnerGroupId
                        }
                    ]
                }
            };

            var query = new CalaisQuery
            {
                Filters =
                [
                    new FilterDescriptor
                    {
                        Field = "sections.ownerGroupId",
                        Operator = "==",
                        Values = [targetOwnerGroupId]
                    }
                ]
            };

            var result = _processor.ApplyFilters(documents.AsQueryable(), query).ToList();

            result.Should().HaveCount(2);
            result.SelectMany(c => c.Sections)
                .Should().AllSatisfy(section =>
                {
                    section.Should().BeAssignableTo<TestOwnedSection>();
                    ((TestOwnedSection)section).OwnerGroupId.Should().Be(targetOwnerGroupId);
                });
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
        }

        private class TestPersonalSection : TestOwnedSection;

        private class TestSharedSection : TestOwnedSection;

        private class TestTaggedSection : TestSection
        {
            public List<TestLabel> Labels { get; set; } = [];
        }

        private class TestLabel
        {
            public Guid Id { get; set; }
        }
    }
}
