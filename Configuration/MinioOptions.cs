
namespace MvcBooks.Configuration 
{
    public class MinioOptions
    {
        
        public const string Minio = "Minio";

        
        public string Endpoint { get; set; } = string.Empty;
        public string AccessKey { get; set; } = string.Empty; 
        public string SecretKey { get; set; } = string.Empty; 
        public string BucketName { get; set; } = string.Empty;
        public bool UseSSL { get; set; } = false; 

    }
}