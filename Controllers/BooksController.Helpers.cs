// File: BooksController.Helpers.cs (Helper Methods)
using System;
using System.Collections.Generic;
using System.IO;                    
using System.Linq;
using System.Security.Claims;       
using System.Threading;             
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;    
using Microsoft.AspNetCore.Mvc;     
using Microsoft.EntityFrameworkCore; 
using Microsoft.Extensions.Logging; 
using MvcBooks.Models;             
using MvcBooks.Models.ViewModels;  
using MvcBooks.Common; 
using MvcBooks.Helpers;
using Microsoft.Extensions.Caching.Memory;

namespace MvcBooks.Controllers
{
    public partial class BooksController 
    {
        // Helper - assumes MinioService handles bucket creation/check
        private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken = default)
        {
            // If MinioService needs explicit check, call it here.
            // await _minioService.EnsureBucketExistsAsync(cancellationToken);
            await Task.CompletedTask; // Assuming it's handled internally or on first use
        }

        private bool ValidateFileType(IFormFile? file, string modelPropertyName, string[] allowedMimeTypes, string[] allowedExtensions, string friendlyFileTypeName)
        {
            if (file == null || file.Length == 0) { return true; } // Optional file is valid if not present

            string? rawExtension = Path.GetExtension(file.FileName);
            string? rawMimeType = file.ContentType;

            // Normalize and check for null/whitespace *after* getting raw values
            string? fileExtension = rawExtension?.ToLowerInvariant().Trim();
            // Handle potential parameters like charset in ContentType
            string? mimeType = rawMimeType?.Split(';')[0].Trim().ToLowerInvariant();

            // Validate presence of type info
            if (string.IsNullOrWhiteSpace(mimeType) || string.IsNullOrWhiteSpace(fileExtension))
            {
                ModelState.AddModelError(modelPropertyName, $"Could not reliably determine the file type for the uploaded {friendlyFileTypeName}. Please ensure the file has a valid extension and type.");
                // Structured logging
                _logger.LogWarning("File type validation failed: Missing type info. Property: {PropertyName}, FileName: {FileName}, RawMime: {RawMimeType}, RawExt: {RawExtension}",
                    modelPropertyName, file.FileName, rawMimeType, rawExtension);
                return false;
            }


             bool mimeTypeIsValid = allowedMimeTypes.Contains(mimeType);
            bool extensionIsValid = allowedExtensions.Contains(fileExtension);

            if (!mimeTypeIsValid || !extensionIsValid)
            {
                string errorMsg;
                if (!mimeTypeIsValid && !extensionIsValid) {
                    errorMsg = $"Invalid file type and extension for {friendlyFileTypeName}. Allowed extensions: {string.Join(", ", allowedExtensions)}. Detected type: {mimeType}, extension: {fileExtension}.";
                } else if (!mimeTypeIsValid) {
                    errorMsg = $"The uploaded {friendlyFileTypeName} has an allowed extension ({fileExtension}) but an unexpected content type ({mimeType}). Please upload a valid file ({string.Join(", ", allowedExtensions)}).";
                } else { // !extensionIsValid
                     errorMsg = $"The uploaded {friendlyFileTypeName} has an allowed content type ({mimeType}) but an unexpected file extension ({fileExtension}). Please use a valid extension: {string.Join(", ", allowedExtensions)}.";
                }
                ModelState.AddModelError(modelPropertyName, errorMsg);
                // Structured logging
                _logger.LogWarning("File type validation failed: Type/Extension mismatch. Property: {PropertyName}, FileName: {FileName}, Mime: {MimeType} (Valid: {MimeValid}), Ext: {Extension} (Valid: {ExtValid}), Message: {ValidationMessage}",
                     modelPropertyName, file.FileName, mimeType, mimeTypeIsValid, fileExtension, extensionIsValid, errorMsg);
                return false;
            }
             _logger.LogDebug("File validation successful. Property: {PropertyName}, FileName: {FileName}, Mime: {MimeType}, Ext: {Extension}",
                modelPropertyName, file.FileName, mimeType, fileExtension);
            return true;
        }
               private async Task RepopulateEditViewModelOnError(BookViewModel viewModel, Book book)
        {
             // Use cached method for categories
             viewModel.AvailableCategories = await GetAvailableCategoriesAsync();

             string? existingCoverUrlOnError = Constants.DefaultCoverImagePath; // Use constant fallback
             bool isCoverFallback = false; // Initialize flag

             if (!string.IsNullOrEmpty(book.CoverImageObjectKey))
             {
                 try {
                    // Use constant expiry
                    existingCoverUrlOnError = await _minioService.GetPresignedFileUrlAsync(book.CoverImageObjectKey, PresignedUrlSettings.EditFormExpirySeconds);
                    _logger.LogDebug("Successfully generated temporary cover URL for error recovery. BookId: {BookId}", book.Id);
                 } catch (Exception ex) {
                     _logger.LogWarning(ex, "Failed to generate existing cover URL for Edit error recovery. BookId: {BookId}, Key: {ObjectKey}",
                         book.Id, book.CoverImageObjectKey);
                     isCoverFallback = true; // Mark as fallback on exception
                 }
                  existingCoverUrlOnError ??= Constants.DefaultCoverImagePath; // Ensure fallback if null returned
                  if (existingCoverUrlOnError == Constants.DefaultCoverImagePath) isCoverFallback = true; // Also fallback if result is the default path
             } else {
                  isCoverFallback = true; // Fallback if no key
             }
             viewModel.ExistingCoverUrl = existingCoverUrlOnError;
             viewModel.IsCoverUrlFallback = isCoverFallback; // Set the flag in the ViewModel

             // Use Constants for source types
             viewModel.ExistingBookUrl = book.BookSourceType == Constants.BookSourceExternal ? book.BookUrl : null;
             viewModel.ExistingEpubFileName = book.BookSourceType == Constants.BookSourceMinioEpub ? "(Existing EPUB - Upload Below to Replace)" : null;
             viewModel.ExistingPdfFileName = book.BookSourceType == Constants.BookSourceMinioPdf ? "(Existing PDF - Upload Below to Replace)" : null;

              if (!ModelState.ContainsKey(nameof(viewModel.IsPublic))) {
                viewModel.IsPublic = book.IsPublic;
             }
             if (!ModelState.ContainsKey(nameof(viewModel.SelectedCategoryIds))) {
                 viewModel.SelectedCategoryIds = book.Categories?.Select(c => c.Id).ToList() ?? new List<int>();
             }
        }

