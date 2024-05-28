namespace eShopSupport.DataGenerator.Model;

public class TicketThreadMessage
{
    public int MessageId { get; set; }

    public required Role AuthorRole { get; set; }

    public required string Text { get; set; }
}
