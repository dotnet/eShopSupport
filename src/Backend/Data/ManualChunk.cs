namespace eShopSupport.Backend.Data;

public class ManualChunk
{
    public int ProductId { get; set; }
    public int ParagraphId { get; set; }
    public required string Text { get; set; }
    public required byte[] Embedding { get; set; }
}
