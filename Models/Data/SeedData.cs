// MvcBooks/Models/Data/SeedData.cs (or similar path)
using Microsoft.EntityFrameworkCore;
using MvcBooks.Models; // Make sure this namespace is correct
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection; // Required for IServiceProvider

namespace MvcBooks.Models.Data
{
    public static class SeedData
    {
        public static async Task Initialize(IServiceProvider serviceProvider)
        {
            // Get the DbContext instance using the service provider
            using (var context = new ApplicationDbContext(
                serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>()))
            {
                // Check if the database has already been seeded
                if (await context.Categories.AnyAsync() || await context.Books.AnyAsync())
                {
                    Console.WriteLine("Database already seeded. Skipping.");
                    return; // DB has been seeded
                }

                Console.WriteLine("Seeding database...");

                // --- Create Categories ---
                var categories = new Dictionary<string, Category>(); // Use dictionary for easy lookup

                var fiction = new Category { Name = "Fiction", DisplayOrder = 1 };
                categories.Add(fiction.Name, fiction);

                var sciFi = new Category { Name = "Science Fiction", DisplayOrder = 2 };
                categories.Add(sciFi.Name, sciFi);

                var fantasy = new Category { Name = "Fantasy", DisplayOrder = 3 };
                categories.Add(fantasy.Name, fantasy);

                var nonFiction = new Category { Name = "Non-Fiction", DisplayOrder = 4 };
                categories.Add(nonFiction.Name, nonFiction);

                var mystery = new Category { Name = "Mystery", DisplayOrder = 5 };
                categories.Add(mystery.Name, mystery);

                var thriller = new Category { Name = "Thriller", DisplayOrder = 6 };
                categories.Add(thriller.Name, thriller);

                // Add categories to DbContext
                await context.Categories.AddRangeAsync(categories.Values);
                // Save changes here so Categories get Ids before books are added (optional but can be clearer)
                // await context.SaveChangesAsync(); // You can save here or just once at the end


                // --- Create Books ---
                var books = new List<Book>
                {
                    new Book
                    {
                        Title = "Dune",
                        Author = "Frank Herbert",
                        Description = "A landmark science fiction novel set in the distant future amidst a feudal interstellar society.",
                        PublishedDate = new DateTime(1965, 8, 1),
                        CoverImageUrl = "https://upload.wikimedia.org/wikipedia/en/d/de/Dune-Frank_Herbert_%281965%29_First_edition.jpg", // Example URL
                        Categories = new List<Category> { categories["Science Fiction"] } // Associate category
                    },
                    new Book
                    {
                        Title = "The Hobbit",
                        Author = "J.R.R. Tolkien",
                        Description = "A fantasy novel and children's book about the quest of home-loving Bilbo Baggins.",
                        PublishedDate = new DateTime(1937, 9, 21),
                        // CoverImageUrl = null, // Test placeholder image
                        Categories = [categories["Fantasy"]]
                    },
                     new Book
                    {
                        Title = "Foundation",
                        Author = "Isaac Asimov",
                        Description = "The first novel in the Foundation Series, following mathematician Hari Seldon's attempt to preserve knowledge.",
                        PublishedDate = new DateTime(1951, 6, 1),
                        CoverImageUrl = "https://upload.wikimedia.org/wikipedia/en/2/25/Foundation_gnome.jpg",
                        Categories = [categories["Science Fiction"]]
                    },
                    new Book
                    {
                        Title = "A Game of Thrones",
                        Author = "George R.R. Martin",
                        Description = "The first novel in A Song of Ice and Fire, a series of epic fantasy novels.",
                        PublishedDate = new DateTime(1996, 8, 1),
                        CoverImageUrl = "https://upload.wikimedia.org/wikipedia/en/9/93/AGameOfThrones.jpg",
                        Categories = [categories["Fantasy"], categories["Fiction"]] // Multiple categories
                    },
                     new Book
                    {
                        Title = "Sapiens: A Brief History of Humankind",
                        Author = "Yuval Noah Harari",
                        Description = "Explores the history of humankind from the Stone Age up to the present day.",
                        PublishedDate = new DateTime(2011, 1, 1), // Original Hebrew date
                        CoverImageUrl = "https://upload.wikimedia.org/wikipedia/en/6/68/Sapiens_A_Brief_History_of_Humankind.jpg",
                        Categories = [categories["Non-Fiction"]]
                    },
                     new Book
                    {
                        Title = "The Da Vinci Code",
                        Author = "Dan Brown",
                        Description = "A mystery thriller novel following symbologist Robert Langdon and cryptologist Sophie Neveu.",
                        PublishedDate = new DateTime(2003, 3, 18),
                        Categories = [categories["Mystery"], categories["Thriller"], categories["Fiction"]]
                    },
                    new Book
                    {
                        Title = "1984",
                        Author = "George Orwell",
                        Description = "A dystopian social science fiction novel and cautionary tale.",
                        PublishedDate = new DateTime(1949, 6, 8),
                        Categories = [categories["Fiction"], categories["Science Fiction"]]
                    },
                     new Book
                    {
                        Title = "The Girl with the Dragon Tattoo",
                        Author = "Stieg Larsson",
                        Description = "A psychological thriller novel that became a posthumous bestseller.",
                        PublishedDate = new DateTime(2005, 8, 1), // Swedish date
                        Categories = [categories["Mystery"], categories["Thriller"]]
                    },
                    new Book
                    {
                        Title = "Cosmos",
                        Author = "Carl Sagan",
                        Description = "Explores cosmic evolution and human civilization based on the television series.",
                        PublishedDate = new DateTime(1980, 10, 12),
                        Categories = [categories["Non-Fiction"], categories["Science Fiction"]]
                    }
                     // Add more books as needed...
                };

                // Add books to DbContext
                // EF Core automatically handles the many-to-many join table insertions
                // when you add Books that have Category objects in their Categories collection.
                await context.Books.AddRangeAsync(books);

                // --- Save all changes to the database ---
                await context.SaveChangesAsync();

                Console.WriteLine("Database seeding finished.");
            }
        }
    }
}