using Azure.Identity;
using Azure.Storage.Blobs;

namespace BsaTroopFinances.Storage;

/// <summary>
/// Builds the app's file stores from configuration (see infra/HOSTING.md). Two stores:
///
/// Main (receipts and thumbnails) -- Storage:Provider, one of
///   Azure      (the default) ConnectionStrings:AzureStorage, or Storage:BlobServiceUri to sign in
///              with the App Service's managed identity
///   S3         Storage:S3:Bucket, :ServiceUrl, :Region, :AccessKey, :SecretKey, :Prefix
///   FileSystem Storage:FileSystem:Root
///
/// Backups -- ConnectionStrings:BackupStorage (an Azure storage account, as deployed by
/// infra/main.bicep), or BackupStorage:Provider with the same S3 / FileSystem settings under
/// BackupStorage. Neither set means no backups (with a startup warning).
/// </summary>
public static class BlobStores
{
    public const string Azure = "Azure";
    public const string S3 = "S3";
    public const string FileSystem = "FileSystem";

    public static IBlobStore CreateMain(IConfiguration configuration)
    {
        var section = configuration.GetSection("Storage");
        var provider = section["Provider"];
        if (string.IsNullOrWhiteSpace(provider) || provider.Equals(Azure, StringComparison.OrdinalIgnoreCase))
        {
            return new AzureBlobStore(MainAzureClient(configuration));
        }

        return CreateOther(provider, section, "Storage");
    }

    /// <summary>The backup store, or null when backups aren't configured.</summary>
    public static IBlobStore? CreateBackup(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BackupStorage");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return new AzureBlobStore(new BlobServiceClient(connectionString));
        }

        var section = configuration.GetSection("BackupStorage");
        var provider = section["Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        if (provider.Equals(Azure, StringComparison.OrdinalIgnoreCase))
        {
            var serviceUri = section["BlobServiceUri"];
            return string.IsNullOrWhiteSpace(serviceUri)
                ? throw new InvalidOperationException("BackupStorage:Provider is Azure: set ConnectionStrings:BackupStorage or BackupStorage:BlobServiceUri.")
                : new AzureBlobStore(new BlobServiceClient(new Uri(serviceUri), new DefaultAzureCredential()));
        }

        return CreateOther(provider, section, "BackupStorage");
    }

    /// <summary>The main store's Azure client: a connection string (Azurite locally), or the
    /// account's URL signed in with the host's managed identity.</summary>
    public static BlobServiceClient MainAzureClient(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AzureStorage");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return new BlobServiceClient(connectionString);
        }

        var blobServiceUri = configuration["Storage:BlobServiceUri"];
        if (!string.IsNullOrWhiteSpace(blobServiceUri))
        {
            return new BlobServiceClient(new Uri(blobServiceUri), new DefaultAzureCredential());
        }

        throw new InvalidOperationException(
            "No storage configuration found. Set ConnectionStrings:AzureStorage (e.g. Azurite or a " +
            "connection string) or Storage:BlobServiceUri (for managed-identity auth), or choose " +
            "another store with Storage:Provider (S3 or FileSystem; see infra/HOSTING.md).");
    }

    private static IBlobStore CreateOther(string provider, IConfigurationSection section, string sectionName)
    {
        if (provider.Equals(S3, StringComparison.OrdinalIgnoreCase))
        {
            var options = section.GetSection("S3").Get<S3StoreOptions>()
                ?? throw new InvalidOperationException($"{sectionName}:Provider is S3, but {sectionName}:S3 isn't set.");
            return new S3BlobStore(options);
        }

        if (provider.Equals(FileSystem, StringComparison.OrdinalIgnoreCase))
        {
            var root = section["FileSystem:Root"];
            return string.IsNullOrWhiteSpace(root)
                ? throw new InvalidOperationException($"{sectionName}:Provider is FileSystem, but {sectionName}:FileSystem:Root isn't set.")
                : new FileSystemBlobStore(root);
        }

        throw new InvalidOperationException($"{sectionName}:Provider '{provider}' isn't known. Use {Azure}, {S3} or {FileSystem}.");
    }

    /// <summary>Copies one file between stores (or within one). Between two Azure accounts it's
    /// done inside Azure when possible; otherwise the bytes stream through this app.</summary>
    public static async Task CopyAsync(IBlobStore source, string sourceContainer, string sourcePath,
        IBlobStore destination, string destinationContainer, string destinationPath, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        if (source is AzureBlobStore azureSource && destination is AzureBlobStore azureDestination
            && await azureDestination.TryCopyFromAsync(azureSource, sourceContainer, sourcePath, destinationContainer, destinationPath, now, cancellationToken))
        {
            return;
        }

        var blob = await source.OpenReadAsync(sourceContainer, sourcePath, cancellationToken)
            ?? throw new FileNotFoundException($"'{sourceContainer}/{sourcePath}' disappeared while it was being copied.");
        await using (blob.Content)
        {
            await destination.UploadAsync(destinationContainer, destinationPath, blob.Content, blob.ContentType, cancellationToken);
        }
    }
}
