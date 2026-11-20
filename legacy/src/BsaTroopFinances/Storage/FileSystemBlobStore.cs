using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.StaticFiles;

namespace BsaTroopFinances.Storage;

/// <summary>
/// Files in a folder: {root}/{container}/{path}. For a single server or container with a
/// persistent disk (a VPS, a Fly.io volume), where no separate storage service is wanted. The
/// folder must survive restarts and redeploys, and backups should go somewhere else -- another
/// disk, or better an S3 bucket -- so losing the disk doesn't take the backups with it.
/// </summary>
public sealed class FileSystemBlobStore : IBlobStore
{
    private const string PartialSuffix = ".partial";
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private readonly string _root;

    public FileSystemBlobStore(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public string Description => $"folder '{_root}'";

    public Task EnsureContainerAsync(string container, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ContainerPath(container));
        return Task.CompletedTask;
    }

    public Task<bool> ContainerExistsAsync(string container, CancellationToken cancellationToken = default) =>
        Task.FromResult(Directory.Exists(ContainerPath(container)));

    public Task<bool> ExistsAsync(string container, string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(FilePath(container, path)));

    public async Task UploadAsync(string container, string path, Stream content, string? contentType, CancellationToken cancellationToken = default)
    {
        await using var target = await OpenWriteAsync(container, path, contentType, cancellationToken);
        await content.CopyToAsync(target, cancellationToken);
    }

    /// <summary>Writes beside the file and moves it into place when the stream is disposed, so a
    /// reader never sees a file while it's still being written.</summary>
    public Task<Stream> OpenWriteAsync(string container, string path, string? contentType, CancellationToken cancellationToken = default)
    {
        var file = FilePath(container, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var partial = file + "." + Guid.NewGuid().ToString("N") + PartialSuffix;
        return Task.FromResult<Stream>(new MoveOnDisposeStream(partial, file));
    }

    public Task<StoredBlob?> OpenReadAsync(string container, string path, CancellationToken cancellationToken = default)
    {
        var file = FilePath(container, path);
        if (!File.Exists(file))
        {
            return Task.FromResult<StoredBlob?>(null);
        }

        Stream content = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true);
        return Task.FromResult<StoredBlob?>(new StoredBlob(content, GuessContentType(path)));
    }

    public Task DeleteAsync(string container, string path, CancellationToken cancellationToken = default)
    {
        // File.Delete ignores a missing file but not a missing folder, which is just as absent here.
        var file = FilePath(container, path);
        if (File.Exists(file))
        {
            File.Delete(file);
        }

        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<StoredBlobInfo> ListAsync(string container, string? prefix, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var directory = ContainerPath(container);
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(PartialSuffix, StringComparison.Ordinal))
            .Select(f => (File: f, Path: Path.GetRelativePath(directory, f).Replace('\\', '/')))
            .Where(f => prefix is null || f.Path.StartsWith(prefix, StringComparison.Ordinal))
            .OrderBy(f => f.Path, StringComparer.Ordinal);
        foreach (var (file, relative) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new StoredBlobInfo(relative, new FileInfo(file).Length, GuessContentType(relative));
        }

        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> ListFoldersAsync(string container, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var directory = ContainerPath(container);
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var folder in Directory.EnumerateDirectories(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return folder!;
        }

        await Task.CompletedTask;
    }

    private static string? GuessContentType(string path) =>
        ContentTypes.TryGetContentType(path, out var type) ? type : null;

    private string ContainerPath(string container) => Path.Combine(_root, CheckedSegment(container));

    /// <summary>The file's full path, refusing anything that could step outside the container
    /// ("..", rooted or backslash paths). The app builds its own paths, but this is the last line.</summary>
    private string FilePath(string container, string path)
    {
        var segments = path.Split('/');
        foreach (var segment in segments)
        {
            CheckedSegment(segment);
        }

        return Path.Combine([ContainerPath(container), .. segments]);
    }

    private static string CheckedSegment(string segment) =>
        segment.Length == 0 || segment is "." or ".." || segment.IndexOfAny(['\\', ':', '\0']) >= 0
            ? throw new ArgumentException($"'{segment}' isn't allowed in a stored file's path.")
            : segment;

    private sealed class MoveOnDisposeStream : FileStream
    {
        private readonly string _partial;
        private readonly string _final;
        private bool _moved;

        public MoveOnDisposeStream(string partial, string final)
            : base(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true)
        {
            _partial = partial;
            _final = final;
        }

        // DisposeAsync below runs base.Dispose(false) itself, so only an explicit Dispose() moves the file here.
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                Finish();
            }
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            Finish();
        }

        private void Finish()
        {
            if (_moved)
            {
                return;
            }

            _moved = true;
            File.Move(_partial, _final, overwrite: true);
        }
    }
}
