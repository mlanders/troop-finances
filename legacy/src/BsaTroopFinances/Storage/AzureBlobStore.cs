using System.Runtime.CompilerServices;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace BsaTroopFinances.Storage;

/// <summary>
/// Azure Blob Storage (Azurite locally). Each container is a real blob container, private.
/// <paramref name="containerPrefix"/> namespaces the containers (tests use it to stay apart from
/// anything else in the same account).
/// </summary>
public sealed class AzureBlobStore(BlobServiceClient client, string containerPrefix = "") : IBlobStore
{
    public BlobServiceClient Client => client;

    public string Description => $"Azure storage account '{client.AccountName}'";

    private BlobContainerClient Container(string container) => client.GetBlobContainerClient(containerPrefix + container);

    public async Task EnsureContainerAsync(string container, CancellationToken cancellationToken = default) =>
        await Container(container).CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

    public async Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default) =>
        (await Container(container).ExistsAsync(cancellationToken)).Value;

    public async Task<bool> ExistsAsync(string container, string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return (await Container(container).GetBlobClient(path).ExistsAsync(cancellationToken)).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    public async Task UploadAsync(string container, string path, Stream content, string? contentType, CancellationToken cancellationToken = default) =>
        await Container(container).GetBlobClient(path).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, cancellationToken);

    public async Task<Stream> OpenWriteAsync(string container, string path, string? contentType, CancellationToken cancellationToken = default) =>
        await Container(container).GetBlobClient(path).OpenWriteAsync(overwrite: true, new BlobOpenWriteOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        }, cancellationToken);

    public async Task<StoredBlob?> OpenReadAsync(string container, string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var download = await Container(container).GetBlobClient(path).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return new StoredBlob(download.Value.Content, download.Value.Details.ContentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string container, string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await Container(container).GetBlobClient(path).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // No container, so nothing to delete.
        }
    }

    public async IAsyncEnumerable<StoredBlobInfo> ListAsync(string container, string? prefix, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in Container(container).GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: prefix, cancellationToken: cancellationToken))
        {
            yield return new StoredBlobInfo(item.Name, item.Properties.ContentLength ?? 0, item.Properties.ContentType);
        }
    }

    public async IAsyncEnumerable<string> ListFoldersAsync(string container, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in Container(container).GetBlobsByHierarchyAsync(BlobTraits.None, BlobStates.None, delimiter: "/", prefix: null, cancellationToken: cancellationToken))
        {
            if (item.IsPrefix)
            {
                yield return item.Prefix.TrimEnd('/');
            }
        }
    }

    /// <summary>Copies inside Azure without the bytes passing through this app, via a short-lived
    /// read SAS. Only possible when the source was opened with an account key (a connection
    /// string); returns false otherwise, and the caller streams the copy instead.</summary>
    public async Task<bool> TryCopyFromAsync(AzureBlobStore source, string sourceContainer, string sourcePath,
        string container, string path, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var sourceBlob = source.Container(sourceContainer).GetBlobClient(sourcePath);
        if (!sourceBlob.CanGenerateSasUri)
        {
            return false;
        }

        var sas = sourceBlob.GenerateSasUri(BlobSasPermissions.Read, now.AddMinutes(30));
        await Container(container).GetBlobClient(path).SyncCopyFromUriAsync(sas, cancellationToken: cancellationToken);
        return true;
    }
}
