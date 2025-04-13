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

namespace MvcBooks.Controllers
{
    [Authorize] // Apply authorization to the whole controller
    public class BooksController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _hostEnvironment;
        private readonly string _coverImageFolder = Path.Combine("images", "covers"); // Relative path within wwwroot
        private readonly string _epubFileFolder = Path.Combine("books");             // Relative path within wwwroot


        public BooksController(ApplicationDbContext context, IWebHostEnvironment hostEnvironment)
        {
            _context = context;
            _hostEnvironment = hostEnvironment;
        }

        // Helper method to get absolute path
        private string GetAbsolutePath(string relativePath)
        {
            return Path.Combine(_hostEnvironment.WebRootPath, relativePath);
        }

        // Helper method to delete a file safely
        private void DeleteFile(string? relativePath)
        {
            if (!string.IsNullOrEmpty(relativePath))
            {
                string absolutePath = GetAbsolutePath(relativePath.TrimStart('/'));
                if (System.IO.File.Exists(absolutePath))
                {
                    try {
                        System.IO.File.Delete(absolutePath);
                    } catch (IOException)
                    {
                        // Log the error, but don't necessarily block operation
                        // E.g., _logger.LogError(ex, "Could not delete file: {Path}", absolutePath);
                    }
                }
            }
        }

        // GET: Books
        [AllowAnonymous] // Allow anyone to view the list
        public async Task<IActionResult> Index()
        {
            var books = await _context.Books
                .Include(b => b.Categories)
                // .Include(b => b.User) // Optional: Include user if displaying who added it
                .OrderBy(b => b.Title) // Example ordering
                .ToListAsync();
            return View(books);
        }

        // GET: Books/Details/5
        [AllowAnonymous] // Allow anyone to view details
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var book = await _context.Books
                .Include(b => b.Categories)
                .Include(b => b.User) // Include user for display
                .FirstOrDefaultAsync(m => m.Id == id);

            if (book == null) return NotFound();

