// File: BooksController.Read.cs (Read Actions)

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using MvcBooks.Models;          

using System.Security.Claims;
using MvcBooks.Common;
using MvcBooks.Helpers;


namespace MvcBooks.Controllers

{
    public partial class BooksController 
    {
        [AllowAnonymous]
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var book = await _context.Books
                .Include(b => b.Categories)
                .Include(b => b.User)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (book == null) { _logger.LogWarning("Book ID {BookId} not found for Details.", id); return NotFound(); }

            try
            {
                if (!string.IsNullOrEmpty(book.CoverImageObjectKey))
                {
                    book.CoverImageUrl = await _minioService.GetPresignedFileUrlAsync(book.CoverImageObjectKey, PresignedUrlSettings.LongExpirySeconds);
                    book.CoverImageUrl ??= Constants.DefaultCoverImagePath;
                }
                else
                {
                    book.CoverImageUrl = Constants.DefaultCoverImagePath;
                }

                if ((book.BookSourceType == Constants.BookSourceMinioEpub || book.BookSourceType == Constants.BookSourceMinioPdf)
                    && !string.IsNullOrEmpty(book.BookFileObjectKey))
                {
                     // Use constant from PresignedUrlSettings
                     book.BookUrl = await _minioService.GetPresignedFileUrlAsync(book.BookFileObjectKey, PresignedUrlSettings.LongExpirySeconds);
                     if (string.IsNullOrEmpty(book.BookUrl))
                     {
                        // Structured logging
                        _logger.LogWarning("Failed to regenerate BookUrl for Details view. BookId: {BookId}, Key: {ObjectKey}", id, book.BookFileObjectKey);
                     }
                }
            }
            catch(Exception ex)
            {
                 _logger.LogError(ex, "Error regenerating presigned URL for Details view. BookId: {BookId}", id);
                 book.CoverImageUrl ??= Constants.DefaultCoverImagePath;
            }

            return View(book);
        }

        [AllowAnonymous] // Or add authorization if needed for private EPUBs
        [HttpGet("GetEpub/{id:int}")] // Define explicit route
        [ResponseCache(Duration = 60 * 60, Location = ResponseCacheLocation.Client)] // Optional: Cache EPUB content
        public async Task<IActionResult> GetEpub(int id)
        {
            _logger.LogInformation("GetEpub action started for BookId: {BookId}", id);

            // 1. Get Book Info
            var bookData = await _context.Books
               .Where(b => b.Id == id && b.BookSourceType == Constants.BookSourceMinioEpub) // Ensure it IS an EPUB
               .Select(b => new { b.BookFileObjectKey, b.IsPublic, b.UserId, b.Title })
               .AsNoTracking()
               .FirstOrDefaultAsync();

            // Check if found, has key, and is correct type
            if (bookData == null || string.IsNullOrEmpty(bookData.BookFileObjectKey)) {
                _logger.LogWarning("GetEpub failed: EPUB source not found or key missing. BookId: {BookId}", id);
                return NotFound("EPUB file not found or not specified for this book.");
            }

            // 2. Authorization Check (Optional if Read action already covers it, but safer)
             if (!bookData.IsPublic && !AuthorizationHelper.IsUserAuthorized(User, bookData.UserId))
             {
                 _logger.LogWarning("Unauthorized access attempt via GetEpub for private book. BookId: {BookId}, Title: {BookTitle}, UserId: {UserId}",
                     id, bookData.Title, User.FindFirstValue(ClaimTypes.NameIdentifier));
                 return Forbid();
             }

            // 3. Get Stream from MinIO
            _logger.LogInformation("Attempting MinioService.GetFileStreamAsync for EPUB Key: {ObjectKey}", bookData.BookFileObjectKey);
            Stream? stream = null;
            try {
               stream = await _minioService.GetFileStreamAsync(bookData.BookFileObjectKey);
            } catch (Exception ex) {
                _logger.LogError(ex, "MinioService GetFileStreamAsync failed for EPUB. Key: {ObjectKey}, BookId: {BookId}", bookData.BookFileObjectKey, id);
                return StatusCode(StatusCodes.Status500InternalServerError, "Could not retrieve EPUB file from storage.");
            }

            if (stream == null) {
                 _logger.LogError("GetEpub failed: MinIO returned null stream for EPUB Key: {ObjectKey}, BookId: {BookId}", bookData.BookFileObjectKey, id);
                 return NotFound("EPUB file could not be retrieved from storage.");
            }

            // 4. Return File Result with Correct EPUB MIME Type
            _logger.LogInformation("Serving EPUB file stream. BookId: {BookId}, Key: {ObjectKey}", id, bookData.BookFileObjectKey);
            string downloadFileName = $"{bookData.Title ?? $"Book_{id}"}.epub"; // Suggest a filename

            // Return the stream with the correct MIME type for EPUB
            return File(stream, "application/epub+zip", downloadFileName);
        }

