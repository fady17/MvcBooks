using MvcBooks.Models; // Your base models namespace
using System.Collections.Generic; // Required for List

namespace MvcBooks.Models.ViewModels
{
    public class HomeViewModel
    {
        // List of books added by the currently logged-in user (their history)
        public List<Book> UserHistoryBooks { get; set; } = new List<Book>();

        // List of categories containing only their publicly visible books *after filtering*
        public List<Category> PublicCategories { get; set; } = new List<Category>();

        // --- ADDED FOR FILTERING ---
        // List of all categories that have at least one public book, used to populate filter options
        public List<Category> FilterableCategories { get; set; } = new List<Category>();

        // List of IDs selected by the user in the filter UI
        public List<int>? SelectedCategoryIds { get; set; } // Nullable list of integers
        // ---------------------------
    }
}