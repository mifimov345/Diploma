using Microsoft.EntityFrameworkCore;
using AuthService.Models;
using System.Collections.Generic;
using System.Reflection.Emit;
using AuthService.Utils;

namespace AuthService.Data
{
    public class AuthDbContext : DbContext
    {
        public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Group> Groups { get; set; }
        public DbSet<UserGroup> UserGroups { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserGroup>()
                .HasKey(ug => new { ug.UserId, ug.GroupId });

            modelBuilder.Entity<UserGroup>()
                .HasOne(ug => ug.User)
                .WithMany(u => u.UserGroups)
                .HasForeignKey(ug => ug.UserId);

            modelBuilder.Entity<UserGroup>()
                .HasOne(ug => ug.Group)
                .WithMany(g => g.UserGroups)
                .HasForeignKey(ug => ug.GroupId);

            modelBuilder.Entity<Group>().HasData(
                new Group { Id = 1, Name = "System", Description = "Системная группа, не может быть удалена" },
                new Group { Id = 2, Name = "Admins", Description = "Группа администраторов" },
                new Group { Id = 3, Name = "Users", Description = "Группа обычных пользователей" }
            );

            modelBuilder.Entity<User>().HasData(
                new User { Id = 1, UserName = "sysadmin", PasswordHash = PasswordHelper.HashPassword("password"), Role = "SuperAdmin" , CreatedByAdminId = 1 },
                new User { Id = 2, UserName = "admin", PasswordHash = PasswordHelper.HashPassword("password1"), Role = "Admin", CreatedByAdminId = 1 },
                new User { Id = 3, UserName = "user", PasswordHash = PasswordHelper.HashPassword("password2"), Role = "User" , CreatedByAdminId = 1 }
            );

            modelBuilder.Entity<UserGroup>().HasData(
                new UserGroup { UserId = 1, GroupId = 1 },
                new UserGroup { UserId = 2, GroupId = 2 },
                new UserGroup { UserId = 3, GroupId = 3 }
            );
        }
    }
}
