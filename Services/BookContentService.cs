// File: Services/BookContentService.cs

using MuPDFCore;
using MuPDFCore.StructuredText; // Required for structured text classes
using MvcBooks.Services;         // For MinioService if in different namespace
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions; // Needed for Regex
using System.Threading.Tasks;
using System.Linq;                 // For Any() if used

namespace MvcBooks.Services // Ensure this namespace matches your project structure
{
    // Assumes IBookContentService interface exists as defined previously:
    // public interface IBookContentService
    // {
    //     Task<string?> ExtractTextFromPageAsync(string objectKey, int pageNumber, string originalFileNameHint = "");
    // }

    public class BookContentService : IBookContentService
    {
        private readonly MinioService _minioService;
        private readonly ILogger<BookContentService> _logger;
        // Regex to find 2 or more consecutive whitespace characters
        private static readonly Regex CollapseSpacesRegex = new Regex(@"\s{2,}", RegexOptions.Compiled);

        public BookContentService(MinioService minioService, ILogger<BookContentService> logger)
        {
            _minioService = minioService ?? throw new ArgumentNullException(nameof(minioService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Extracts text from a specific page of a document stored in Minio, attempting to preserve
        /// intra-line spacing relevant for tables while separating blocks.
        /// </summary>
        /// <param name="objectKey">The Minio object key (e.g., "books/guid.pdf").</param>
        /// <param name="pageNumber">The 1-based page number to extract text from.</param>
        /// <param name="originalFileNameHint">Optional original filename to help determine file type.</param>
        /// <returns>The extracted text as a string, an error message string starting with "[Error:", or null if the initial stream retrieval fails.</returns>
        public async Task<string?> ExtractTextFromPageAsync(string objectKey, int pageNumber, string originalFileNameHint = "")
        {
            // --- Input Validation ---
            if (pageNumber <= 0)
            {
                _logger.LogWarning("MuPDFCore: Invalid page number requested ({PageNumber}) for Key: {ObjectKey}", pageNumber, objectKey);
                return "[Error: Invalid Page Number Requested]";
            }
            if (string.IsNullOrWhiteSpace(objectKey))
            {
                 _logger.LogWarning("MuPDFCore: Null or empty objectKey provided for text extraction.");
                 return "[Error: Invalid Object Key]";
            }

            string? tempFilePath = null;
            Stream? minioStream = null;
            // MuPDFContext needs to be disposed
            using MuPDFContext ctx = new MuPDFContext();

            try
            {
                // --- 1. Get Stream from Minio ---
                _logger.LogDebug("MuPDFCore: Getting stream for Key: {ObjectKey}", objectKey);
                minioStream = await _minioService.GetFileStreamAsync(objectKey);
                if (minioStream == null)
                {
                    _logger.LogWarning("MuPDFCore: Stream could not be retrieved from Minio for Key: {ObjectKey}", objectKey);
                    return null; // Return null for fundamental retrieval failure
                }

                // --- 2. Save Stream to Temporary File ---
                // Determine extension safely
                string extension = Path.GetExtension(originalFileNameHint)?.ToLowerInvariant().TrimStart('.') ??
                                 Path.GetExtension(objectKey)?.ToLowerInvariant().TrimStart('.') ??
                                 "tmp";
                extension = "." + extension; // Ensure leading dot

                tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
                _logger.LogDebug("MuPDFCore: Saving stream to temp file: {TempFilePath}", tempFilePath);

                using (var fileStreamOutput = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await minioStream.CopyToAsync(fileStreamOutput);
                    await fileStreamOutput.FlushAsync(); // Ensure data is written
                }
                _logger.LogDebug("MuPDFCore: Stream saved to temp file.");

                // Dispose Minio stream after copy
                await minioStream.DisposeAsync();
                minioStream = null;

                // --- 3. Process with MuPDFCore ---
                _logger.LogInformation("MuPDFCore: Opening document from path: {TempFilePath}", tempFilePath);
                // MuPDFDocument needs to be disposed
                using (MuPDFDocument doc = new MuPDFDocument(ctx, tempFilePath))
                {
                    int actualPageCount = doc.Pages.Count; // Use Pages.Count
                    int zeroBasedIndex = pageNumber - 1;   // Convert to 0-based

                    // Validate page number against actual document
                    if (zeroBasedIndex < 0 || zeroBasedIndex >= actualPageCount)
                    {
                        _logger.LogWarning("MuPDFCore: Requested page number {RequestedPage} (Index {PageIndex}) is out of bounds for document with {ActualCount} pages. Key: {ObjectKey}", pageNumber, zeroBasedIndex, actualPageCount, objectKey);
                        return $"[Error: Page {pageNumber} does not exist ({actualPageCount} pages total)]";
                    }

                    _logger.LogInformation("MuPDFCore: Extracting text from page index {PageIndex}", zeroBasedIndex);
                    StringBuilder pageTextBuilder = new StringBuilder(); // Builder for the entire page

                    // Get the structured text page (also needs disposal)
                    using (MuPDFStructuredTextPage? structuredPage = doc.GetStructuredTextPage(zeroBasedIndex))
                    {
                        // Check if structure exists and has content
                        if (structuredPage?.StructuredTextBlocks != null && structuredPage.StructuredTextBlocks.Any())
                        {
                            bool isFirstBlockOnPage = true;
                            foreach (var block in structuredPage.StructuredTextBlocks)
                            {
                                // Add separation *before* processing the block (unless it's the first)
                                if (!isFirstBlockOnPage)
                                {
                                    pageTextBuilder.AppendLine(); // Blank line between blocks
                                }
                                isFirstBlockOnPage = false;

                                StringBuilder currentLineBuilder = new StringBuilder(); // Build lines within block

                                // Iterate through lines within the block
                                int lineCount = block.Count;
                                for (int j = 0; j < lineCount; j++)
                                {
                                    MuPDFStructuredTextLine line = block[j]; // Access line
                                    if (!string.IsNullOrWhiteSpace(line.Text))
                                    {
                                        // Clean up extra spaces within the line text, keep single spaces
                                        string cleanedLineText = CollapseSpacesRegex.Replace(line.Text.Trim(), " ");

                                        // Add a space between "cells" or segments detected on the same visual line
                                        if (currentLineBuilder.Length > 0)
                                        {
                                            currentLineBuilder.Append(' '); // Separator (could be '\t' for tab)
                                        }
                                        currentLineBuilder.Append(cleanedLineText);
                                    }
                                } // End for lines in block

                                // Append the combined line(s) from the block if it contained text
                                if (currentLineBuilder.Length > 0)
                                {
                                     pageTextBuilder.Append(currentLineBuilder.ToString());
                                }

                                // Always add a newline after processing a block's content (or lack thereof)
                                // to ensure separation even if the next block starts immediately
                                pageTextBuilder.AppendLine();

                            } // End foreach block
                        }
                        else
                        {
                            _logger.LogWarning("MuPDFCore: GetStructuredTextPage returned null or page has no blocks for page index {PageIndex}. Key: {ObjectKey}", zeroBasedIndex, objectKey);
                            return string.Empty; // No text found on page
                        }
                    } // Dispose structuredPage

                    _logger.LogInformation("MuPDFCore: Page {RequestedPage} extraction finished. Approx Length: {Length}", pageNumber, pageTextBuilder.Length);

                    // Final cleanup: Trim overall result and collapse excessive blank lines (3+ becomes 2)
                    string resultText = Regex.Replace(pageTextBuilder.ToString(), @"(\r?\n){3,}", "\n\n").Trim();
                    return resultText;

                } // Dispose doc
            }
            // --- Exception Handling ---
            catch (MuPDFException muEx) {
                 _logger.LogError(muEx, "MuPDFCore: MuPDFException during page extraction. Key: {ObjectKey}, Page: {Page}", objectKey, pageNumber);
                 return "[Error: Failed to process document page]";
            }
            catch (FileNotFoundException fnfEx) {
                 _logger.LogError(fnfEx, "MuPDFCore: Temporary file not found. Path: {Path}, Key: {ObjectKey}, Page: {Page}", tempFilePath, objectKey, pageNumber);
                 return "[Error: Could not read temporary file]";
            }
            catch (IOException ioEx) {
                 _logger.LogError(ioEx, "MuPDFCore: IO Error during page extraction. Key: {ObjectKey}, Page: {Page}", objectKey, pageNumber);
                 return "[Error: File system error during extraction]";
            }
            catch (Exception ex) {
                _logger.LogError(ex, "MuPDFCore: General error during page extraction. Key: {ObjectKey}, Page: {Page}", objectKey, pageNumber);
                return "[Error: An unexpected error occurred]";
            }
            finally // --- Cleanup ---
            {
                // Dispose any leftover stream reference
                if (minioStream != null) {
                    try { await minioStream.DisposeAsync(); } catch { /* Ignore disposal error */ }
                }
                // Delete Temporary File reliably
                if (tempFilePath != null && File.Exists(tempFilePath)) {
                    try {
                        File.Delete(tempFilePath);
                        _logger.LogDebug("MuPDFCore: Deleted temporary file: {TempFilePath}", tempFilePath);
                    } catch (Exception deleteEx){
                        _logger.LogWarning(deleteEx, "MuPDFCore: Failed to delete temporary file: {TempFilePath}", tempFilePath);
                    }
                }
            }
        } // End ExtractTextFromPageAsync

    } // End Class
} // End Namespace