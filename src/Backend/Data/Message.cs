namespace eShopSupport.Backend.Data;

public class Message
{
    public int MessageId { get; set; }

    public required string AuthorName { get; set; }

    public required string Text { get; set; }
}
