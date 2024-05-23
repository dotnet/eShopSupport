using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace eShopSupport.Backend.Data;

public class Product
{
    public int ProductId { get; set; }
    public int CategoryId { get; set; }
    public required string Brand { get; set; }
    public required string Model { get; set; }
    public required string Description { get; set; }
    public decimal Price { get; set; }

    [NotMapped, JsonConverter(typeof(EmbeddingJsonConverter))]
    public required ReadOnlyMemory<float> NameEmbedding { get; set; }
}

class EmbeddingJsonConverter : JsonConverter<ReadOnlyMemory<float>>
{
    public override ReadOnlyMemory<float> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new InvalidOperationException($"JSON deserialization failed because the value type was {reader.TokenType} but should be {JsonTokenType.String}");
        }

        var bytes = reader.GetBytesFromBase64();
        var floats = MemoryMarshal.Cast<byte, float>(bytes);
        return floats.ToArray(); // TODO: Can we avoid copying? The memory is already in the right format.
    }

    public override void Write(Utf8JsonWriter writer, ReadOnlyMemory<float> value, JsonSerializerOptions options)
    {
        var bytes = MemoryMarshal.AsBytes(value.Span);
        writer.WriteBase64StringValue(bytes);
    }
}
