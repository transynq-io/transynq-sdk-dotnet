using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Transynq.Sdk.Serialization;

/// <summary>JSON (de)serialisation with the SDK's source-generated metadata.</summary>
public static class TransynqJson
{
    /// <summary>The serializer options (camelCase, nulls omitted, numbers readable from strings, UTC instants, ISO dates).</summary>
    public static JsonSerializerOptions Options => TransynqJsonContext.Default.Options;

    /// <summary>Metadata of a registered type.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <returns>The metadata.</returns>
    public static JsonTypeInfo<T> Info<T>()
        => TransynqJsonContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>
           ?? throw new NotSupportedException($"{typeof(T)} is not registered in TransynqJsonContext.");

    /// <summary>Serialises to UTF-8.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>UTF-8 JSON.</returns>
    public static byte[] SerializeToUtf8Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Info<T>());

    /// <summary>Serialises to a string.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>JSON.</returns>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Info<T>());

    /// <summary>Deserialises UTF-8 JSON.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <param name="utf8">UTF-8 JSON.</param>
    /// <returns>The value.</returns>
    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8) => JsonSerializer.Deserialize(utf8, Info<T>());

    /// <summary>Deserialises a JSON string.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <param name="json">JSON.</param>
    /// <returns>The value.</returns>
    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize(json, Info<T>());

    /// <summary>Deserialises a JSON element.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <param name="element">The element.</param>
    /// <returns>The value.</returns>
    public static T? Deserialize<T>(JsonElement element) => element.Deserialize(Info<T>());
}

/// <summary>
/// Instants are UTC (docs/api-conventions.md section 1): written as <c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>; a value read without an offset is UTC.
/// </summary>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    /// <inheritdoc />
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("An instant cannot be null.");
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
            return value;
        throw new JsonException($"'{text}' is not an ISO-8601 instant.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(Format(value));
    }

    internal static string Format(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
}

/// <summary>Calendar days are <c>yyyy-MM-dd</c>, never a midnight instant.</summary>
public sealed class DateOnlyConverter : JsonConverter<DateOnly>
{
    /// <inheritdoc />
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("A date cannot be null.");
        // tolerate a date-time from older payloads: the calendar day is its first ten characters
        var day = text.Length > 10 ? text[..10] : text;
        if (DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)) return value;
        throw new JsonException($"'{text}' is not a yyyy-MM-dd date.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
