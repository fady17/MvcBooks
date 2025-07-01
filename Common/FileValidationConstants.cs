namespace MvcBooks.Common // Or your preferred namespace
{
    public static class FileValidationConstants
    {
        // Allowed MIME Types
        public static readonly string[] AllowedCoverImageMimeTypes = { "image/jpeg", "image/png", "image/gif", "image/webp" };
        public static readonly string[] AllowedEpubMimeTypes = { "application/epub+zip" };
        public static readonly string[] AllowedPdfMimeTypes = { "application/pdf" };

        // Allowed Extensions (ensure leading dot and lowercase)
        public static readonly string[] AllowedCoverImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        public static readonly string[] AllowedEpubExtensions = { ".epub" };
        public static readonly string[] AllowedPdfExtensions = { ".pdf" };

        // Friendly Names
        public const string CoverImageFriendlyName = "Cover Image";
        public const string EpubFileFriendlyName = "EPUB file";
        public const string PdfFileFriendlyName = "PDF file";
    }
}