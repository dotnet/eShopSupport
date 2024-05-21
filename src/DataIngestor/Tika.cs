using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace AspirePython.VectorDbIngestor;

public class Tika : IDisposable
{
    private readonly DistributedApplication _app;
    private readonly HttpClient _httpClient;

    public Tika()
    {
        // Using Aspire as an internal implementation detail of this class (specifically, to
        // deal with starting up a container) is certainly not how Aspire is intended to be used.
        // Consider this an exploration, and possibly fine for an offline data ingestion process,
        // but don't assume it's wise to do this in an online app.
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions() { DisableDashboard = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Polly"] = "Error",
        });
        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        // Start Apache Tika in a container
        var tika = builder
            .AddContainer("Tika", "apache/tika", "latest")
            .WithHttpEndpoint(targetPort: 9998);
        _app = builder.Build();
        _app.Start();

        _httpClient = _app.Services.GetRequiredService<HttpClient>();
        _httpClient.BaseAddress = new Uri(tika.Resource.GetEndpoint("http").Url);
    }

    public async Task<ExtractedText[]> ExtractTextAsync(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);

        var request = new HttpRequestMessage(HttpMethod.Put, "/tika");
        request.Content = new StreamContent(fileStream);
        request.Content.Headers.ContentType = new(GetContentType(filePath));
        request.Headers.Accept.Add(new("text/xml"));

        var response = await _httpClient.SendAsync(request);
        var xmlText = await response.Content.ReadAsStringAsync();

        return ProcessXml(xmlText);
    }

    private ExtractedText[] ProcessXml(string xmlText)
    {
        var doc = XDocument.Parse(xmlText);
        var ns = doc.Root!.GetDefaultNamespace().NamespaceName;
        var manager = new XmlNamespaceManager(doc.CreateNavigator().NameTable);
        manager.AddNamespace("xhtml", ns);
        var pages = doc.XPathSelectElements($"//xhtml:div[@class='page']", manager).ToList();
        return pages.Select((page, pageIndex) =>
        {
            var paragraphs = page.XPathSelectElements($".//xhtml:p", manager).ToList();

            var result = new StringBuilder();
            foreach (var p in paragraphs)
            {
                var text = p.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (IsLikelyHeading(text))
                    {
                        result.AppendLine();
                    }

                    result.AppendLine(text.ReplaceLineEndings(" "));
                    result.AppendLine();
                }
            }

            return new ExtractedText(pageIndex + 1, result.ToString());
        }).ToArray();
    }

    // Assume that multiple sentences mean "not a heading"
    private bool IsLikelyHeading(string text)
        => !text.Contains(". ");

    public void Dispose()
    {
        _app.StopAsync();
        _app.WaitForShutdown();
    }

    private static string GetContentType(string filename) => Path.GetExtension(filename) switch
    {
        ".pdf" => "application/pdf",
        string extension => throw new ArgumentException($"Unknown file type {extension}"),
    };

    public record ExtractedText(int PageNumber, string Text);
}
