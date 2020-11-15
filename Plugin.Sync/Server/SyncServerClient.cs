using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;
using Plugin.Sync.Model;

namespace Plugin.Sync.Server
{
    public class SyncServerClient
    {
        public static string BaseUrl = "http://localhost:3001";

        private JsonSerializer serializer = new JsonSerializer();
        private HttpClient http = new HttpClient();
        private HttpClient pollingHttp = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(Timeout.Infinite)
        };

        public async Task<MonsterModel[]> PullGame(string sessionId)
        {
            var json = await this.http.GetStringAsync($"{BaseUrl}/game/{HttpUtility.UrlEncode(sessionId)}");
            return JsonConvert.DeserializeObject<MonsterModel[]>(json);
        }

        public async Task PushChangedMonsters(string sessionId, IList<MonsterModel> monsters, CancellationToken cancellationToken)
        {
            var json = JsonConvert.SerializeObject(monsters);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var rsp = await this.http.PutAsync($"{BaseUrl}/game/{HttpUtility.UrlEncode(sessionId)}", content, cancellationToken);
            rsp.EnsureSuccessStatusCode();
        }

        public async Task<List<MonsterModel>> PollMonsterChanges(string sessionId, string pollId, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/game/{HttpUtility.UrlEncode(sessionId)}/poll/{pollId}");

            using var response = await this.pollingHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            using var body = await response.EnsureSuccessStatusCode().Content.ReadAsStreamAsync();
            using var sr = new StreamReader(body);
            using var jsonTextReader = new JsonTextReader(sr);

            return this.serializer.Deserialize<List<MonsterModel>>(jsonTextReader);
        }
    }
}
