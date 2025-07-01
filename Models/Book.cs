using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;

namespace MvcBooks.Models
{
    public class Book
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [StringLength(200)]
        public required string Title { get; set; }
        
        [StringLength(500)]
        public string? Description { get; set; }
        
        [StringLength(2048)]
        [Display(Name = "Cover Image URL")]
        public string? CoverImageUrl { get; set; }

        [StringLength(1024)] 
        public string? CoverImageObjectKey { get; set; }

        
        [DataType(DataType.Date)]
        public DateTime PublishedDate { get; set; }
        
        [StringLength(200)]
        public string? Author { get; set; }
            
        public string? UserId { get; set; }
        
        [ForeignKey("UserId")]
        public User? User { get; set; }
        
        // Direct many-to-many relationship
        public ICollection<Category> Categories { get; set; } = new List<Category>(); 

        [StringLength(2048)] 
        [Url] 
        [Display(Name = "Book Source URL")]
        public string? BookUrl { get; set; } 

         [StringLength(1024)]
        public string? BookFileObjectKey { get; set; }

        [StringLength(20)]
        public string? BookSourceType { get; set; }

        [Display(Name = "Publicly Visible?")]
        public bool IsPublic { get; set; } = true;
    }
}