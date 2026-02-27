using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace Calais.Tests.TestEntities
{
    public enum UserStatus
    {
        Pending = 0,
        Active = 1,
        Suspended = 2,
        Banned = 3
    }

    public class User
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public string? Email { get; set; }
        public string PasswordHash { get; set; } = string.Empty;
        public DateTimeOffset? LockoutEnd { get; set; }
        public JsonDocument? JsonbColumn { get; set; }
        public string[] Tags { get; set; } = [];
        public UserStatus Status { get; set; } = UserStatus.Pending;
        public DateOnly? BirthDate { get; set; }
        public TimeOnly? PreferredContactTime { get; set; }
        public TimeSpan? SessionDuration { get; set; }
        public List<Post> Posts { get; set; } = [];
        public List<Comment> Comments { get; set; } = [];
    }

    public class Post
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public NpgsqlTsVector ContentVector { get; set; } = null!;
        public Guid UserId { get; set; }
        public User User { get; set; } = null!;
        public List<Comment> Comments { get; set; } = [];
    }

    public class Comment
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public Guid PostId { get; set; }
        public Post Post { get; set; } = null!;
        public Guid UserId { get; set; }
        public User User { get; set; } = null!;
    }
}
