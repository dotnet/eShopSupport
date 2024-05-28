using System.Text.Json.Serialization;

namespace eShopSupport.DataGenerator.Model;

public class TicketThread
{
    public int TicketId { get; set; }

    public int ProductId { get; set; }

    public required string CustomerFullName { get; set; }

    public required List<TicketThreadMessage> Messages { get; set; }

    public string? ShortSummary { get; set; }

    public string? LongSummary { get; set; }

    public int? CustomerSatisfaction { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TicketStatus? TicketStatus { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TicketType? TicketType { get; set; }
}

public enum TicketStatus
{
    Open,
    Closed,
}

public enum TicketType
{
    Question,
    Idea,
    Complaint,
    Returns,
}
