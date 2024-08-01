using AutoGen.Core;

namespace eShopSupport.Backend;

public enum AssistantEvent
{
    CreateTask = 0,
    CreateStep = 1,
    CompleteStep = 4,
    CompleteTask = 5,
    WriteReply = 6,
    ReplyNeedsRevision = 8,
    Nuance = 9,
}

public class EventMessage : IMessage, ICanGetTextContent
{
    private readonly IMessage innerMessage;
    public EventMessage(IMessage innerMessage, AssistantEvent assistantEvent)
    {
        this.AssistantEvent = assistantEvent;
        this.innerMessage = innerMessage;
        this.From = innerMessage.From;
    }

    public string? From { get; set; }

    public IMessage Content => innerMessage;

    public AssistantEvent AssistantEvent { get; set; }

    public string? GetContent()
    {
        return this.Content.GetContent();
    }
}

public static class EventMessageExtension
{
    public static EventMessage WithEvent(this IMessage message, AssistantEvent assistantEvent)
    {
        return new EventMessage(message, assistantEvent);
    }
}

public class EventDrivenOrchestrator : IOrchestrator
{
    private readonly IAgent planner;
    private readonly IAgent manualSearch;
    private readonly IAgent supportAgent;
    private readonly IAgent userAgent;
    private readonly IAgent critic;

    public EventDrivenOrchestrator(IAgent planner, IAgent manualSearch, IAgent supportAgent, IAgent userAgent, IAgent critic)
    {
        this.planner = planner;
        this.manualSearch = manualSearch;
        this.supportAgent = supportAgent;
        this.userAgent = userAgent;
        this.critic = critic;
    }

    public async Task<IAgent?> GetNextSpeakerAsync(OrchestrationContext context, CancellationToken cancellationToken = default)
    {
        var messages = context.ChatHistory;
        var lastMessage = messages.Last() as EventMessage;
        if (lastMessage is null)
        {
            // process shortcut like @<assigned_agent>, <step>
            var text = messages.Last().GetContent();
            if (text?.ToLower().Contains($"@{this.manualSearch.Name}") is true)
            {
                return manualSearch;
            }
            else if (text?.ToLower().Contains($"@{this.supportAgent.Name}") is true)
            {
                return supportAgent;
            }
            else
            {
                return planner;
            }
        }

        if (lastMessage.AssistantEvent == AssistantEvent.CreateTask)
        {
            // create_task -> create_step
            return planner;
        }


        if (lastMessage.AssistantEvent == AssistantEvent.CreateStep)
        {
            // the content will be in the format: @<assigned_agent>, <step>
            var content = lastMessage.Content.GetContent();
            if (content.ToLower().Contains(this.manualSearch.Name.ToLower()))
            {
                return manualSearch;
            }

            if (content.ToLower().Contains(this.supportAgent.Name.ToLower()))
            {
                return supportAgent;
            }

            if (content.ToLower().Contains(this.userAgent.Name.ToLower()))
            {
                return userAgent;
            }

            throw new InvalidOperationException("Agent not found");
        }

        if (lastMessage.AssistantEvent == AssistantEvent.CompleteStep)
        {
            if (messages.Any(m => m is EventMessage eventMessage && eventMessage.AssistantEvent == AssistantEvent.CreateStep))
            {
                return planner;
            }

            return null;
        }

        if (lastMessage.AssistantEvent == AssistantEvent.CompleteTask)
        {
            // complete_task -> null
            return null;
        }

        if (lastMessage.AssistantEvent == AssistantEvent.WriteReply)
        {
            // write_reply -> review_reply
            return critic;
        }

        if (lastMessage.AssistantEvent == AssistantEvent.ReplyNeedsRevision)
        {
            // review_reply -> approve_reply | write_reply
            return supportAgent;
        }

        throw new InvalidOperationException("Invalid event");
    }
}

public class EventMessageMiddleware : IStreamingMiddleware
{
    public string? Name => nameof(EventMessageMiddleware);

    public IAsyncEnumerable<IMessage> InvokeAsync(MiddlewareContext context, IStreamingAgent agent, CancellationToken cancellationToken = default)
    {
        var messages = context.Messages.Select(m =>
        {
            return m switch
            {
                EventMessage eventMessage => eventMessage.Content,
                _ => m,
            };
        });

        return agent.GenerateStreamingReplyAsync(messages, context.Options, cancellationToken);
    }

    public Task<IMessage> InvokeAsync(MiddlewareContext context, IAgent agent, CancellationToken cancellationToken = default)
    {
        var messages = context.Messages.Select(m =>
        {
            return m switch
            {
                EventMessage eventMessage => eventMessage.Content,
                _ => m,
            };
        });

        return agent.GenerateReplyAsync(messages, context.Options, cancellationToken);
    }
}
