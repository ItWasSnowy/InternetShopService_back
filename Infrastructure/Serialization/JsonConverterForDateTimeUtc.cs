using System.Text.Json;
using System.Text.Json.Serialization;

namespace InternetShopService_back.Infrastructure.Serialization;

/// <summary>
/// JSON конвертер для DateTime, который всегда сериализует даты в UTC формате ISO 8601 (с Z в конце)
/// Пример: "2024-01-15T10:30:00.000Z"
/// </summary>
public class JsonConverterForDateTimeUtc : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (DateTime.TryParse(stringValue, out var dateTime))
            {
                // Если дата приходит без указания UTC, считаем её UTC
                if (dateTime.Kind == DateTimeKind.Unspecified)
                {
                    return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
                }
                // Конвертируем в UTC если нужно
                return dateTime.ToUniversalTime();
            }
        }
        else if (reader.TokenType == JsonTokenType.Null)
        {
            return default;
        }
        
        throw new JsonException($"Unexpected token type {reader.TokenType} when parsing DateTime");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Всегда конвертируем в UTC и сериализуем в ISO 8601 формате с Z в конце
        DateTime utcValue;
        if (value.Kind == DateTimeKind.Utc)
        {
            utcValue = value;
        }
        else if (value.Kind == DateTimeKind.Unspecified)
        {
            // Entity Framework возвращает DateTime с Kind=Unspecified из PostgreSQL timestamp with time zone
            // Предполагаем, что это уже UTC время (так как в БД хранится UTC)
            utcValue = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
        else
        {
            // DateTimeKind.Local - конвертируем в UTC
            utcValue = value.ToUniversalTime();
        }
        writer.WriteStringValue(utcValue.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
    }
}
