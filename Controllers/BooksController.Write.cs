// File: BooksController.Write.cs (Write Actions)
using System;
using System.Collections.Generic;      
using System.Linq;
using System.Security.Claims;        
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;   
using Microsoft.Extensions.Logging;    
using MvcBooks.Models;                 
using MvcBooks.Models.ViewModels;      
using Microsoft.AspNetCore.Http;       
using MvcBooks.Common;      
using MvcBooks.Helpers;     
using Microsoft.Extensions.Caching.Memory;

namespace MvcBooks.Controllers
{
    public partial class BooksController
    {
        // GET: Books/Create
        [HttpGet("Create")] 
        public async Task<IActionResult> Create()
        {
            // *** VII. Performance Optimizations: Cache Categories ***
            if (!_memoryCache.TryGetValue(AllCategoriesCacheKey, out List<Category>? availableCategories))
            {
                _logger.LogInformation("Cache miss for {CacheKey}. Fetching categories from database.", AllCategoriesCacheKey);
                availableCategories = await _context.Categories.OrderBy(c => c.Name).AsNoTracking().ToListAsync();

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    // Keep in cache for this time, reset time if accessed.
                    // .SetSlidingExpiration(TimeSpan.FromMinutes(5))
                    // Keep in cache for a fixed duration
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(5)); // Cache for 5 minutes

                _memoryCache.Set(AllCategoriesCacheKey, availableCategories, cacheEntryOptions);
            }
            else
            {
                 _logger.LogDebug("Cache hit for {CacheKey}.", AllCategoriesCacheKey);
            }
            // *** END Caching ***

            BookViewModel viewModel = new BookViewModel
            {
                PublishedDate = DateTime.Today,
                AvailableCategories = availableCategories ?? new List<Category>(), // Use cached/fetched list
                IsPublic = true
            };
            return PartialView("_CreateBookFormPartial", viewModel);
        }

// File: BooksController.Write.cs (or BooksController.cs)

[HttpPost("Create")]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Create(BookViewModel viewModel)
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    _logger.LogInformation("POST Create started. User: {UserId}, Title: {BookTitle}", userId ?? "Anon", viewModel.Title);

    // --- Basic Validation ---
    bool hasEpubFile = viewModel.EpubFile?.Length > 0;
    bool hasPdfFile = viewModel.PdfFile?.Length > 0;
    bool hasUrl = !string.IsNullOrWhiteSpace(viewModel.BookUrl);
    int sourceCount = (hasEpubFile ? 1 : 0) + (hasPdfFile ? 1 : 0) + (hasUrl ? 1 : 0);
    if (sourceCount == 0) ModelState.AddModelError("", "Please provide EPUB, PDF, or URL.");
    if (sourceCount > 1) ModelState.AddModelError("", "Please provide only ONE source (EPUB, PDF, or URL).");
    if (userId == null) ModelState.AddModelError("", "Authentication error.");

    // --- File Type Validation ---
    ValidateFileType(viewModel.CoverImage, nameof(viewModel.CoverImage), FileValidationConstants.AllowedCoverImageMimeTypes, FileValidationConstants.AllowedCoverImageExtensions, FileValidationConstants.CoverImageFriendlyName);
    ValidateFileType(viewModel.EpubFile, nameof(viewModel.EpubFile), FileValidationConstants.AllowedEpubMimeTypes, FileValidationConstants.AllowedEpubExtensions, FileValidationConstants.EpubFileFriendlyName);
    ValidateFileType(viewModel.PdfFile, nameof(viewModel.PdfFile), FileValidationConstants.AllowedPdfMimeTypes, FileValidationConstants.AllowedPdfExtensions, FileValidationConstants.PdfFileFriendlyName);

    if (!ModelState.IsValid)
    {
        LogModelStateErrors("Initial Validation Failed", userId);
        await PrepareViewModelForError(viewModel);
        Response.StatusCode = StatusCodes.Status400BadRequest;
        return PartialView("_CreateBookFormPartial", viewModel);
    }

    _logger.LogInformation("Initial validation passed.");

