using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestration.Core.Models;

/// <summary>
/// Thrown when a workflow runtime value cannot be normalized into the canonical JSON-compatible shape.
/// </summary>
public sealed class WorkflowRuntimeValueNormalizationException : InvalidOperationException
{
    public WorkflowRuntimeValueNormalizationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Normalizes dynamic workflow runtime values into a JSON-compatible tree.
/// </summary>
public static class WorkflowRuntimeValueNormalizer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static object? Normalize(object? value, string context)
    {
        return value switch
        {
            null => null,
            string text => text,
            char character => character.ToString(),
            bool boolean => boolean,
            byte number => (long)number,
            sbyte number => (long)number,
            short number => (long)number,
            ushort number => (long)number,
            int number => (long)number,
            uint number => (long)number,
            long number => number,
            ulong number when number <= long.MaxValue => (long)number,
            ulong => throw CreateUnsupportedValueException(context, value, "Unsigned integer exceeds Int64 range."),
            float number => NormalizeFloatingPoint(number, context),
            double number => NormalizeFloatingPoint(number, context),
            decimal number => NormalizeDecimal(number, context),
            JsonElement element => NormalizeJsonElement(element, context),
            JsonDocument document => NormalizeJsonElement(document.RootElement, context),
            IDictionary<string, object?> dictionary => NormalizeDictionary(dictionary, context),
            IEnumerable enumerable when value is not string && value is not byte[] => NormalizeList(enumerable, context),
            _ => NormalizeSerializableObject(value, context)
        };
    }

    public static Dictionary<string, object?>? NormalizeDictionary(
        IDictionary<string, object?>? value,
        string context)
    {
        if (value == null)
        {
            return null;
        }

        var normalized = new Dictionary<string, object?>(value.Count);
        foreach (var (key, item) in value)
        {
            normalized[key] = Normalize(item, $"{context}.{key}");
        }

        return normalized;
    }

    public static object? NormalizeJsonElement(JsonElement element, string context)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => NormalizeJsonObject(element, context),
            JsonValueKind.Array => NormalizeJsonArray(element, context),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => NormalizeFloatingPoint(doubleValue, context),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => throw CreateUnsupportedValueException(context, element, $"Unsupported JSON value kind '{element.ValueKind}'.")
        };
    }

    public static void WriteNormalizedValue(Utf8JsonWriter writer, object? value)
    {
        switch (Normalize(value, "$"))
        {
            case null:
                writer.WriteNullValue();
                break;

            case string text:
                writer.WriteStringValue(text);
                break;

            case bool boolean:
                writer.WriteBooleanValue(boolean);
                break;

            case long number:
                writer.WriteNumberValue(number);
                break;

            case double number:
                writer.WriteNumberValue(number);
                break;

            case Dictionary<string, object?> dictionary:
                writer.WriteStartObject();
                foreach (var (key, item) in dictionary)
                {
                    writer.WritePropertyName(key);
                    WriteNormalizedValue(writer, item);
                }
                writer.WriteEndObject();
                break;

            case List<object?> list:
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    WriteNormalizedValue(writer, item);
                }
                writer.WriteEndArray();
                break;

            default:
                throw CreateUnsupportedValueException("$", value, "Unexpected normalized runtime value type.");
        }
    }

    private static Dictionary<string, object?> NormalizeJsonObject(JsonElement element, string context)
    {
        var normalized = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            normalized[property.Name] = NormalizeJsonElement(property.Value, $"{context}.{property.Name}");
        }

        return normalized;
    }

    private static List<object?> NormalizeJsonArray(JsonElement element, string context)
    {
        var normalized = new List<object?>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            normalized.Add(NormalizeJsonElement(item, $"{context}[{index}]"));
            index++;
        }

        return normalized;
    }

    private static List<object?> NormalizeList(IEnumerable value, string context)
    {
        var normalized = new List<object?>();
        var index = 0;
        foreach (var item in value)
        {
            normalized.Add(Normalize(item, $"{context}[{index}]"));
            index++;
        }

        return normalized;
    }

    private static object NormalizeSerializableObject(object value, string context)
    {
        try
        {
            var element = JsonSerializer.SerializeToElement(value, SerializerOptions);
            return NormalizeJsonElement(element, context)
                ?? throw CreateUnsupportedValueException(context, value, "Serialized runtime value normalized to null unexpectedly.");
        }
        catch (WorkflowRuntimeValueNormalizationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CreateUnsupportedValueException(
                context,
                value,
                "Runtime values must be JSON-compatible trees or JSON-serializable objects.",
                exception);
        }
    }

    private static double NormalizeFloatingPoint(double value, string context)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw CreateUnsupportedValueException(context, value, "Floating-point values must be finite.");
        }

        return value;
    }

    private static double NormalizeFloatingPoint(float value, string context)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw CreateUnsupportedValueException(context, value, "Floating-point values must be finite.");
        }

        return value;
    }

    private static object NormalizeDecimal(decimal value, string context)
    {
        if (value >= long.MinValue && value <= long.MaxValue && decimal.Truncate(value) == value)
        {
            return (long)value;
        }

        var asDouble = (double)value;
        if (double.IsNaN(asDouble) || double.IsInfinity(asDouble))
        {
            throw CreateUnsupportedValueException(context, value, "Decimal value is outside the supported double range.");
        }

        return asDouble;
    }

    private static WorkflowRuntimeValueNormalizationException CreateUnsupportedValueException(
        string context,
        object? value,
        string message,
        Exception? innerException = null)
    {
        var typeName = value?.GetType().FullName ?? "null";
        return new WorkflowRuntimeValueNormalizationException(
            $"Invalid workflow runtime value at '{context}'. {message} Value type: {typeName}.",
            innerException);
    }
}

/// <summary>
/// JSON converter for dynamic runtime values stored as object.
/// </summary>
public sealed class WorkflowRuntimeValueJsonConverter : JsonConverter<object?>
{
    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return WorkflowRuntimeValueNormalizer.NormalizeJsonElement(document.RootElement, "$");
    }

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        WorkflowRuntimeValueNormalizer.WriteNormalizedValue(writer, value);
    }
}

/// <summary>
/// JSON converter for dynamic runtime dictionaries keyed by string.
/// </summary>
public sealed class WorkflowRuntimeValueDictionaryJsonConverter : JsonConverter<Dictionary<string, object?>?>
{
    public override Dictionary<string, object?>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        return WorkflowRuntimeValueNormalizer.NormalizeJsonElement(document.RootElement, "$") switch
        {
            null => null,
            Dictionary<string, object?> dictionary => dictionary,
            _ => throw new WorkflowRuntimeValueNormalizationException(
                "Invalid workflow runtime dictionary value at '$'. JSON object expected.")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        Dictionary<string, object?>? value,
        JsonSerializerOptions options)
    {
        WorkflowRuntimeValueNormalizer.WriteNormalizedValue(writer, value);
    }
}
