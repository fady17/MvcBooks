// Path: Services/MinioService.cs
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Microsoft.Extensions.Options;
using MvcBooks.Configuration;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace MvcBooks.Services
{
    public class MinioService : IDisposable
    {
        private readonly IMinioClient _minioClient;
        private readonly MinioOptions _options;
        private readonly ILogger<MinioService> _logger;
        private bool _bucketChecked = false;
        private readonly object _bucketLock = new object();
        private const string PublicCoverPrefix = "covers"; // Define prefix constant

        public MinioService(IOptions<MinioOptions> options, ILogger<MinioService> logger)
        {
            _options = options.Value;
            _logger = logger;

            if (string.IsNullOrEmpty(_options.Endpoint) || string.IsNullOrEmpty(_options.AccessKey) || string.IsNullOrEmpty(_options.SecretKey) || string.IsNullOrEmpty(_options.BucketName)) {
                _logger.LogCritical("MinIO configuration is incomplete.");
                throw new InvalidOperationException("MinIO configuration incomplete.");
            }

            try {
                 _minioClient = new MinioClient()
                                    .WithEndpoint(_options.Endpoint)
                                    .WithCredentials(_options.AccessKey, _options.SecretKey)
                                    .WithSSL(_options.UseSSL).Build();
                 _logger.LogInformation("MinIO client configured for endpoint {Endpoint}", _options.Endpoint);
            } catch (Exception ex) { _logger.LogCritical(ex, "Failed to initialize MinIO client."); throw; }
        }

        private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken = default)
        {
            // Quick check if already verified in this instance lifespan
            if (_bucketChecked) return;

            // Lock to prevent race conditions during check/creation across requests
            lock (_bucketLock) {
                if (_bucketChecked) return; // Double-check inside lock
            }

            try {
                var beArgs = new BucketExistsArgs().WithBucket(_options.BucketName);
                bool found = await _minioClient.BucketExistsAsync(beArgs, cancellationToken).ConfigureAwait(false);

                if (!found) {
                    _logger.LogInformation("Bucket '{BucketName}' not found. Creating...", _options.BucketName);
                    var mbArgs = new MakeBucketArgs().WithBucket(_options.BucketName);
                    await _minioClient.MakeBucketAsync(mbArgs, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Bucket '{BucketName}' created.", _options.BucketName);

                    // --- START: Apply public read policy specifically for covers prefix ---
                    _logger.LogInformation("Applying public read policy for '{Prefix}' prefix...", PublicCoverPrefix);
                    string resource = $"arn:aws:s3:::{_options.BucketName}/{PublicCoverPrefix}/*"; // Target covers/*
                    string policyJson = $$"""
                    {
                        "Version": "2012-10-17",
                        "Statement": [
                            {
                                "Effect": "Allow",
                                "Principal": {"AWS": ["*"]},
                                "Action": ["s3:GetObject"],
                                "Resource": ["{{resource}}"],
                                "Sid": "PublicReadForCoversOnly"
                            }
                        ]
                    }
                    """;
                    try {
                        var policyArgs = new SetPolicyArgs().WithBucket(_options.BucketName).WithPolicy(policyJson);
                        await _minioClient.SetPolicyAsync(policyArgs, cancellationToken).ConfigureAwait(false);
                        _logger.LogInformation("Public read policy successfully set for prefix '{Prefix}' in bucket '{BucketName}'.", PublicCoverPrefix, _options.BucketName);
                    } catch (Exception policyEx) {
                        _logger.LogError(policyEx, "Failed to set public read policy for prefix '{Prefix}'. Manual configuration might be needed.", PublicCoverPrefix);
                        // Decide if this is critical enough to throw
                        // throw;
                    }
                    // --- END: Apply public policy ---
                }
                else {
                     _logger.LogDebug("Bucket '{BucketName}' already exists.", _options.BucketName);
                     // Optional: You could add logic here to *check* if the policy exists and apply it if missing,
                     // even if the bucket already existed. This makes it more robust if the policy is ever removed manually.
                     // However, calling SetPolicyAsync repeatedly might have performance implications.
                }

                 lock (_bucketLock) { _bucketChecked = true; } // Mark as checked only after success or handling failure
            }
            catch (MinioException e) { _logger.LogError(e, "MinioException during EnsureBucketExistsAsync for '{BucketName}'", _options.BucketName); throw; }
            catch (Exception e) { _logger.LogError(e, "General exception during EnsureBucketExistsAsync for '{BucketName}'", _options.BucketName); throw; }
        }

        public async Task<string?> UploadFileAsync(IFormFile file, string objectPrefix = "", CancellationToken cancellationToken = default)
        {
             if (file == null || file.Length == 0) return null;
             await EnsureBucketExistsAsync(cancellationToken);
             try {
                 string extension = Path.GetExtension(file.FileName);
                 string objectName = $"{Guid.NewGuid()}{extension}";
                 if (!string.IsNullOrEmpty(objectPrefix)) {
                     objectName = $"{objectPrefix.Trim('/')}/{objectName}";
                 }
                 using var stream = file.OpenReadStream();
                 var putObjectArgs = new PutObjectArgs()
                     .WithBucket(_options.BucketName).WithObject(objectName)
                     .WithStreamData(stream).WithObjectSize(file.Length)
                     .WithContentType(file.ContentType);
                 await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken).ConfigureAwait(false);
                 _logger.LogInformation("File '{FileName}' uploaded as '{ObjectName}'.", file.FileName, objectName);
                 return objectName;
             }
             catch (Exception ex) { _logger.LogError(ex, "Error uploading file '{FileName}'.", file.FileName); return null; }
        }

        public async Task<bool> DeleteFileAsync(string objectName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(objectName)) { _logger.LogWarning("DeleteFileAsync called with empty objectName."); return false; }
            // No need to EnsureBucketExistsAsync for delete, it will fail naturally if bucket doesn't exist.
            try {
                var rmArgs = new RemoveObjectArgs().WithBucket(_options.BucketName).WithObject(objectName);
                await _minioClient.RemoveObjectAsync(rmArgs, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Object '{ObjectName}' deleted.", objectName);
                return true;
            }
            catch (ObjectNotFoundException) { _logger.LogInformation("Object '{ObjectName}' not found during deletion attempt.", objectName); return true; } // OK if already gone
            catch (Exception e) { _logger.LogError(e, "Error deleting '{ObjectName}'.", objectName); return false; }
        }

        public async Task<Stream?> GetFileStreamAsync(string objectName, CancellationToken cancellationToken = default)
        {
             if (string.IsNullOrEmpty(objectName)) return null;
             // No need to EnsureBucketExistsAsync for get, it will fail naturally if bucket doesn't exist.
             try {
                 var statArgs = new StatObjectArgs().WithBucket(_options.BucketName).WithObject(objectName);
                 await _minioClient.StatObjectAsync(statArgs, cancellationToken).ConfigureAwait(false);

                 MemoryStream memoryStream = new MemoryStream();
                 var getObjectArgs = new GetObjectArgs()
                     .WithBucket(_options.BucketName).WithObject(objectName)
                     .WithCallbackStream((stream) => { stream.CopyTo(memoryStream); });
                 await _minioClient.GetObjectAsync(getObjectArgs, cancellationToken).ConfigureAwait(false);
                 memoryStream.Position = 0;
                 return memoryStream;
             }
             catch (ObjectNotFoundException) { _logger.LogWarning("Object '{ObjectName}' not found for streaming.", objectName); return null; }
             catch (Exception e) { _logger.LogError(e, "Error getting stream for '{ObjectName}'.", objectName); return null; }
         }

        public string GetPublicFileUrl(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return string.Empty;
            objectName = objectName.TrimStart('/');
            return $"{(_options.UseSSL ? "https" : "http")}://{_options.Endpoint}/{_options.BucketName}/{objectName}";
        }

        public async Task<string?> GetPresignedFileUrlAsync(string objectName, int expiryInSeconds = 60 * 60, CancellationToken cancellationToken = default)
        {
             if (string.IsNullOrEmpty(objectName)) return null;
             // No need to EnsureBucketExistsAsync for presigned URL generation
             try {
                 var psArgs = new PresignedGetObjectArgs()
                     .WithBucket(_options.BucketName).WithObject(objectName)
                     .WithExpiry(expiryInSeconds);
                 string presignedUrl = await _minioClient.PresignedGetObjectAsync(psArgs).ConfigureAwait(false);
                 return presignedUrl;
             }
             catch (ObjectNotFoundException) { _logger.LogWarning("Object '{ObjectName}' not found for presigned URL generation.", objectName); return null; }
             catch (Exception e) { _logger.LogError(e, "Error getting presigned URL for '{ObjectName}'.", objectName); return null; }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}