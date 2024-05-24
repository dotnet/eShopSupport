using eShopSupport.ServiceDefaults.Clients.Backend;
using Microsoft.AspNetCore.Components.Rendering;

namespace eShopSupport.StaffWebUI.Components;

public class CustomerSatisfactionColumn : LinkTemplateColumn<ListTicketsResultItem>
{
    protected override void InnerContent(RenderTreeBuilder builder, ListTicketsResultItem item)
    {
        if (item.CustomerSatisfaction.HasValue)
        {
            builder.OpenElement(0, "progress");
            builder.AddAttribute(1, "title", $"Satisfaction: {item.CustomerSatisfaction}");
            builder.AddAttribute(2, "max", 9);
            builder.AddAttribute(3, "value", item.CustomerSatisfaction - 1);
            builder.CloseElement();
        }
    }
}
