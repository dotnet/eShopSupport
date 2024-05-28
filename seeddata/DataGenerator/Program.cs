using eShopSupport.DataGenerator.Generators;

var categories = await new CategoryGenerator().GenerateAsync();
Console.WriteLine($"Got {categories.Count} categories");

var products = await new ProductGenerator(categories).GenerateAsync();
Console.WriteLine($"Got {products.Count} products");

var manualTocs = await new ManualTocGenerator(categories, products).GenerateAsync();
Console.WriteLine($"Got {manualTocs.Count} manual TOCs");

var manuals = await new ManualGenerator(categories, products, manualTocs).GenerateAsync();
Console.WriteLine($"Got {manuals.Count} manuals");

var manualPdfs = await new ManualPdfConverter(products, manuals).ConvertAsync();
Console.WriteLine($"Got {manualPdfs.Count} PDFs");

var tickets = await new TicketGenerator(products, categories).GenerateAsync();
Console.WriteLine($"Got {tickets.Count} tickets");

var ticketThreads = await new TicketThreadGenerator(tickets, products, manuals).GenerateAsync();
Console.WriteLine($"Got {ticketThreads.Count} threads");

var summarizedThreads = await new TicketSummaryGenerator(products, ticketThreads).GenerateAsync();
Console.WriteLine($"Got {summarizedThreads.Count} thread summaries");
