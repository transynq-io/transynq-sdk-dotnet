using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Transynq.Sdk.Serialization;

namespace Transynq.Sdk;

/// <summary>An answer of the API: the data plus the headers integrators need (ETag, API version, correlation id, Location).</summary>
/// <typeparam name="T">The data type.</typeparam>
public sealed class ApiResponse<T>
{
    internal ApiResponse(int statusCode, T data, IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
    {
        StatusCode = statusCode;
        Data = data;
        Headers = headers;
    }

    /// <summary>HTTP status (200, 201, 202, 204).</summary>
    public int StatusCode { get; }

    /// <summary>The deserialised body (default when the body is empty).</summary>
    public T Data { get; }

    /// <summary>Response headers (case-insensitive names).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }

    /// <summary>The <c>ETag</c> header verbatim (quotes kept), to send back as <see cref="RequestOptions.IfMatch"/>.</summary>
    public string? ETag => Header("ETag");

    /// <summary>The contract version that answered (<c>X-Api-Version</c>).</summary>
    public string? ApiVersion => Header("X-Api-Version");

    /// <summary>The request's correlation id (<c>X-Correlation-Id</c>), to quote to support.</summary>
    public string? CorrelationId => Header("X-Correlation-Id");

    /// <summary>The <c>Location</c> header (a 202 points at <c>/api/v1/long-operations/{id}</c>).</summary>
    public string? Location => Header("Location");

    /// <summary>Whether the API accepted long-running work (202 with a Location); see <see cref="TransynqClient.PollLongOperation(ApiResponse{ResponseBody})"/>.</summary>
    public bool IsAccepted => StatusCode == 202;

    /// <summary>The first value of a header, or null.</summary>
    /// <param name="name">Header name (case-insensitive).</param>
    /// <returns>The value or null.</returns>
    public string? Header(string name) => Headers.TryGetValue(name, out var v) && v.Count > 0 ? v[0] : null;

    /// <summary>The data.</summary>
    /// <param name="response">The response.</param>
#pragma warning disable CA2225 // a named alternate exists: Data
    public static implicit operator T(ApiResponse<T> response)
#pragma warning restore CA2225
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Data;
    }
}

/// <summary>
/// A buffered body of an operation whose contract does not declare its response (JSON, a small file, or nothing).
/// </summary>
public sealed class ResponseBody
{
    internal ResponseBody(byte[] content, string? contentType, string? fileName)
    {
        Content = content;
        ContentType = contentType;
        FileName = fileName;
    }

    /// <summary>The raw bytes.</summary>
#pragma warning disable CA1819
    public byte[] Content { get; }
#pragma warning restore CA1819

    /// <summary>The <c>Content-Type</c> media type, if any.</summary>
    public string? ContentType { get; }

    /// <summary>The file name from <c>Content-Disposition</c>, if any.</summary>
    public string? FileName { get; }

    /// <summary>True when the body is empty (204 or an empty 200).</summary>
    public bool IsEmpty => Content.Length == 0;

    /// <summary>True when the body is JSON.</summary>
    public bool IsJson => ContentType is { } t && (t.Contains("json", StringComparison.OrdinalIgnoreCase));

    /// <summary>The body as text (UTF-8).</summary>
    /// <returns>The text.</returns>
    public string AsText() => Encoding.UTF8.GetString(Content);

    /// <summary>The body as a JSON element, or null when empty.</summary>
    /// <returns>The element.</returns>
    public JsonElement? AsJson()
    {
        if (IsEmpty) return null;
        using var doc = JsonDocument.Parse(Content);
        return doc.RootElement.Clone();
    }

    /// <summary>The body deserialised as a generated model (or any type registered in <see cref="TransynqJsonContext"/>).</summary>
    /// <typeparam name="T">Target type.</typeparam>
    /// <returns>The value, or default when empty.</returns>
    public T? As<T>() => IsEmpty ? default : TransynqJson.Deserialize<T>(Content);
}

/// <summary>A streamed file answer. Dispose it (it holds the connection) after reading <see cref="Content"/>.</summary>
public sealed class FileDownload : IDisposable, IAsyncDisposable
{
    private readonly HttpResponseMessage _response;

    internal FileDownload(HttpResponseMessage response, Stream content)
    {
        _response = response;
        Content = content;
        ContentType = response.Content.Headers.ContentType?.MediaType;
        FileName = Http.HttpPipeline.FileNameOf(response);
        Length = response.Content.Headers.ContentLength;
    }

    /// <summary>The file content (read once).</summary>
    public Stream Content { get; }

    /// <summary>The media type, e.g. <c>application/pdf</c>.</summary>
    public string? ContentType { get; }

