using System.IO.Compression;

public class ManualZipIngestor
{
    public Task RunAsync(string generatedDataPath, string outputDir)
    {
        Console.WriteLine("Compressing manuals...");

        var manualsSourceDir = Path.Combine(generatedDataPath, "manuals", "pdf");
        var manualsZipPath = Path.Combine(outputDir, "manuals.zip");
        File.Delete(manualsZipPath);
        ZipFile.CreateFromDirectory(manualsSourceDir, manualsZipPath);
        return Task.CompletedTask;
    }
}
