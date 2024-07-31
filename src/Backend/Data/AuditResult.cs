namespace eShopSupport.Backend.Data;

[GenerateSerializer]
public class AuditResult
{
    [Id(0)]
    public bool Success { get; set; }
    [Id(1)]
    public string Feedback { get; set; }
}
