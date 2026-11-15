using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using NSubstitute;

namespace BsaTroopFinances.Tests;

/// <summary>
/// An in-memory stand-in for an S3 bucket, enough to run <see cref="Storage.S3BlobStore"/> without
/// a real service: put, get, metadata, delete and paged listing (two keys a page, so paging is
/// always exercised). Records whether each upload body could be read twice, as signing needs.
/// </summary>
public sealed class FakeS3
{
    private readonly SortedDictionary<string, (byte[] Content, string? ContentType)> _objects = new(StringComparer.Ordinal);

    public FakeS3(string bucket)
    {
        Bucket = bucket;
        Client = Substitute.For<IAmazonS3>();

        Client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var request = call.Arg<PutObjectRequest>();
            CheckBucket(request.BucketName);
            if (!request.InputStream.CanSeek)
            {
                UnseekableUploads++;
            }

            using var buffer = new MemoryStream();
            request.InputStream.CopyTo(buffer);
            _objects[request.Key] = (buffer.ToArray(), request.ContentType);
            return new PutObjectResponse();
        });

        Client.GetObjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            CheckBucket(call.ArgAt<string>(0));
            var (content, contentType) = Find(call.ArgAt<string>(1));
            var response = new GetObjectResponse { ResponseStream = new MemoryStream(content) };
            response.Headers.ContentType = contentType;
            return response;
        });

        Client.GetObjectMetadataAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            CheckBucket(call.ArgAt<string>(0));
            Find(call.ArgAt<string>(1));
            return new GetObjectMetadataResponse();
        });

        Client.DeleteObjectAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            CheckBucket(call.ArgAt<string>(0));
            _objects.Remove(call.ArgAt<string>(1));
            return new DeleteObjectResponse();
        });

        Client.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>()).Returns(call => List(call.Arg<ListObjectsV2Request>()));
    }

    public IAmazonS3 Client { get; }

    public string Bucket { get; }

    public int UnseekableUploads { get; private set; }

    public IReadOnlyCollection<string> Keys => _objects.Keys;

    private ListObjectsV2Response List(ListObjectsV2Request request)
    {
        CheckBucket(request.BucketName);
        const int pageSize = 2;
        var prefix = request.Prefix ?? "";

        // Each entry is a key, or with a delimiter a "folder/" standing for every key under it.
        var entries = _objects.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k =>
            {
                if (string.IsNullOrEmpty(request.Delimiter))
                {
                    return (Entry: k, IsPrefix: false);
                }

                var cut = k.IndexOf(request.Delimiter, prefix.Length, StringComparison.Ordinal);
                return cut < 0 ? (Entry: k, IsPrefix: false) : (Entry: k[..(cut + request.Delimiter.Length)], IsPrefix: true);
            })
            .Distinct()
            .OrderBy(e => e.Entry, StringComparer.Ordinal)
            .ToList();

        var start = string.IsNullOrEmpty(request.ContinuationToken) ? 0 : int.Parse(request.ContinuationToken);
        var page = entries.Skip(start).Take(pageSize).ToList();
        var more = start + pageSize < entries.Count;
        return new ListObjectsV2Response
        {
            S3Objects = page.Where(e => !e.IsPrefix).Select(e => new S3Object { Key = e.Entry, Size = _objects[e.Entry].Content.Length }).ToList(),
            CommonPrefixes = page.Where(e => e.IsPrefix).Select(e => e.Entry).ToList(),
            IsTruncated = more,
            NextContinuationToken = more ? (start + pageSize).ToString() : null,
        };
    }

    private (byte[] Content, string? ContentType) Find(string key) =>
        _objects.TryGetValue(key, out var found)
            ? found
            : throw new AmazonS3Exception("Not Found", ErrorType.Sender, "NoSuchKey", "request-id", HttpStatusCode.NotFound);

    private void CheckBucket(string bucket)
    {
        if (bucket != Bucket)
        {
            throw new InvalidOperationException($"Wrong bucket '{bucket}'.");
        }
    }
}
