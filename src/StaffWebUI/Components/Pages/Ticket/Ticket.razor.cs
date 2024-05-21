namespace eShopSupport.StaffWebUI.Components.Pages.Ticket;

public partial class Ticket
{
    private string GetSmartTextAreaRole()
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
            """;
        }
    }
}
