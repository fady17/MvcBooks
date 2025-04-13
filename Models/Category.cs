using System.ComponentModel.DataAnnotations;

namespace MvcBooks.Models
{
    public class Category
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public required string Name { get; set; }
        
        // Display order for sorting on the home page
        public int DisplayOrder { get; set; }
        
        // Direct many-to-many relationship
        public ICollection<Book> Books { get; set; } = new List<Book>();
    }
}