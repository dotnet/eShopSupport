namespace eShopSupport.DataGenerator.Model;

public class Product
{
    public int CategoryId { get; set; }

    public int ProductId { get; set; }

    public string? Brand { get; set; }

    public required string Model { get; set; }

    public required string Description { get; set; }

    public decimal Price { get; set; }
}
