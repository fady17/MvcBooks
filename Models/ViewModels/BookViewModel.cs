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
        public IFormFile? CoverImage { get; set; } // For upload

        [Display(Name = "Current Cover")]
        public string? ExistingCoverUrl { get; set; } // For showing existing in Edit view

        [Display(Name = "Publication Date")]
        [DataType(DataType.Date)]
        public DateTime PublishedDate { get; set; } = DateTime.Now;

        [StringLength(200)]
        [Display(Name = "Author")]
        public string? Author { get; set; }

        [Display(Name = "Categories")]
        public List<int>? SelectedCategoryIds { get; set; } // For binding selected category IDs

        public IEnumerable<Category>? AvailableCategories { get; set; } // For populating selection options

        // --- EPUB Source Options ---

        [Display(Name = "Upload EPUB File")]
        // REMOVED [Required] to allow providing URL instead. Controller will check.
        public IFormFile? EpubFile { get; set; } // For upload

        [Display(Name = "Current EPUB File")]
        public string? ExistingEpubFileName { get; set; } // For showing existing in Edit view (if file was uploaded)

        // We still need ExistingBookUrl for Edit context if allowing URL option
        [Display(Name = "Current Book URL")]
        public string? ExistingBookUrl { get; set; } // For showing existing URL in Edit view


        [Display(Name = "OR Enter Book URL")]
        [StringLength(2048)]
        [Url(ErrorMessage = "Please enter a valid URL (e.g., https://example.com/book.epub)")]
        public string? BookUrl { get; set; } // For linking to external source


    }
}