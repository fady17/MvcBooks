using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MvcBooks.Models.Data
{
    public class ApplicationDbContext : IdentityDbContext<User>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Book> Books { get; set; }
        public DbSet<Category> Categories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

        // Book ↔ Category (Many-to-Many)
        modelBuilder.Entity<Book>()
            .HasMany(b => b.Categories)
            .WithMany(c => c.Books);

        // Book → User (Many-to-One)
        modelBuilder.Entity<Book>()
            .HasOne(b => b.User)
            .WithMany(u => u.PublishedBooks)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.SetNull);
            }
    }
}