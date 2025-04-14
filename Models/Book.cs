using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
        
        [StringLength(200)]
        public string? CoverImageUrl { get; set; }
        
        [DataType(DataType.Date)]
        public DateTime PublishedDate { get; set; }
        
        [StringLength(200)]
        public string? Author { get; set; }
        
        
        public string? UserId { get; set; }
        
        [ForeignKey("UserId")]
        public User? User { get; set; }
        
        // Direct many-to-many relationship
        public ICollection<Category> Categories { get; set; } = new List<Category>();

        

         [StringLength(255)] 
        public string? EpubFilePath { get; set; }

        [StringLength(100)] 
        public string? EpubFileName { get; set; }

       

        [StringLength(2048)] 
        [Url] 
        [Display(Name = "Book Source URL")]
        public string? BookUrl { get; set; } 

        [Display(Name = "Publicly Visible?")]
        public bool IsPublic { get; set; } = true;
    }
}