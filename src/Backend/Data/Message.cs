namespace eShopSupport.Backend.Data;

public class Message
{
    public int MessageId { get; set; }

    public int TicketId { get; set; }

    public bool IsCustomerMessage { get; set; }

    public required string Text { get; set; }
}
