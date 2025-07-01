namespace MvcBooks.Common // Or your preferred namespace
{
    public static class PresignedUrlSettings
    {
        // Expiry durations in seconds
        public const int LongExpirySeconds = 60 * 60 * 24 * 7; // 7 days
        public const int EditFormExpirySeconds = 60 * 15; // 15 minutes
        public const int DefaultDownloadExpirySeconds = 60 * 60; // 1 hour (example)
        public const int HomePageExpirySeconds = 60 * 10;
        

    }
}