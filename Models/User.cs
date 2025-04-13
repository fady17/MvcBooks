using Microsoft.AspNetCore.Identity;

namespace MvcBooks.Models
{
    public class User : IdentityUser
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        
        // Navigation property for books published by this user
        public ICollection<Book> PublishedBooks { get; set; } = [];
    }
}