using System.Text.Json;
using eShopSupport.Backend.Api;
using eShopSupport.ServiceDefaults.Clients.Backend;

var backend = new BackendClient(new HttpClient {  BaseAddress = new Uri("https://localhost:7223/") });
var ticketId = 1; // TODO
var message = "Who manufactures this?";
var answer = await backend.AssistantChatAsync(new AssistantChatRequest(
    ticketId,
    [new() { IsAssistant = true, Text = message }]),
    CancellationToken.None);
var reader = new StreamReader(answer);
var responseJson = await reader.ReadToEndAsync();
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var responseParsed = JsonSerializer.Deserialize<AssistantApi.AssistantReply[]>(responseJson, jsonOptions)!;
var finalAnswer = responseParsed.LastOrDefault(a => !string.IsNullOrEmpty(a.Answer))?.Answer;
Console.WriteLine(finalAnswer ?? "No answer given");