        [AllowAnonymous]
        [HttpGet("Read/{id:int}")]
        public async Task<IActionResult> Read(int? id) // Handles EPUB/External
        {
             if (id == null) return NotFound();
             var book = await _context.Books
                 .Where(b => b.Id == id)
                 .Select(b => new { b.Id, b.Title, b.BookUrl, b.BookSourceType, b.BookFileObjectKey, b.IsPublic, b.UserId })
                 .AsNoTracking()
                 .FirstOrDefaultAsync();

             if (book == null) { _logger.LogWarning("Book ID {BookId} not found for Read.", id); return NotFound(); }

             if (!book.IsPublic && !AuthorizationHelper.IsUserAuthorized(User, book.UserId))
             {
                 _logger.LogWarning("Unauthorized access attempt to private book Read action. BookId: {BookId}, UserId: {UserId}",
                     id, User.FindFirstValue(ClaimTypes.NameIdentifier));
                 TempData["ErrorMessage"] = "You do not have permission to view this book.";
                 return RedirectToAction(nameof(HomeController.Index), "Home");
             }

             bool canHandle = book.BookSourceType == Constants.BookSourceMinioEpub || book.BookSourceType == Constants.BookSourceExternal;
             if (!canHandle) {
                if (book.BookSourceType == Constants.BookSourceMinioPdf) {
                    _logger.LogInformation("Redirecting from Read to ReadPdf. BookId: {BookId}", id);
                    return RedirectToAction(nameof(ReadPdf), new { id = id });
                }
                 _logger.LogWarning("Cannot display book in Read action due to unsupported source type. BookId: {BookId}, SourceType: {SourceType}",
                     id, book.BookSourceType);
                 TempData["ErrorMessage"] = "This book cannot be read online directly (Unsupported format or missing source).";
                 return RedirectToAction(nameof(Details), new { id = id });
             }

             string? accessUrl = null;

             if (book.BookSourceType == Constants.BookSourceExternal) {
                 accessUrl = book.BookUrl; // Assume already validated URL
             }
             else if (book.BookSourceType == Constants.BookSourceMinioEpub) {
                 if (!string.IsNullOrEmpty(book.BookFileObjectKey)) {
                     try {
                         // Use constant from PresignedUrlSettings
                         accessUrl = await _minioService.GetPresignedFileUrlAsync(book.BookFileObjectKey, PresignedUrlSettings.LongExpirySeconds);
                     } catch (Exception ex) {
                         _logger.LogError(ex, "Failed to generate presigned URL for EPUB reading. BookId: {BookId}, Key: {ObjectKey}",
                             id, book.BookFileObjectKey);
                     }
                 } else {
                     _logger.LogWarning("MINIO_EPUB source type but BookFileObjectKey is missing. BookId: {BookId}", id);
                 }
             }

             if (string.IsNullOrEmpty(accessUrl)) {
                 _logger.LogError("Failed to obtain a valid access URL for Read action. BookId: {BookId}, SourceType: {SourceType}",
                      id, book.BookSourceType);
                 TempData["ErrorMessage"] = "Could not retrieve the book's content source URL.";
                  return RedirectToAction(nameof(Details), new { id = id });
             }

             var readViewModel = new Book {
                 Id = book.Id,
                 Title = book.Title ?? "Untitled Book",
                 BookUrl = accessUrl
             };
             return View("Read", readViewModel);
        }

