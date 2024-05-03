using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;
using Microsoft.AspNetCore.Components.Rendering;

namespace eShopSupport.StaffWebUI.Components
{
    public class LinkPropertyColumn<TGridItem, TProp> : PropertyColumn<TGridItem, TProp>
    {
        [Parameter]
        public required Func<TGridItem, string>? Href { get; set; }

        protected override void CellContent(RenderTreeBuilder builder, TGridItem item)
        {
            builder.OpenElement(0, "a");
            builder.AddAttribute(1, "class", "link-col");
            builder.AddAttribute(2, "href", Href?.Invoke(item));
            builder.OpenRegion(3);
            base.CellContent(builder, item);
            builder.CloseRegion();
            builder.CloseElement();
        }
    }
}
