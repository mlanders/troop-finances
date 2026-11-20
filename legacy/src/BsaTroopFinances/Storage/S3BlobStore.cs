using System.Net;
using System.Runtime.CompilerServices;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace BsaTroopFinances.Storage;

/// <summary>Settings for an S3-compatible bucket (the Storage:S3 or BackupStorage:S3 section).</summary>
public sealed class S3StoreOptions
{
    /// <summary>The bucket. It must already exist and be private.</summary>
    public string Bucket { get; set; } = "";

    /// <summary>The endpoint for anything other than AWS itself, e.g.
    /// https://&lt;account-id&gt;.r2.cloudflarestorage.com (Cloudflare R2),
    /// https://s3.us-west-004.backblazeb2.com (Backblaze B2), http://minio:9000 (MinIO).
    /// Leave empty for AWS S3.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>AWS region ("us-west-2"), or the region the provider asks for ("auto" on R2).</summary>
    public string? Region { get; set; }

    /// <summary>Access key id and secret. Leave both empty on AWS to use the host's own credentials
    /// (an instance or task role).</summary>
    public string? AccessKey { get; set; }

    public string? SecretKey { get; set; }

    /// <summary>bucket-in-the-path addressing (http://host/bucket/key), which MinIO and most
    /// self-hosted services need. On by default whenever ServiceUrl is set.</summary>
    public bool? ForcePathStyle { get; set; }

    /// <summary>Optional folder inside the bucket to keep everything under, e.g. "troop-a/".</summary>
    public string? Prefix { get; set; }
}

