// File: BooksController.cs (Main Definition - Corrected Constructor)
using System;
using System.Collections.Generic; // Required if using Lists etc. in helpers/actions
using System.IO;                 // Required if using Path etc. in helpers/actions
using System.Linq;               // Required if using LINQ etc. in helpers/actions
using System.Security.Claims;    // Required if using User.FindFirstValue etc. in helpers/actions
using System.Threading;          // Required if using CancellationToken etc. in helpers/actions
using System.Threading.Tasks;    // Required for async methods
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http; // Required if using StatusCodes etc. in actions
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore; // Required if using DbContext directly here or helpers
using Microsoft.Extensions.Logging;
using MvcBooks.Models;           // Required if using Models directly here or helpers
using MvcBooks.Models.Data;
using MvcBooks.Models.ViewModels; // Required if using ViewModels directly here or helpers
using Microsoft.AspNetCore.Identity; // Required if using Identity types directly here or helpers
using MvcBooks.Services;
using MvcBooks.Common;
using MvcBooks.Helpers;
using Microsoft.Extensions.Caching.Memory;

namespace MvcBooks.Controllers
{
    public class ExtractPageRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] // Ensure page is positive
    public int PageNumber { get; set; }
}
    [Authorize] // Applies to all actions unless overridden by [AllowAnonymous]
    [Route("Books")]
        public partial class BooksController : Controller 
    {
        // --- Dependencies and Configuration ---
        private readonly ApplicationDbContext _context;
        private readonly ILogger<BooksController> _logger;
        private readonly MinioService _minioService;
        private readonly IMemoryCache _memoryCache;
        private readonly IBookContentService _bookContentService; // Service for text extraction

        private readonly string _coverImagePrefix = "covers";
        private readonly string _bookFilePrefix = "books";
        // --- End Dependencies and Configuration ---

        // --- Cache Keys ---
        private const string AllCategoriesCacheKey = "AllCategories";
        // --- End Cache Keys ---

        // NOTE: Presigned URL expiry constants are now in PresignedUrlSettings.cs
        // Example usage reference (actual values from PresignedUrlSettings):
        // private const int LongPresignedUrlExpirySeconds = PresignedUrlSettings.LongExpirySeconds;
        // private const int EditFormPresignedUrlExpirySeconds = PresignedUrlSettings.EditFormExpirySeconds;


        // --- Constructor ---
        public BooksController(
            ApplicationDbContext context,
            ILogger<BooksController> logger,
            MinioService minioService,
            IMemoryCache memoryCache, // <<< REMOVED extra parenthesis here
            IBookContentService bookContentService) // Added parameter
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _minioService = minioService ?? throw new ArgumentNullException(nameof(minioService));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _bookContentService = bookContentService ?? throw new ArgumentNullException(nameof(bookContentService)); // Assign injected service and add null check
        }
        // --- End Constructor ---

        // NOTE: Actions (like Create, Edit, Delete, Details, ReadPdf, GetPdf, ExtractPageText)
        //       and Helper methods (like GetAvailableCategoriesAsync, ValidateFileType,
        //       UpdateBookCategoriesAsync, DeleteMinioObjectAsync, EnsureBucketExistsAsync,
        //       RepopulateEditViewModelOnError) should be in other partial files
        //       (e.g., BooksController.Read.cs, BooksController.Write.cs, BooksController.Helpers.cs).

        // NOTE: Allowed File Type constants are now in FileValidationConstants.cs
    }
}