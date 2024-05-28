namespace eShopSupport.DataGenerator.Model;

public class Category
{
    public int CategoryId { get; set; }
    public required string Name { get; set; }
    public required string[] Brands { get; set; }
}
