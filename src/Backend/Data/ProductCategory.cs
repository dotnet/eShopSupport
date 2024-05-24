using System.ComponentModel.DataAnnotations;

namespace eShopSupport.Backend.Data;

public class ProductCategory
{
    [Key]
    public int CategoryId { get; set; }

    public required string Name { get; set; }

    public required string NameEmbeddingBase64 { get; set; }
}
