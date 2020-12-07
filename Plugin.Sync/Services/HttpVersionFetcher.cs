using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.Sync.Services
{
    public class HttpVersionFetcher : IVersionFetcher
    {
        public async Task<Version> FetchVersion(CancellationToken token)
        {
            var rsp = await new HttpClient().GetStringAsync($"{ConfigService.Current.ServerUrl}/version");
            token.ThrowIfCancellationRequested();
            return Version.Parse(rsp);
        }
    }
}