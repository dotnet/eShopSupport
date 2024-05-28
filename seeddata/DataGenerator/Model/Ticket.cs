namespace eShopSupport.DataGenerator.Model;

public class Ticket
{
    public int TicketId { get; set; }

    public int ProductId { get; set; }

    public required string CustomerFullName { get; set; }

    public required string Message { get; set; }

    public string? CustomerSituation { get; set; }

    public string? CustomerStyle { get; set; }
}
