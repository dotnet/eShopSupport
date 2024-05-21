using eShopSupport.ServiceDefaults.Clients.Backend;
using eShopSupport.StaffWebUI.Components;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();

builder.Services.AddHttpClient<BackendClient>(client =>
    client.BaseAddress = new Uri("http://backend/"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/manual", async (string file, BackendClient backend, CancellationToken cancellationToken) =>
{
    var result = await backend.ReadManualAsync(file, cancellationToken);
    return result is null ? Results.NotFound() : Results.Stream(result, "application/pdf");
});

app.Run();