    // --- Prepare Book Object ---
    Book book = new Book { /* ... Initialize required fields ... */ Title = viewModel.Title, UserId = userId!, /* etc */ };
    string? uploadedCoverKey = null;
    string? uploadedBookKey = null;
    bool coverUploadAttempted = false;
    bool bookFileUploadAttempted = false;

    try
    {
        await EnsureBucketExistsAsync();

        // --- Cover Image Upload ---
        if (viewModel.CoverImage?.Length > 0)
        {
            coverUploadAttempted = true;
            _logger.LogInformation("Attempting cover upload: {FileName}", viewModel.CoverImage.FileName);
            // *** FOCUS HERE: Debugging UploadFileAsync is key ***
            uploadedCoverKey = await _minioService.UploadFileAsync(viewModel.CoverImage, _coverImagePrefix);
            if (uploadedCoverKey != null)
            {
                _logger.LogInformation("Cover upload SUCCEEDED. Key: {Key}", uploadedCoverKey);
                book.CoverImageObjectKey = uploadedCoverKey;
                // Get URL non-critically - don't add model error if it fails now
                book.CoverImageUrl = await _minioService.GetPresignedFileUrlAsync(uploadedCoverKey, PresignedUrlSettings.LongExpirySeconds);
                if (string.IsNullOrEmpty(book.CoverImageUrl)) _logger.LogWarning("Failed to generate Cover URL post-upload. Key: {Key}", uploadedCoverKey);
            }
            else
            {
                _logger.LogError("Cover upload FAILED (MinioService returned null). FileName: {FileName}", viewModel.CoverImage.FileName);
                ModelState.AddModelError(nameof(viewModel.CoverImage), "Cover image upload to storage failed.");
            }
        }

        // --- Book File Upload ---
        IFormFile? bookFileToUpload = hasEpubFile ? viewModel.EpubFile : (hasPdfFile ? viewModel.PdfFile : null);
        string? bookSourceTypeForDb = hasEpubFile ? Constants.BookSourceMinioEpub : (hasPdfFile ? Constants.BookSourceMinioPdf : null);

        if (bookFileToUpload != null && bookSourceTypeForDb != null)
        {
            bookFileUploadAttempted = true;
            _logger.LogInformation("Attempting book file upload: {FileName}, Type: {Type}", bookFileToUpload.FileName, bookSourceTypeForDb);
             // *** FOCUS HERE: Debugging UploadFileAsync is key ***
            uploadedBookKey = await _minioService.UploadFileAsync(bookFileToUpload, _bookFilePrefix);
             if (uploadedBookKey != null)
             {
                _logger.LogInformation("Book file upload SUCCEEDED. Key: {Key}", uploadedBookKey);
                book.BookFileObjectKey = uploadedBookKey;
                book.BookSourceType = bookSourceTypeForDb;
                 // Get URL non-critically
                 book.BookUrl = await _minioService.GetPresignedFileUrlAsync(uploadedBookKey, PresignedUrlSettings.LongExpirySeconds);
                 if (string.IsNullOrEmpty(book.BookUrl)) _logger.LogWarning("Failed to generate Book URL post-upload. Key: {Key}", uploadedBookKey);
             }
             else
             {
                 _logger.LogError("Book file upload FAILED (MinioService returned null). FileName: {FileName}, Type: {Type}", bookFileToUpload.FileName, bookSourceTypeForDb);
                 string fileType = bookSourceTypeForDb == Constants.BookSourceMinioEpub ? "EPUB" : "PDF";
                 ModelState.AddModelError(fileType == "EPUB" ? nameof(viewModel.EpubFile) : nameof(viewModel.PdfFile), $"Failed to upload the {fileType} file to storage.");
             }
        }
        else if (hasUrl) // External URL
        {
            _logger.LogInformation("Using external URL: {Url}", viewModel.BookUrl);
            book.BookUrl = viewModel.BookUrl;
            book.BookSourceType = Constants.BookSourceExternal;
            book.BookFileObjectKey = null;
        }

        // --- Final Validation Check (AFTER upload attempts) ---
        if (!ModelState.IsValid)
        {
            // If validation failed *after* an upload *succeeded*, we need to delete the orphaned file.
            LogModelStateErrors("Validation Failed AFTER Upload Attempt", userId);
            if (uploadedCoverKey != null && coverUploadAttempted) { // Only delete if upload succeeded but model is now invalid
                 _logger.LogWarning("Rolling back successful Cover upload due to later validation errors. Key: {Key}", uploadedCoverKey);
                 await DeleteMinioObjectAsync(uploadedCoverKey); // Use helper
            }
            if (uploadedBookKey != null && bookFileUploadAttempted) { // Only delete if upload succeeded but model is now invalid
                _logger.LogWarning("Rolling back successful Book file upload due to later validation errors. Key: {Key}", uploadedBookKey);
                 await DeleteMinioObjectAsync(uploadedBookKey); // Use helper
            }
            await PrepareViewModelForError(viewModel);
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return PartialView("_CreateBookFormPartial", viewModel);
        }

        // --- Save to Database ---
        _logger.LogInformation("Attempting DB save. Title: {Title}, CoverKey: {CoverKey}, BookKey: {BookKey}", book.Title, book.CoverImageObjectKey ?? "N/A", book.BookFileObjectKey ?? "N/A");
        await UpdateBookCategoriesAsync(book, viewModel.SelectedCategoryIds);
        _context.Add(book);
        await _context.SaveChangesAsync(); // Keys are now committed
        _logger.LogInformation("DB save successful. BookId: {BookId}", book.Id);

        // --- Return Success Response for AJAX ---
        stopwatch.Stop();
        _logger.LogInformation("POST Create finished successfully. BookId: {BookId}, Duration: {ElapsedMs}ms", book.Id, stopwatch.ElapsedMilliseconds);
        return Json(new { success = true, message = $"Book '{book.Title}' created successfully!" }); // Use JSON success

    }
    catch (Exception ex) // Catch ALL exceptions during the process
    {
        stopwatch.Stop();
        _logger.LogError(ex, "EXCEPTION during Create Book process. Title: {Title}, Duration: {ElapsedMs}ms", viewModel.Title, stopwatch.ElapsedMilliseconds);
        // Attempt cleanup for files that might have been uploaded before the exception
         if (uploadedCoverKey != null) await DeleteMinioObjectAsync(uploadedCoverKey);
         if (uploadedBookKey != null) await DeleteMinioObjectAsync(uploadedBookKey);

        // Return Error Response for AJAX
        ModelState.AddModelError("", "An unexpected error occurred saving the book. Please check logs.");
        await PrepareViewModelForError(viewModel);
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        return PartialView("_CreateBookFormPartial", viewModel);
    }
}

