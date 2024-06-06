using eShopSupport.DataGenerator;
using eShopSupport.DataGenerator.Generators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json");
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
builder.Services.AddSingleton<HttpClient>();

builder.AddOpenAIChatCompletion("chatcompletion");
//builder.AddOllamaChatCompletionService("localhost:11434", "mistral:7b");

var services = builder.Build().Services;

/*
var categories = await new CategoryGenerator(services).GenerateAsync();
Console.WriteLine($"Got {categories.Count} categories");

var products = await new ProductGenerator(categories, services).GenerateAsync();
Console.WriteLine($"Got {products.Count} products");

var manualTocs = await new ManualTocGenerator(categories, products, services).GenerateAsync();
Console.WriteLine($"Got {manualTocs.Count} manual TOCs");

var manuals = await new ManualGenerator(categories, products, manualTocs, services).GenerateAsync();
Console.WriteLine($"Got {manuals.Count} manuals");

var manualPdfs = await new ManualPdfConverter(products, manuals).ConvertAsync();
Console.WriteLine($"Got {manualPdfs.Count} PDFs");

var tickets = await new TicketGenerator(products, categories, manuals, services).GenerateAsync();
Console.WriteLine($"Got {tickets.Count} tickets");

var ticketThreads = await new TicketThreadGenerator(tickets, products, manuals, services).GenerateAsync();
Console.WriteLine($"Got {ticketThreads.Count} threads");

var ticketSummaries = await new TicketSummaryGenerator(products, ticketThreads, services).GenerateAsync();
Console.WriteLine($"Got {ticketThreads.Count} summaries");

var evalQuestions = await new EvalQuestionGenerator(products, categories, manuals, services).GenerateAsync();
Console.WriteLine($"Got {evalQuestions.Count} evaluation questions");

