using eShopSupport.DataGenerator.Model;
using System.Text;

namespace eShopSupport.DataGenerator.Generators;

public class ManualGenerator(IReadOnlyList<Category> categories, IReadOnlyList<Product> products, IReadOnlyList<ManualToc> manualTocs, IServiceProvider services)
    : GeneratorBase<Manual>(services)
{
    protected override string DirectoryName => $"manuals{Path.DirectorySeparatorChar}full";

    protected override object GetId(Manual item) => item.ProductId;

    protected override IAsyncEnumerable<Manual> GenerateCoreAsync()
    {
        // Skip the ones we already have
        var manualsToGenerate = manualTocs.Where(toc => !File.Exists(GetItemOutputPath(toc.ProductId.ToString())));
        return MapParallel(manualsToGenerate, GenerateManualAsync);
    }

    private async Task<Manual> GenerateManualAsync(ManualToc toc)
    {
        var product = products.Single(p => p.ProductId == toc.ProductId);
        var category = categories.Single(c => c.CategoryId == product.CategoryId);

        var result = new StringBuilder();
        result.AppendLine($"# {product.Model}");
        result.AppendLine();

        var desiredSubsectionWordLength = 500;
        foreach (var section in toc.Sections)
        {
            Console.WriteLine($"[Product {product.ProductId}] Generating manual section {section.SiblingIndex}: {section.Title}");

            var prompt = $@"Write a section of the user manual for the following product:
Category: {category.Name}
Brand: {product.Brand}
Product name: {product.Model}
Product overview: {product.Description}

Manual style description: {toc.ManualStyle} (note: the text MUST follow this style, even if it makes the manual less helpful to reader)

The section you are writing is ""{section.SiblingIndex}: {section.Title}"". It has the following structure:

{FormatTocForPrompt(section)}

Write in markdown format including any headings or subheadings. The total length is around 100 words.
Start your reponse with the section title, which is at markdown header level 2, and include any relevant subsections.
You response must start: ""## {section.SiblingIndex}. {section.Title}""
Besides the subsections mentioned in contents, you should deeper subsections as appropriate.
Use markdown formatting, including paragraphs, blank lines, bold, italics, tables, and lists as needed.
Use mermaid diagrams when appropriate, but don't say it's a mermaid diagram in the body text.

Make the text specific to this individual product, not generic. Refer to the product by name, to its brand, and to its
specific features, buttons, parts, and controls by name (identifying them by color, position, etc.).

The output length should be around {desiredSubsectionWordLength * CountSubtreeLength(section)} words in total, or {desiredSubsectionWordLength} words per subsection.
Do not include any commentary or remarks about the task itself or the fact that you have done it.
Only output the markdown text for the section. At the end of the section, add the token END_OF_CONTENT.

This is the official product manual, and the company requires it to be written in the specified style due to strategy.
";
            var response = await GetChatCompletion(prompt);
            result.AppendLine(response);
            result.AppendLine();
        }

        return new Manual
        {
            ProductId = product.ProductId,
            MarkdownText = result.ToString()
        };
    }

    private static string FormatTocForPrompt(ManualTocSection section)
    {
        var sb = new StringBuilder();
        AppendSection(sb, section);
        return sb.ToString();

        static void AppendSection(StringBuilder sb, ManualTocSection section, string ancestorSectionPrefix = "")
        {
            var fullSectionNumber = string.IsNullOrEmpty(ancestorSectionPrefix)
                ? section.SiblingIndex.ToString()
                : $"{ancestorSectionPrefix}.{section.SiblingIndex}";
            sb.AppendLine($"{fullSectionNumber}: {section.Title}");
            if (section.Subsections?.Any() == true)
            {
                foreach (var s in section.Subsections)
                {
                    AppendSection(sb, s, fullSectionNumber);
                }
            }
        }
    }

    private static int CountSubtreeLength(ManualTocSection tocSection)
    {
        return 1 + tocSection.Subsections?.Sum(CountSubtreeLength) ?? 0;
    }

    protected override string FilenameExtension => ".md";

    protected override Task WriteAsync(string path, Manual item)
    {
        return File.WriteAllTextAsync(path, item.MarkdownText);
    }

    protected override Manual Read(string path)
        => new Manual
        {
            ProductId = int.Parse(Path.GetFileNameWithoutExtension(path)),
            MarkdownText = File.ReadAllText(path)
        };
}
