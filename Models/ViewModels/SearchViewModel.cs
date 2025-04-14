using MvcBooks.Models; // Your base models namespace
using System.Collections.Generic; // Required for List

namespace MvcBooks.Models.ViewModels
{
    public class SearchViewModel
    {
        // The term the user searched for
        public string SearchTerm { get; set; } = string.Empty;

        // The list of books matching the search term
        public List<Book> Results { get; set; } = new List<Book>();
    }
}