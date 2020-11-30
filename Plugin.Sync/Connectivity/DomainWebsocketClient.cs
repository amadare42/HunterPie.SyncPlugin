using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Plugin.Sync.Util;
using vtortola.WebSockets;
using vtortola.WebSockets.Deflate;
using vtortola.WebSockets.Rfc6455;
using WebSocket = vtortola.WebSockets.WebSocket;

namespace Plugin.Sync.Connectivity
{
    /// <summary>
    /// Wraps Websockets transport details such as ping-ponging, reconnect, receiving and sending messages. 
    /// NOTE: this class is not thread-safe by design. Should be used with locks.
    /// </summary>
    public class DomainWebsocketClient : IDisposable
    {
        private readonly IMessageHandler messageHandler;
        private readonly IMessageEncoder encoder;
        private WebSocketClient client;
        private WebSocket wsClient;
        private CancellationTokenSource receiveMessagesCts = new CancellationTokenSource();
        private WebSocketListenerOptions options;

        public event EventHandler<Exception> OnConnectionError;

        public bool IsConnected => this.wsClient != null && this.wsClient.IsConnected;

        public DomainWebsocketClient(IMessageHandler messageHandler, IMessageEncoder encoder)
        {
            this.messageHandler = messageHandler;
            this.encoder = encoder;
            const int bufferSize = 1024 * 8; // 8KiB
            const int bufferPoolSize = 100 * bufferSize; // 800KiB pool
            
            this.options = new WebSocketListenerOptions
            {
                SendBufferSize = bufferSize,
                BufferManager = BufferManager.CreateBufferManager(bufferPoolSize, bufferSize),
                PingMode = PingMode.LatencyControl,
                Logger = new LoggerAdapter
                {
                    IsDebugEnabled = false,
                    IsWarningEnabled = true,
                    IsErrorEnabled = true
                },
            };
            var rfc6455 = new WebSocketFactoryRfc6455();
            rfc6455.MessageExtensions.Add(new WebSocketDeflateExtension());
            this.options.Standards.Add(rfc6455);
        }

        /// <summary>
        /// If connection isn't active, connect. Otherwise don't do anything.
        /// </summary>
        /// <returns>false if was already connected, true if connected successfully</returns>
        /// <exception cref="OperationCanceledException"></exception>
        /// <exception cref="WebsocketConnectionFailedException"></exception>
        public async Task<bool> AssertConnected(string endpoint, string playerName, CancellationToken cancellationToken)
        {
            if (this.client != null && this.wsClient != null && this.wsClient.IsConnected)
            {
                return false;
            }
            
            var retryCount = 0;
            var uriBuilder = new UriBuilder(endpoint) {Query = "playerName=" + HttpUtility.UrlEncode(playerName)};
            var uri = uriBuilder.Uri;

            do
            {
                try
                {
                    this.receiveMessagesCts.Cancel();
                    this.receiveMessagesCts = new CancellationTokenSource();
                    
                    this.client = new WebSocketClient(this.options);
                    this.wsClient = await this.client.ConnectAsync(uri, cancellationToken);
                    _ = Task.Run(() => ReceiveMessagesLoop(this.receiveMessagesCts.Token), cancellationToken);
                    Logger.Log("Connected to server");
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Connection failed, retry {++retryCount}/10");
                    Logger.Debug($"{ex}");
                    await Task.Delay(2000, cancellationToken);
                }
            } while (retryCount < 10);
            
            throw new WebsocketConnectionFailedException();
        }

        /// <summary>
        /// Send object using defined encoder.
        /// </summary>
        /// <exception cref="Exception"></exception>
        public async Task Send<T>(T dto, CancellationToken cancellationToken)
        {
            if (!this.IsConnected)
            {
                throw new Exception("Client isn't connected!");
            }
            var (buffer, len) = this.encoder.EncodeMessage(dto);
            await this.wsClient.WriteStringAsync(buffer, 0, len, cancellationToken);
        }

        public async Task Close()
        {
            Logger.Debug($"Called close {Environment.StackTrace}");
            try
            {
                if (this.wsClient != null && this.wsClient.IsConnected)
                {
                    await this.client.CloseAsync();
                }
            }
            catch (Exception)
            {
                // client doesn't expose any properties to know if it is safe to close,
                // so we'll just swallow this exception
            }

            if (!this.receiveMessagesCts.IsCancellationRequested)
            {
                this.receiveMessagesCts.Cancel();
            }

            this.receiveMessagesCts = new CancellationTokenSource();
        }

        private async void ReceiveMessagesLoop(CancellationToken token)
        {
            Logger.Debug("WS message loop started.");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var responseStream = await this.wsClient.ReadMessageAsync(token).ConfigureAwait(false);
                    Logger.Trace("WS stream.");
                    if (responseStream == null) continue;
                    this.messageHandler.ReceiveMessage(responseStream);
                }
                catch (WebSocketException ex)
                {
                    this.OnConnectionError?.Invoke(this, ex);
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Error on receiving message: {ex.Message}");
                    Logger.Debug($"{ex}");
                    try
                    {
                        await Task.Delay(1000, token);
                    }
                    catch (OperationCanceledException)
                    {
                        // will return normally
                    }
                }
            }
            Logger.Debug("WS message loop stopped.");
        }
        
        public void Dispose()
        {
            Logger.Debug("WS disposed.");
            Close().Wait();
        }
    }
    
    public class LoggerAdapter : ILogger
    {
        public void Debug(string message, Exception error = null)
        {
            Logger.Debug($"{message} {error}");
        }

        public void Warning(string message, Exception error = null)
        {
            Logger.Warn($"{message} {error}");
        }

        public void Error(string message, Exception error = null)
        {
            Logger.Error($"{message} {error}");
        }

        public bool IsDebugEnabled { get; set; }
        public bool IsWarningEnabled { get; set; }
        public bool IsErrorEnabled { get; set; }
    }
}