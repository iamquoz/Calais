using System;
using System.Collections.Generic;
using System.Linq;
using Calais.Exceptions;
using Calais.Models;
using FluentAssertions;
using Xunit;

namespace Calais.Tests
{
    public class ExceptionTests
    {
        [Fact]
        public void PropertyNotFound_ThrowsPropertyNotFoundException()
        {
            var processor = new CalaisBuilder()
                .ThrowOnInvalidFields(true)
                .Build();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "nonExistentProperty",
		                Operator = "==",
		                Values = ["test"]
	                }
                ]
            };

            var users = new List<TestUser>().AsQueryable();

            var act = () => processor.ApplyFilters(users, query);

            act.Should().Throw<PropertyNotFoundException>()
                .Where(e => e.PropertyName == "nonExistentProperty" && e.EntityType == typeof(TestUser));
        }

        [Fact]
        public void PropertyNotFilterable_ThrowsPropertyNotFilterableException()
        {
            var processor = new CalaisBuilder()
                .ThrowOnInvalidFields(true)
                .ConfigureEntity<TestUser>(e => e.Ignore(u => u.Secret, filter: true, sorts: false))
                .Build();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "secret",
		                Operator = "==",
		                Values = ["test"]
	                }
                ]
            };

            var users = new List<TestUser>().AsQueryable();

            var act = () => processor.ApplyFilters(users, query);

            act.Should().Throw<PropertyNotFilterableException>()
                .Where(e => e.PropertyName == "secret");
        }

        [Fact]
        public void PropertyNotSortable_ThrowsPropertyNotSortableException()
        {
            var processor = new CalaisBuilder()
                .ThrowOnInvalidFields(true)
                .ConfigureEntity<TestUser>(e => e.Ignore(u => u.Secret, filter: false, sorts: true))
                .Build();

            var query = new CalaisQuery
            {
                Sorts =
                [
	                new SortDescriptor
	                {
		                Field = "secret",
		                Direction = "asc"
	                }
                ]
            };

            var users = new List<TestUser>().AsQueryable();

            var act = () => processor.ApplySorting(users, query);

            act.Should().Throw<PropertyNotSortableException>()
                .Where(e => e.PropertyName == "secret");
        }

        [Fact]
        public void InvalidJsonPath_ThrowsInvalidJsonPathException()
        {
            var processor = new CalaisBuilder()
                .ThrowOnInvalidFields(true)
                .Build();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "singlePart", // No dot, invalid for JSON
		                IsJson = true,
		                Operator = "==",
		                Values = ["test"]
	                }
                ]
            };

            var users = new List<TestUser>().AsQueryable();

            var act = () => processor.ApplyFilters(users, query);

            act.Should().Throw<InvalidJsonPathException>()
                .Where(e => e.Path == "singlePart");
        }

        [Fact]
        public void InvalidJsonPathSort_ThrowsInvalidJsonPathException()
        {
            var processor = new CalaisBuilder()
                .ThrowOnInvalidFields(true)
                .Build();

            var query = new CalaisQuery
            {
                Sorts =
                [
	                new SortDescriptor
	                {
		                Field = "singlePart", // No dot, invalid for JSON
		                IsJson = true,
		                Direction = "asc"
	                }
                ]
            };

            var users = new List<TestUser>().AsQueryable();

            var act = () => processor.ApplySorting(users, query);

            act.Should().Throw<InvalidJsonPathException>()
                .Where(e => e.Path == "singlePart");
        }

        [Fact]
        public void NestedPropertyNotFound_ThrowsPropertyNotFoundException()
        {
            var processor = new CalaisBuilder()
                .ThrowOnInvalidFields(true)
                .Build();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "address.nonExistent",
		                Operator = "==",
		                Values = ["test"]
	                }
                ]
            };

            var users = new List<TestUser>().AsQueryable();

            var act = () => processor.ApplyFilters(users, query);

            act.Should().Throw<PropertyNotFoundException>();
        }

        [Fact]
        public void ThrowOnInvalidFields_False_DoesNotThrow()
        {
            var processor = new CalaisBuilder()
                .ThrowOnInvalidFields(false) // Default
                .Build();

            var query = new CalaisQuery
            {
                Filters =
                [
	                new FilterDescriptor
	                {
		                Field = "nonExistentProperty",
		                Operator = "==",
		                Values = ["test"]
	                }
                ]
            };

            var users = new List<TestUser>().AsQueryable();

            var act = () => processor.ApplyFilters(users, query);

            act.Should().NotThrow();
        }

        [Fact]
        public void CalaisException_IsBaseForAllExceptions()
        {
            var propertyNotFound = new PropertyNotFoundException("test", typeof(string));
            var propertyNotFilterable = new PropertyNotFilterableException("test");
            var propertyNotSortable = new PropertyNotSortableException("test");
            var invalidJsonPath = new InvalidJsonPathException("test");
            var invalidOperator = new InvalidFilterOperatorException("test");
            var valueConversion = new ValueConversionException("test", typeof(int));
            var expressionBuild = new ExpressionBuildException("test");

            propertyNotFound.Should().BeAssignableTo<CalaisException>();
            propertyNotFilterable.Should().BeAssignableTo<CalaisException>();
            propertyNotSortable.Should().BeAssignableTo<CalaisException>();
            invalidJsonPath.Should().BeAssignableTo<CalaisException>();
            invalidOperator.Should().BeAssignableTo<CalaisException>();
            valueConversion.Should().BeAssignableTo<CalaisException>();
            expressionBuild.Should().BeAssignableTo<CalaisException>();
        }

        private class TestUser
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Secret { get; set; } = string.Empty;
            public TestAddress? Address { get; set; }
        }

        private class TestAddress
        {
            public string Street { get; set; } = string.Empty;
        }
    }
}
