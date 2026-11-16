# Dates, money and enums (type mapping)

| OpenAPI | Meaning | .NET |
|---|---|---|
| `string` | text | `string` |
| `string` / `uuid` | identifier | `Guid` |
| `string` / `date` (C# `DateOnly`) | calendar day `yyyy-MM-dd`, never a midnight instant | `DateOnly` |
| `string` / `date-time` | instant, always UTC on the wire (`2026-10-02T03:04:05Z`) | `DateTimeOffset` (UTC; values read without an offset are UTC) |
| `integer` / `int32`, `int64` | whole number (the API also accepts it as a string) | `int`, `long` |
| `number` | money, rates, quantities: `decimal(18,2)` in the API (the spec cannot tell decimals from doubles, so every number is a decimal) | `decimal` |
| `boolean` | flag | `bool` |
| `array`, `additionalProperties` | list, map | `IReadOnlyList<T>`, `IReadOnlyDictionary<string, T>` |
| untyped object, `JsonElement` | free-form JSON (grid rows, long-operation results) | `JsonElement` |
| `binary` (`IFormFile`) | uploaded file | `FileUpload` |

Nullable members are `T?`; members not in the schema's `required` list are nullable too, so you can leave them unset. Unset (null) members are omitted from request bodies. Unknown JSON properties in answers are ignored. Codes such as `status` or `kind` are plain strings (the contract declares no enums), so a new code never breaks deserialisation; well-known values are constants such as `LongOperationStatus.Succeeded`. Serialisation uses System.Text.Json source generation (`TransynqJsonContext`), so the SDK is trimming- and AOT-friendly.

## Rules worth knowing

- **Calendar days are not instants.** A `date` member (delivery day, public holiday) is `yyyy-MM-dd`. Never send a midnight
  instant for it.
- **Instants are UTC.** The SDK always writes them in UTC with `Z`; the company's time zone (`MeResponse.timeZone`) is for display only.
- **Money is decimal.** The API stores `decimal(18,2)` values and rounds them in its domain; the SDK never converts them through
  binary floating point.
- **Codes are open.** `status`, `kind`, `reasonType` and similar members are strings. New values may appear in later API versions;
  handle unknown values instead of failing.
- **Forward compatible.** Properties the SDK does not know yet are ignored when reading.
