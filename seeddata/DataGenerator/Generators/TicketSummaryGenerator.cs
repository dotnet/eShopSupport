using eShopSupport.DataGenerator.Model;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace eShopSupport.DataGenerator.Generators;

public class TicketSummaryGenerator(IReadOnlyList<Product> products, IReadOnlyList<TicketThread> threads, IServiceProvider services) : GeneratorBase<TicketThread>(services)
{
    protected override object GetId(TicketThread item) => item.TicketId;

    protected override string DirectoryName => "tickets/threads";

    protected override IAsyncEnumerable<TicketThread> GenerateCoreAsync()
    {
        var threadsNeedingSummaries = threads.Where(
            thread => string.IsNullOrEmpty(thread.ShortSummary)
                || string.IsNullOrEmpty(thread.LongSummary)
                || !thread.TicketStatus.HasValue
                || !thread.TicketType.HasValue);

        return MapParallel(threadsNeedingSummaries, async thread =>
        {
            await GenerateSummaryAsync(thread);
            return thread;
        });
    }

    private async Task GenerateSummaryAsync(TicketThread thread)
    {
        // The reason for prompting to express satisfation in words rather than numerically, and forcing it to generate a summary
        // of the customer's words before doing so, are necessary prompt engineering techniques. If it's asked to generate sentiment
        // score without first summarizing the customer's words, then it scores the agent's response even when told not to. If it's
        // asked to score numerically, it produces wildly random scores - it's much better with words than numbers.
        string[] satisfactionScores = ["AbsolutelyFurious", "VeryUnhappy", "Unhappy", "Disappointed", "Indifferent", "Pleased", "Happy", "Delighted", "UnspeakablyThrilled"];

        var product = products.Single(p => p.ProductId == thread.ProductId);
        var prompt = $@"You are part of a customer support ticketing system.
            Your job is to write brief summaries of customer support interactions. This is to help support agents
            understand the context quickly so they can help the customer efficiently.

            Here are details of a support ticket.

            Product: {product.Model}
            Brand: {product.Brand}
            Customer name: {thread.CustomerFullName}

            The message log so far is:

            {TicketThreadGenerator.FormatMessagesForPrompt(thread.Messages)}

            Write these summaries:

            1. A longer summary that is up to 30 words long, condensing as much distinctive information
               as possible. Do NOT repeat the customer or product name, since this is known anyway.
               Try to include what SPECIFIC questions/info were given, not just stating in general that questions/info were given.
               Always cite specifics of the questions or answers. For example, if there is pending question, summarize it in a few words.
               FOCUS ON THE CURRENT STATUS AND WHAT KIND OF RESPONSE (IF ANY) WOULD BE MOST USEFUL FROM THE NEXT SUPPORT AGENT.

            2. A shorter summary that is up to 8 words long. This functions as a title for the ticket,
               so the goal is to distinguish what's unique about this ticket.

            3. A 10-word summary of the latest thing the CUSTOMER has said, ignoring any agent messages. Then, based
               ONLY on that, score the customer's satisfaction using one of the following phrases ranked from worst to best:
               {string.Join(", ", satisfactionScores)}.
               Pay particular attention to the TONE of the customer's messages, as we are most interested in their emotional state.

            Both summaries will only be seen by customer support agents.

            Respond as JSON in the following form: {{
              ""longSummary"": ""string"",
              ""shortSummary"": ""string"",
              ""tenWordsSummarizingOnlyWhatCustomerSaid"": ""string"",
              ""customerSatisfaction"": ""string"",
              ""ticketStatus"": ""Open""|""Closed"",
              ""ticketType"": ""Question""|""Idea""|""Complaint""|""Returns""
            }}

            ticketStatus should be Open if there is some remaining work for support agents to handle, otherwise Closed.
            ticketType must be one of the specified values best matching the ticket. Do not use any other value except the specified ones.";

        var response = await GetAndParseJsonChatCompletion<Response>(prompt);
        // Not including these fields because we'll show creating them in the demo
        //thread.ShortSummary = response.ShortSummary;
        //thread.LongSummary = response.LongSummary;
        thread.CustomerSatisfaction = null;
        thread.TicketStatus = response.TicketStatus;
        thread.TicketType = response.TicketType;

        var satisfactionScore = Array.IndexOf(satisfactionScores, response.CustomerSatisfaction ?? string.Empty);
        if (satisfactionScore > 0)
        {
            var satisfactionPercent = (int)(10 * ((double)satisfactionScore / (satisfactionScores.Length - 1)));
            //thread.CustomerSatisfaction = satisfactionPercent;
        }
    }

    private class Response
    {
        public required string LongSummary { get; set; }
        public required string ShortSummary { get; set; }
        public string? CustomerSatisfaction { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverterWithFallback))]
        public TicketStatus? TicketStatus { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverterWithFallback))]
        public TicketType? TicketType { get; set; }
    }

    private class JsonStringEnumConverterWithFallback : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert.IsEnum;

        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var closedType = typeof(ErrorHandlingEnumConverter<>).MakeGenericType(typeToConvert);
            return (JsonConverter?)Activator.CreateInstance(closedType);
        }

        private class ErrorHandlingEnumConverter<T> : JsonConverter<T> where T: struct
        {
            public override bool CanConvert(Type typeToConvert)
                => typeToConvert.IsEnum;

            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    return default;
                }

                var enumString = reader.GetString();
                return Enum.TryParse<T>(enumString, ignoreCase: true, out var result)
                    ? result
                    : default;
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
                => throw new NotImplementedException();
        }
    }
}
