using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations; 

namespace MvcBooks.Models
{
    public class User : IdentityUser
    {
        [StringLength(100)] 
        public string? FirstName { get; set; }

        [StringLength(100)]
        public string? LastName { get; set; }

        
        [StringLength(255)] 
        [Display(Name = "Profile Picture")]
        public string? ProfilePictureUrl { get; set; }

        public ICollection<Book> PublishedBooks { get; set; } = [];
    }
}