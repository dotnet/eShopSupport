using System.Text.Json.Serialization;
using eShopSupport.ServiceDefaults.Clients.Backend;

namespace eShopSupport.Backend.Data;

public class Ticket
{
    public int TicketId { get; set; }
    
    public int? ProductId { get; set; }

    [JsonIgnore]
    public Product? Product { get; set; }

    public int CustomerId { get; set; }

    [JsonIgnore]
    public Customer Customer { get; set; } = default!;

    public string? ShortSummary { get; set; }
    
    public string? LongSummary { get; set; }
    
    public int? CustomerSatisfaction { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TicketStatus TicketStatus { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TicketType TicketType { get; set; }

    public List<Message> Messages { get; set; } = new();
}