// Helper to repopulate ViewModel for error redisplay
private async Task PrepareViewModelForError(BookViewModel viewModel)
{
    viewModel.AvailableCategories = await GetAvailableCategoriesAsync();
    // Add any other necessary properties for the partial view
}

// Helper to log ModelState errors
private void LogModelStateErrors(string contextMessage, string? userId)
{
     if (!ModelState.IsValid)
     {
        var errors = ModelState.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray()
        );
        _logger.LogWarning("{Context}: ModelState Invalid. UserId: {UserId}, Errors: {@Errors}", contextMessage, userId ?? "Unknown", errors);
     }
}

        // GET: Books/Edit/5
        [HttpGet("Edit")] 
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
             var book = await _context.Books.Include(b => b.Categories).AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);

            if (book == null)
            {
                _logger.LogWarning("Book not found for Edit GET. BookId: {BookId}", id);
                return NotFound();
            }
            // *** VI. Authorization Check Refactor ***
            if (!AuthorizationHelper.IsUserAuthorized(User, book.UserId))
            {
                _logger.LogWarning("Unauthorized Edit GET attempt. BookId: {BookId}, UserId: {UserId}",
                    id, User.FindFirstValue(ClaimTypes.NameIdentifier));
                TempData["ErrorMessage"] = "Not Authorized";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }

            string? currentCoverUrl = Constants.DefaultCoverImagePath;
            bool isCoverFallback = false;
            if(!string.IsNullOrEmpty(book.CoverImageObjectKey)) {
                 try {
                     currentCoverUrl = await _minioService.GetPresignedFileUrlAsync(book.CoverImageObjectKey, PresignedUrlSettings.EditFormExpirySeconds); // Use constant
                 } catch (Exception ex) {
                      _logger.LogWarning(ex, "Failed to generate existing cover URL for Edit form. BookId: {BookId}, Key: {ObjectKey}",
                          id, book.CoverImageObjectKey);
                     isCoverFallback = true; // Mark as fallback if generation fails
                 }
                 currentCoverUrl ??= Constants.DefaultCoverImagePath;
                 if (currentCoverUrl == Constants.DefaultCoverImagePath) isCoverFallback = true; // Also fallback if result is null
            } else {
                 isCoverFallback = true; // Fallback if no key exists
            }


            BookViewModel viewModel = new BookViewModel {
                 Id = book.Id, Title = book.Title, Description = book.Description, PublishedDate = book.PublishedDate,
                 Author = book.Author, IsPublic = book.IsPublic,
                 ExistingCoverUrl = currentCoverUrl,
                 IsCoverUrlFallback = isCoverFallback, // Set the flag
                 BookUrl = book.BookSourceType == Constants.BookSourceExternal ? book.BookUrl : null,
                 SelectedCategoryIds = book.Categories.Select(c => c.Id).ToList(),
                 AvailableCategories = await GetAvailableCategoriesAsync(), // Use cached method
                 ExistingEpubFileName = book.BookSourceType == Constants.BookSourceMinioEpub ? "(Existing EPUB - Upload Below to Replace)" : null,
                 ExistingPdfFileName = book.BookSourceType == Constants.BookSourceMinioPdf ? "(Existing PDF - Upload Below to Replace)" : null,
            };
            return View(viewModel);
        }

        // POST: Books/Edit/5
        [HttpPost("Edit")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, BookViewModel viewModel)
        {
             if (id != viewModel.Id) {
                 _logger.LogWarning("ID mismatch in Edit POST. RouteId: {RouteId}, ViewModelId: {ViewModelId}", id, viewModel.Id);
                 return BadRequest("ID mismatch.");
             }

             var book = await _context.Books.Include(b => b.Categories).FirstOrDefaultAsync(b => b.Id == id);
             if (book == null) {
                 _logger.LogWarning("Book not found for Edit POST. BookId: {BookId}", id);
                 return NotFound();
             }
             // *** VI. Authorization Check Refactor ***
             if (!AuthorizationHelper.IsUserAuthorized(User, book.UserId)) {
                 _logger.LogWarning("Unauthorized Edit POST attempt. BookId: {BookId}, UserId: {UserId}",
                     id, User.FindFirstValue(ClaimTypes.NameIdentifier));
                 return Forbid();
             }

             bool hasNewEpubFile = viewModel.EpubFile != null && viewModel.EpubFile.Length > 0;
             bool hasNewPdfFile = viewModel.PdfFile != null && viewModel.PdfFile.Length > 0;
             bool hasNewUrl = !string.IsNullOrWhiteSpace(viewModel.BookUrl);
             int newSourceCount = (hasNewEpubFile ? 1 : 0) + (hasNewPdfFile ? 1 : 0) + (hasNewUrl ? 1 : 0);
             if (newSourceCount > 1) { ModelState.AddModelError("", "Provide only ONE new source (EPUB, PDF, or URL) if replacing."); }

             // *** II. File Validation & Upload Improvements: Use Constants ***
             ValidateFileType(viewModel.CoverImage, nameof(viewModel.CoverImage),
                 FileValidationConstants.AllowedCoverImageMimeTypes, FileValidationConstants.AllowedCoverImageExtensions, FileValidationConstants.CoverImageFriendlyName);
             ValidateFileType(viewModel.EpubFile, nameof(viewModel.EpubFile),
                 FileValidationConstants.AllowedEpubMimeTypes, FileValidationConstants.AllowedEpubExtensions, FileValidationConstants.EpubFileFriendlyName);
             ValidateFileType(viewModel.PdfFile, nameof(viewModel.PdfFile),
                 FileValidationConstants.AllowedPdfMimeTypes, FileValidationConstants.AllowedPdfExtensions, FileValidationConstants.PdfFileFriendlyName);

            if (!ModelState.IsValid) {
                 _logger.LogWarning("Edit Book failed validation. BookId: {BookId}, UserId: {UserId}",
                     id, User.FindFirstValue(ClaimTypes.NameIdentifier));
                 await RepopulateEditViewModelOnError(viewModel, book);
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
                         string? generatedCoverUrl = await _minioService.GetPresignedFileUrlAsync(uploadedCoverKey, PresignedUrlSettings.LongExpirySeconds); // Use constant
                         if (string.IsNullOrEmpty(generatedCoverUrl)) {
                             ModelState.AddModelError(nameof(viewModel.CoverImage), "Failed to generate URL for the new cover image.");
                             _logger.LogWarning("Failed to get presigned URL for updated cover. BookId: {BookId}, NewKey: {ObjectKey}", id, uploadedCoverKey);
                             book.CoverImageUrl = null;
                             deleteOldCover = false;
                         } else {
                             book.CoverImageUrl = generatedCoverUrl;
                             deleteOldCover = !string.IsNullOrEmpty(oldCoverKey) && oldCoverKey != uploadedCoverKey;
                         }
                     } else {
                         ModelState.AddModelError(nameof(viewModel.CoverImage), "New cover image upload failed.");
                         _logger.LogWarning("Minio UploadFileAsync failed for cover image during Edit. BookId: {BookId}", id);
                    }
                 }

                 IFormFile? newBookFile = null;
                 string? newBookSourceTypeForDb = null;
                 if (hasNewEpubFile) { newBookFile = viewModel.EpubFile; newBookSourceTypeForDb = Constants.BookSourceMinioEpub; }
                 else if (hasNewPdfFile) { newBookFile = viewModel.PdfFile; newBookSourceTypeForDb = Constants.BookSourceMinioPdf; }

                if (newBookFile != null && newBookSourceTypeForDb != null) {
                     uploadedBookKey = await _minioService.UploadFileAsync(newBookFile!, _bookFilePrefix);
                     if (uploadedBookKey != null) {
                         book.BookFileObjectKey = uploadedBookKey;
                         book.BookSourceType = newBookSourceTypeForDb;
                         string? generatedBookUrl = await _minioService.GetPresignedFileUrlAsync(uploadedBookKey, PresignedUrlSettings.LongExpirySeconds); // Use constant
                         if (string.IsNullOrEmpty(generatedBookUrl)) {
                             ModelState.AddModelError("", $"Failed to generate access URL for the new {newBookSourceTypeForDb.Split('_').LastOrDefault() ?? "file"}.");
                             _logger.LogWarning("Failed to get presigned URL for updated book file. BookId: {BookId}, NewKey: {ObjectKey}, Type: {SourceType}",
                                 id, uploadedBookKey, newBookSourceTypeForDb);
                             book.BookUrl = null;
                             deleteOldBookFile = false;
                         } else {
                             book.BookUrl = generatedBookUrl;
                             deleteOldBookFile = !string.IsNullOrEmpty(oldBookFileKey);
                         }
                     } else {
                         string fileType = newBookSourceTypeForDb.Split('_').LastOrDefault()?.ToLowerInvariant() ?? "file";
                         ModelState.AddModelError(fileType == "epub" ? nameof(viewModel.EpubFile) : nameof(viewModel.PdfFile), $"Failed to upload the new {fileType}.");
                         _logger.LogWarning("Minio UploadFileAsync failed for book file during Edit. BookId: {BookId}, Type: {SourceType}",
                             id, newBookSourceTypeForDb);
                     }
                 }
                 else if (hasNewUrl) {
                     if (book.BookUrl != viewModel.BookUrl || book.BookSourceType != Constants.BookSourceExternal) {
                         book.BookUrl = viewModel.BookUrl; // Assumes valid URL
                         book.BookSourceType = Constants.BookSourceExternal;
                         deleteOldBookFile = !string.IsNullOrEmpty(oldBookFileKey);
                         book.BookFileObjectKey = null;
                         _logger.LogInformation("Book source changed to External URL. BookId: {BookId}", id);
                     }
                 }

                 if (!ModelState.IsValid) {
                      _logger.LogWarning("Edit Book failed validation after file processing. BookId: {BookId}, UserId: {UserId}",
                          id, User.FindFirstValue(ClaimTypes.NameIdentifier));
                      if(uploadedCoverKey != null) await DeleteMinioObjectAsync(uploadedCoverKey);
                      if(uploadedBookKey != null) await DeleteMinioObjectAsync(uploadedBookKey);
                       await RepopulateEditViewModelOnError(viewModel, book);
                       return View(viewModel);
                 }

                await UpdateBookCategoriesAsync(book, viewModel.SelectedCategoryIds);
                 _context.Entry(book).State = EntityState.Modified;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Attempting post-save cleanup for Edit. BookId: {BookId}. DeleteOldCover: {DeleteOldCover}, DeleteOldBookFile: {DeleteOldBookFile}",
                    id, deleteOldCover, deleteOldBookFile);
                if (deleteOldCover && !string.IsNullOrEmpty(oldCoverKey)) {
                    await DeleteMinioObjectAsync(oldCoverKey);
                }
                if (deleteOldBookFile && !string.IsNullOrEmpty(oldBookFileKey)) {
                     await DeleteMinioObjectAsync(oldBookFileKey);
                }

                _logger.LogInformation("Book updated successfully. BookId: {BookId}, Title: {BookTitle}, UserId: {UserId}",
                    id, book.Title, User.FindFirstValue(ClaimTypes.NameIdentifier));
                TempData["SuccessMessage"] = $"Book '{book.Title}' updated.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            catch (DbUpdateConcurrencyException ex) {
                 _logger.LogError(ex, "Concurrency error updating book. BookId: {BookId}, UserId: {UserId}",
                     id, User.FindFirstValue(ClaimTypes.NameIdentifier));
                 ModelState.AddModelError("", "This book was modified by another user. Please reload and try again.");
                 if(uploadedCoverKey != null) await DeleteMinioObjectAsync(uploadedCoverKey);
                 if(uploadedBookKey != null) await DeleteMinioObjectAsync(uploadedBookKey);
            }
            catch (Exception ex) {
                 _logger.LogError(ex, "Error updating book. BookId: {BookId}, UserId: {UserId}",
                     id, User.FindFirstValue(ClaimTypes.NameIdentifier));
                 if(uploadedCoverKey != null) await DeleteMinioObjectAsync(uploadedCoverKey);
                 if(uploadedBookKey != null) await DeleteMinioObjectAsync(uploadedBookKey);
                 ModelState.AddModelError("", "An unexpected error occurred while updating the book.");
            }

             _logger.LogWarning("Returning Edit view due to error. BookId: {BookId}", id);
             await RepopulateEditViewModelOnError(viewModel, book);
             return View(viewModel);
        }

        // GET: Books/Delete/5
        [HttpGet("Delete")]
        public async Task<IActionResult> Delete(int? id)
        {
             if (id == null) return NotFound();
             var bookData = await _context.Books
                 .Where(m => m.Id == id)
                 .Select(b => new { b.Id, b.Title, b.Author, b.UserId })
                 .FirstOrDefaultAsync();

             if (bookData == null)
             {
                 _logger.LogWarning("Book not found for Delete GET. BookId: {BookId}", id);
                 return NotFound();
             }
             // Use refactored AuthorizationHelper
             if (!AuthorizationHelper.IsUserAuthorized(User, bookData.UserId))
             {
                 _logger.LogWarning("Unauthorized Delete GET attempt. BookId: {BookId}, UserId: {UserId}",
                     id, User.FindFirstValue(ClaimTypes.NameIdentifier));
                 TempData["ErrorMessage"] = "Not Authorized";
                 return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
             }

             ViewData["BookTitle"] = bookData.Title ?? "Untitled Book";
             ViewData["BookAuthor"] = bookData.Author ?? "N/A";
             return View(new Book { Id = bookData.Id, Title = bookData.Title ?? "Book" }); // Minimal model for form helper
        }

        // POST: Books/Delete/5
        [HttpPost("Delete"), ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var bookToDelete = await _context.Books
                .Where(b => b.Id == id)
                .Select(b => new { b.Id, b.Title, b.UserId, b.CoverImageObjectKey, b.BookFileObjectKey })
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (bookToDelete == null) {
                _logger.LogWarning("Book not found for Delete POST (already deleted?). BookId: {BookId}", id);
                // Still redirect with success message if goal is achieved (book is gone)
                TempData["SuccessMessage"] = "Book already deleted.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            // Use refactored AuthorizationHelper
            if (!AuthorizationHelper.IsUserAuthorized(User, bookToDelete.UserId)) {
                _logger.LogWarning("Unauthorized Delete POST attempt. BookId: {BookId}, UserId: {UserId}",
                    id, User.FindFirstValue(ClaimTypes.NameIdentifier));
                return Forbid();
            }

            string? coverKey = bookToDelete.CoverImageObjectKey;
            string? bookFileKey = bookToDelete.BookFileObjectKey;
            string bookTitle = bookToDelete.Title ?? "Untitled Book";
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            try {
                 int deletedRows = await _context.Books.Where(b => b.Id == id).ExecuteDeleteAsync();

                 if (deletedRows == 0) {
                    // Log if DB delete didn't affect rows but we expected it to
                     _logger.LogWarning("ExecuteDeleteAsync reported 0 rows affected for Delete POST. BookId: {BookId}", id);
                 }

                // Attempt MinIO cleanup regardless of deletedRows count
                _logger.LogInformation("Attempting post-delete cleanup for BookId: {BookId}", id);
                if (!string.IsNullOrEmpty(coverKey)) await DeleteMinioObjectAsync(coverKey);
                if (!string.IsNullOrEmpty(bookFileKey)) await DeleteMinioObjectAsync(bookFileKey);

                _logger.LogInformation("Book deleted successfully (or cleanup attempted). BookId: {BookId}, Title: {BookTitle}, DeletedByUserId: {UserId}",
                    id, bookTitle, currentUserId);
                TempData["SuccessMessage"] = $"Book '{bookTitle}' deleted.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            catch (Exception ex) {
                 _logger.LogError(ex, "Error during book deletion process. BookId: {BookId}, Title: {BookTitle}, UserId: {UserId}",
                     id, bookTitle, currentUserId);
                 TempData["ErrorMessage"] = $"Could not delete book '{bookTitle}'. An error occurred.";
                 // Redirect even on error, state might be inconsistent
                 return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
        }

        // Helper method to get categories, using cache
        [HttpGet("GetAvailableCategorie")]
        private async Task<List<Category>> GetAvailableCategoriesAsync()
        {
            if (!_memoryCache.TryGetValue(AllCategoriesCacheKey, out List<Category>? availableCategories))
            {
                 _logger.LogInformation("Cache miss for {CacheKey} in GetAvailableCategoriesAsync. Fetching from DB.", AllCategoriesCacheKey);
                availableCategories = await _context.Categories.OrderBy(c => c.Name).AsNoTracking().ToListAsync();
                var cacheEntryOptions = new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(5));
                _memoryCache.Set(AllCategoriesCacheKey, availableCategories, cacheEntryOptions);
            } else {
                 _logger.LogDebug("Cache hit for {CacheKey} in GetAvailableCategoriesAsync.", AllCategoriesCacheKey);
            }
            return availableCategories ?? new List<Category>();
        }
    }
}