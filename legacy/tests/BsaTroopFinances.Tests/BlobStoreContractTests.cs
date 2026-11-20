using System.IO.Compression;
using System.Text;
using BsaTroopFinances.Storage;

namespace BsaTroopFinances.Tests;

/// <summary>
/// What the app relies on from any file store, run against each one: a temporary folder, an S3
/// bucket (an in-memory stand-in for the AWS client), and Azure blob storage when Azurite is running.
/// </summary>
public class BlobStoreContractTests
{
    private const string S3 = "S3";

    public static TheoryData<string> Stores => [TestBlobStores.FileSystem, S3, TestBlobStores.Azurite];

    private static TestBlobStores.TestStore Create(string kind) => kind == S3
        ? new TestBlobStores.TestStore(new S3BlobStore(new FakeS3("bucket").Client, "bucket", prefix: "troop"), () => Task.CompletedTask)
        : TestBlobStores.Create(kind);

    private static MemoryStream Text(string text) => new(Encoding.UTF8.GetBytes(text));

    private static async Task<string?> ReadTextAsync(IBlobStore store, string container, string path) =>
        await TestBlobStores.ReadAllAsync(store, container, path) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;

    [SkippableTheory]
    [MemberData(nameof(Stores))]
    public async Task A_missing_file_reads_as_absent_and_deleting_it_is_harmless(string kind)
    {
        await using var test = Create(kind);
        var store = test.Store;
        await store.EnsureContainerAsync("receipts");

        Assert.False(await store.ExistsAsync("receipts", "nobody/none.pdf"));
        Assert.Null(await store.OpenReadAsync("receipts", "nobody/none.pdf"));
        await store.DeleteAsync("receipts", "nobody/none.pdf");
        await store.DeleteAsync("never-created", "nobody/none.pdf");
    }

    [SkippableTheory]
    [MemberData(nameof(Stores))]
    public async Task A_file_reads_back_with_its_type_and_a_second_write_replaces_it(string kind)
    {
        await using var test = Create(kind);
        var store = test.Store;
        await store.EnsureContainerAsync("receipts");

        using (var first = Text("first"))
        {
            await store.UploadAsync("receipts", "maria/req1.pdf", first, "application/pdf");
        }
        using (var second = Text("second"))
        {
            await store.UploadAsync("receipts", "maria/req1.pdf", second, "application/pdf");
        }

        Assert.True(await store.ExistsAsync("receipts", "maria/req1.pdf"));
        var blob = await store.OpenReadAsync("receipts", "maria/req1.pdf");
        Assert.NotNull(blob);
        await using (blob.Content)
        {
            Assert.Equal("application/pdf", blob.ContentType);
            using var reader = new StreamReader(blob.Content);
            Assert.Equal("second", await reader.ReadToEndAsync());
        }
    }

    [SkippableTheory]
    [MemberData(nameof(Stores))]
    public async Task An_upload_that_can_only_be_read_once_is_stored_whole(string kind)
    {
        await using var test = Create(kind);
        var store = test.Store;
        await store.EnsureContainerAsync("receipts");
        var content = new string('x', 200_000);

        await using (var once = new ReadOnceStream(Encoding.UTF8.GetBytes(content)))
        {
            await store.UploadAsync("receipts", "maria/big.png", once, "image/png");
        }

        Assert.Equal(content, await ReadTextAsync(store, "receipts", "maria/big.png"));
    }

    [SkippableTheory]
    [MemberData(nameof(Stores))]
    public async Task A_written_stream_is_saved_when_disposed_even_through_gzip(string kind)
    {
        await using var test = Create(kind);
        var store = test.Store;
        await store.EnsureContainerAsync("backups");

        // As the backup writes each table: the gzip stream disposes the store's stream, then the
        // outer using disposes it again.
        await using (var target = await store.OpenWriteAsync("backups", "2026-11-14/sql/Scouts.jsonl.gz", "application/gzip"))
        await using (var gzip = new GZipStream(target, CompressionLevel.Optimal))
        await using (var writer = new StreamWriter(gzip))
        {
            await writer.WriteLineAsync("{\"RowKey\":\"alex\"}");
        }

        var blob = await store.OpenReadAsync("backups", "2026-11-14/sql/Scouts.jsonl.gz");
        Assert.NotNull(blob);
        await using var unzip = new GZipStream(blob.Content, CompressionMode.Decompress);
        using var reader = new StreamReader(unzip);
        Assert.Equal("{\"RowKey\":\"alex\"}", (await reader.ReadToEndAsync()).Trim());
    }

