// File: HomeController.cs (Refactored - Sequential Fix)
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MvcBooks.Models;
using System.Diagnostics;
using MvcBooks.Models.Data;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
// using Microsoft.AspNetCore.Identity; // Not currently used directly here
using MvcBooks.Models.ViewModels;
using System.Security.Claims;
using System.Collections.Generic;
using MvcBooks.Services;
using MvcBooks.Common;
using Microsoft.Extensions.Caching.Memory;
using System;

namespace MvcBooks.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _context; // Keep scoped context
        private readonly IMemoryCache _memoryCache;
        private readonly MinioService _minioService;

        private const string FilterableCategoriesCacheKey = "FilterableCategories";

        public HomeController(
            ILogger<HomeController> logger,
            ApplicationDbContext context,
            IMemoryCache memoryCache,
            MinioService minioService)
        {
            _logger = logger;
            _context = context; // Keep injected scoped context
            _memoryCache = memoryCache;
            _minioService = minioService;
        }

    public async Task<IActionResult> Index(List<int>? selectedCategoryIds) // Parameter name matches form input
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        _logger.LogInformation("Starting Index action. UserId: {UserId}, SelectedCategoryIds: {SelectedIds}",
            userId ?? "Anonymous", selectedCategoryIds != null ? string.Join(',', selectedCategoryIds) : "None");

        // Fetch filterable categories (for the dropdown) using cache
        var filterableCategories = await FetchFilterableCategoriesAsync();

        // Fetch public categories based on selection (for the main page content)
        var publicCategories = await FetchPublicCategoriesWithCoversAsync(selectedCategoryIds);

        // Fetch user history (only shown if no filters are applied)
        List<Book> userHistoryBooks = new List<Book>();
        bool filtersApplied = selectedCategoryIds != null && selectedCategoryIds.Any();
        if (!filtersApplied) // Only fetch if no filters applied
        {
            userHistoryBooks = await FetchUserHistoryWithCoversAsync(userId);
        }


        var viewModel = new HomeViewModel
        {
            UserHistoryBooks = userHistoryBooks,
            FilterableCategories = filterableCategories, // Keep this for potential JS use if needed later, but not for initial population
            PublicCategories = publicCategories,
            SelectedCategoryIds = selectedCategoryIds ?? new List<int>()
        };

        // --- Pass data needed for the _Layout dropdown via ViewBag ---
        ViewBag.LayoutFilterCategories = filterableCategories; // Pass the list for dropdown options
        ViewBag.LayoutSelectedCategoryIds = viewModel.SelectedCategoryIds; // Pass the currently selected IDs

        return View(viewModel);
    }


    [HttpGet]
