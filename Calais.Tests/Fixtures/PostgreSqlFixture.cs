using System;
using System.Text.Json;
using System.Threading.Tasks;
using Calais.Tests.TestEntities;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Calais.Tests.Fixtures
{
    public class PostgreSqlFixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _container;
        public string ConnectionString => _container.GetConnectionString();

        public PostgreSqlFixture()
        {
            _container = new PostgreSqlBuilder("postgres:15-alpine")
                .WithCleanUp(true)
                .Build();
        }

        public async ValueTask InitializeAsync()
        {
            await _container.StartAsync();
            await SeedDatabase();
        }

        public async ValueTask DisposeAsync()
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }

        public TestDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<TestDbContext>()
                .UseNpgsql(ConnectionString)
                .Options;

            return new TestDbContext(options);
        }

        private async Task SeedDatabase()
        {
            await using var context = CreateContext();
            await context.Database.EnsureCreatedAsync();

            // Seed test data
            var user1 = new User
            {
                Id = Guid.Parse("cf9175ce-ded3-4447-9725-e6f018430de9"),
                Name = "alice",
                Age = 25,
                Email = "alice@example.com",
                PasswordHash = "hash1",
                Tags = ["developer", "admin"],
                Status = UserStatus.Active,
                JsonbColumn = JsonDocument.Parse("{\"randomData\": \"tagged\", \"score\": 100}")
            };

            var user2 = new User
            {
                Id = Guid.Parse("a435a45f-a07f-4219-97e5-e2f6d534c3b0"),
                Name = "bob",
                Age = 30,
                Email = "bob@example.com",
                PasswordHash = "hash2",
                Tags = ["developer", "tester"],
                Status = UserStatus.Suspended,
                LockoutEnd = DateTimeOffset.UtcNow.AddDays(1),
                JsonbColumn = JsonDocument.Parse("{\"randomData\": \"other\", \"score\": 50}")
            };

            var user3 = new User
            {
                Id = Guid.NewGuid(),
                Name = "charlie",
                Age = 35,
                Email = "charlie@example.com",
                PasswordHash = "hash3",
                Tags = ["admin", "manager"],
                Status = UserStatus.Active,
                JsonbColumn = JsonDocument.Parse("{\"randomData\": \"tagged\", \"score\": 75}")
            };

            var user4 = new User
            {
                Id = Guid.NewGuid(),
                Name = "diana",
                Age = 22,
                Email = "diana@example.com",
                PasswordHash = "hash4",
                Tags = ["tester"],
                Status = UserStatus.Pending
            };

            var user5 = new User
            {
                Id = Guid.NewGuid(),
                Name = "eve",
                Age = 40,
                Email = "eve@example.com",
                PasswordHash = "hash5",
                Tags = [],
                Status = UserStatus.Banned
            };

            context.Users.AddRange(user1, user2, user3, user4, user5);
            await context.SaveChangesAsync();

            // Add posts - ContentVector is auto-generated from Content via HasGeneratedTsVectorColumn
            var post1 = new Post
            {
                Id = Guid.NewGuid(),
                Title = "abc test post",
                Content = "This is a test content with examples",
                UserId = user1.Id
            };

            var post2 = new Post
            {
                Id = Guid.NewGuid(),
                Title = "another post",
                Content = "Different content here",
                UserId = user2.Id
            };

            var post3 = new Post
            {
                Id = Guid.NewGuid(),
                Title = "abc different",
                Content = "More test content examples",
                UserId = user3.Id
            };

            context.Posts.AddRange(post1, post2, post3);
            await context.SaveChangesAsync();

            // Add comments
            var comment1 = new Comment
            {
                Id = Guid.NewGuid(),
                Text = "This is a good comment",
                PostId = post1.Id,
                UserId = user2.Id
            };

            var comment2 = new Comment
            {
                Id = Guid.NewGuid(),
                Text = "Another comment here",
                PostId = post1.Id,
                UserId = user3.Id
            };

            var comment3 = new Comment
            {
                Id = Guid.NewGuid(),
                Text = "A good review",
                PostId = post2.Id,
                UserId = user1.Id
            };

            context.Comments.AddRange(comment1, comment2, comment3);
            await context.SaveChangesAsync();
        }
    }

    [CollectionDefinition("PostgreSql")]
    public class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
    {
    }
}
