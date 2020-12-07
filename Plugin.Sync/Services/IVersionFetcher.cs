using System;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.Sync.Services
{
    public interface IVersionFetcher
    {
        Task<Version> FetchVersion(CancellationToken token);
    }
}