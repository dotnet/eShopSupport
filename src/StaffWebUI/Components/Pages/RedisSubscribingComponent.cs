using Microsoft.AspNetCore.Components;
using StackExchange.Redis;

namespace eShopSupport.StaffWebUI.Components.Pages;

/// <summary>
/// Base class for a component that can easily subscribe to a RedisChannel and override a method to handle notifications.
/// This base class deals with registering and unregistering subscriptions.
/// </summary>
public abstract class RedisSubscribingComponent : ComponentBase, IDisposable
{
    private RedisChannel? _channel;
    private bool disposedValue;

    [Inject]
    private IConnectionMultiplexer Redis { get; set; } = default!;

    protected RedisChannel? SubscriptionChannel
    {
        get => _channel;
        set
        {
            if (_channel != value)
            {
                // Remove the old subscription, if any
                var subscriber = Redis.GetSubscriber();
                if (_channel.HasValue)
                {
                    subscriber.Unsubscribe(_channel.Value, HandleMessage);
                }

                // Create the new subscription
                _channel = value;
                if (_channel.HasValue && !disposedValue)
                {
                    subscriber.Subscribe(_channel.Value, HandleMessage);
                }
            }
        }
    }

    void HandleMessage(RedisChannel channel, RedisValue value)
        => _ = HandleMessageAsync(channel, value);

    async Task HandleMessageAsync(RedisChannel channel, RedisValue value)
    {
        // This pattern deals with getting onto the renderer sync context, causing the UI to refresh around the
        // returned task, and processing any exceptions within the component's rendering context
        try
        {

            await InvokeAsync(async () =>
            {
                var eventCallback = EventCallback.Factory.Create<RedisValue>(this, OnRedisNotificationAsync);
                await eventCallback.InvokeAsync(value);
            });
        }
        catch (Exception ex)
        {
            await DispatchExceptionAsync(ex);
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                // Unsubscribe
                SubscriptionChannel = null;
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual Task OnRedisNotificationAsync(RedisValue value)
        => Task.CompletedTask;
}