        [AllowAnonymous]
        [HttpGet("ReadPdf/{id:int}")]
        public async Task<IActionResult> ReadPdf(int? id) // Handles PDF
        {
             if (id == null) return NotFound();
             var book = await _context.Books
                 .Where(b => b.Id == id)
                 .Select(b => new { b.Id, b.Title, b.BookSourceType, b.BookFileObjectKey, b.IsPublic, b.UserId })
                 .AsNoTracking()
                 .FirstOrDefaultAsync();

             if (book == null)
             {
                 _logger.LogWarning("Book not found for ReadPdf action. BookId: {BookId}", id);
                 return NotFound();
             }

             // Use refactored AuthorizationHelper
             if (!book.IsPublic && !AuthorizationHelper.IsUserAuthorized(User, book.UserId))
             {
                  _logger.LogWarning("Unauthorized access attempt to private book ReadPdf action. BookId: {BookId}, UserId: {UserId}",
                      id, User.FindFirstValue(ClaimTypes.NameIdentifier));
                 TempData["ErrorMessage"] = "You do not have permission to view this book.";
                 return RedirectToAction(nameof(HomeController.Index), "Home");
             }

             bool hasPdfSource = book.BookSourceType == Constants.BookSourceMinioPdf;
             if (!hasPdfSource) {
                 if(book.BookSourceType == Constants.BookSourceMinioEpub || book.BookSourceType == Constants.BookSourceExternal) {
                      _logger.LogInformation("Redirecting from ReadPdf to Read. BookId: {BookId}", id);
                      return RedirectToAction(nameof(Read), new { id = id });
                 }
                  _logger.LogWarning("Cannot display book in ReadPdf action due to incorrect source type. BookId: {BookId}, SourceType: {SourceType}",
                      id, book.BookSourceType);
                  TempData["ErrorMessage"] = "This book is not available as a PDF.";
                 return RedirectToAction(nameof(Details), new { id = id });
             }

             if(string.IsNullOrEmpty(book.BookFileObjectKey)) {
                 _logger.LogError("MINIO_PDF source type but BookFileObjectKey is missing. BookId: {BookId}", id);
                 TempData["ErrorMessage"] = "The PDF file source is missing for this book.";
                 return RedirectToAction(nameof(Details), new { id = id });
             }

             string? accessUrl = null;
             try {
                 // Use constant from PresignedUrlSettings
                 accessUrl = await _minioService.GetPresignedFileUrlAsync(book.BookFileObjectKey, PresignedUrlSettings.LongExpirySeconds);
             } catch (Exception ex) {
                 _logger.LogError(ex, "Failed to generate presigned URL for PDF reading. BookId: {BookId}, Key: {ObjectKey}",
                     id, book.BookFileObjectKey);
             }

             if(string.IsNullOrEmpty(accessUrl)) {
                 TempData["ErrorMessage"] = "Could not retrieve the PDF file URL.";
                 return RedirectToAction(nameof(Details), new { id = id });
             }

              var pdfViewModel = new Book {
                 Id = book.Id,
                 Title = book.Title ?? "Untitled Book",
                 BookUrl = accessUrl
             };
             return View("ReadPdf", pdfViewModel);
        }

