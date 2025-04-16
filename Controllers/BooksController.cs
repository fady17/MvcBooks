using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MvcBooks.Models;
using MvcBooks.Models.Data;
using MvcBooks.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using MvcBooks.Services; // Reference MinioService

namespace MvcBooks.Controllers
{
    [Authorize]
    public class BooksController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<BooksController> _logger;
        private readonly MinioService _minioService;
        private readonly string _coverImagePrefix = "covers";
        private readonly string _bookFilePrefix = "books";
        private const int LongPresignedUrlExpirySeconds = 60 * 60 * 24 * 7; // 7 days
        private const int EditFormPresignedUrlExpirySeconds = 60 * 15; // 15 mins

        public BooksController(ApplicationDbContext context,
                               ILogger<BooksController> logger,
                               MinioService minioService)
        {
            _context = context;
            _logger = logger;
            _minioService = minioService;
        }

        // Helper - assumes MinioService handles bucket creation/check
        private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken = default)
        {
            // If MinioService needs explicit check, call it here.
            // await _minioService.EnsureBucketExistsAsync(cancellationToken);
            await Task.CompletedTask; // Assuming it's handled internally or on first use
        }

        [AllowAnonymous]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var book = await _context.Books
                .Include(b => b.Categories)
                .Include(b => b.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (book == null) { _logger.LogWarning("Book ID {BookId} not found for Details.", id); return NotFound(); }

            // Regenerate potentially expired presigned URLs just before sending to view
            try
            {
                if (!string.IsNullOrEmpty(book.CoverImageObjectKey))
                {
                    book.CoverImageUrl = await _minioService.GetPresignedFileUrlAsync(book.CoverImageObjectKey, EditFormPresignedUrlExpirySeconds); // Use shorter expiry for details view maybe? Or long? Let's use long.
                    // book.CoverImageUrl = await _minioService.GetPresignedFileUrlAsync(book.CoverImageObjectKey, LongPresignedUrlExpirySeconds);
                    book.CoverImageUrl ??= "/images/placeholder-cover.png"; // Fallback if generation fails
                }
                if ((book.BookSourceType == "MINIO_EPUB" || book.BookSourceType == "MINIO_PDF") && !string.IsNullOrEmpty(book.BookFileObjectKey))
                {
                     book.BookUrl = await _minioService.GetPresignedFileUrlAsync(book.BookFileObjectKey, LongPresignedUrlExpirySeconds);
                      if(string.IsNullOrEmpty(book.BookUrl)) _logger.LogWarning("Failed to regenerate BookUrl for Details view, Book ID {BookId}", id);
                }
            }
            catch(Exception ex)
            {
                 _logger.LogError(ex, "Error regenerating presigned URL for Details view for Book ID {BookId}", id);
            }

            return View(book);
        }

        public async Task<IActionResult> Create()
        {
            BookViewModel viewModel = new BookViewModel
            {
                PublishedDate = DateTime.Today,
                AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync(),
                IsPublic = true
            };
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BookViewModel viewModel)
        {
            bool hasEpubFile = viewModel.EpubFile != null && viewModel.EpubFile.Length > 0;
            bool hasPdfFile = viewModel.PdfFile != null && viewModel.PdfFile.Length > 0;
            bool hasUrl = !string.IsNullOrWhiteSpace(viewModel.BookUrl);

            int sourceCount = (hasEpubFile ? 1 : 0) + (hasPdfFile ? 1 : 0) + (hasUrl ? 1 : 0);
            if (sourceCount == 0) { ModelState.AddModelError("", "Please provide EPUB, PDF, or URL."); }
            if (sourceCount > 1) { ModelState.AddModelError("", "Please provide only ONE source."); }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) { ModelState.AddModelError("", "Authentication error."); }

            if (!ModelState.IsValid) {
                _logger.LogWarning("Create Book ModelState invalid (Initial).");
                viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                return View(viewModel);
            }

            Book book = new Book {
                Title = viewModel.Title, Description = viewModel.Description, PublishedDate = viewModel.PublishedDate,
                Author = viewModel.Author, UserId = userId!, IsPublic = viewModel.IsPublic
            };

            string? uploadedCoverKey = null;
            string? uploadedBookKey = null;

            await EnsureBucketExistsAsync();

            try {
                if (viewModel.CoverImage != null && viewModel.CoverImage.Length > 0) {
                    uploadedCoverKey = await _minioService.UploadFileAsync(viewModel.CoverImage!, _coverImagePrefix);
                    if (uploadedCoverKey != null) {
                        book.CoverImageObjectKey = uploadedCoverKey;
                        book.CoverImageUrl = await _minioService.GetPresignedFileUrlAsync(uploadedCoverKey, LongPresignedUrlExpirySeconds);
                        if (string.IsNullOrEmpty(book.CoverImageUrl)) ModelState.AddModelError("CoverImage", "Failed to generate cover image URL.");
                    } else { ModelState.AddModelError("CoverImage", "Cover image upload failed."); }
                }

                IFormFile? bookFileToUpload = null;
                string? bookSourceTypeForDb = null;
                if (hasEpubFile) { bookFileToUpload = viewModel.EpubFile; bookSourceTypeForDb = "MINIO_EPUB"; }
                else if (hasPdfFile) { bookFileToUpload = viewModel.PdfFile; bookSourceTypeForDb = "MINIO_PDF"; }

                if (bookFileToUpload != null && bookSourceTypeForDb != null) {
                     uploadedBookKey = await _minioService.UploadFileAsync(bookFileToUpload!, _bookFilePrefix);
                     if (uploadedBookKey != null) {
                        book.BookFileObjectKey = uploadedBookKey;
                        book.BookSourceType = bookSourceTypeForDb;
                        book.BookUrl = await _minioService.GetPresignedFileUrlAsync(uploadedBookKey, LongPresignedUrlExpirySeconds);
                        if (string.IsNullOrEmpty(book.BookUrl)) ModelState.AddModelError("", "Failed to generate book access URL.");
                     } else {
                        string fileType = bookSourceTypeForDb.Split('_').ElementAtOrDefault(1) ?? "file";
                        ModelState.AddModelError("", $"Failed to upload the {fileType}.");
                     }
                } else if (hasUrl) {
                    book.BookUrl = viewModel.BookUrl;
                    book.BookSourceType = "EXTERNAL";
                    book.BookFileObjectKey = null;
                    // Keep existing Cover key/url if only providing external book url
                    // book.CoverImageObjectKey = null; // Maybe don't clear these?
                    // book.CoverImageUrl = null;
                }

                 if (!ModelState.IsValid) {
                     _logger.LogWarning("Create Book ModelState invalid after file processing.");
                     if(uploadedCoverKey != null) await _minioService.DeleteFileAsync(uploadedCoverKey);
                     if(uploadedBookKey != null) await _minioService.DeleteFileAsync(uploadedBookKey);
                     viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                     return View(viewModel);
                 }

                await UpdateBookCategoriesAsync(book, viewModel.SelectedCategoryIds);
                _context.Add(book);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Book '{Title}' created.", book.Title);
                TempData["SuccessMessage"] = $"Book '{book.Title}' created.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error creating book '{Title}'.", viewModel.Title);
                if(uploadedCoverKey != null) await _minioService.DeleteFileAsync(uploadedCoverKey);
                if(uploadedBookKey != null) await _minioService.DeleteFileAsync(uploadedBookKey);
                ModelState.AddModelError("", "Unexpected error saving book.");
                viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                return View(viewModel);
            }
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
             var book = await _context.Books.Include(b => b.Categories).AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
            if (book == null) { return NotFound(); }
            if (!IsUserAuthorized(book.UserId)) { TempData["ErrorMessage"] = "Not Authorized"; return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" }); }

            string? currentCoverUrl = null;
            if(!string.IsNullOrEmpty(book.CoverImageObjectKey)) {
                 currentCoverUrl = await _minioService.GetPresignedFileUrlAsync(book.CoverImageObjectKey, EditFormPresignedUrlExpirySeconds);
            }
            currentCoverUrl ??= "/images/placeholder-cover.png";

            BookViewModel viewModel = new BookViewModel {
                 Id = book.Id, Title = book.Title, Description = book.Description, PublishedDate = book.PublishedDate,
                 Author = book.Author, IsPublic = book.IsPublic,
                 ExistingCoverUrl = currentCoverUrl, // Use fresh temporary URL
                 BookUrl = book.BookSourceType == "EXTERNAL" ? book.BookUrl : null,
                 ExistingBookUrl = book.BookSourceType == "EXTERNAL" ? book.BookUrl : null,
                 SelectedCategoryIds = book.Categories.Select(c => c.Id).ToList(),
                 AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync(),
                 ExistingEpubFileName = book.BookSourceType == "MINIO_EPUB" ? "(Uploaded EPUB)" : null,
                 ExistingPdfFileName = book.BookSourceType == "MINIO_PDF" ? "(Uploaded PDF)" : null,
            };
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, BookViewModel viewModel)
        {
             if (id != viewModel.Id) return NotFound();

             var book = await _context.Books.Include(b => b.Categories).FirstOrDefaultAsync(b => b.Id == id);
             if (book == null) { return NotFound(); }
             if (!IsUserAuthorized(book.UserId)) { return Forbid(); }

             bool hasNewEpubFile = viewModel.EpubFile != null && viewModel.EpubFile.Length > 0;
             bool hasNewPdfFile = viewModel.PdfFile != null && viewModel.PdfFile.Length > 0;
             bool hasNewUrl = !string.IsNullOrWhiteSpace(viewModel.BookUrl);
             int newSourceCount = (hasNewEpubFile ? 1 : 0) + (hasNewPdfFile ? 1 : 0) + (hasNewUrl ? 1 : 0);
             if (newSourceCount > 1) { ModelState.AddModelError("", "Provide only ONE new source."); }

            if (!ModelState.IsValid) {
                 viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                 viewModel.ExistingCoverUrl = !string.IsNullOrEmpty(book.CoverImageObjectKey) ? await _minioService.GetPresignedFileUrlAsync(book.CoverImageObjectKey, EditFormPresignedUrlExpirySeconds) ?? book.CoverImageUrl ?? "/images/placeholder-cover.png" : "/images/placeholder-cover.png";
                 viewModel.ExistingBookUrl = book.BookSourceType == "EXTERNAL" ? book.BookUrl : null;
                 viewModel.ExistingEpubFileName = book.BookSourceType == "MINIO_EPUB" ? "(Uploaded EPUB)" : null;
                 viewModel.ExistingPdfFileName = book.BookSourceType == "MINIO_PDF" ? "(Uploaded PDF)" : null;
                 viewModel.IsPublic = book.IsPublic;
                 return View(viewModel);
            }

            string? oldCoverKey = book.CoverImageObjectKey;
            string? oldBookFileKey = book.BookFileObjectKey;
            string? uploadedCoverKey = null;
            string? uploadedBookKey = null;
            bool deleteOldCover = false;
            bool deleteOldBookFile = false;

            try
            {
                book.Title = viewModel.Title; book.Description = viewModel.Description; book.PublishedDate = viewModel.PublishedDate;
                book.Author = viewModel.Author; book.IsPublic = viewModel.IsPublic;

                 if (viewModel.CoverImage != null && viewModel.CoverImage.Length > 0) {
                     uploadedCoverKey = await _minioService.UploadFileAsync(viewModel.CoverImage!, _coverImagePrefix);
                     if (uploadedCoverKey != null) {
                         book.CoverImageObjectKey = uploadedCoverKey;
                         book.CoverImageUrl = await _minioService.GetPresignedFileUrlAsync(uploadedCoverKey, LongPresignedUrlExpirySeconds);
                         if (string.IsNullOrEmpty(book.CoverImageUrl)) ModelState.AddModelError("CoverImage", "Failed to generate cover URL.");
                         else deleteOldCover = !string.IsNullOrEmpty(oldCoverKey) && oldCoverKey != uploadedCoverKey;
                     } else { ModelState.AddModelError("CoverImage", "Cover update failed."); }
                 }

                 IFormFile? newBookFile = null;
                 string? newBookSourceTypeForDb = null;
                 if (hasNewEpubFile) { newBookFile = viewModel.EpubFile; newBookSourceTypeForDb = "MINIO_EPUB"; }
                 else if (hasNewPdfFile) { newBookFile = viewModel.PdfFile; newBookSourceTypeForDb = "MINIO_PDF"; }

                if (newBookFile != null && newBookSourceTypeForDb != null) {
                     uploadedBookKey = await _minioService.UploadFileAsync(newBookFile!, _bookFilePrefix);
                     if (uploadedBookKey != null) {
                         book.BookFileObjectKey = uploadedBookKey;
                         book.BookSourceType = newBookSourceTypeForDb;
                         book.BookUrl = await _minioService.GetPresignedFileUrlAsync(uploadedBookKey, LongPresignedUrlExpirySeconds);
                         if(string.IsNullOrEmpty(book.BookUrl)) { ModelState.AddModelError("", "Failed to generate book URL."); }
                         else { deleteOldBookFile = !string.IsNullOrEmpty(oldBookFileKey); }
                     } else {
                         string fileType = newBookSourceTypeForDb.Split('_').ElementAtOrDefault(1) ?? "file";
                         ModelState.AddModelError("", $"Failed to upload new {fileType}.");
                     }
                 } else if (hasNewUrl) {
                     if (book.BookUrl != viewModel.BookUrl || book.BookSourceType != "EXTERNAL") {
                         book.BookUrl = viewModel.BookUrl;
                         book.BookSourceType = "EXTERNAL";
                         deleteOldBookFile = !string.IsNullOrEmpty(oldBookFileKey);
                         book.BookFileObjectKey = null;
                     }
                 }

                 if (!ModelState.IsValid) { throw new InvalidOperationException("File processing failed during edit."); }

                await UpdateBookCategoriesAsync(book, viewModel.SelectedCategoryIds);
                await _context.SaveChangesAsync();

                // Delete old files using KEYS after successful save
                if (deleteOldCover) await _minioService.DeleteFileAsync(oldCoverKey!);
                if (deleteOldBookFile) await _minioService.DeleteFileAsync(oldBookFileKey!);

                TempData["SuccessMessage"] = $"Book '{book.Title}' updated.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            catch (DbUpdateConcurrencyException ex) {
                 _logger.LogError(ex, "Concurrency error updating Book ID {BookId}.", id);
                 if (!BookExists(id)) return NotFound();
                 ModelState.AddModelError("", "Concurrency conflict. Reload and try again.");
            }
            catch (Exception ex) {
                 _logger.LogError(ex, "Error updating book {BookId}.", id);
                 if(uploadedCoverKey != null) await _minioService.DeleteFileAsync(uploadedCoverKey);
                 if(uploadedBookKey != null) await _minioService.DeleteFileAsync(uploadedBookKey);
                 if (!ModelState.ContainsKey("")) ModelState.AddModelError("", "Unexpected error.");
            }

             // Common error return path
             viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
             viewModel.ExistingCoverUrl = !string.IsNullOrEmpty(book.CoverImageObjectKey) ? await _minioService.GetPresignedFileUrlAsync(book.CoverImageObjectKey, EditFormPresignedUrlExpirySeconds) ?? book.CoverImageUrl ?? "/images/placeholder-cover.png" : "/images/placeholder-cover.png";
             viewModel.ExistingBookUrl = book.BookSourceType == "EXTERNAL" ? book.BookUrl : null;
             viewModel.ExistingEpubFileName = book.BookSourceType == "MINIO_EPUB" ? "(Uploaded EPUB)" : null;
             viewModel.ExistingPdfFileName = book.BookSourceType == "MINIO_PDF" ? "(Uploaded PDF)" : null;
             viewModel.IsPublic = book.IsPublic;
             return View(viewModel);
        }

        public async Task<IActionResult> Delete(int? id)
        {
             if (id == null) return NotFound();
             var bookData = await _context.Books.Select(b => new { b.Id, b.Title, b.Author, b.UserId }).FirstOrDefaultAsync(m => m.Id == id);
             if (bookData == null) return NotFound();
             if (!IsUserAuthorized(bookData.UserId)) { TempData["ErrorMessage"] = "Not Authorized"; return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" }); }
             ViewData["BookTitle"] = bookData.Title; ViewData["BookAuthor"] = bookData.Author ?? "N/A";
             return View(new Book { Id = bookData.Id, Title = bookData.Title ?? "Book" });
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var bookToDelete = await _context.Books.Select(b => new { b.Id, b.Title, b.UserId, b.CoverImageObjectKey, b.BookFileObjectKey }).AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
            if (bookToDelete == null) return NotFound();
            if (!IsUserAuthorized(bookToDelete.UserId)) return Forbid();

            string? coverKey = bookToDelete.CoverImageObjectKey;
            string? bookFileKey = bookToDelete.BookFileObjectKey;
            string bookTitle = bookToDelete.Title;

            try {
                 int deletedRows = await _context.Books.Where(b => b.Id == id).ExecuteDeleteAsync();
                 if (deletedRows == 0) { return NotFound(); }

                if (!string.IsNullOrEmpty(coverKey)) await _minioService.DeleteFileAsync(coverKey);
                if (!string.IsNullOrEmpty(bookFileKey)) await _minioService.DeleteFileAsync(bookFileKey);

                _logger.LogInformation("Book '{Title}' deleted.", bookTitle);
                TempData["SuccessMessage"] = $"Book '{bookTitle}' deleted.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            catch (Exception ex) {
                 _logger.LogError(ex, "Error deleting book {BookId}.", id);
                 TempData["ErrorMessage"] = $"Could not delete book '{bookTitle}'.";
                 return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
        }

        [AllowAnonymous]
        public async Task<IActionResult> Read(int? id) // Handles EPUB/External
        {
             if (id == null) return NotFound();
             var book = await _context.Books.Select(b => new { b.Id, b.Title, b.BookUrl, b.BookSourceType, b.BookFileObjectKey }).AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
             if (book == null) return NotFound();

             bool canHandle = book.BookSourceType == "MINIO_EPUB" || book.BookSourceType == "EXTERNAL";
             if (!canHandle) {
                if(book.BookSourceType == "MINIO_PDF") return RedirectToAction(nameof(ReadPdf), new { id = id });
                TempData["ErrorMessage"] = "No viewable EPUB or link found."; return View("Read", new Book { Id = book.Id, Title = book.Title ?? "Book" });
             }

             string? accessUrl = book.BookUrl;
             // Regenerate MinIO URL if it's MinIO (covers potential expiry)
             if(book.BookSourceType == "MINIO_EPUB" && !string.IsNullOrEmpty(book.BookFileObjectKey)) {
                accessUrl = await _minioService.GetPresignedFileUrlAsync(book.BookFileObjectKey, LongPresignedUrlExpirySeconds);
             }

             if(string.IsNullOrEmpty(accessUrl)) { TempData["ErrorMessage"] = "Source URL missing or invalid."; return View("Read", new Book { Id = book.Id, Title = book.Title ?? "Book" }); }
             return View("Read", new Book { Id = book.Id, Title = book.Title ?? "Book", BookUrl = accessUrl });
        }

        [AllowAnonymous]
        public async Task<IActionResult> ReadPdf(int? id) // Handles PDF
        {
             if (id == null) return NotFound();
             var book = await _context.Books.Select(b => new { b.Id, b.Title, b.BookUrl, b.BookSourceType, b.BookFileObjectKey }).AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
             if (book == null) return NotFound();

             bool hasPdfSource = book.BookSourceType == "MINIO_PDF";
             if (!hasPdfSource) {
                 if(book.BookSourceType == "MINIO_EPUB" || book.BookSourceType == "EXTERNAL") return RedirectToAction(nameof(Read), new { id = id });
                 TempData["ErrorMessage"] = "No PDF source."; return RedirectToAction(nameof(Details), new { id = id });
             }

             string? accessUrl = book.BookUrl;
             // Regenerate MinIO URL if it's MinIO
             if(!string.IsNullOrEmpty(book.BookFileObjectKey)) {
                 accessUrl = await _minioService.GetPresignedFileUrlAsync(book.BookFileObjectKey, LongPresignedUrlExpirySeconds);
             }

             if(string.IsNullOrEmpty(accessUrl)) { TempData["ErrorMessage"] = "PDF Source URL missing or invalid."; return RedirectToAction(nameof(Details), new { id = id }); }
             // Pass only ID/Title and the URL to the view
             return View("ReadPdf", new Book { Id = book.Id, Title = book.Title ?? "Book", BookUrl = accessUrl });
        }

                [AllowAnonymous]
        public async Task<IActionResult> GetPdf(int id)
        {
             // 1. Retrieve the Key needed for this PDF book
             var bookData = await _context.Books
                .Where(b => b.Id == id && b.BookSourceType == "MINIO_PDF") // Checks ID and Type
                .Select(b => new { b.BookFileObjectKey, b.IsPublic, b.UserId }) // Select only key and auth fields
                .AsNoTracking()
                .FirstOrDefaultAsync();

            // --- Possibility 1: Book doesn't exist OR is not MINIO_PDF ---
            if (bookData == null || string.IsNullOrEmpty(bookData.BookFileObjectKey)) {
                 _logger.LogWarning("GetPdf failed: PDF object key not found or book type mismatch for Book ID {BookId}.", id);
                 return NotFound("PDF file not found or not specified for this book."); // Returns 404
             }

             // Optional Auth Check
             // if (!bookData.IsPublic && !IsUserAuthorized(bookData.UserId)) return Forbid(); // Could return 403

             // --- Possibility 2: MinIO Service fails to get the stream ---
             Stream? stream = await _minioService.GetFileStreamAsync(bookData.BookFileObjectKey);
             if (stream == null) {
                  _logger.LogError("GetPdf failed: Could not retrieve stream for key '{ObjectKey}' for Book ID {BookId}.", bookData.BookFileObjectKey, id);
                  return NotFound("PDF file stream could not be retrieved."); // Returns 404
             }

             _logger.LogInformation("Serving PDF stream for key {ObjectKey}", bookData.BookFileObjectKey);
             return File(stream, "application/pdf"); // Returns 200 OK with file data
        }

        // --- Helper Methods ---
        private async Task UpdateBookCategoriesAsync(Book book, List<int>? selectedCategoryIds) {
             if (book.Id > 0 && !_context.Entry(book).Collection(b => b.Categories).IsLoaded) { await _context.Entry(book).Collection(b => b.Categories).LoadAsync(); }
             book.Categories ??= new List<Category>();
             if (selectedCategoryIds == null || !selectedCategoryIds.Any()) { book.Categories.Clear(); return; }
             var selectedIdsSet = new HashSet<int>(selectedCategoryIds);
             var currentIdsSet = new HashSet<int>(book.Categories.Select(c => c.Id));
             var categoriesToRemove = book.Categories.Where(c => !selectedIdsSet.Contains(c.Id)).ToList();
             foreach (var cat in categoriesToRemove) { book.Categories.Remove(cat); }
             var idsToAdd = selectedIdsSet.Where(id => !currentIdsSet.Contains(id)).ToList();
             if (idsToAdd.Any()){ var catsToAdd = await _context.Categories.Where(c => idsToAdd.Contains(c.Id)).ToListAsync(); foreach (var cat in catsToAdd) { book.Categories.Add(cat); } }
         }
        private bool IsUserAuthorized(string? resourceOwnerUserId) { var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier); return (resourceOwnerUserId != null && resourceOwnerUserId == currentUserId) || User.IsInRole("Admin"); }
        private bool BookExists(int id) => _context.Books.Any(e => e.Id == id);
        private async Task DeleteMinioObjectAsync(string? objectKey) { if (!string.IsNullOrEmpty(objectKey)) { bool deleted = await _minioService.DeleteFileAsync(objectKey); if(!deleted) _logger.LogWarning("Delete failed for key '{Key}'.", objectKey); else _logger.LogInformation("Deleted MinIO object: {Key}", objectKey); } }

    }
}