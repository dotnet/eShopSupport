namespace eShopSupport.DataGenerator.Model;

public class ManualToc
{
    public int ProductId { get; set; }

    public string? ManualStyle { get; set; }

    public required List<ManualTocSection> Sections { get; set; }
}

public class ManualTocSection
{
    public int SiblingIndex { get; set; }

    public required string Title { get; set; }

    public List<ManualTocSection>? Subsections { get; set; }
}