public async Task<IActionResult> Search(string searchTerm, [FromQuery] List<int>? selectedCategoryIds)
{
    var currentSelectedIds = selectedCategoryIds ?? new List<int>(); // Ensure non-null list

    // Pass search term and selected IDs to the view model
    var viewModel = new SearchViewModel {
         SearchTerm = searchTerm ?? string.Empty,
         SelectedCategoryIds = currentSelectedIds // Assign the non-null list
         // Results list initialized empty by default
    };

    // If no search term, return immediately with empty results
    if (string.IsNullOrWhiteSpace(searchTerm))
    {
        _logger.LogInformation("Search action called with empty search term.");
        // Optionally fetch categories for display even on empty search page?
        // viewModel.AvailableCategories = await FetchFilterableCategoriesAsync(); // Example
        return View(viewModel);
    }

    _logger.LogInformation("Executing search. Term: {SearchTerm}, CategoryIds: {CategoryIds}",
        searchTerm, currentSelectedIds.Any() ? string.Join(',', currentSelectedIds) : "None");

    // Base query for public books containing the search term
    var query = _context.Books
        .Where(b => b.IsPublic == true && b.Title.Contains(searchTerm)) // Case sensitivity depends on DB
        .AsNoTracking();

    // --- Apply Category Filter ---
    if (currentSelectedIds.Any())
    {
        query = query.Where(b => b.Categories.Any(c => currentSelectedIds.Contains(c.Id)));
        _logger.LogInformation("Applying category filter to search results. CategoryIds: {CategoryIds}", string.Join(',', currentSelectedIds));
    }
    // --- End Category Filter ---

    // Select necessary data for results + URL generation
    var resultsData = await query
        .OrderBy(b => b.Title)
        .Select(b => new { // Project into an anonymous type first
            b.Id,
            b.Title, // Select required Title
            b.Author,
            b.CoverImageObjectKey
        })
        .ToListAsync();

    // Prepare list and tasks for URL generation
    viewModel.Results = new List<Book>();
    var urlTasks = new List<Task>();

    foreach (var data in resultsData)
    {
        // *** FIX CS9035: Initialize required properties ***
        var book = new Book {
             Id = data.Id,
             Title = data.Title, // Initialize required Title
             Author = data.Author,
             CoverImageObjectKey = data.CoverImageObjectKey, // Store key if needed later
             CoverImageUrl = Constants.DefaultCoverImagePath, // Set default fallback
             // Initialize other non-nullable properties if Book model has them
             PublishedDate = default, // Example if PublishedDate is required
             IsPublic = true          // Example if IsPublic is required
             // Add other required properties here...
        };
        viewModel.Results.Add(book); // Add the fully initialized book

        // If a cover key exists, create a task to generate its URL
        if (!string.IsNullOrEmpty(data.CoverImageObjectKey))
        {
            // Pass the 'book' instance to the helper so its CoverImageUrl can be updated
            urlTasks.Add(GenerateAndUpdateCoverUrl(book, data.CoverImageObjectKey, PresignedUrlSettings.HomePageExpirySeconds));
        }
    }

    // Wait for all concurrent URL generation tasks to complete
    if (urlTasks.Any())
    {
        _logger.LogDebug("Waiting for {UrlCount} presigned URL generation tasks for search results.", urlTasks.Count);
        await Task.WhenAll(urlTasks);
    }

    _logger.LogInformation("Search completed. Term: {SearchTerm}, FoundResults: {ResultCount}", searchTerm, viewModel.Results.Count);

    // Optionally add categories to viewmodel for display on results page
    // viewModel.AvailableCategories = await FetchFilterableCategoriesAsync();

    return View(viewModel); // Return the view with results and filter info
}
        [HttpGet]
        public async Task<IActionResult> GetSuggestions(string term, [FromQuery] List<int>? categoryIds) // Added categoryIds parameter
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(term) || term.Length < 1)
            {
                return Json(new List<object>());
            }

            _logger.LogDebug("Getting suggestions for Term: {SearchTerm}, CategoryIds: {CategoryIds}",
                term, categoryIds != null ? string.Join(',', categoryIds) : "None");

            var query = _context.Books
                .Where(b => b.IsPublic == true && b.Title.StartsWith(term))
                .AsNoTracking();

            // --- Apply Category Filter ---
            if (categoryIds != null && categoryIds.Any())
            {
                // Filter books that belong to *any* of the selected categories
                query = query.Where(b => b.Categories.Any(c => categoryIds.Contains(c.Id)));
            }
            // --- End Category Filter ---

            var suggestions = await query
                .OrderBy(b => b.Title)
                .Take(8)
                .Select(b => new { id = b.Id, title = b.Title }) // Keep projection simple
                .ToListAsync();

            return Json(suggestions);
        }



        public IActionResult Privacy() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

        // --- Private Helper Methods (No change needed here for Option 1) ---

        private async Task<List<Book>> FetchUserHistoryWithCoversAsync(string? userId)
        {
            if (userId == null) return new List<Book>();

             _logger.LogDebug("Fetching user history for UserId: {UserId}", userId);
            // Use the injected scoped context
            var historyBooks = await _context.Books
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.Id)
                .Select(b => new Book {
                    Id = b.Id, Title = b.Title,
                    CoverImageObjectKey = b.CoverImageObjectKey,
                    CoverImageUrl = Constants.DefaultCoverImagePath
                })
                .Take(15)
                .AsNoTracking()
                .ToListAsync();
             _logger.LogDebug("Fetched {BookCount} history books for UserId: {UserId}", historyBooks.Count, userId);


            // MinIO calls can potentially run concurrently if MinioService is thread-safe
            var urlTasks = historyBooks
                .Where(b => !string.IsNullOrEmpty(b.CoverImageObjectKey))
                .Select(b => GenerateAndUpdateCoverUrl(b, b.CoverImageObjectKey!, PresignedUrlSettings.HomePageExpirySeconds))
                .ToList();

            if (urlTasks.Any())
            {
                 _logger.LogDebug("Generating {UrlCount} cover URLs for user history.", urlTasks.Count);
                 await Task.WhenAll(urlTasks);
            }

            return historyBooks;
        }

        private async Task<List<Category>> FetchFilterableCategoriesAsync()
        {
            // Cache check remains the same
            if (!_memoryCache.TryGetValue(FilterableCategoriesCacheKey, out List<Category>? filterableCategories))
            {
                _logger.LogInformation("Cache miss for {CacheKey}. Fetching filterable categories from database.", FilterableCategoriesCacheKey);
                 // Use the injected scoped context
                filterableCategories = await _context.Categories
                    .Where(c => c.Books.Any(b => b.IsPublic == true))
                    .OrderBy(c => c.Name)
                    .Select(c => new Category { Id = c.Id, Name = c.Name })
                    .AsNoTracking()
                    .ToListAsync();

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(10));

                _memoryCache.Set(FilterableCategoriesCacheKey, filterableCategories, cacheEntryOptions);
                 _logger.LogDebug("Fetched and cached {CategoryCount} filterable categories.", filterableCategories?.Count ?? 0);
            }
            else
            {
                 _logger.LogDebug("Cache hit for {CacheKey}.", FilterableCategoriesCacheKey);
            }
            return filterableCategories ?? new List<Category>();
        }

        private async Task<List<Category>> FetchPublicCategoriesWithCoversAsync(List<int>? selectedCategoryIds)
        {
             _logger.LogDebug("Fetching public categories. SelectedIds: {SelectedCategoryIds}", selectedCategoryIds != null ? string.Join(',', selectedCategoryIds) : "None");
             // Use the injected scoped context
             IQueryable<Category> categoriesQuery = _context.Categories.AsQueryable();

            if (selectedCategoryIds != null && selectedCategoryIds.Any()) {
                categoriesQuery = categoriesQuery.Where(c => selectedCategoryIds.Contains(c.Id));
            }

            var publicCategories = await categoriesQuery
                .Where(c => c.Books.Any(b => b.IsPublic == true))
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new Category {
                     Id = c.Id, Name = c.Name, DisplayOrder = c.DisplayOrder,
                     Books = c.Books.Where(b => b.IsPublic == true)
                                      .OrderByDescending(b => b.Id)
                                      .Select(b => new Book {
                                         Id = b.Id, Title = b.Title,
                                         CoverImageObjectKey = b.CoverImageObjectKey,
                                         CoverImageUrl = Constants.DefaultCoverImagePath
                                      })
                                      .Take(10)
                                      .ToList() // Subquery executed here
                })
                .AsNoTracking()
                .ToListAsync();
             _logger.LogDebug("Fetched {CategoryCount} public categories.", publicCategories.Count);


            // MinIO calls can potentially run concurrently
             var allBooks = publicCategories.SelectMany(c => c.Books).ToList();
             var urlTasks = allBooks
                .Where(b => !string.IsNullOrEmpty(b.CoverImageObjectKey))
                .Select(b => GenerateAndUpdateCoverUrl(b, b.CoverImageObjectKey!, PresignedUrlSettings.HomePageExpirySeconds))
                .ToList();

            if (urlTasks.Any())
            {
                 _logger.LogDebug("Generating {UrlCount} cover URLs for public categories.", urlTasks.Count);
                 await Task.WhenAll(urlTasks);
            }

            var finalCategories = publicCategories.Where(c => c.Books.Any()).ToList();
             _logger.LogDebug("Returning {CategoryCount} non-empty public categories.", finalCategories.Count);
            return finalCategories;
        }

        private async Task GenerateAndUpdateCoverUrl(Book book, string objectKey, int expirySeconds)
        {
            try
            {
                string? coverUrl = await _minioService.GetPresignedFileUrlAsync(objectKey, expirySeconds);
                if (!string.IsNullOrEmpty(coverUrl))
                {
                     book.CoverImageUrl = coverUrl;
                }
                 else
                 {
                    _logger.LogWarning("MinIO returned null/empty presigned URL. BookId: {BookId}, Key: {ObjectKey}", book.Id, objectKey);
                    // Fallback already set, do nothing extra
                 }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate presigned URL. BookId: {BookId}, Key: {ObjectKey}", book.Id, objectKey);
                 // Fallback already set, do nothing extra
            }
        }
    }
}