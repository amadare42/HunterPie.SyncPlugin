using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Plugin.Sync.Connectivity;
using Plugin.Sync.Connectivity.Model.Messages;
using Plugin.Sync.Logging;
using Plugin.Sync.Sync;
using Plugin.Sync.Util;
using Xunit;
using Xunit.Abstractions;

namespace Plugin.Sync.Tests.Manual
{
    public class SyncServiceTests : BaseTests
    {
        public SyncServiceTests(ITestOutputHelper testOutput) : base(testOutput)
        {
        }

        [Fact]
        public async Task ReplaceFuncWithExpressions()
        {
            var executed = false;
            var foo = ReflectionsHelper.WrapActionWithName(() =>
            {
                executed = true;
                this.TestOutput.WriteLine("foo executed");
                return Task.CompletedTask;
            }, "Foo");
            await foo();
            Assert.Equal("Foo", foo.Method.Name);
            Assert.True(executed);
        }

        [Fact]
        public async Task HandleParallelCalls()
        {
            var client = new DomainWebsocketClient(ConfigService.GetWsUrl());
            var syncService = new SyncService(client);
            syncService.SetSessionId("test-session");
            syncService.PlayerName = "test-player";
            var rnd = new Random();
            var tasks = Enumerable.Range(1, 5)
                .Select(i => Task.Run(async () =>
                {
                    while (true)
                    {
                        var val = rnd.Next(0, 100);
                        var mode = val switch
                        {
                            < 40 => SyncServiceMode.Poll,
                            < 80 => SyncServiceMode.Push,
                            _ => SyncServiceMode.Idle
                        };
                        Logger.Debug($"  --> {mode:G}");
                        syncService.SetMode(mode);
                        await Task.Delay(i * 30);
                    }
                }))
                .ToArray();
            Task.WaitAll(tasks);
        }

        [Fact]
        public async Task WaitBeforeModeSwitch()
        {
            var wsClient = new FakeDomainWebsocketClient();
            var versionFetcher = new FakeVersionFetcher();
            var syncService = new SyncService(wsClient)
            {
                SessionId = "test-id",
                VersionFetcher = versionFetcher
            };
            var modes = new BufferBlock<SyncServiceMode>();
            syncService.OnSyncModeChanged += (sender, mode) =>
            {
                Logger.Debug("== Mode: " + mode);
                modes.Post(mode);
            };

            var i = 0;
            async Task SetMode(SyncServiceMode mode)
            {
                var j = ++i;
                Logger.Debug($"  --> {mode:G} {j}");
                syncService.SetMode(mode);
                await Task.Yield();
            }
            await SetMode(SyncServiceMode.Push); // Idle -> Version check [Mode = PUSH]
            Assert.Equal(SyncServiceMode.Push, modes.Receive());
            await versionFetcher.Proceed(); // Versioncheck -> connecting
            wsClient.SetCompleted(); // connecting -> connected
            
            // same mode should be ignored
            await SetMode(SyncServiceMode.Push);
            await SetMode(SyncServiceMode.Poll);
            Assert.Equal(SyncServiceMode.Poll, modes.Receive());
        }
    }

    public class FakeVersionFetcher : IVersionFetcher
    {
        private TaskCompletionSource<Version> taskCompletionSource = new TaskCompletionSource<Version>();
        
        public async Task Proceed()
        {
            this.taskCompletionSource.SetResult(SyncService.RequiredApiVersion);
            await Task.Yield();
        }

        public void Reset()
        {
            this.taskCompletionSource = new TaskCompletionSource<Version>();
        }
        
        public Task<Version> FetchVersion(CancellationToken token)
        {
            return this.taskCompletionSource.Task;
        }
    }

    public class FakeDomainWebsocketClient : IDomainWebsocketClient
    {
        public event EventHandler<Exception> OnConnectionError;
        public event EventHandler<IMessage> OnMessage;
        public bool IsConnected { get; }
        public Uri Endpoint { get; }
        
        private TaskCompletionSource<object> taskCompletionSource = new TaskCompletionSource<object>();

        public async Task Proceed()
        {
            this.taskCompletionSource.SetResult(null);
            this.taskCompletionSource = new TaskCompletionSource<object>();
            await Task.Yield();
        }

        public void SetCompleted()
        {
            this.taskCompletionSource.SetResult(null);
        }
        public async Task<bool> AssertConnected(CancellationToken cancellationToken)
        {
            await this.taskCompletionSource.Task;
            return true;
        }

        public Task Send<T>(T dto, CancellationToken cancellationToken) where T : IMessage
        {
            return this.taskCompletionSource.Task;
        }

        public Task Close()
        {
            return this.taskCompletionSource.Task;
        }
    }
}