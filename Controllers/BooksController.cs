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
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
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
        private readonly string _coverImageFolder = Path.Combine("images", "covers");
        private readonly string _epubFileFolder = Path.Combine("books");

        public BooksController(ApplicationDbContext context, IWebHostEnvironment hostEnvironment)
        {
            _context = context;
            _hostEnvironment = hostEnvironment;
        }

        // --- Index() action removed ---

        // GET: Books/Details/5
        [AllowAnonymous]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var book = await _context.Books
                .Include(b => b.Categories)
                .Include(b => b.User)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (book == null) return NotFound();
            return View(book);
        }

        // GET: Books/Create
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

        // POST: Books/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BookViewModel viewModel)
        {
            bool hasFile = viewModel.EpubFile != null && viewModel.EpubFile.Length > 0;
            bool hasUrl = !string.IsNullOrWhiteSpace(viewModel.BookUrl);

            if (!hasFile && !hasUrl) { ModelState.AddModelError("", "Either an EPUB file must be uploaded or a Book URL must be provided."); }
            if (hasFile && hasUrl) { ModelState.AddModelError("", "Please provide either an EPUB file upload OR a Book URL, not both."); }

            if (!ModelState.IsValid)
            {
                viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                return View(viewModel);
            }

            Book book = new Book
            {
                Title = viewModel.Title,
                Description = viewModel.Description,
                PublishedDate = viewModel.PublishedDate,
                Author = viewModel.Author,
                UserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                IsPublic = viewModel.IsPublic
            };

            string? uploadedCoverPath = null;
            string? uploadedEpubPath = null;
            string? uploadedEpubName = null;

            try
            {
                if (viewModel.CoverImage != null && viewModel.CoverImage.Length > 0)
                {
                    // *** FIX: Use null-forgiving operator '!' ***
                    uploadedCoverPath = await SaveFileAsync(viewModel.CoverImage!, _coverImageFolder);
                    book.CoverImageUrl = uploadedCoverPath;
                }

                if (hasFile)
                {
                    // *** FIX: Use null-forgiving operator '!' ***
                    uploadedEpubPath = await SaveFileAsync(viewModel.EpubFile!, _epubFileFolder, ensureExtension: ".epub");
                    // *** FIX: Use null-forgiving operator '!' ***
                    uploadedEpubName = viewModel.EpubFile!.FileName;
                    book.EpubFilePath = uploadedEpubPath;
                    book.EpubFileName = uploadedEpubName;
                    book.BookUrl = null;
                }
                else if (hasUrl)
                {
                    book.BookUrl = viewModel.BookUrl;
                    book.EpubFilePath = null;
                    book.EpubFileName = null;
                }

                await UpdateBookCategoriesAsync(book, viewModel.SelectedCategoryIds);

                _context.Add(book);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Book '{book.Title}' created successfully.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating book: {ex.Message}");
                DeleteFile(uploadedCoverPath);
                DeleteFile(uploadedEpubPath);
                ModelState.AddModelError("", "An unexpected error occurred while saving the book. Please try again.");
                viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                return View(viewModel);
            }
        }

        // GET: Books/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var book = await _context.Books
                .Include(b => b.Categories)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (book == null) return NotFound();
            if (!IsUserAuthorized(book.UserId))
            {
                TempData["ErrorMessage"] = "You are not authorized to edit this book.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            BookViewModel viewModel = new BookViewModel { /* ... mapping ... */
                Id = book.Id, Title = book.Title, Description = book.Description, PublishedDate = book.PublishedDate, Author = book.Author, ExistingCoverUrl = book.CoverImageUrl, ExistingEpubFileName = book.EpubFileName, ExistingBookUrl = book.BookUrl, SelectedCategoryIds = book.Categories.Select(c => c.Id).ToList(), AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync(), IsPublic = book.IsPublic
            };
            return View(viewModel);
        }

        // POST: Books/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, BookViewModel viewModel)
        {
            if (id != viewModel.Id) return NotFound();

            bool hasNewFile = viewModel.EpubFile != null && viewModel.EpubFile.Length > 0;
            bool hasNewUrl = !string.IsNullOrWhiteSpace(viewModel.BookUrl);

            if (hasNewFile && hasNewUrl) { ModelState.AddModelError("", "Please provide either an EPUB file upload OR a Book URL, not both."); }

            if (!ModelState.IsValid)
            {
                viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                return View(viewModel);
            }

            var book = await _context.Books
                        .Include(b => b.Categories)
                        .FirstOrDefaultAsync(b => b.Id == id);
            if (book == null) return NotFound();
            if (!IsUserAuthorized(book.UserId)) return Forbid();

            string? oldCoverPath = book.CoverImageUrl;
            string? oldEpubPath = book.EpubFilePath;
            string? uploadedCoverPath = null;
            string? uploadedEpubPath = null;

            try
            {
                book.Title = viewModel.Title;
                book.Description = viewModel.Description;
                book.PublishedDate = viewModel.PublishedDate;
                book.Author = viewModel.Author;
                book.IsPublic = viewModel.IsPublic;

                if (viewModel.CoverImage != null && viewModel.CoverImage.Length > 0)
                {
                    // *** FIX: Use null-forgiving operator '!' ***
                    uploadedCoverPath = await SaveFileAsync(viewModel.CoverImage!, _coverImageFolder);
                    book.CoverImageUrl = uploadedCoverPath;
                    DeleteFile(oldCoverPath);
                }

                if (hasNewFile)
                {
                    // *** FIX: Use null-forgiving operator '!' ***
                    uploadedEpubPath = await SaveFileAsync(viewModel.EpubFile!, _epubFileFolder, ensureExtension: ".epub");
                    // *** FIX: Use null-forgiving operator '!' ***
                    book.EpubFileName = viewModel.EpubFile!.FileName;
                    book.EpubFilePath = uploadedEpubPath;
                    book.BookUrl = null;
                    DeleteFile(oldEpubPath);
                }
                else if (hasNewUrl)
                {
                    if (book.BookUrl != viewModel.BookUrl)
                    {
                        book.BookUrl = viewModel.BookUrl;
                        DeleteFile(oldEpubPath);
                        book.EpubFilePath = null;
                        book.EpubFileName = null;
                    }
                }

                await UpdateBookCategoriesAsync(book, viewModel.SelectedCategoryIds);

                _context.Update(book);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Book '{book.Title}' updated successfully.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            catch (DbUpdateConcurrencyException) { if (!BookExists(viewModel.Id)) return NotFound(); else throw; }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating book {id}: {ex.Message}");
                DeleteFile(uploadedCoverPath);
                DeleteFile(uploadedEpubPath);
                ModelState.AddModelError("", "An unexpected error occurred while saving the book changes. Please try again.");
                viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                var originalBook = await _context.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
                viewModel.ExistingCoverUrl = originalBook?.CoverImageUrl;
                viewModel.ExistingEpubFileName = originalBook?.EpubFileName;
                viewModel.ExistingBookUrl = originalBook?.BookUrl;
                viewModel.IsPublic = originalBook?.IsPublic ?? true;
                return View(viewModel);
            }
        }

        // GET: Books/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
             if (id == null) return NotFound();
             var book = await _context.Books.Include(b => b.User).FirstOrDefaultAsync(m => m.Id == id);
             if (book == null) return NotFound();
             if (!IsUserAuthorized(book.UserId))
             {
                 TempData["ErrorMessage"] = "You are not authorized to delete this book.";
                 return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
             }
             return View(book);
        }

        // POST: Books/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();
            if (!IsUserAuthorized(book.UserId)) return Forbid();

            string? coverPath = book.CoverImageUrl;
            string? epubPath = book.EpubFilePath;
            string bookTitle = book.Title;

            try
            {
                _context.Books.Remove(book);
                await _context.SaveChangesAsync();
                DeleteFile(coverPath);
                DeleteFile(epubPath);
                TempData["SuccessMessage"] = $"Book '{bookTitle}' deleted successfully.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting book {id}: {ex.Message}");
                TempData["ErrorMessage"] = $"Could not delete the book '{bookTitle}' due to an error. Please try again.";
                return RedirectToPage("/Account/Manage/MyBooks", new { area = "Identity" });
            }
        }

        // GET: Books/Read/5
        [AllowAnonymous]
        public async Task<IActionResult> Read(int? id)
        {
             if (id == null) return NotFound();
             var book = await _context.Books.FindAsync(id);
             if (book == null) return NotFound();
             // Optional access check
             // if (!book.IsPublic && !IsUserAuthorized(book.UserId)) return Forbid();
             if (string.IsNullOrEmpty(book.EpubFilePath) && string.IsNullOrEmpty(book.BookUrl))
             {
                 TempData["ErrorMessage"] = "No readable content found for this book.";
                 return RedirectToAction(nameof(Details), new { id = id });
             }
             return View(book);
        }

        // GET: Books/GetEpub/5
        [AllowAnonymous]
        public async Task<IActionResult> GetEpub(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null || string.IsNullOrEmpty(book.EpubFilePath)) { return NotFound(); }
            // Optional access check
            // if (!book.IsPublic && !IsUserAuthorized(book.UserId)) return Forbid();
            string epubPath = GetAbsolutePath(book.EpubFilePath.TrimStart('/')); // *** USE GetAbsolutePath ***
            if (!System.IO.File.Exists(epubPath)) { return NotFound(); }
            try
            {
                var fileBytes = await System.IO.File.ReadAllBytesAsync(epubPath);
                return File(fileBytes, "application/epub+zip");
            }
            catch (IOException) { /* ... error handling ... */ return StatusCode(500); }
        }

        // --- Helper Methods ---

        // *** FIX: Added GetAbsolutePath back ***
        private string GetAbsolutePath(string relativePath)
        {
            // Trim leading slash if present to ensure Path.Combine works correctly from WebRootPath
            relativePath = relativePath.TrimStart('/', '\\');
            return Path.Combine(_hostEnvironment.WebRootPath, relativePath);
        }

        private async Task<string?> SaveFileAsync(IFormFile file, string folderPath, string? ensureExtension = null)
        {
            // Note: Null check happened before calling this, so 'file' is assumed non-null here.
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
            if (idsToAdd.Any())
            {
                var catsToAdd = await _context.Categories.Where(c => idsToAdd.Contains(c.Id)).ToListAsync();
                foreach (var cat in catsToAdd) { book.Categories.Add(cat); }
            }
        }

        private bool IsUserAuthorized(string? resourceOwnerUserId)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return (resourceOwnerUserId != null && resourceOwnerUserId == currentUserId) || User.IsInRole("Admin");
        }

        private bool BookExists(int id)
        {
            return _context.Books.Any(e => e.Id == id);
        }

        private void DeleteFile(string? relativePath)
        {
            if (!string.IsNullOrEmpty(relativePath))
            {
                try
                {
                    string absolutePath = GetAbsolutePath(relativePath); // Use helper method
                    if (System.IO.File.Exists(absolutePath))
                    {
                        System.IO.File.Delete(absolutePath);
                    }
                }
                catch (Exception ex) { Console.WriteLine($"Warning: Could not delete file {relativePath}. Error: {ex.Message}"); }
            }
        }
    }
}