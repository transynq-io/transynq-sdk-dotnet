using System.Globalization;
using System.Text;
using System.Text.Json;
using Transynq.Sdk.Serialization;

namespace Transynq.Sdk.Http;

/// <summary>A request built by a generated service method: method, path template and values, query, body.</summary>
internal sealed class ApiRequest(string method, string pathTemplate, string operationId)
{
    private readonly Dictionary<string, string> _path = new(StringComparer.Ordinal);
    private readonly List<KeyValuePair<string, string>> _query = [];

    public string Method { get; } = method;

    public string PathTemplate { get; } = pathTemplate;

    public string OperationId { get; } = operationId;

    public byte[]? Json { get; private set; }

    public MultipartForm? Form { get; private set; }

    /// <summary>A POST whose body carries a client-generated, non-empty <c>id</c>: replaying it returns the same record, so it may be retried.</summary>
    public bool HasClientId { get; private set; }

    public IReadOnlyList<KeyValuePair<string, string>> QueryValues => _query;

    public void Path(string name, string value) => _path[name] = Uri.EscapeDataString(value ?? throw new ArgumentNullException(name));
    public void Path(string name, Guid value) => Path(name, ParamFormat.Format(value));
    public void Path(string name, int value) => Path(name, ParamFormat.Format(value));
    public void Path(string name, long value) => Path(name, ParamFormat.Format(value));
    public void Path(string name, DateOnly value) => Path(name, ParamFormat.Format(value));
    public void Path(string name, DateTimeOffset value) => Path(name, ParamFormat.Format(value));
    public void Path(string name, bool value) => Path(name, ParamFormat.Format(value));
    public void Path(string name, decimal value) => Path(name, ParamFormat.Format(value));

    public void Query(string name, string? value)
    {
        if (value is not null) _query.Add(new(name, value));
    }

    public void Query(string name, Guid? value) => Query(name, ParamFormat.Format(value));
    public void Query(string name, int? value) => Query(name, ParamFormat.Format(value));
    public void Query(string name, long? value) => Query(name, ParamFormat.Format(value));
    public void Query(string name, DateOnly? value) => Query(name, ParamFormat.Format(value));
    public void Query(string name, DateTimeOffset? value) => Query(name, ParamFormat.Format(value));
    public void Query(string name, bool? value) => Query(name, ParamFormat.Format(value));
    public void Query(string name, decimal? value) => Query(name, ParamFormat.Format(value));

    public void Query(string name, IEnumerable<Guid>? values)
    {
        if (values is null) return;
        foreach (var v in values) Query(name, ParamFormat.Format(v));
    }

    public void Query(string name, IEnumerable<string>? values)
    {
        if (values is null) return;
        foreach (var v in values) Query(name, v);
    }

    public void Query(string name, IEnumerable<int>? values)
    {
        if (values is null) return;
        foreach (var v in values) Query(name, ParamFormat.Format(v));
    }

    public void JsonBody<T>(T body, bool clientIdCreate = false)
    {
        if (body is null) return;
        Json = TransynqJson.SerializeToUtf8Bytes(body);
        HasClientId = clientIdCreate && CarriesClientId(Json);
    }

    public void MultipartBody(MultipartForm form) => Form = form;

    public Uri BuildUri(Uri baseUri)
    {
        var path = PathTemplate;
        foreach (var (name, value) in _path) path = path.Replace("{" + name + "}", value, StringComparison.Ordinal);
        if (path.Contains('{', StringComparison.Ordinal)) throw new InvalidOperationException($"{OperationId}: path parameter missing in {path}.");
        var sb = new StringBuilder(baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/')).Append(path);
        for (var i = 0; i < _query.Count; i++)
            sb.Append(i == 0 ? '?' : '&').Append(Uri.EscapeDataString(_query[i].Key)).Append('=').Append(Uri.EscapeDataString(_query[i].Value));
        return new Uri(sb.ToString(), UriKind.Absolute);
    }

    internal static bool CarriesClientId(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("id", out var id)
                   && id.ValueKind == JsonValueKind.String
                   && Guid.TryParse(id.GetString(), out var g) && g != Guid.Empty;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>Formats path, query and form values the way the API binds them (invariant culture, ISO dates, UTC instants).</summary>
internal static class ParamFormat
{
    public static string Format(Guid value) => value.ToString("D");
    public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
    public static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
    public static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
    public static string Format(bool value) => value ? "true" : "false";
    public static string Format(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string Format(DateTimeOffset value) => UtcDateTimeOffsetConverter.Format(value);

    public static string? Format(string? value) => value;
    public static string? Format(Guid? value) => value is { } v ? Format(v) : null;
    public static string? Format(int? value) => value is { } v ? Format(v) : null;
    public static string? Format(long? value) => value is { } v ? Format(v) : null;
    public static string? Format(decimal? value) => value is { } v ? Format(v) : null;
    public static string? Format(bool? value) => value is { } v ? Format(v) : null;
    public static string? Format(DateOnly? value) => value is { } v ? Format(v) : null;
    public static string? Format(DateTimeOffset? value) => value is { } v ? Format(v) : null;
}

/// <summary>A multipart/form-data body.</summary>
internal sealed class MultipartForm
{
    private readonly List<(string Name, string? Text, FileUpload? File)> _parts = [];

    public bool IsReplayable => _parts.All(p => p.File is null || p.File.IsReplayable);

    public void AddText(string name, string? value)
    {
        if (value is not null) _parts.Add((name, value, null));
    }

    public void AddFile(string name, FileUpload? file)
    {
        if (file is not null) _parts.Add((name, null, file));
    }

    public void AddFiles(string name, IEnumerable<FileUpload>? files)
    {
        if (files is null) return;
        foreach (var f in files) AddFile(name, f);
    }

    public HttpContent CreateContent()
    {
        var content = new MultipartFormDataContent("transynq-" + Guid.NewGuid().ToString("N"));
        foreach (var (name, text, file) in _parts)
        {
            if (file is null) content.Add(new StringContent(text!, Encoding.UTF8), name);
            else content.Add(file.CreateContent(), name, file.FileName);
        }
        return content;
    }
}
