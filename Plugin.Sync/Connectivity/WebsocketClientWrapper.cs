using System;
using System.Threading;
using System.Threading.Tasks;
using Plugin.Sync.Logging;
using vtortola.WebSockets;
using vtortola.WebSockets.Deflate;
using vtortola.WebSockets.Rfc6455;
using WebSocket = vtortola.WebSockets.WebSocket;

namespace Plugin.Sync.Connectivity
{
    /// <summary>
    /// Wraps Websockets transport details such as ping-ponging, reconnect, receiving and sending messages.
    /// </summary>
    public class WebsocketClientWrapper : IDisposable
    {
        private static readonly IClassLogger Logger = LogManager.CreateLogger("WS");
        
        private readonly IMessageHandler messageHandler;
        private readonly IMessageEncoder encoder;
        private WebSocketClient client;
        private WebSocket wsClient;
        private readonly WebSocketListenerOptions options;
        
        private Task messageLoopTask = Task.CompletedTask;
        private CancellationTokenSource receiveMessagesCts = new CancellationTokenSource();
        
        private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1);

        public event EventHandler<Exception> OnConnectionError;

        public Uri Endpoint { get; private set; }
        public bool IsConnected => this.wsClient != null && this.wsClient.IsConnected;

        public WebsocketClientWrapper(Uri endpoint, IMessageHandler messageHandler, IMessageEncoder encoder)
        {
            this.Endpoint = endpoint;
            this.messageHandler = messageHandler;
            this.encoder = encoder;
            const int bufferSize = 1024 * 8; // 8KiB
            const int bufferPoolSize = 100 * bufferSize; // 800KiB pool
            
            this.options = new WebSocketListenerOptions
            {
                SendBufferSize = bufferSize,
                BufferManager = BufferManager.CreateBufferManager(bufferPoolSize, bufferSize),
                PingMode = PingMode.LatencyControl,
                Logger = new LoggerAdapter(Logger)
                {
                    IsDebugEnabled = Logger.IsEnabled(LogLevel.Trace),
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
        public async Task<bool> AssertConnected(CancellationToken cancellationToken)
        {
            try
            {
                await this.semaphore.WaitAsync(cancellationToken);
                return await InternalAssertConnected(cancellationToken);
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        private async Task<bool> InternalAssertConnected(CancellationToken cancellationToken)
        {
            if (this.client != null 
                && this.wsClient != null 
                && this.wsClient.IsConnected 
                && !this.receiveMessagesCts.IsCancellationRequested)
            {
                Logger.Trace("Already connected");
                return false;
            }
            
            var retryCount = 0;
            do
            {
                try
                {
                    this.receiveMessagesCts.Cancel();
                    await this.messageLoopTask;
                    Logger.Trace("Message loop task finished");
                    this.receiveMessagesCts = new CancellationTokenSource();
                    
                    this.client = new WebSocketClient(this.options);
                    this.wsClient = await this.client.ConnectAsync(this.Endpoint, cancellationToken);
                    this.messageLoopTask = Task.Run(() => ReceiveMessagesLoop(this.receiveMessagesCts.Token), cancellationToken);
                    
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
            try
            {
                await this.semaphore.WaitAsync(cancellationToken);
                if (!this.IsConnected)
                {
                    throw new Exception("Client isn't connected!");
                }

                var (buffer, len) = this.encoder.EncodeMessage(dto);
                await this.wsClient.WriteStringAsync(buffer, 0, len, cancellationToken);
            }
            finally
            {
                this.semaphore.Release();
            }
        }

        public async Task Close()
        {
            try
            {
                await this.semaphore.WaitAsync();
                Logger.Debug("Called websockets close");

                // Cancel any ongoing operations 
                if (!this.receiveMessagesCts.IsCancellationRequested)
                {
                    this.receiveMessagesCts.Cancel();
                }
                this.receiveMessagesCts = new CancellationTokenSource();
                
                // close current connection
                try
                {
                    await this.messageLoopTask;
                    if (!this.wsClient.IsConnected)
                    {
                        Logger.Trace($"Socket already closed.");
                        return;
                    }
                    await this.wsClient.CloseAsync();
                    this.wsClient.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Debug($"Exception during closing ws: {ex}");
                    // client doesn't expose any properties to know if it is safe to close,
                    // so we'll just swallow this exception
                }

            }
            finally
            {
                this.client = null;
                this.semaphore.Release();
            }
        }

        private async void ReceiveMessagesLoop(CancellationToken token)
        {
            Logger.Debug("WS message loop started.");
            try
            {
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
                    catch (OperationCanceledException)
                    {
                        Logger.Trace("receive loop cancelled");
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
                            Logger.Trace("reconnect cancelled");
                            // will return normally
                        }
                    }
                }
            }
            finally
            {
                Logger.Debug("WS message loop stopped.");
            }
        }
        
        public void Dispose()
        {
            Logger.Debug("WS disposed.");
            Close().Wait();
        }
    }
    
    public class LoggerAdapter : ILogger
    {
        private IClassLogger Logger { get; }

        public LoggerAdapter(IClassLogger logger)
        {
            this.Logger = logger;
        }

        public void Debug(string message, Exception error = null)
        {
            Logger.Trace($"{message} {error}");
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