    /// <summary>The file name from <c>Content-Disposition</c>.</summary>
    public string? FileName { get; }

    /// <summary>The length when the server sent it.</summary>
    public long? Length { get; }

    /// <summary>Copies the content to a file.</summary>
    /// <param name="path">Destination path (overwritten).</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>A task.</returns>
    public async Task SaveToFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var file = File.Create(path);
        await using (file.ConfigureAwait(false))
            await Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the whole content into memory.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The bytes.</returns>
    public async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await Content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Content.Dispose();
        _response.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync().ConfigureAwait(false);
        _response.Dispose();
    }
}

/// <summary>A file to upload in a multipart form.</summary>
public sealed class FileUpload
{
    private readonly byte[]? _bytes;
    private readonly Stream? _stream;
    private readonly long _start;

    private FileUpload(byte[]? bytes, Stream? stream, string fileName, string contentType)
    {
        _bytes = bytes;
        _stream = stream;
        _start = stream is { CanSeek: true } ? stream.Position : 0;
        FileName = fileName;
        ContentType = contentType;
    }

    /// <summary>The file name sent to the API.</summary>
    public string FileName { get; }

    /// <summary>The media type sent to the API.</summary>
    public string ContentType { get; }

    /// <summary>Whether the content can be sent again (bytes, or a seekable stream), so the request can be retried.</summary>
    public bool IsReplayable => _bytes is not null || _stream is { CanSeek: true };

    /// <summary>A file from bytes.</summary>
    /// <param name="content">The bytes.</param>
    /// <param name="fileName">File name.</param>
    /// <param name="contentType">Media type (default <c>application/octet-stream</c>).</param>
    /// <returns>The upload.</returns>
    public static FileUpload FromBytes(byte[] content, string fileName, string contentType = "application/octet-stream")
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(content, null, fileName, contentType);
    }

    /// <summary>A file from a stream (not disposed by the SDK). A non-seekable stream makes the request non-retryable.</summary>
    /// <param name="content">The stream.</param>
    /// <param name="fileName">File name.</param>
    /// <param name="contentType">Media type (default <c>application/octet-stream</c>).</param>
    /// <returns>The upload.</returns>
    public static FileUpload FromStream(Stream content, string fileName, string contentType = "application/octet-stream")
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(null, content, fileName, contentType);
    }

    /// <summary>A file read from disk.</summary>
    /// <param name="path">The path.</param>
    /// <param name="contentType">Media type (default <c>application/octet-stream</c>).</param>
    /// <returns>The upload.</returns>
    public static FileUpload FromFile(string path, string contentType = "application/octet-stream")
        => new(File.ReadAllBytes(path), null, Path.GetFileName(path), contentType);

    internal HttpContent CreateContent()
    {
        HttpContent content;
        if (_bytes is not null)
        {
            content = new ByteArrayContent(_bytes);
        }
        else
        {
            if (_stream!.CanSeek) _stream.Position = _start;
            content = new StreamContent(new NonDisposingStream(_stream));
        }
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ContentType);
        return content;
    }

    private sealed class NonDisposingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>A page of a collection (<c>GET /resource?page=&amp;pageSize=</c>, docs/api-conventions.md section 4).</summary>
/// <typeparam name="T">Item type.</typeparam>
public sealed record PagedResponse<T>
{
    /// <summary>The items of this page.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>1-based page number.</summary>
    [JsonPropertyName("page")]
    public int Page { get; init; }

    /// <summary>Page size (1..200).</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }

    /// <summary>Total number of items.</summary>
    [JsonPropertyName("total")]
    public long Total { get; init; }
}

/// <summary>Auto-paging over <see cref="PagedResponse{T}"/> operations.</summary>
public static class Paging
{
    /// <summary>Reads page after page until an empty page or <c>page * pageSize &gt;= total</c>.</summary>
    /// <typeparam name="T">Item type.</typeparam>
    /// <param name="fetch">Reads one page.</param>
    /// <param name="startPage">First page.</param>
    /// <param name="cancellationToken">Cancels the iteration.</param>
    /// <returns>Every item.</returns>
    public static async IAsyncEnumerable<T> IterateAsync<T>(Func<int, CancellationToken, Task<ApiResponse<PagedResponse<T>>>> fetch, int startPage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fetch);
        var page = Math.Max(1, startPage);
        while (true)
        {
            var response = await fetch(page, cancellationToken).ConfigureAwait(false);
            var data = response.Data;
            if (data is null || data.Items.Count == 0) yield break;
            foreach (var item in data.Items) yield return item;
            if ((long)data.Page * data.PageSize >= data.Total) yield break;
            page++;
        }
    }
}
