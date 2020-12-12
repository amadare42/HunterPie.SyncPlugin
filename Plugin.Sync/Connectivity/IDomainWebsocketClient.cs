using System;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Sync.Connectivity.Model.Messages;

namespace Plugin.Sync.Connectivity
{
    public interface IDomainWebsocketClient
    {
        event EventHandler<Exception> OnConnectionError;
        event EventHandler<IMessage> OnMessage; 
        bool IsConnected { get; }
        Uri Endpoint { get; }
        Task<bool> AssertConnected(CancellationToken cancellationToken);
        Task Send<T>(T dto, CancellationToken cancellationToken) where T : IMessage;
        Task Close();
    }
}