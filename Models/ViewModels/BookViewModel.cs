using System.ComponentModel.DataAnnotations;

namespace MvcBooks.Models.ViewModels
{
    public class BookViewModel
    {
        public int Id { get; set; }

        [Required]
        [StringLength(200)]
        [Display(Name = "Book Title")]
        public string Title { get; set; } = string.Empty;

        [StringLength(500)]
        [Display(Name = "Description")]
        public string? Description { get; set; }

        [Display(Name = "Cover Image")]
        public IFormFile? CoverImage { get; set; } 

        [Display(Name = "Current Cover")]
        public string? ExistingCoverUrl { get; set; } 

        [Display(Name = "Publication Date")]
        [DataType(DataType.Date)]
        public DateTime PublishedDate { get; set; } = DateTime.Now;

        [StringLength(200)]
        [Display(Name = "Author")]
        public string? Author { get; set; }

        [Display(Name = "Categories")]
        public List<int>? SelectedCategoryIds { get; set; } 

        public IEnumerable<Category>? AvailableCategories { get; set; } 

       

        [Display(Name = "Upload EPUB File")]
        
        public IFormFile? EpubFile { get; set; } 

        [Display(Name = "Current EPUB File")]
        public string? ExistingEpubFileName { get; set; } 

       
        [Display(Name = "Current Book URL")]
        public string? ExistingBookUrl { get; set; } 


        [Display(Name = "OR Enter Book URL")]
        [StringLength(2048)]
        [Url(ErrorMessage = "Please enter a valid URL (e.g., https://example.com/book.epub)")]
        public string? BookUrl { get; set; } 

        [Display(Name = "Make Publicly Visible on Home Page?")]
        public bool IsPublic { get; set; } = true; 


    }
}