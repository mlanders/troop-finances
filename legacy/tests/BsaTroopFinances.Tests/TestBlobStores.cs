using System.Net.Sockets;
using Azure.Storage.Blobs;
using BsaTroopFinances.Storage;

namespace BsaTroopFinances.Tests;

/// <summary>
/// File stores for tests, each isolated and cleaned up afterwards: a temporary folder (always
/// available), or Azurite (skips the test unless it's running on 127.0.0.1:10000).
/// </summary>
public static class TestBlobStores
{
    public const string FileSystem = "FileSystem";
    public const string Azurite = "Azurite";

    public static TestStore Create(string kind) => kind switch
    {
        FileSystem => TemporaryFolder(),
        Azurite => AzuriteStore(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static TestStore TemporaryFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "troopfin-tests", Guid.NewGuid().ToString("N"));
        return new TestStore(new FileSystemBlobStore(root), () =>
        {
            Directory.Delete(root, recursive: true);
            return Task.CompletedTask;
        });
    }

    /// <summary>Containers named with a random prefix, so nothing else in Azurite is touched.</summary>
    public static TestStore AzuriteStore()
    {
        if (!AzuriteRunning())
        {
            // In CI (TROOPFIN_TEST_REQUIRE_DATABASE) Azurite is started for the tests: a skip
            // would hide a broken setup.
            Assert.True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SqlTestDatabase.RequireVariable)),
                "Azurite isn't running on 127.0.0.1:10000.");
            Skip.If(true, "Azurite isn't running on 127.0.0.1:10000.");
        }
        var client = new BlobServiceClient("UseDevelopmentStorage=true");
        var prefix = $"t{Guid.NewGuid():N}"[..20] + "-";
        return new TestStore(new AzureBlobStore(client, prefix), async () =>
        {
            await foreach (var container in client.GetBlobContainersAsync(prefix: prefix))
            {
                await client.DeleteBlobContainerAsync(container.Name);
            }
        });
    }

    public static async Task<byte[]?> ReadAllAsync(IBlobStore store, string container, string path)
    {
        var blob = await store.OpenReadAsync(container, path);
        if (blob is null)
        {
            return null;
        }

        await using (blob.Content)
        {
            using var buffer = new MemoryStream();
            await blob.Content.CopyToAsync(buffer);
            return buffer.ToArray();
        }
    }

    private static bool AzuriteRunning()
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", 10000).Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            return false;
        }
    }

    public sealed class TestStore(IBlobStore store, Func<Task> cleanUp) : IAsyncDisposable
    {
        public IBlobStore Store => store;

        public async ValueTask DisposeAsync() => await cleanUp();
    }
}
