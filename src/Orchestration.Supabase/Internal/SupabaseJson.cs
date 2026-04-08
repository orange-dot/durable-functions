using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Orchestration.Core.Models;
using Orchestration.Core.Workflow;
using STJ = System.Text.Json.JsonSerializer;

namespace Orchestration.Supabase.Internal;

internal static class SupabaseJson
{
    internal static readonly JsonSerializerOptions RuntimeSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowOutOfOrderMetadataProperties = true
    };

    internal static readonly JsonSerializerOptions DefinitionSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowOutOfOrderMetadataProperties = true,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true)
        }
    };

    internal static object? SerializeRuntimeValue<T>(T value)
    {
        var json = STJ.Serialize(value, RuntimeSerializerOptions);
        return JsonConvert.DeserializeObject<object?>(json);
    }

    internal static T DeserializeRuntimeValue<T>(object? raw)
    {
        if (raw is null)
        {
            return default!;
        }

        if (raw is JToken token)
        {
            var tokenJson = token.ToString(Formatting.None);

            if (typeof(T) == typeof(object))
            {
                using var document = JsonDocument.Parse(tokenJson);
                return (T)WorkflowRuntimeValueNormalizer.NormalizeJsonElement(document.RootElement, "$")!;
            }

            try
            {
                return STJ.Deserialize<T>(tokenJson, RuntimeSerializerOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize runtime payload to {typeof(T).Name}.");
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
            {
                throw CreateDeserializationException(typeof(T), raw, tokenJson, exception);
            }
        }

        if (raw is string rawJson)
        {
            if (typeof(T) == typeof(object))
            {
                if (!LooksLikeJson(rawJson))
                {
                    return (T)(object)rawJson;
                }

                using var document = JsonDocument.Parse(rawJson);
                return (T)WorkflowRuntimeValueNormalizer.NormalizeJsonElement(document.RootElement, "$")!;
            }

            try
            {
                return STJ.Deserialize<T>(rawJson, RuntimeSerializerOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize runtime payload to {typeof(T).Name}.");
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
            {
                throw CreateDeserializationException(typeof(T), raw, rawJson, exception);
            }
        }

        var normalized = WorkflowRuntimeValueNormalizer.Normalize(raw, "$");

        if (typeof(T) == typeof(object))
        {
            return (T)normalized!;
        }

        if (normalized is string normalizedJson && LooksLikeJson(normalizedJson))
        {
            try
            {
                return STJ.Deserialize<T>(normalizedJson, RuntimeSerializerOptions)
                    ?? throw new InvalidOperationException($"Failed to deserialize runtime payload to {typeof(T).Name}.");
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
            {
                throw CreateDeserializationException(typeof(T), raw, normalizedJson, exception);
            }
        }

        var json = JsonConvert.SerializeObject(normalized);
        try
        {
            return STJ.Deserialize<T>(json, RuntimeSerializerOptions)
                ?? throw new InvalidOperationException($"Failed to deserialize runtime payload to {typeof(T).Name}.");
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or NotSupportedException)
        {
            throw CreateDeserializationException(typeof(T), raw, json, exception);
        }
    }

    internal static object? SerializeDefinition(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = STJ.Serialize(definition, DefinitionSerializerOptions);
        return JsonConvert.DeserializeObject<object?>(json);
    }

    internal static WorkflowDefinition DeserializeDefinition(object? raw)
    {
        if (raw is null)
        {
            throw new InvalidOperationException("Workflow definition payload was null.");
        }

        var json = JsonConvert.SerializeObject(raw);
        return STJ.Deserialize<WorkflowDefinition>(json, DefinitionSerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize workflow definition payload.");
    }

    private static bool LooksLikeJson(string value)
    {
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            return character is '{' or '[' or '"';
        }

        return false;
    }

    private static InvalidOperationException CreateDeserializationException(
        Type targetType,
        object raw,
        string json,
        Exception innerException)
    {
        var rawType = raw.GetType().FullName ?? raw.GetType().Name;
        var snippet = json.Length <= 400 ? json : json[..400];
        return new InvalidOperationException(
            $"Failed to deserialize runtime payload to {targetType.Name}. Raw type: {rawType}. JSON: {snippet}",
            innerException);
    }
}
