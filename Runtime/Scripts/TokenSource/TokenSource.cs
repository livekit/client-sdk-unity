using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace LiveKit
{
    public class TokenSource : MonoBehaviour
    {
        [SerializeField] private TokenSourceConfig _config;

        private static readonly string SandboxUrl = "https://cloud-api.livekit.io/api/v2/sandbox/connection-details";
        private static readonly HttpClient HttpClient = new HttpClient();

        public async Task<ConnectionDetails> FetchConnectionDetails()
        {
            if (_config == null)
                throw new InvalidOperationException("Token source configuration was not provided");
            if (!_config.IsValid)
                throw new InvalidOperationException("Token source configuration is invalid");

            switch (_config.TokenSourceType)
            {
                case TokenSourceType.Sandbox:
                    return await FetchFromTokenSource(SandboxUrl, new[] { new StringPair { key = "X-Sandbox-ID", value = _config.SandboxId } });

                case TokenSourceType.Endpoint:
                    return await FetchFromTokenSource(_config.EndpointUrl, _config.EndpointHeaders);

                case TokenSourceType.Literal:
                    return new ConnectionDetails
                    {
                        ServerUrl = _config.ServerUrl,
                        ParticipantToken = _config.Token
                    };

                default:
                    throw new InvalidOperationException("Unknown token source type");
            }
        }

        private async Task<ConnectionDetails> FetchFromTokenSource(string url, IEnumerable<StringPair> headers)
        {
            var requestBody = BuildRequest(_config);
            var jsonBody = JsonConvert.SerializeObject(requestBody);

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    if (!string.IsNullOrEmpty(header.key))
                        request.Headers.TryAddWithoutValidation(header.key, header.value);
                }
            }
            var content = new StringContent(jsonBody, System.Text.Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            request.Content = content;

            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Token server error: {response.StatusCode}, response: {await response.Content.ReadAsStringAsync()}");

            var jsonContent = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ConnectionDetails>(jsonContent);
        }

        private static TokenSourceRequest BuildRequest(TokenSourceConfig config)
        {
            var request = new TokenSourceRequest
            {
                RoomName = NullIfEmpty(config.RoomName),
                ParticipantName = NullIfEmpty(config.ParticipantName),
                ParticipantIdentity = NullIfEmpty(config.ParticipantIdentity),
                ParticipantMetadata = NullIfEmpty(config.ParticipantMetadata),
            };

            if (config.ParticipantAttributes != null && config.ParticipantAttributes.Count > 0)
            {
                request.ParticipantAttributes = config.ParticipantAttributes
                    .Where(a => !string.IsNullOrEmpty(a.key))
                    .ToDictionary(a => a.key, a => a.value);
                if (request.ParticipantAttributes.Count == 0)
                    request.ParticipantAttributes = null;
            }

            if (!string.IsNullOrEmpty(config.AgentName) || !string.IsNullOrEmpty(config.AgentMetadata))
            {
                request.RoomConfig = new RoomConfig
                {
                    Agents = new List<AgentDispatch>
                    {
                        new AgentDispatch
                        {
                            AgentName = NullIfEmpty(config.AgentName),
                            Metadata = NullIfEmpty(config.AgentMetadata)
                        }
                    }
                };
            }

            return request;
        }

        private static string NullIfEmpty(string value) =>
            string.IsNullOrEmpty(value) ? null : value;
    }

    class TokenSourceRequest
    {
        [JsonProperty("room_name", NullValueHandling = NullValueHandling.Ignore)]
        public string RoomName;

        [JsonProperty("participant_name", NullValueHandling = NullValueHandling.Ignore)]
        public string ParticipantName;

        [JsonProperty("participant_identity", NullValueHandling = NullValueHandling.Ignore)]
        public string ParticipantIdentity;

        [JsonProperty("participant_metadata", NullValueHandling = NullValueHandling.Ignore)]
        public string ParticipantMetadata;

        [JsonProperty("participant_attributes", NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string> ParticipantAttributes;

        [JsonProperty("room_config", NullValueHandling = NullValueHandling.Ignore)]
        public RoomConfig RoomConfig;
    }

    class RoomConfig
    {
        [JsonProperty("agents", NullValueHandling = NullValueHandling.Ignore)]
        public List<AgentDispatch> Agents;
    }

    class AgentDispatch
    {
        [JsonProperty("agent_name", NullValueHandling = NullValueHandling.Ignore)]
        public string AgentName;

        [JsonProperty("metadata", NullValueHandling = NullValueHandling.Ignore)]
        public string Metadata;
    }

    [Serializable]
    public struct ConnectionDetails
    {
        [JsonProperty("server_url")]
        public string ServerUrl;

        [JsonProperty("participant_token")]
        public string ParticipantToken;
    }
}