    [SkippableTheory]
    [MemberData(nameof(Stores))]
    public async Task Listing_finds_files_under_a_prefix_and_the_top_level_folders(string kind)
    {
        await using var test = Create(kind);
        var store = test.Store;
        await store.EnsureContainerAsync("backups");
        await store.EnsureContainerAsync("receipts");
        foreach (var path in new[] { "2026-11-13/manifest.json", "2026-11-14/manifest.json", "2026-11-14/receipts/maria/req1.pdf", "2026-11-14/sql/Scouts.jsonl.gz", "2026-11-15/manifest.json" })
        {
            using var content = Text(path);
            await store.UploadAsync("backups", path, content, null);
        }
        using (var other = Text("elsewhere"))
        {
            await store.UploadAsync("receipts", "2026-11-14/not-a-backup.pdf", other, null);
        }

        var under = new List<StoredBlobInfo>();
        await foreach (var item in store.ListAsync("backups", "2026-11-14/"))
        {
            under.Add(item);
        }

        var folders = new List<string>();
        await foreach (var folder in store.ListFoldersAsync("backups"))
        {
            folders.Add(folder);
        }

        Assert.Equal(["2026-11-14/manifest.json", "2026-11-14/receipts/maria/req1.pdf", "2026-11-14/sql/Scouts.jsonl.gz"], under.Select(i => i.Path).Order(StringComparer.Ordinal));
        Assert.Equal("2026-11-14/manifest.json".Length, under.Single(i => i.Path == "2026-11-14/manifest.json").Length);
        Assert.Equal(["2026-11-13", "2026-11-14", "2026-11-15"], folders.Order(StringComparer.Ordinal));
    }

    [SkippableTheory]
    [MemberData(nameof(Stores))]
    public async Task Copying_between_stores_keeps_the_bytes_and_type(string kind)
    {
        await using var source = Create(kind);
        await using var destination = TestBlobStores.TemporaryFolder();
        await source.Store.EnsureContainerAsync("receipts");
        using (var content = Text("receipt-bytes"))
        {
            await source.Store.UploadAsync("receipts", "maria/req1.pdf", content, "application/pdf");
        }

        await BlobStores.CopyAsync(source.Store, "receipts", "maria/req1.pdf", destination.Store, "backups", "day/receipts/maria/req1.pdf", DateTimeOffset.UtcNow);

        Assert.Equal("receipt-bytes", await ReadTextAsync(destination.Store, "backups", "day/receipts/maria/req1.pdf"));
    }

    [Fact]
    public async Task A_folder_store_refuses_paths_that_leave_its_folder()
    {
        await using var test = TestBlobStores.TemporaryFolder();
        using var content = Text("x");

        await Assert.ThrowsAsync<ArgumentException>(() => test.Store.UploadAsync("receipts", "../outside.pdf", content, null));
        await Assert.ThrowsAsync<ArgumentException>(() => test.Store.UploadAsync("receipts", "maria/../../outside.pdf", content, null));
        await Assert.ThrowsAsync<ArgumentException>(() => test.Store.OpenReadAsync("..", "receipts/x.pdf"));
        await Assert.ThrowsAsync<ArgumentException>(() => test.Store.UploadAsync("receipts", "C:\\outside.pdf", content, null));
    }

    [Fact]
    public async Task An_s3_store_keeps_everything_under_its_prefix_and_signs_uploads_from_a_rereadable_body()
    {
        var s3 = new FakeS3("bucket");
        var store = new S3BlobStore(s3.Client, "bucket", prefix: "troop441/");

        await using (var once = new ReadOnceStream("receipt"u8.ToArray()))
        {
            await store.UploadAsync("receipts", "maria/req1.pdf", once, "application/pdf");
        }
        await using (var target = await store.OpenWriteAsync("backups", "day/manifest.json", "application/json"))
        {
            await target.WriteAsync("{}"u8.ToArray());
        }

        Assert.Equal(["troop441/backups/day/manifest.json", "troop441/receipts/maria/req1.pdf"], s3.Keys);
        Assert.Equal(0, s3.UnseekableUploads);
        Assert.Equal("S3 bucket 'bucket' at AWS", store.Description);
    }

    /// <summary>A body like a browser upload's: read forward once, no seeking, no length.</summary>
    private sealed class ReadOnceStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
