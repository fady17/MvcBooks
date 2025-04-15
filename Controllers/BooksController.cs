using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MvcBooks.Models;
using MvcBooks.Models.Data;
using MvcBooks.Models.ViewModels;
using Microsoft.AspNetCore.Identity;

namespace MvcBooks.Controllers
{
    [Authorize]
    public class BooksController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _hostEnvironment;
        private readonly ILogger<BooksController> _logger;
        private readonly string _coverImageFolder = Path.Combine("images", "covers");
        private readonly string _bookFileFolder = Path.Combine("books");

        public BooksController(ApplicationDbContext context, IWebHostEnvironment hostEnvironment, ILogger<BooksController> logger)
        {
            _context = context;
            _hostEnvironment = hostEnvironment;
            _logger = logger;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                _logger.LogWarning("Details requested with null ID.");
                return NotFound();
            }
            var book = await _context.Books
                .Include(b => b.Categories)
                .Include(b => b.User)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (book == null)
            {
                 _logger.LogWarning("Book with ID {BookId} not found for Details.", id);
                 return NotFound();
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
            if (sourceCount == 0) { ModelState.AddModelError(string.Empty, "Please provide either an EPUB file, a PDF file, OR a Book URL."); }
            if (sourceCount > 1) { ModelState.AddModelError(string.Empty, "Please provide only ONE source: EPUB file, PDF file, OR Book URL."); }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Create Book ModelState invalid.");
                viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                return View(viewModel);
            }

            Book book = new Book
            {
                Title = viewModel.Title, Description = viewModel.Description, PublishedDate = viewModel.PublishedDate,
                Author = viewModel.Author, UserId = User.FindFirstValue(ClaimTypes.NameIdentifier), IsPublic = viewModel.IsPublic
            };

            string? uploadedCoverPath = null;
            string? uploadedBookFilePath = null;

