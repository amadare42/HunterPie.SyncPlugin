using System;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Sync.Connectivity.Model.Messages;

namespace Plugin.Sync.Connectivity
{
    internal class DomainWebsocketClient : IDomainWebsocketClient
    {
        public event EventHandler<Exception> OnConnectionError
        {
            add => this.client.OnConnectionError += value;
            remove => this.client.OnConnectionError -= value;
        }
        
        public event EventHandler<IMessage> OnMessage
        {
            add => this.msgHandler.OnMessage += value;
            remove => this.msgHandler.OnMessage -= value;
        }
        
        public bool IsConnected => this.client.IsConnected;
        public Uri Endpoint => this.client.Endpoint;

        private readonly WebsocketClientWrapper client;
        private readonly EventMessageHandler msgHandler;

        public DomainWebsocketClient(string endpoint)
        {
            var uri = new UriBuilder(endpoint).Uri;
            this.msgHandler = new EventMessageHandler();
            this.client = new WebsocketClientWrapper(uri, this.msgHandler, new JsonMessageEncoder());
        }

        public Task<bool> AssertConnected(CancellationToken cancellationToken) => this.client.AssertConnected(cancellationToken);

        public Task Send<T>(T dto, CancellationToken cancellationToken) where T : IMessage => this.client.Send(dto, cancellationToken);

        public Task Close() => this.client.Close();
    }
}