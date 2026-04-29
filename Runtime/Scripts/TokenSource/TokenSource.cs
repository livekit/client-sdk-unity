using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LiveKit
{
    public interface ITokenSource
    {
    }

    public interface ITokenSourceFixed : ITokenSource
    {
        public Task<ConnectionDetails> FetchConnectionDetails();
    }

    public interface ITokenSourceConfigurable : ITokenSource
    {
        public Task<ConnectionDetails> FetchConnectionDetails(TokenSourceFetchOptions options);
    }

    public class TokenSourceLiteral : ITokenSourceFixed
    {
        private string _serverUrl;
        private string _participantToken;

        public TokenSourceLiteral(string serverUrl, string participantToken)
        {
            _serverUrl = serverUrl;
            _participantToken = participantToken;
        }

        public Task<ConnectionDetails> FetchConnectionDetails()
        {
            var result = new ConnectionDetails { ServerUrl = _serverUrl, ParticipantToken = _participantToken };
            return Task.FromResult(result);
        }
    }

    public class TokenSourceCustom : ITokenSourceFixed
    {
        public delegate Task<ConnectionDetails> CustomTokenFunction();

        private CustomTokenFunction _customTokenFunction;

        public TokenSourceCustom(CustomTokenFunction customTokenFunction)
        {
            _customTokenFunction = customTokenFunction;
        }

        public Task<ConnectionDetails> FetchConnectionDetails()
        {
            return _customTokenFunction();
        }
    }

    public class TokenSourceEndpoint : ITokenSourceConfigurable
    {
        private string _endpointUrl;
        IEnumerable<StringPair> _headers;
        private static readonly HttpClient HttpClient = new HttpClient();

        public TokenSourceEndpoint(string endpointUrl, IEnumerable<StringPair> headers)
        {
            _endpointUrl = endpointUrl;
            _headers = headers;
        }

        public async Task<ConnectionDetails> FetchConnectionDetails(TokenSourceFetchOptions options)
        {
            var requestBody = BuildRequest(options);
            var jsonBody = JsonConvert.SerializeObject(requestBody);

            var request = new HttpRequestMessage(HttpMethod.Post, _endpointUrl);
            if (_headers != null)
            {
                foreach (var header in _headers)
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

        private static TokenSourceRequest BuildRequest(TokenSourceFetchOptions options)
        {
            var request = new TokenSourceRequest
            {
                RoomName = NullIfEmpty(options.RoomName),
                ParticipantName = NullIfEmpty(options.ParticipantName),
                ParticipantIdentity = NullIfEmpty(options.ParticipantIdentity),
                ParticipantMetadata = NullIfEmpty(options.ParticipantMetadata),
            };

            if (options.ParticipantAttributes != null && options.ParticipantAttributes.Count > 0)
            {
                request.ParticipantAttributes = options.ParticipantAttributes
                    .Where(a => !string.IsNullOrEmpty(a.Key))
                    .ToDictionary(a => a.Key, a => a.Value);
                if (request.ParticipantAttributes.Count == 0)
                    request.ParticipantAttributes = null;
            }

            if (!string.IsNullOrEmpty(options.AgentName) || !string.IsNullOrEmpty(options.AgentMetadata))
            {
                request.RoomConfig = new RoomConfig
                {
                    Agents = new List<AgentDispatch>
                    {
                        new AgentDispatch
                        {
                            AgentName = NullIfEmpty(options.AgentName),
                            Metadata = NullIfEmpty(options.AgentMetadata)
                        }
                    }
                };
            }

            return request;
        }

        private static string NullIfEmpty(string value) =>
            string.IsNullOrEmpty(value) ? null : value;
    }

    public class TokenSourceSandbox : TokenSourceEndpoint
    {
        public TokenSourceSandbox(string sandboxId) : base("https://cloud-api.livekit.io/api/v2/sandbox/connection-details", new[] { new StringPair { key = "X-Sandbox-ID", value = sandboxId } }) {}
    }
}