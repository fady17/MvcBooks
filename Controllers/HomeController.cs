using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MvcBooks.Models;
using Microsoft.EntityFrameworkCore;
using MvcBooks.Models.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using MvcBooks.Models.ViewModels; // Added
using System.Security.Claims;
using System.Collections.Generic;

namespace MvcBooks.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager;

        public HomeController(
            ILogger<HomeController> logger,
            ApplicationDbContext context,
            UserManager<User> userManager)
        {
            _logger = logger;
            _context = context;
            _userManager = userManager;
        }

        // Index action remains the same (fetches filterable categories, user history, public categories)
        public async Task<IActionResult> Index(List<int>? selectedCategoryIds)
        {
            var viewModel = new HomeViewModel();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId != null)
            {
                viewModel.UserHistoryBooks = await _context.Books
                    .Where(b => b.UserId == userId)
                    .OrderByDescending(b => b.Id).Take(15).ToListAsync();
            }

            viewModel.FilterableCategories = await _context.Categories
                                                    .Where(c => c.Books.Any(b => b.IsPublic == true))
                                                    .OrderBy(c => c.Name).ToListAsync();

            IQueryable<Category> categoriesQuery = _context.Categories.AsQueryable();
            if (selectedCategoryIds != null && selectedCategoryIds.Any())
            {
                categoriesQuery = categoriesQuery.Where(c => selectedCategoryIds.Contains(c.Id));
                viewModel.SelectedCategoryIds = selectedCategoryIds;
            }
            else { viewModel.SelectedCategoryIds = new List<int>(); }

            viewModel.PublicCategories = await categoriesQuery
                                                .OrderBy(c => c.DisplayOrder)
                                                .Include(c => c.Books.Where(b => b.IsPublic == true)
                                                                      .OrderByDescending(b => b.Id)
                                                                      .Take(10))
                                                .ToListAsync();
            viewModel.PublicCategories = viewModel.PublicCategories.Where(c => c.Books.Any()).ToList();

            return View(viewModel);
        }


        // --- START: Added Search Action ---
        [HttpGet] 
        public async Task<IActionResult> Search(string searchTerm) 
        {
            var viewModel = new SearchViewModel
            {
                SearchTerm = searchTerm 
            };

            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                
                return View(viewModel);
            }

            viewModel.Results = await _context.Books
                .Where(b => b.IsPublic == true && b.Title.Contains(searchTerm)) // Simple Contains search
                .OrderBy(b => b.Title) // Order results alphabetically
                .ToListAsync();

            return View(viewModel); // Pass ViewModel to the Search view
        }
        
         [HttpGet]
        public async Task<IActionResult> GetSuggestions(string term)
        {
            // Basic validation: require at least 1 or 2 characters
            if (string.IsNullOrWhiteSpace(term) || term.Length < 1)
            {
                return Json(new List<object>()); // Return empty list if term is too short
            }

            // Query for public books where the title starts with the term (case-insensitive)
            // Select only Id and Title for efficiency
            var suggestions = await _context.Books
                .Where(b => b.IsPublic == true && b.Title.StartsWith(term))
                .OrderBy(b => b.Title) // Order suggestions alphabetically
                .Take(8) // Limit the number of suggestions
                .Select(b => new { id = b.Id, title = b.Title }) // Select anonymous object
                .ToListAsync();

            return Json(suggestions); // Return suggestions as JSON
        }


        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}