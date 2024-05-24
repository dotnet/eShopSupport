using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace eShopSupport.Backend.Data;

public class ProductCategory
{
    [Key]
    public int CategoryId { get; set; }

    public required string Name { get; set; }

    [JsonConverter(typeof(EmbeddingJsonConverter))]
    public required ReadOnlyMemory<float> NameEmbedding { get; set; }
}
