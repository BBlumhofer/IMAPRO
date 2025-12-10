using System.Collections.Concurrent;
using System.Linq;
using I40Sharp.Messaging.Transport;

namespace MAS_BT.Tests.TestHelpers;

/// <summary>
/// Simple in-memory transport for tests. Publishes to all subscribers of a topic within the same process.
/// </summary>
public class InMemoryTransport : IMessagingTransport
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<InMemoryTransport>> Subscriptions = new(StringComparer.OrdinalIgnoreCase);
    private bool _connected;

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public bool IsConnected => _connected;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _connected = true;
        Connected?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _connected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task PublishAsync(string topic, string payload, CancellationToken cancellationToken = default)
    {
        if (!_connected)
            throw new InvalidOperationException("Transport not connected");

        if (Subscriptions.TryGetValue(topic, out var subscribers))
        {
            foreach (var subscriber in subscribers)
            {
                subscriber.MessageReceived?.Invoke(this, new MessageReceivedEventArgs
                {
                    Topic = topic,
                    Payload = payload
                });
            }
        }

        return Task.CompletedTask;
    }

    public Task SubscribeAsync(string topic, CancellationToken cancellationToken = default)
    {
        var bag = Subscriptions.GetOrAdd(topic, _ => new ConcurrentBag<InMemoryTransport>());
        bag.Add(this);
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(string topic, CancellationToken cancellationToken = default)
    {
        if (Subscriptions.TryGetValue(topic, out var bag))
        {
            // rebuild bag without this transport
            var remaining = new ConcurrentBag<InMemoryTransport>(bag.Where(t => t != this));
            Subscriptions[topic] = remaining;
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // nothing to clean up beyond unsubscribing
    }
}
