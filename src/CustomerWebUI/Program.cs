using CustomerWebUI.Components;
using eShopSupport.ServiceDefaults.Clients.Backend;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();
builder.Services.AddRazorComponents();
builder.Services.AddSmartComponents();
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

app.MapRazorComponents<App>();

app.MapSmartComboBox("api/product-search", async request =>
{
    var backend = request.HttpContext.RequestServices.GetRequiredService<BackendClient>();
    var results = await backend.FindProductsAsync(request.Query.SearchText);
    return results.Select(r => $"{r.Model} ({r.Brand})");
});

app.Run();