/// <summary>
/// An S3-compatible bucket: AWS S3, Cloudflare R2, Backblaze B2, MinIO and the like. Containers
/// are folders inside the one bucket: {prefix}{container}/{path}.
/// </summary>
public sealed class S3BlobStore : IBlobStore, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly string _prefix;
    private readonly string _host;

    public S3BlobStore(S3StoreOptions options)
        : this(CreateClient(options), options.Bucket, options.Prefix,
            string.IsNullOrWhiteSpace(options.ServiceUrl) ? "AWS" : new Uri(options.ServiceUrl).Host)
    {
    }

    /// <summary>Over a client that's already set up (tests pass a stand-in).</summary>
    public S3BlobStore(IAmazonS3 client, string bucket, string? prefix = null, string host = "AWS")
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new InvalidOperationException("An S3 store needs a Bucket.");
        }

        _client = client;
        _bucket = bucket;
        _prefix = string.IsNullOrWhiteSpace(prefix) ? "" : prefix.TrimEnd('/') + "/";
        _host = host;
    }

    private static AmazonS3Client CreateClient(S3StoreOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Bucket))
        {
            throw new InvalidOperationException("An S3 store needs a Bucket.");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceUrl) && string.IsNullOrWhiteSpace(options.Region))
        {
            throw new InvalidOperationException(
                "An S3 store needs a Region (e.g. us-west-2) for AWS, or a ServiceUrl for any other provider.");
        }

        var config = new AmazonS3Config
        {
            // Many S3-compatible services (R2, B2, older MinIO) reject the extra checksums newer AWS
            // SDKs send on every request by default; send them only where S3 requires them.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            config.ServiceURL = options.ServiceUrl;
            config.ForcePathStyle = options.ForcePathStyle ?? true;
            if (!string.IsNullOrWhiteSpace(options.Region))
            {
                config.AuthenticationRegion = options.Region;
            }
        }
        else
        {
            config.ForcePathStyle = options.ForcePathStyle ?? false;
            if (!string.IsNullOrWhiteSpace(options.Region))
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
            }
        }

        return string.IsNullOrWhiteSpace(options.AccessKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }

    public string Description => $"S3 bucket '{_bucket}' at {_host}";

    private string Key(string container, string path) => $"{_prefix}{container}/{path}";

    private string ContainerPrefix(string container) => $"{_prefix}{container}/";

    // A bucket has no containers to create; its folders exist as soon as something is written in them.
    public Task EnsureContainerAsync(string container, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public async Task<bool> ExistsAsync(string container, string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.GetObjectMetadataAsync(_bucket, Key(container, path), cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task UploadAsync(string container, string path, Stream content, string? contentType, CancellationToken cancellationToken = default)
    {
        // Signing an upload means hashing the whole body before sending it, so it has to be
        // readable twice. Anything that isn't (a browser upload, a download from another store)
        // is spooled to a temporary file first.
        if (content.CanSeek)
        {
            await PutAsync(Key(container, path), content, contentType, cancellationToken);
            return;
        }

        await using var spooled = TemporaryFile();
        await content.CopyToAsync(spooled, cancellationToken);
        spooled.Position = 0;
        await PutAsync(Key(container, path), spooled, contentType, cancellationToken);
    }

    public Task<Stream> OpenWriteAsync(string container, string path, string? contentType, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new UploadOnDisposeStream(this, Key(container, path), contentType));

    public async Task<StoredBlob?> OpenReadAsync(string container, string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetObjectAsync(_bucket, Key(container, path), cancellationToken);
            return new StoredBlob(response.ResponseStream, response.Headers.ContentType);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string container, string path, CancellationToken cancellationToken = default) =>
        await _client.DeleteObjectAsync(_bucket, Key(container, path), cancellationToken);

    public async IAsyncEnumerable<StoredBlobInfo> ListAsync(string container, string? prefix, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var containerPrefix = ContainerPrefix(container);
        var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = containerPrefix + prefix };
        ListObjectsV2Response response;
        do
        {
            response = await _client.ListObjectsV2Async(request, cancellationToken);
            foreach (var item in response.S3Objects ?? [])
            {
                yield return new StoredBlobInfo(item.Key[containerPrefix.Length..], item.Size ?? 0, null);
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);
    }

    public async IAsyncEnumerable<string> ListFoldersAsync(string container, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var containerPrefix = ContainerPrefix(container);
        var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = containerPrefix, Delimiter = "/" };
        ListObjectsV2Response response;
        do
        {
            response = await _client.ListObjectsV2Async(request, cancellationToken);
            foreach (var folder in response.CommonPrefixes ?? [])
            {
                yield return folder[containerPrefix.Length..].TrimEnd('/');
            }

            request.ContinuationToken = response.NextContinuationToken;
        }
        while (response.IsTruncated == true);
    }

    public void Dispose() => _client.Dispose();

    private async Task PutAsync(string key, Stream content, string? contentType, CancellationToken cancellationToken)
    {
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            // A plain signed body: some S3-compatible services don't accept AWS's chunked upload signing.
            UseChunkEncoding = false,
        }, cancellationToken);
    }

    private static FileStream TemporaryFile() =>
        new(Path.Combine(Path.GetTempPath(), $"troopfin-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);

    /// <summary>S3 has no append-as-you-go writes, so this collects everything in a temporary file
    /// and uploads it in one piece when disposed.</summary>
    private sealed class UploadOnDisposeStream(S3BlobStore store, string key, string? contentType) : Stream
    {
        private readonly FileStream _spool = TemporaryFile();
        private bool _done;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_done;
        public override long Length => _spool.Length;
        public override long Position { get => _spool.Position; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => _spool.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => _spool.Write(buffer);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _spool.WriteAsync(buffer, offset, count, cancellationToken);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            _spool.WriteAsync(buffer, cancellationToken);
        public override void Flush() => _spool.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _spool.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override async ValueTask DisposeAsync()
        {
            if (!_done)
            {
                _done = true;
                try
                {
                    await _spool.FlushAsync();
                    _spool.Position = 0;
                    await store.PutAsync(key, _spool, contentType, CancellationToken.None);
                }
                finally
                {
                    await _spool.DisposeAsync();
                }
            }

            GC.SuppressFinalize(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_done)
            {
                DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            base.Dispose(disposing);
        }
    }
}
