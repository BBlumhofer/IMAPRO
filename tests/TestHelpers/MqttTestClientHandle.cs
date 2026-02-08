using System;
using System.Threading.Tasks;
using I40Sharp.Messaging;
using I40Sharp.Messaging.Transport;

namespace MAS_BT.Tests.TestHelpers;

internal sealed class MqttTestClientHandle : IAsyncDisposable
{
    private readonly IMessagingTransport _transport;

    public MessagingClient Client { get; }

    public MqttTestClientHandle(
        string host,
        int port,
        string? username,
        string? password,
        string clientPrefix)
    {
        var clientId = $"{clientPrefix}_{Guid.NewGuid():N}";
        _transport = new MqttTransport(host, port, clientId, username, password);
        Client = new MessagingClient(_transport, $"{clientPrefix}/logs");
    }

    public Task ConnectAsync() => Client.ConnectAsync();

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Client.IsConnected)
            {
                await Client.DisconnectAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Ignore disconnect failures for test cleanup.
        }
        finally
        {
            Client.Dispose();
            _transport.Dispose();
        }
    }
}