        [AllowAnonymous]
        [HttpGet("GetPdf/{id:int}")]
        public async Task<IActionResult> GetPdf(int id)
        {
             var bookData = await _context.Books
                .Where(b => b.Id == id && b.BookSourceType == Constants.BookSourceMinioPdf)
                .Select(b => new { b.BookFileObjectKey, b.IsPublic, b.UserId, b.Title })
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (bookData == null || string.IsNullOrEmpty(bookData.BookFileObjectKey)) {
                 _logger.LogWarning("GetPdf failed: PDF source not found or key missing. BookId: {BookId}", id);
                 return NotFound("PDF file not found or not specified for this book.");
             }

             // Use refactored AuthorizationHelper
             if (!bookData.IsPublic && !AuthorizationHelper.IsUserAuthorized(User, bookData.UserId))
             {
                 _logger.LogWarning("Unauthorized access attempt via GetPdf for private book. BookId: {BookId}, Title: {BookTitle}, UserId: {UserId}",
                     id, bookData.Title, User.FindFirstValue(ClaimTypes.NameIdentifier));
                 return Forbid();
             }

             _logger.LogInformation("Attempting to get PDF stream. BookId: {BookId}, Title: {BookTitle}, Key: {ObjectKey}",
                 id, bookData.Title, bookData.BookFileObjectKey);
             System.IO.Stream? stream = null;
             try {
                stream = await _minioService.GetFileStreamAsync(bookData.BookFileObjectKey);
             } catch (Exception ex) {
                 _logger.LogError(ex, "MinioService GetFileStreamAsync failed. Key: {ObjectKey}, BookId: {BookId}, Title: {BookTitle}",
                      bookData.BookFileObjectKey, id, bookData.Title);
                 return StatusCode(StatusCodes.Status500InternalServerError, "Could not retrieve PDF file from storage.");
             }

             if (stream == null) {
                  _logger.LogError("GetPdf failed: MinIO returned null stream. Key: {ObjectKey}, BookId: {BookId}, Title: {BookTitle}",
                       bookData.BookFileObjectKey, id, bookData.Title);
                  return NotFound("PDF file could not be retrieved from storage.");
             }

             _logger.LogInformation("Serving PDF stream. BookId: {BookId}, Title: {BookTitle}, Key: {ObjectKey}",
                 id, bookData.Title, bookData.BookFileObjectKey);
             // Consider adding Content-Disposition header for better browser handling/filename
             // var contentDisposition = new System.Net.Mime.ContentDisposition
             // {
             //     FileName = $"{bookData.Title ?? "book"}.pdf",
             //     Inline = true, // False = prompt for download
             // };
             // Response.Headers.Add("Content-Disposition", contentDisposition.ToString());
             return File(stream, "application/pdf");
        }
 
    [HttpPost("{id:int}/extract-page-text")] // Route: e.g., /Books/123/extract-page-text
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(string), StatusCodes.Status500InternalServerError)]
    // Add [ValidateAntiForgeryToken] if your fetch POST includes the token
    public async Task<IActionResult> ExtractPageText(int id, [FromBody] ExtractPageRequest request)
    {
        // Check if the request body is valid based on annotations (e.g., Required, Range)
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState); // Return validation errors
        }

        _logger.LogInformation("ExtractPageText received request. BookId: {BookId}, Page: {PageNumber}", id, request.PageNumber);

        // 1. Get Book Info & Authorize
        var bookInfo = await _context.Books
            .Where(b => b.Id == id)
            .Select(b => new { b.UserId, b.BookFileObjectKey, b.Title, b.BookSourceType })
            .AsNoTracking()
            .FirstOrDefaultAsync();

        if (bookInfo == null) return NotFound("Book record not found.");
        if (!AuthorizationHelper.IsUserAuthorized(User, bookInfo.UserId)) return Forbid();

        // 2. Validate Book Source
        bool isExtractable = bookInfo.BookSourceType == Constants.BookSourceMinioPdf ||
                            bookInfo.BookSourceType == Constants.BookSourceMinioEpub; // Add other types MuPDFCore supports if needed

        if (string.IsNullOrWhiteSpace(bookInfo.BookFileObjectKey) || !isExtractable)
        {
            return BadRequest("Book source is not a stored PDF or EPUB suitable for text extraction.");
        }

        // 3. Call Service
        string? extractedText = await _bookContentService.ExtractTextFromPageAsync(
            bookInfo.BookFileObjectKey,
            request.PageNumber,
            bookInfo.Title); // Pass title as hint

        // 4. Handle Service Response
        if (extractedText == null)
        {
            // Service indicated a failure (e.g., couldn't get Minio stream)
            _logger.LogError("Text extraction service returned null. BookId: {BookId}, Page: {Page}", id, request.PageNumber);
            return StatusCode(StatusCodes.Status500InternalServerError, "Service failed to extract text. Check server logs for details.");
        }
        if (extractedText.StartsWith("[Error:")) // Check for specific error messages from service
        {
            // Contains specific error like invalid page or processing error
            _logger.LogWarning("Text extraction service returned error message. BookId: {BookId}, Page: {Page}, Error: {ServiceError}", id, request.PageNumber, extractedText);
            // Return 400 Bad Request with the specific error message from the service
            return BadRequest(extractedText);
        }

        // 5. Return Success
        _logger.LogInformation("Successfully extracted text. BookId: {BookId}, Page: {Page}", id, request.PageNumber);
        return Content(extractedText, "text/plain; charset=utf-8"); // Return text result
    }
            
        }
    }