            return View(book);
        }

        // GET: Books/Create
        public async Task<IActionResult> Create()
        {
            BookViewModel viewModel = new BookViewModel
            {
                PublishedDate = DateTime.Today, // Default to today's date
                AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync()
            };
            return View(viewModel);
        }

        // POST: Books/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BookViewModel viewModel)
        {
            // --- Custom Validation: Ensure at least one source (File or URL) ---
            bool hasFile = viewModel.EpubFile != null && viewModel.EpubFile.Length > 0;
            bool hasUrl = !string.IsNullOrWhiteSpace(viewModel.BookUrl);

            if (!hasFile && !hasUrl)
            {
                ModelState.AddModelError("", "Either an EPUB file must be uploaded or a Book URL must be provided.");
            }
            if (hasFile && hasUrl)
            {
                 ModelState.AddModelError("", "Please provide either an EPUB file upload OR a Book URL, not both.");
            }
            // --- End Custom Validation ---

            // Re-populate categories if validation fails before checking ModelState
            if (!ModelState.IsValid)
            {
                viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                return View(viewModel);
            }

            // Passed validation, proceed with creation
            Book book = new Book
            {
                Title = viewModel.Title,
                Description = viewModel.Description,
                PublishedDate = viewModel.PublishedDate,
                Author = viewModel.Author,
                UserId = User.FindFirstValue(ClaimTypes.NameIdentifier) // Get current user ID
                // ProjectGutenbergId removed
            };

            string? uploadedCoverPath = null;
            string? uploadedEpubPath = null;
            string? uploadedEpubName = null;

            try
            {
                 // Handle cover image upload if provided
                if (viewModel.CoverImage != null && viewModel.CoverImage.Length > 0)
                {
                    string coverFolderFullPath = GetAbsolutePath(_coverImageFolder);
                    Directory.CreateDirectory(coverFolderFullPath); // Ensure exists
                    string uniqueCoverFileName = Guid.NewGuid().ToString() + Path.GetExtension(viewModel.CoverImage.FileName);
                    string coverFilePath = Path.Combine(coverFolderFullPath, uniqueCoverFileName);

                    using (var fileStream = new FileStream(coverFilePath, FileMode.Create))
                    {
                        await viewModel.CoverImage.CopyToAsync(fileStream);
                    }
                    uploadedCoverPath = $"/{_coverImageFolder.Replace(Path.DirectorySeparatorChar, '/')}/{uniqueCoverFileName}"; // Store relative path
                    book.CoverImageUrl = uploadedCoverPath;
                }

                // Handle EPUB source (Prioritize uploaded file if both were somehow provided despite validation)
                if (hasFile)
                {
                    string epubFolderFullPath = GetAbsolutePath(_epubFileFolder);
                     Directory.CreateDirectory(epubFolderFullPath); // Ensure exists
                    string uniqueEpubFileName = Guid.NewGuid().ToString() + ".epub"; // Ensure .epub extension
                    string epubFilePath = Path.Combine(epubFolderFullPath, uniqueEpubFileName);

                    using (var fileStream = new FileStream(epubFilePath, FileMode.Create))
                    {
                        await viewModel.EpubFile!.CopyToAsync(fileStream); // Use ! null-forgiving operator as hasFile check passed
                    }
                    uploadedEpubPath = $"/{_epubFileFolder.Replace(Path.DirectorySeparatorChar, '/')}/{uniqueEpubFileName}"; // Store relative path
                    uploadedEpubName = viewModel.EpubFile!.FileName;

                    book.EpubFilePath = uploadedEpubPath;
                    book.EpubFileName = uploadedEpubName;
                    book.BookUrl = null; // Ensure URL is null if file is uploaded
                }
                else if (hasUrl) // Only use URL if file was not uploaded
                {
                    book.BookUrl = viewModel.BookUrl;
                    book.EpubFilePath = null; // Ensure file path is null
                    book.EpubFileName = null;
                }

                // Handle category assignments
                await UpdateBookCategories(book, viewModel.SelectedCategoryIds);

                _context.Add(book);
                await _context.SaveChangesAsync();
                // Consider adding a success message (TempData)
                return RedirectToAction(nameof(Index));
            }
            catch (Exception) // Catch potential exceptions during file IO or DB save
            {
                // Log the exception (using an ILogger instance is recommended)
                // E.g., _logger.LogError(ex, "Error creating book.");

                // Clean up any files that might have been created before the error
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
                .Include(b => b.Categories) // Include categories to pre-select them
                .FirstOrDefaultAsync(m => m.Id == id);

            if (book == null) return NotFound();

            // Authorization Check: Only owner or Admin can edit
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (book.UserId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid(); // Or RedirectToAction("AccessDenied", "Account");
            }

            // Map Book to BookViewModel
            BookViewModel viewModel = new BookViewModel
            {
                Id = book.Id,
                Title = book.Title,
                Description = book.Description,
                PublishedDate = book.PublishedDate,
                Author = book.Author,
                ExistingCoverUrl = book.CoverImageUrl,
                ExistingEpubFileName = book.EpubFileName, // Show current file name
                ExistingBookUrl = book.BookUrl,           // Show current URL
                SelectedCategoryIds = book.Categories.Select(c => c.Id).ToList(),
                AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync()
                // ProjectGutenbergId removed
            };

            return View(viewModel);
        }

        // POST: Books/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, BookViewModel viewModel)
        {
            if (id != viewModel.Id) return NotFound();

            // --- Custom Validation: Ensure at least one source (File or URL OR Existing) ---
             bool hasNewFile = viewModel.EpubFile != null && viewModel.EpubFile.Length > 0;
             bool hasNewUrl = !string.IsNullOrWhiteSpace(viewModel.BookUrl);
             bool hasExistingSource = !string.IsNullOrWhiteSpace(viewModel.ExistingEpubFileName) || !string.IsNullOrWhiteSpace(viewModel.ExistingBookUrl);

             if (hasNewFile && hasNewUrl) // Cannot provide both new sources
             {
                  ModelState.AddModelError("", "Please provide either an EPUB file upload OR a Book URL, not both.");
             }
             // Implicit: If !hasNewFile && !hasNewUrl, we intend to keep the existing source (if any) - this is valid for Edit.

             // Reload categories before checking ModelState in case of failure
             if (!ModelState.IsValid)
             {
                 // Need to ensure Existing... properties are preserved if model binding fails??
                 // They should be okay as they are part of the viewModel posted back.
                 viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                 return View(viewModel);
             }

            // Find the existing book, including its current categories
            var book = await _context.Books
                        .Include(b => b.Categories)
                        .FirstOrDefaultAsync(b => b.Id == id);

            if (book == null) return NotFound();

            // Authorization Check: Only owner or Admin can edit
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (book.UserId != currentUserId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }


            string? oldCoverPath = book.CoverImageUrl; // Store old paths for potential deletion
            string? oldEpubPath = book.EpubFilePath;
            string? uploadedCoverPath = null; // Track newly uploaded files for potential rollback
            string? uploadedEpubPath = null;

            try
            {
                // Update book properties from view model
                book.Title = viewModel.Title;
                book.Description = viewModel.Description;
                book.PublishedDate = viewModel.PublishedDate;
                book.Author = viewModel.Author;
                // ProjectGutenbergId removed

                // Handle Cover Image Update
                if (viewModel.CoverImage != null && viewModel.CoverImage.Length > 0)
                {
                    string coverFolderFullPath = GetAbsolutePath(_coverImageFolder);
                    Directory.CreateDirectory(coverFolderFullPath);
                    string uniqueCoverFileName = Guid.NewGuid().ToString() + Path.GetExtension(viewModel.CoverImage.FileName);
                    string coverFilePath = Path.Combine(coverFolderFullPath, uniqueCoverFileName);

                    using (var fileStream = new FileStream(coverFilePath, FileMode.Create))
                    {
                        await viewModel.CoverImage.CopyToAsync(fileStream);
                    }
                    uploadedCoverPath = $"/{_coverImageFolder.Replace(Path.DirectorySeparatorChar, '/')}/{uniqueCoverFileName}";
                    book.CoverImageUrl = uploadedCoverPath;

                    // Delete the old cover image *after* successful upload & path update
                    DeleteFile(oldCoverPath);
                 }

                // Handle EPUB Source Update (Prioritize new file upload)
                if (hasNewFile)
                {
                    string epubFolderFullPath = GetAbsolutePath(_epubFileFolder);
                     Directory.CreateDirectory(epubFolderFullPath);
                    string uniqueEpubFileName = Guid.NewGuid().ToString() + ".epub";
                    string epubFilePath = Path.Combine(epubFolderFullPath, uniqueEpubFileName);

                    using (var fileStream = new FileStream(epubFilePath, FileMode.Create))
                    {
                        await viewModel.EpubFile!.CopyToAsync(fileStream);
                    }
                    uploadedEpubPath = $"/{_epubFileFolder.Replace(Path.DirectorySeparatorChar, '/')}/{uniqueEpubFileName}";

                    book.EpubFilePath = uploadedEpubPath;
                    book.EpubFileName = viewModel.EpubFile!.FileName;
                    book.BookUrl = null; // Clear URL if file is uploaded

                    // Delete old EPUB file *after* successful upload & path update
                    DeleteFile(oldEpubPath);
                }
                else if (hasNewUrl) // Only update URL if NO new file was uploaded
                {
                    // Check if URL is actually different from existing URL to avoid unnecessary updates/deletions
                    if (book.BookUrl != viewModel.BookUrl)
                    {
                        book.BookUrl = viewModel.BookUrl;

                        // If switching from File to URL, delete the old file
                         DeleteFile(oldEpubPath); // Delete the previous file path if it existed
                         book.EpubFilePath = null; // Clear file path properties
                         book.EpubFileName = null;
                    }
                }
                // ELSE: If neither new file nor new URL is provided, we keep the existing book.EpubFilePath / book.BookUrl


                // Update category assignments
                await UpdateBookCategories(book, viewModel.SelectedCategoryIds);

                _context.Update(book);
                await _context.SaveChangesAsync();
                 // Consider adding a success message (TempData)
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateConcurrencyException) // Handle concurrency issues
            {
                 if (!BookExists(viewModel.Id)) return NotFound();
                 else throw;
            }
             catch (Exception) // Catch potential exceptions during file IO or DB save
            {
                 // Log the exception
                 // E.g., _logger.LogError(ex, "Error updating book with ID {BookId}.", id);

                 // IMPORTANT: Attempt to rollback file changes if the DB update failed
                 // If a new cover was uploaded, but DB failed, delete the new cover
                 DeleteFile(uploadedCoverPath);
                 // If a new epub was uploaded, but DB failed, delete the new epub
                 DeleteFile(uploadedEpubPath);
                 // (We don't restore the old files automatically here, that's more complex)

                 ModelState.AddModelError("", "An unexpected error occurred while saving the book. Please try again.");
                 viewModel.AvailableCategories = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
                 // Ensure Existing... properties are still correct for redisplay
                 viewModel.ExistingCoverUrl = book?.CoverImageUrl ?? oldCoverPath; // Use value from potentially partially updated entity or original
                 viewModel.ExistingEpubFileName = book?.EpubFileName;
                 viewModel.ExistingBookUrl = book?.BookUrl;

                 return View(viewModel);
             }
        }


        // GET: Books/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var book = await _context.Books
                .Include(b => b.User) // Include user to display who added it, if desired
                .FirstOrDefaultAsync(m => m.Id == id);

            if (book == null) return NotFound();

            // Authorization Check
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
             if (book.UserId != currentUserId && !User.IsInRole("Admin"))
             {
                 return Forbid();
             }

            return View(book); // Pass the Book model to the Delete confirmation view
        }

        // POST: Books/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var book = await _context.Books.FindAsync(id); // Find book by ID

            if (book == null) return NotFound();

            // Authorization Check
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
             if (book.UserId != currentUserId && !User.IsInRole("Admin"))
             {
                 return Forbid();
             }

            // Store paths before removing the entity
            string? coverPath = book.CoverImageUrl;
            string? epubPath = book.EpubFilePath;

            try
            {
                _context.Books.Remove(book);
                await _context.SaveChangesAsync();

                // Delete physical files *after* successful DB deletion
                DeleteFile(coverPath);
                DeleteFile(epubPath); // Only deletes if EpubFilePath was set

                 // Consider adding a success message (TempData)
                return RedirectToAction(nameof(Index));
            }
            catch (Exception)
            {
                 // Log error
                 // E.g., _logger.LogError(ex, "Error deleting book with ID {BookId}.", id);
                // Maybe add a model error or TempData message
                 ModelState.AddModelError("", "An error occurred while deleting the book.");
                 // It's tricky to redisplay the Delete confirmation view meaningfully here.
                 // Maybe redirect back to details or index with an error message.
                 TempData["ErrorMessage"] = "Could not delete the book due to an error.";
                 return RedirectToAction(nameof(Index));
            }
        }


        // GET: Books/Read/5 - View for the EPUB Reader
        [AllowAnonymous]
        public async Task<IActionResult> Read(int? id)
        {
            if (id == null) return NotFound();

            var book = await _context.Books.FindAsync(id);

            if (book == null) return NotFound();

            // Check if there's a source to read from
            if (string.IsNullOrEmpty(book.EpubFilePath) && string.IsNullOrEmpty(book.BookUrl))
            {
                 // Maybe redirect to Details view with a message?
                 TempData["ErrorMessage"] = "No readable content (EPUB file or URL) found for this book.";
                 return RedirectToAction(nameof(Details), new { id = id });
                // Or return a specific "NotFound" view for reading content:
                // return View("NoContentFound", book);
            }

            // Pass the book model to the Read view, which will decide how to load epub.js
            return View(book);
        }

        // GET: Books/GetEpub/5 - Action to serve the uploaded EPUB file content
        [AllowAnonymous]
        public async Task<IActionResult> GetEpub(int id)
        {
            var book = await _context.Books.FindAsync(id);

            // Only serve if EpubFilePath exists and is not empty
            if (book == null || string.IsNullOrEmpty(book.EpubFilePath))
            {
                return NotFound("EPUB file not found or not specified for this book.");
            }

            string epubPath = GetAbsolutePath(book.EpubFilePath.TrimStart('/'));
            if (!System.IO.File.Exists(epubPath))
            {
                // Log this inconsistency?
                return NotFound($"EPUB file not found at specified path: {book.EpubFilePath}");
            }

            try
            {
                 var fileBytes = await System.IO.File.ReadAllBytesAsync(epubPath);
                 // Use the stored original filename if available, otherwise a generic name
                 string downloadName = book.EpubFileName ?? $"book_{book.Id}.epub";
                 return File(fileBytes, "application/epub+zip", downloadName);
            }
            catch (IOException)
            {
                // Log error reading file
                 return StatusCode(StatusCodes.Status500InternalServerError, "Error reading EPUB file.");
            }
        }


        // Helper method to update categories for a book
        private async Task UpdateBookCategories(Book book, List<int>? selectedCategoryIds)
        {
             // If the book is being created, Categories collection is initially empty.
             // If editing, book.Categories holds the currently associated categories (if included).

             if (selectedCategoryIds == null)
             {
                 book.Categories.Clear(); // Remove all categories if none selected
                 return;
             }

             var selectedIdsSet = new HashSet<int>(selectedCategoryIds);
             var currentIdsSet = new HashSet<int>(book.Categories.Select(c => c.Id));

             // Find categories to remove
             var categoriesToRemove = book.Categories.Where(c => !selectedIdsSet.Contains(c.Id)).ToList();
             foreach (var categoryToRemove in categoriesToRemove)
             {
                 book.Categories.Remove(categoryToRemove);
             }

             // Find category IDs to add
             var idsToAdd = selectedIdsSet.Where(id => !currentIdsSet.Contains(id)).ToList();
             if (idsToAdd.Any())
             {
                var categoriesToAdd = await _context.Categories.Where(c => idsToAdd.Contains(c.Id)).ToListAsync();
                foreach (var categoryToAdd in categoriesToAdd)
                {
                     book.Categories.Add(categoryToAdd);
                }
             }
        }

        private bool BookExists(int id)
        {
            return _context.Books.Any(e => e.Id == id);
        }
    }
}