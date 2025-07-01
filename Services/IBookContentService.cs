// File: Services/IBookContentService.cs
using System.IO; // Potentially needed if passing streams later
using System.Threading.Tasks;

namespace MvcBooks.Services // Adjust namespace if needed
{
    public interface IBookContentService
    {
        // Extracts text from a file stored in Minio, identified by its object key.
        // pageNumber is 1-based.
        Task<string?> ExtractTextFromPageAsync(string objectKey, int pageNumber, string originalFileNameHint = "");

        // Add other methods later if needed (e.g., extracting all text)
        // Task<string?> ExtractAllTextFromObjectKeyAsync(string objectKey, string originalFileNameHint = "");
    }
}