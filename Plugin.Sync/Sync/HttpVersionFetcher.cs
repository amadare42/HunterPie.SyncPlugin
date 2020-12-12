using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.Sync.Sync
{
    public class HttpVersionFetcher : IVersionFetcher
    {
        private readonly HttpClient client = new HttpClient();
        
        public async Task<Version> FetchVersion(CancellationToken token)
        {
            var rsp = await this.client.GetStringAsync($"{ConfigService.Current.ServerUrl.TrimEnd('/')}/version");
            token.ThrowIfCancellationRequested();
            return Version.Parse(rsp);
        }
    }
}