        private async Task UpdateBookCategoriesAsync(Book book, List<int>? selectedCategoryIds)
        {
             // Ensure Categories collection is loaded if needed
             if (book.Id > 0 && _context.Entry(book).State != EntityState.Detached && !_context.Entry(book).Collection(b => b.Categories).IsLoaded)
             {
                 await _context.Entry(book).Collection(b => b.Categories).LoadAsync();
             }
            book.Categories ??= new List<Category>(); // Ensure initialized

            // Use HashSet for efficient lookups
            var selectedIds = selectedCategoryIds == null ? new HashSet<int>() : new HashSet<int>(selectedCategoryIds);
            var currentIds = book.Categories.Select(c => c.Id).ToHashSet(); // Efficiently create HashSet

            // Find categories to remove (currently present but not in selection)
            // No need for ToList() here if we iterate carefully or create a list for removal
            var categoriesToRemove = new List<Category>();
            foreach(var category in book.Categories)
            {
                if (!selectedIds.Contains(category.Id))
                {
                    categoriesToRemove.Add(category);
                }
            }

            if (categoriesToRemove.Any()) {
                 _logger.LogDebug("Removing categories {CategoryIds} from BookId {BookId}", string.Join(",", categoriesToRemove.Select(c=>c.Id)), book.Id);
                foreach (var cat in categoriesToRemove) {
                    book.Categories.Remove(cat); // Remove from tracked collection
                }
            }

            // Find category IDs to add (in selection but not currently present)
            var idsToAdd = new List<int>();
             foreach(var selectedId in selectedIds)
             {
                 if (!currentIds.Contains(selectedId))
                 {
                     idsToAdd.Add(selectedId);
                 }
             }
            // Alternative LINQ way for idsToAdd (less clear maybe?):
            // var idsToAdd = selectedIds.Except(currentIds).ToList();


            if (idsToAdd.Any()){
                 _logger.LogDebug("Adding categories {CategoryIds} to BookId {BookId}", string.Join(",", idsToAdd), book.Id);
                 // Fetch only the needed categories from DB
                 var categoriesToAdd = await _context.Categories
                     .Where(c => idsToAdd.Contains(c.Id))
                     .ToListAsync(); // Need ToListAsync for DB query

                 foreach (var cat in categoriesToAdd) {
                     // Double-check not already added (shouldn't happen with correct logic, but safe)
                     if (!book.Categories.Any(existing => existing.Id == cat.Id)) {
                        book.Categories.Add(cat);
                     } else {
                        _logger.LogWarning("Attempted to add category {CategoryId} which already exists for BookId {BookId}", cat.Id, book.Id);
                     }
                 }
            }
         }
        

        private bool BookExists(int id) => _context.Books.Any(e => e.Id == id);

         private async Task DeleteMinioObjectAsync(string? objectKey)
        {
            if (string.IsNullOrWhiteSpace(objectKey))
            {
                _logger.LogDebug("Skipped MinIO deletion because object key was null or whitespace.");
                return; // Nothing to do
            }

             _logger.LogInformation("Attempting to delete MinIO object. Key: {ObjectKey}", objectKey);
            try
            {
                bool deleted = await _minioService.DeleteFileAsync(objectKey);
                if(!deleted) {
                    // Log as warning - might be okay if already deleted, but could indicate an issue.
                    _logger.LogWarning("MinIO DeleteFileAsync reported false (object likely not found). Key: {ObjectKey}", objectKey);
                } else {
                     _logger.LogInformation("Successfully deleted MinIO object. Key: {ObjectKey}", objectKey);
                }
            } catch (Exception ex) {
                // Log error but do not re-throw; allow calling process to continue.
                // Consider a separate monitoring mechanism for persistent MinIO errors.
                _logger.LogError(ex, "Error during MinIO object deletion. Key: {ObjectKey}. Manual cleanup might be required.", objectKey);
            }
        }
    }
}