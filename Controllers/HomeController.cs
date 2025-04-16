using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MvcBooks.Models;
using Microsoft.EntityFrameworkCore;
using MvcBooks.Models.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using MvcBooks.Models.ViewModels;
using System.Security.Claims;
using System.Collections.Generic;
using MvcBooks.Services; // Added for MinioService
using Microsoft.Extensions.Logging; // Added for ILogger

namespace MvcBooks.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<User> _userManager; // Kept for SignedIn check consistency
        private readonly MinioService _minioService;
        private const int HomePagePresignedUrlExpirySeconds = 60 * 10; // 10 minutes

        public HomeController(
            ILogger<HomeController> logger,
            ApplicationDbContext context,
            UserManager<User> userManager, // Kept for DI even if only used minimally
            MinioService minioService)
        {
            _logger = logger;
            _context = context;
            _userManager = userManager;
            _minioService = minioService;
        }

        public async Task<IActionResult> Index(List<int>? selectedCategoryIds)
        {
            var viewModel = new HomeViewModel();
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // --- Fetch User History Books ---
            if (userId != null)
            {
                viewModel.UserHistoryBooks = await _context.Books
                    .Where(b => b.UserId == userId)
                    .OrderByDescending(b => b.Id)
                    .Select(b => new Book { // Project needed fields
                        Id = b.Id, Title = b.Title,
                        CoverImageObjectKey = b.CoverImageObjectKey
                        // No need to select CoverImageUrl as we regenerate it
                    })
                    .Take(15)
                    .ToListAsync();

                // --- Regenerate Presigned URLs for User History Covers ---
                foreach (var book in viewModel.UserHistoryBooks) {
                    if (!string.IsNullOrEmpty(book.CoverImageObjectKey)) {
                         try {
                            // --- FIX: Assign result to variable and check ---
                            string? coverUrl = await _minioService.GetPresignedFileUrlAsync(book.CoverImageObjectKey, HomePagePresignedUrlExpirySeconds);
                            book.CoverImageUrl = coverUrl; // Assign checked (potentially null) value
                            // --- END FIX ---
                         } catch (Exception ex) {
                            _logger.LogWarning(ex, "Failed to generate presigned URL for user history cover (Key: {Key})", book.CoverImageObjectKey);
                            book.CoverImageUrl = null;
                         }
                    }
                }
            }

            // --- Fetch Filterable Categories ---
            viewModel.FilterableCategories = await _context.Categories
                                                    .Where(c => c.Books.Any(b => b.IsPublic == true))
                                                    .OrderBy(c => c.Name)
                                                    .Select(c => new Category { Id = c.Id, Name = c.Name })
                                                    .ToListAsync();

            // --- Build Query for Categories to Display ---
            IQueryable<Category> categoriesQuery = _context.Categories.AsQueryable();
            if (selectedCategoryIds != null && selectedCategoryIds.Any()) {
                categoriesQuery = categoriesQuery.Where(c => selectedCategoryIds.Contains(c.Id));
                viewModel.SelectedCategoryIds = selectedCategoryIds;
            } else { viewModel.SelectedCategoryIds = new List<int>(); }

            // --- Fetch Public Categories with Public Books ---
            viewModel.PublicCategories = await categoriesQuery
                                                .OrderBy(c => c.DisplayOrder)
                                                .Select(c => new Category {
                                                     Id = c.Id, Name = c.Name, DisplayOrder = c.DisplayOrder,
                                                     Books = c.Books.Where(b => b.IsPublic == true)
                                                                      .OrderByDescending(b => b.Id)
                                                                      .Select(b => new Book { // Project needed fields
                                                                         Id = b.Id, Title = b.Title,
                                                                         CoverImageObjectKey = b.CoverImageObjectKey
                                                                      })
                                                                      .Take(10).ToList()
                                                })
                                                .ToListAsync();

            // --- Regenerate Presigned URLs for Public Category Covers ---
             foreach (var category in viewModel.PublicCategories) {
                foreach (var book in category.Books) {
                    if (!string.IsNullOrEmpty(book.CoverImageObjectKey)) {
                        try {
                            // --- FIX: Assign result to variable and check ---
                             string? coverUrl = await _minioService.GetPresignedFileUrlAsync(book.CoverImageObjectKey, HomePagePresignedUrlExpirySeconds);
                             book.CoverImageUrl = coverUrl; // Assign checked (potentially null) value
                            // --- END FIX ---
                        } catch (Exception ex) {
                            _logger.LogWarning(ex, "Failed to generate presigned URL for public cover (Key: {Key})", book.CoverImageObjectKey);
                            book.CoverImageUrl = null;
                        }
                    }
                }
             }

            // Filter out categories that might be empty after Take(10) or failed URL generation (if needed)
            viewModel.PublicCategories = viewModel.PublicCategories.Where(c => c.Books.Any()).ToList();

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Search(string searchTerm)
        {
            var viewModel = new SearchViewModel { SearchTerm = searchTerm };
            if (string.IsNullOrWhiteSpace(searchTerm)) { return View(viewModel); }

            var resultsData = await _context.Books
                .Where(b => b.IsPublic == true && b.Title.Contains(searchTerm))
                .OrderBy(b => b.Title)
                .Select(b => new { b.Id, b.Title, b.Author, b.CoverImageObjectKey }) // Select key
                .ToListAsync();

            viewModel.Results = new List<Book>();
            foreach(var data in resultsData)
            {
                 string? coverUrl = null; // Default to null
                 if (!string.IsNullOrEmpty(data.CoverImageObjectKey)) {
                    try {
                         // --- FIX: Assign result to variable and check ---
                         string? generatedUrl = await _minioService.GetPresignedFileUrlAsync(data.CoverImageObjectKey, HomePagePresignedUrlExpirySeconds);
                         coverUrl = generatedUrl; // Assign checked (potentially null) value
                         // --- END FIX ---
                    } catch (Exception ex) {
                         _logger.LogWarning(ex, "Failed presigned URL for search result cover (Key: {Key})", data.CoverImageObjectKey);
                         // coverUrl remains null
                    }
                 }
                 viewModel.Results.Add(new Book {
                    Id = data.Id, Title = data.Title, Author = data.Author, CoverImageUrl = coverUrl
                 });
            }

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> GetSuggestions(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || term.Length < 1) {
                return Json(new List<object>());
            }
            var suggestions = await _context.Books
                .Where(b => b.IsPublic == true && b.Title.StartsWith(term))
                .OrderBy(b => b.Title)
                .Take(8)
                .Select(b => new { id = b.Id, title = b.Title })
                .ToListAsync();
            return Json(suggestions);
        }

        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}