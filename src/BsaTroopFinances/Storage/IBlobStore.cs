namespace BsaTroopFinances.Storage;

/// <summary>A stored file as listed: its path within the container, size, and type when the store knows it.</summary>
public sealed record StoredBlobInfo(string Path, long Length, string? ContentType);

/// <summary>An open stored file. The caller disposes <see cref="Content"/>.</summary>
public sealed record StoredBlob(Stream Content, string? ContentType);

/// <summary>
/// Where the app keeps files -- receipts, their thumbnails, and nightly backups -- independent of
/// the service behind it, so the app can be hosted outside Azure (see infra/HOSTING.md):
/// <see cref="AzureBlobStore"/> (Azure Blob Storage, Azurite locally), <see cref="S3BlobStore"/>
/// (Cloudflare R2, AWS S3, Backblaze B2, MinIO, ...) and <see cref="FileSystemBlobStore"/> (a
/// folder on a persistent disk). Files are grouped in named containers ("receipts", "backups"),
/// and a path may contain "/" to form folders. Every write replaces what was at that path.
/// </summary>
public interface IBlobStore
{
    /// <summary>Where this store writes, for command-line output and logs (never includes secrets).</summary>
    string Description { get; }

    Task EnsureContainerAsync(string container, CancellationToken cancellationToken = default);

    Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string container, string path, CancellationToken cancellationToken = default);

    Task UploadAsync(string container, string path, Stream content, string? contentType, CancellationToken cancellationToken = default);

    /// <summary>A stream whose contents are saved at <paramref name="path"/> once it's disposed --
    /// for writing something too large to build in memory first (a backup table, say).</summary>
    Task<Stream> OpenWriteAsync(string container, string path, string? contentType, CancellationToken cancellationToken = default);

    /// <summary>The file, or null when there's nothing at that path.</summary>
    Task<StoredBlob?> OpenReadAsync(string container, string path, CancellationToken cancellationToken = default);

    /// <summary>Removes the file; nothing happens when it isn't there.</summary>
    Task DeleteAsync(string container, string path, CancellationToken cancellationToken = default);

    /// <summary>Every file in the container whose path starts with <paramref name="prefix"/> (all of them when null).</summary>
    IAsyncEnumerable<StoredBlobInfo> ListAsync(string container, string? prefix, CancellationToken cancellationToken = default);

    /// <summary>The names of the container's top-level folders (the backup dates, for instance).</summary>
    IAsyncEnumerable<string> ListFoldersAsync(string container, CancellationToken cancellationToken = default);
}
