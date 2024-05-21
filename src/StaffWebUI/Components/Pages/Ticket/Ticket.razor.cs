
using System.Text;

namespace eShopSupport.StaffWebUI.Components.Pages.Ticket;

public partial class Ticket
{
    private string GetSmartTextAreaRole(List<TicketAssistant.MessageState>? messages)
    {
        if (ticket is null)
        {
            return smartTextAreaRolePrefix;
        }
        else
        {
            return $$"""
            {{smartTextAreaRolePrefix}}, currently helping a customer {{ticket.CustomerFullName}} with this enquiry:
            <ticket_summary>{{ticket.LongSummary}}</ticket_summary>
            <first_customer_message>{{ticket.Messages.FirstOrDefault(m => m.AuthorName != "Support")?.MessageText}}</first_customer_message>
            <last_customer_message>{{ticket.Messages.LastOrDefault(m => m.AuthorName != "Support")?.MessageText}}</first_customer_message>
            <suggestions>
                {{GetSmartTextAreaSuggestions(messages)}}
            </suggestions>
            """;
        }
    }

    private static string GetSmartTextAreaSuggestions(List<TicketAssistant.MessageState>? messages)
    {
        if (messages is null)
        {
            return string.Empty;
        }

        var suggestions = new StringBuilder();
        foreach (var message in messages.Where(m => m.Message.IsAssistant))
        {
            suggestions.Append($"<suggestion>{message.Message.Text}</suggestion>\n");
        }

        return suggestions.ToString();
    }
}