            try
            {
                if (viewModel.CoverImage != null && viewModel.CoverImage.Length > 0)
                {
                    uploadedCoverPath = await SaveFileAsync(viewModel.CoverImage!, _coverImageFolder);
                    book.CoverImageUrl = uploadedCoverPath;
                }

                if (hasEpubFile)
                {
                    uploadedBookFilePath = await SaveFileAsync(viewModel.EpubFile!, _bookFileFolder, ensureExtension: ".epub");
                    book.EpubFilePath = uploadedBookFilePath;
                    book.EpubFileName = viewModel.EpubFile!.FileName;
                    book.PdfFilePath = null; book.PdfFileName = null; book.BookUrl = null;
                }
                else if (hasPdfFile)
                {
                    uploadedBookFilePath = await SaveFileAsync(viewModel.PdfFile!, _bookFileFolder, ensureExtension: ".pdf");
                    book.PdfFilePath = uploadedBookFilePath;
                    book.PdfFileName = viewModel.PdfFile!.FileName;
                    book.EpubFilePath = null; book.EpubFileName = null; book.BookUrl = null;
                }
                else if (hasUrl)
                {
                    book.BookUrl = viewModel.BookUrl;
                    book.EpubFilePath = null; book.EpubFileName = null;
                    book.PdfFilePath = null; book.PdfFileName = null;
                }

                await UpdateBookCategoriesAsync(book, viewModel.SelectedCategoryIds);

                _context.Add(book);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Book '{BookTitle}' (ID: {BookId}) created successfully by User {UserId}.", book.Title, book.Id, book.UserId);

                TempData["SuccessMessage"] = $"Book '{book.Title}' created successfully.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating book '{BookTitle}'.", viewModel.Title);
                DeleteFile(uploadedCoverPath);
                DeleteFile(uploadedBookFilePath);
                ModelState.AddModelError(string.Empty, "An unexpected error occurred while saving the book.");
                viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                return View(viewModel);
            }
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var book = await _context.Books.Include(b => b.Categories).FirstOrDefaultAsync(m => m.Id == id);
            if (book == null)
            {
                 _logger.LogWarning("Book with ID {BookId} not found for Edit.", id);
                 return NotFound();
            }

            if (!IsUserAuthorized(book.UserId))
            {
                _logger.LogWarning("User {UserId} attempted unauthorized edit of Book ID {BookId} owned by {OwnerId}.", User.FindFirstValue(ClaimTypes.NameIdentifier), id, book.UserId);
                TempData["ErrorMessage"] = "You are not authorized to edit this book.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }

            BookViewModel viewModel = new BookViewModel
            {
                Id = book.Id, Title = book.Title, Description = book.Description, PublishedDate = book.PublishedDate,
                Author = book.Author, ExistingCoverUrl = book.CoverImageUrl,
                ExistingEpubFileName = book.EpubFileName,
                ExistingPdfFileName = book.PdfFileName,
                ExistingBookUrl = book.BookUrl,
                SelectedCategoryIds = book.Categories.Select(c => c.Id).ToList(),
                AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync(),
                IsPublic = book.IsPublic
            };
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, BookViewModel viewModel)
        {
            if (id != viewModel.Id) return NotFound();

            bool hasNewEpubFile = viewModel.EpubFile != null && viewModel.EpubFile.Length > 0;
            bool hasNewPdfFile = viewModel.PdfFile != null && viewModel.PdfFile.Length > 0;
            bool hasNewUrl = !string.IsNullOrWhiteSpace(viewModel.BookUrl);

            int newSourceCount = (hasNewEpubFile ? 1 : 0) + (hasNewPdfFile ? 1 : 0) + (hasNewUrl ? 1 : 0);
            if (newSourceCount > 1) { ModelState.AddModelError(string.Empty, "Please provide only ONE new source: EPUB file, PDF file, OR Book URL."); }

            if (!ModelState.IsValid)
            {
                 _logger.LogWarning("Edit Book ModelState invalid for Book ID {BookId}.", id);
                 viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                 // --- FIX: Repopulate needed existing data ---
                 var currentBookData = await _context.Books.AsNoTracking().Where(b => b.Id == id).Select(b => new {b.EpubFileName, b.PdfFileName, b.BookUrl, b.CoverImageUrl, b.IsPublic }).FirstOrDefaultAsync();
                 viewModel.ExistingEpubFileName = currentBookData?.EpubFileName;
                 viewModel.ExistingPdfFileName = currentBookData?.PdfFileName;
                 viewModel.ExistingBookUrl = currentBookData?.BookUrl;
                 viewModel.ExistingCoverUrl = currentBookData?.CoverImageUrl;
                 viewModel.IsPublic = currentBookData?.IsPublic ?? true;
                 // --- END FIX ---
                 return View(viewModel);
            }

            var book = await _context.Books.Include(b => b.Categories).FirstOrDefaultAsync(b => b.Id == id);
            if (book == null)
            {
                 _logger.LogWarning("Book with ID {BookId} not found during Edit POST.", id);
                 return NotFound();
            }
            if (!IsUserAuthorized(book.UserId))
            {
                 _logger.LogWarning("User {UserId} attempted unauthorized POST edit of Book ID {BookId} owned by {OwnerId}.", User.FindFirstValue(ClaimTypes.NameIdentifier), id, book.UserId);
                 return Forbid();
            }

            string? oldCoverPath = book.CoverImageUrl;
            string? oldEpubPath = book.EpubFilePath;
            string? oldPdfPath = book.PdfFilePath;
            string? uploadedCoverPath = null;
            string? uploadedBookFilePath = null;

            try
            {
                book.Title = viewModel.Title; book.Description = viewModel.Description; book.PublishedDate = viewModel.PublishedDate;
                book.Author = viewModel.Author; book.IsPublic = viewModel.IsPublic;

                if (viewModel.CoverImage != null && viewModel.CoverImage.Length > 0)
                {
                    uploadedCoverPath = await SaveFileAsync(viewModel.CoverImage!, _coverImageFolder);
                    book.CoverImageUrl = uploadedCoverPath;
                    DeleteFile(oldCoverPath);
                }

                if (hasNewEpubFile)
                {
                    uploadedBookFilePath = await SaveFileAsync(viewModel.EpubFile!, _bookFileFolder, ensureExtension: ".epub");
                    book.EpubFilePath = uploadedBookFilePath;
                    book.EpubFileName = viewModel.EpubFile!.FileName;
                    DeleteFile(oldEpubPath); DeleteFile(oldPdfPath);
                    book.PdfFilePath = null; book.PdfFileName = null; book.BookUrl = null;
                }
                else if (hasNewPdfFile)
                {
                    uploadedBookFilePath = await SaveFileAsync(viewModel.PdfFile!, _bookFileFolder, ensureExtension: ".pdf");
                    book.PdfFilePath = uploadedBookFilePath;
                    book.PdfFileName = viewModel.PdfFile!.FileName;
                    DeleteFile(oldPdfPath); DeleteFile(oldEpubPath);
                    book.EpubFilePath = null; book.EpubFileName = null; book.BookUrl = null;
                }
                else if (hasNewUrl)
                {
                    if (book.BookUrl != viewModel.BookUrl)
                    {
                        book.BookUrl = viewModel.BookUrl;
                        DeleteFile(oldEpubPath); DeleteFile(oldPdfPath);
                        book.EpubFilePath = null; book.EpubFileName = null;
                        book.PdfFilePath = null; book.PdfFileName = null;
                    }
                }

                await UpdateBookCategoriesAsync(book, viewModel.SelectedCategoryIds);

                _context.Update(book);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Book '{BookTitle}' (ID: {BookId}) updated successfully by User {UserId}.", book.Title, book.Id, book.UserId);

                TempData["SuccessMessage"] = $"Book '{book.Title}' updated successfully.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            catch (DbUpdateConcurrencyException ex)
            {
                 _logger.LogError(ex, "Concurrency error updating Book ID {BookId}.", id);
                 // --- FIX: Use viewModel.Id for BookExists check ---
                 if (!BookExists(viewModel.Id)) return NotFound(); else throw;
                 // --- END FIX ---
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Error updating book {BookId}.", id);
                 DeleteFile(uploadedCoverPath); DeleteFile(uploadedBookFilePath);
                 ModelState.AddModelError(string.Empty, "An unexpected error occurred saving changes.");
                 viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                 // --- FIX: Repopulate needed existing data ---
                 var originalBookData = await _context.Books.AsNoTracking().Where(b => b.Id == id).Select(b => new {b.CoverImageUrl, b.EpubFileName, b.PdfFileName, b.BookUrl, b.IsPublic}).FirstOrDefaultAsync();
                 viewModel.ExistingCoverUrl = originalBookData?.CoverImageUrl;
                 viewModel.ExistingEpubFileName = originalBookData?.EpubFileName;
                 viewModel.ExistingPdfFileName = originalBookData?.PdfFileName;
                 viewModel.ExistingBookUrl = originalBookData?.BookUrl;
                 viewModel.IsPublic = originalBookData?.IsPublic ?? true;
                 // --- END FIX ---
                 return View(viewModel);
             }
        }

        public async Task<IActionResult> Delete(int? id)
        {
             if (id == null) return NotFound();
             var book = await _context.Books.Include(b => b.User).FirstOrDefaultAsync(m => m.Id == id);
             if (book == null) return NotFound();
             if (!IsUserAuthorized(book.UserId))
             {
                 _logger.LogWarning("User {UserId} attempted unauthorized GET delete of Book ID {BookId} owned by {OwnerId}.", User.FindFirstValue(ClaimTypes.NameIdentifier), id, book.UserId);
                 TempData["ErrorMessage"] = "You are not authorized to delete this book.";
                 return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
             }
             return View(book);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();
            if (!IsUserAuthorized(book.UserId))
            {
                 _logger.LogWarning("User {UserId} attempted unauthorized POST delete of Book ID {BookId} owned by {OwnerId}.", User.FindFirstValue(ClaimTypes.NameIdentifier), id, book.UserId);
                 return Forbid();
            }

            string? coverPath = book.CoverImageUrl;
            string? epubPath = book.EpubFilePath;
            string? pdfPath = book.PdfFilePath;
            string bookTitle = book.Title;

            try
            {
                _context.Books.Remove(book);
                await _context.SaveChangesAsync();

                DeleteFile(coverPath);
                DeleteFile(epubPath);
                DeleteFile(pdfPath);
                 _logger.LogInformation("Book '{BookTitle}' (ID: {BookId}) deleted successfully by User {UserId}.", bookTitle, id, book.UserId);

                TempData["SuccessMessage"] = $"Book '{bookTitle}' deleted successfully.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Error deleting book {BookId}.", id);
                 TempData["ErrorMessage"] = $"Could not delete book '{bookTitle}'.";
                 return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
        }

        [AllowAnonymous]
        public async Task<IActionResult> Read(int? id)
        {
             if (id == null) return NotFound();
             var book = await _context.Books.FindAsync(id);
             if (book == null) return NotFound();

             bool hasEpubSource = !string.IsNullOrEmpty(book.EpubFilePath) ||
                                 (!string.IsNullOrEmpty(book.BookUrl) && !book.BookUrl.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

             if (!hasEpubSource)
             {
                 if (!string.IsNullOrEmpty(book.PdfFilePath) || (!string.IsNullOrEmpty(book.BookUrl) && book.BookUrl.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
                 {
                     _logger.LogInformation("Redirecting from Read to ReadPdf for Book ID {BookId}", id);
                     return RedirectToAction(nameof(ReadPdf), new { id = id });
                 }
                 else
                 {
                     _logger.LogWarning("No EPUB/viewable content found for Book ID {BookId} in Read action.", id);
                     TempData["ErrorMessage"] = "No EPUB/viewable content found for this book.";
                 }
             }
             return View("Read", book);
        }

        [AllowAnonymous]
        public async Task<IActionResult> ReadPdf(int? id)
        {
             if (id == null) return NotFound();
             var book = await _context.Books.FindAsync(id);
             if (book == null) return NotFound();

             bool hasPdfSource = !string.IsNullOrEmpty(book.PdfFilePath);

             if (!hasPdfSource)
             {
                  if (!string.IsNullOrEmpty(book.EpubFilePath) || (!string.IsNullOrEmpty(book.BookUrl) && !book.BookUrl.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)))
                  {
                      _logger.LogInformation("Redirecting from ReadPdf to Read for Book ID {BookId}", id);
                      return RedirectToAction(nameof(Read), new { id = id });
                  }
                  else
                  {
                      _logger.LogWarning("No PDF content found for Book ID {BookId} in ReadPdf action.", id);
                      TempData["ErrorMessage"] = "No PDF content found for this book.";
                      return RedirectToAction(nameof(Details), new { id = id });
                  }
             }
             return View("ReadPdf", book);
        }

        [AllowAnonymous]
        public async Task<IActionResult> GetEpub(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null || string.IsNullOrEmpty(book.EpubFilePath))
            {
                _logger.LogWarning("GetEpub failed: EPUB file not found or not specified for Book ID {BookId}.", id);
                return NotFound("EPUB file not found or not specified for this book.");
            }

            string epubPath = GetAbsolutePath(book.EpubFilePath.TrimStart('/'));
            if (!System.IO.File.Exists(epubPath))
            {
                _logger.LogError("GetEpub failed: File not found at specified path '{EpubPath}' for Book ID {BookId}.", book.EpubFilePath, id);
                return NotFound($"EPUB file not found at specified path.");
            }
            try
            {
                return File(await System.IO.File.ReadAllBytesAsync(epubPath), "application/epub+zip");
            }
            catch (IOException ex)
            {
                 _logger.LogError(ex, "IOException reading EPUB file {EpubPath} for Book ID {BookId}.", epubPath, id);
                 return StatusCode(StatusCodes.Status500InternalServerError, "Error reading EPUB file.");
             }
        }

        [AllowAnonymous]
        public async Task<IActionResult> GetPdf(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null || string.IsNullOrEmpty(book.PdfFilePath))
            {
                _logger.LogWarning("GetPdf failed: PDF file not found or not specified for Book ID {BookId}.", id);
                return NotFound("PDF file not found or not specified for this book.");
            }

            string pdfPath = GetAbsolutePath(book.PdfFilePath.TrimStart('/'));
            if (!System.IO.File.Exists(pdfPath))
            {
                _logger.LogError("GetPdf failed: File not found at specified path '{PdfPath}' for Book ID {BookId}.", book.PdfFilePath, id);
                return NotFound($"PDF file not found at specified path.");
            }
            try
            {
                 return File(await System.IO.File.ReadAllBytesAsync(pdfPath), "application/pdf");
            }
            catch (IOException ex)
            {
                 _logger.LogError(ex, "IOException reading PDF file {PdfPath} for Book ID {BookId}.", pdfPath, id);
                 return StatusCode(StatusCodes.Status500InternalServerError, "Error reading PDF file.");
             }
        }

        private string GetAbsolutePath(string relativePath)
        {
            relativePath = relativePath.TrimStart('/', '\\');
            return Path.Combine(_hostEnvironment.WebRootPath, relativePath);
        }

        private async Task<string?> SaveFileAsync(IFormFile file, string folderPath, string? ensureExtension = null)
        {
            string targetFolder = Path.Combine(_hostEnvironment.WebRootPath, folderPath);
            Directory.CreateDirectory(targetFolder);
            string extension = Path.GetExtension(file.FileName);
            if (ensureExtension != null && !extension.Equals(ensureExtension, StringComparison.OrdinalIgnoreCase))
            {
                extension = ensureExtension;
            }
            string uniqueFileNameBase = Guid.NewGuid().ToString();
            string uniqueFileName = uniqueFileNameBase + extension;
            string absoluteFilePath = Path.Combine(targetFolder, uniqueFileName);
            using (var fileStream = new FileStream(absoluteFilePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }
            _logger.LogInformation("File saved: {RelativePath}", $"/{folderPath.Replace(Path.DirectorySeparatorChar, '/')}/{uniqueFileName}");
            return $"/{folderPath.Replace(Path.DirectorySeparatorChar, '/')}/{uniqueFileName}";
        }

        private async Task UpdateBookCategoriesAsync(Book book, List<int>? selectedCategoryIds)
        {
            book.Categories ??= new List<Category>();
            if (selectedCategoryIds == null || !selectedCategoryIds.Any()) { book.Categories.Clear(); return; }
            var selectedIdsSet = new HashSet<int>(selectedCategoryIds);
            var currentIdsSet = new HashSet<int>(book.Categories.Select(c => c.Id));
            var categoriesToRemove = book.Categories.Where(c => !selectedIdsSet.Contains(c.Id)).ToList();
            foreach (var cat in categoriesToRemove) { book.Categories.Remove(cat); }
            var idsToAdd = selectedIdsSet.Where(id => !currentIdsSet.Contains(id)).ToList();
            if (idsToAdd.Any()){
                var catsToAdd = await _context.Categories.Where(c => idsToAdd.Contains(c.Id)).ToListAsync();
                foreach (var cat in catsToAdd) { book.Categories.Add(cat); }
            }
        }

        private bool IsUserAuthorized(string? resourceOwnerUserId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            
            return (resourceOwnerUserId != null && resourceOwnerUserId == currentUserId) || User.IsInRole("Admin");
        }

        private bool BookExists(int id) => _context.Books.Any(e => e.Id == id);

        private void DeleteFile(string? relativePath)
        {
            if (!string.IsNullOrEmpty(relativePath))
            {
                try
                {
                    string absolutePath = GetAbsolutePath(relativePath);
                    if (System.IO.File.Exists(absolutePath))
                    {
                        System.IO.File.Delete(absolutePath);
                        _logger.LogInformation("File deleted: {FilePath}", absolutePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete file {RelativePath}.", relativePath);
                }
            }
        }
    }
}