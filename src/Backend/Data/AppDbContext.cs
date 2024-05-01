using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace eShopSupport.Backend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Ticket> Tickets { get; set; }

    public static async Task EnsureDbCreatedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        using var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var createdDb = await dbContext.Database.EnsureCreatedAsync();

        if (createdDb)
        {
            var importDataFromDir = Environment.GetEnvironmentVariable("ImportInitialDataDir");
            if (!string.IsNullOrEmpty(importDataFromDir))
            {
                await ImportInitialData(dbContext, importDataFromDir);
            }
        }
    }

    private static async Task ImportInitialData(AppDbContext dbContext, string dirPath)
    {
        try
        {
            var tickets = JsonSerializer.Deserialize<Ticket[]>(
                File.ReadAllText(Path.Combine(dirPath, "tickets.json")))!;
            await dbContext.Tickets.AddRangeAsync(tickets);

            await dbContext.SaveChangesAsync();
        }
        catch
        {
            // If the initial import failed, we drop the DB so it will try again next time
            await dbContext.Database.EnsureDeletedAsync();
            throw;
        }
    }
}
