namespace eShopSupport.Backend.Data;

public class Ticket
{
    public int TicketId { get; set; }
    
    public int ProductId { get; set; }

    public required string CustomerFullName { get; set; }

    public List<Message> Messages { get; set; } = new();
}
