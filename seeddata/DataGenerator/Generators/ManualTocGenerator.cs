using eShopSupport.DataGenerator.Model;

namespace eShopSupport.DataGenerator.Generators;

public class ManualTocGenerator(IReadOnlyList<Category> categories, IReadOnlyList<Product> products, IServiceProvider services) : GeneratorBase<ManualToc>(services)
{
    protected override string DirectoryName => $"manuals{Path.DirectorySeparatorChar}toc";

    protected override object GetId(ManualToc item) => item.ProductId;

    protected override IAsyncEnumerable<ManualToc> GenerateCoreAsync()
    {
        return MapParallel(
            products.Where(p => !File.Exists(GetItemOutputPath(p.ProductId.ToString()))),
            GenerateTocForProductAsync);
    }

    private async Task<ManualToc> GenerateTocForProductAsync(Product product)
    {
        // An issue with current LLMs is that, unless strongly prompted otherwise, they tend to write
        // in a very insipid, bland style that is frustrating to read. A mitigation is to offer strong
        // and even surprising style guidance to vary the results. Some of the following styles may sound
        // silly or unprofessional, but this level of hinting is necessary to get the desired variety.
        var styles = new[] {
            "normal",
            "friendly",
            "trying to be cool and hip, with lots of emojis",
            "extremely formal and embarassingly over-polite",
            "extremely technical, with many references to industrial specifications. Require the user to perform complex diagnostics using specialized industrial and scientific equipment before and after use. Refer to standards bodies, formal industry specification codes, and academic research papers",
            "extremely badly translated from another language - most sentences are in broken English, grammatically incorrect, and misspelled",
            "confusing and often off-topic, with spelling mistakes",
            "incredibly negative and risk-averse, implying it would be unreasonable to use the product for any use case at all, and that it must not be used even for its most obvious and primary use case. Do not admit any design or manufacturing faults. Do not apologise that the product is unsuitable. No matter what the user may be trying to do, emphasize that the product must not be used in that specific way. Give examples of harms that came to prior users.",
        };
        var chosenStyle = styles[Random.Shared.Next(styles.Length)];
        var category = categories.Single(c => c.CategoryId == product.CategoryId);

        var prompt = @$"Write a suggested table of contents for the user manual for the following product:

            Category: {category.Name}
            Brand: {product.Brand}
            Product name: {product.Model}
            Overview: {product.Description}

            The manual MUST be written in the following style: {chosenStyle}
            The table of contents MUST follow that style, even if it makes the manual useless to users.
            
            The response should be a JSON object of the form
            {{
                ""sections"": [
                    {{
                        ""title"": ""..."",
                        ""subsections"": [
                            {{
                                ""title"": ""..."",
                                ""subsections"": [...]
                            }},
                            ...
                        ]
                    }},
                    ...
                ]
            }}

            Subsections can be nested up to 3 levels deep. Most sections have no subsections. Only use subsections for the most important, longest sections.";

        var toc = await GetAndParseJsonChatCompletion<ManualToc>(prompt, maxTokens: 4000);
        toc.ManualStyle = chosenStyle;
        toc.ProductId = product.ProductId;
        PopulateSiblingIndexes(toc.Sections);
        return toc;
    }

    void PopulateSiblingIndexes(List<ManualTocSection> sections)
    {
        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            section.SiblingIndex = index + 1;
            if (section.Subsections is not null)
            {
                PopulateSiblingIndexes(section.Subsections);
            }
        }
    }
}
