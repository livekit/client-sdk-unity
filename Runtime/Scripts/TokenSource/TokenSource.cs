using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using LiveKit.Internal.Threading;
using Newtonsoft.Json;

namespace LiveKit
{
    /// <summary>
    /// Factory for the built-in <see cref="ITokenSource"/> implementations. The concrete implementations
    /// are private; obtain them through the factory methods and work with the returned
    /// <see cref="ITokenSourceFixed"/> or <see cref="ITokenSourceConfigurable"/>.
    /// </summary>
    public static class TokenSource
    {
        public delegate Task<ConnectionDetails> CustomTokenFunction();

        internal const string DevelopmentTokenServerUrl = "https://cloud-api.livekit.io/api/v2/sandbox/connection-details";

        /// <summary>
        /// Returns a fixed server URL and participant token. Suitable when credentials are pregenerated
        /// (e.g. via the LiveKit CLI or LiveKit Cloud project page).
        /// </summary>
        public static ITokenSourceFixed Literal(string serverUrl, string participantToken)
        {
            return new LiteralSource(serverUrl, participantToken);
        }

        /// <summary>
        /// Posts a JSON request to a token-server endpoint and returns the parsed <see cref="ConnectionDetails"/>.
        /// The body is built from per-call <see cref="TokenSourceFetchOptions"/> (room name, participant info,
        /// agent dispatch, etc.). Use for production token servers — see
        /// https://docs.livekit.io/frontends/build/authentication/endpoint/.
        /// </summary>
        public static ITokenSourceConfigurable Endpoint(string endpointUrl, IEnumerable<StringPair> headers)
        {
            return new EndpointSource(endpointUrl, headers);
        }

        /// <summary>
        /// Delegates connection-detail retrieval to a user-supplied async function. Use this when your
        /// app already has its own token-fetching code (custom auth flow, cached tokens, etc.).
        /// </summary>
        public static ITokenSourceFixed Custom(CustomTokenFunction customTokenFunction)
        {
            return new CustomSource(customTokenFunction);
        }

        [Obsolete("Use TokenSource.DevelopmentTokenServer instead")]
        public static ITokenSourceConfigurable SandboxTokenServer(string sandboxId)
        {
            return DevelopmentTokenServer(sandboxId);
        }

        /// <summary>
        /// Convenience <see cref="Endpoint"/> preconfigured for LiveKit Cloud development token servers.
        /// Intended for development and testing only — see
        /// https://docs.livekit.io/frontends/build/authentication/sandbox-token-server/.
        /// </summary>
        public static ITokenSourceConfigurable DevelopmentTokenServer(string tokenServerId)
        {
            return new EndpointSource(
                DevelopmentTokenServerUrl,
                new[] { new StringPair { key = "X-Sandbox-ID", value = tokenServerId } });
        }

        private sealed class LiteralSource : ITokenSourceFixed
        {
            private string _serverUrl;
            private string _participantToken;

            public LiteralSource(string serverUrl, string participantToken)
            {
                _serverUrl = serverUrl;
                _participantToken = participantToken;
            }

            public TaskYieldInstruction<ConnectionDetails> FetchConnectionDetails()
            {
                var result = new ConnectionDetails { ServerUrl = _serverUrl, ParticipantToken = _participantToken };
                return new TaskYieldInstruction<ConnectionDetails>(Task.FromResult(result));
            }
        }

        private sealed class CustomSource : ITokenSourceFixed
        {
            private CustomTokenFunction _customTokenFunction;

            public CustomSource(CustomTokenFunction customTokenFunction)
            {
                _customTokenFunction = customTokenFunction;
            }

            public TaskYieldInstruction<ConnectionDetails> FetchConnectionDetails()
            {
                // Route a synchronous throw (or a null return) from the user's function through the
                // instruction's IsError/Exception, so callers never have to guard the call itself.
                Task<ConnectionDetails> task;
                try
                {
                    task = _customTokenFunction()
                        ?? Task.FromException<ConnectionDetails>(
                            new InvalidOperationException("Custom token function returned a null task"));
                }
                catch (Exception e)
                {
                    task = Task.FromException<ConnectionDetails>(e);
                }
                return new TaskYieldInstruction<ConnectionDetails>(task);
            }
        }

        private sealed class EndpointSource : ITokenSourceConfigurable
        {
            private string _endpointUrl;
            IEnumerable<StringPair> _headers;
            private static readonly HttpClient HttpClient = new HttpClient();

            public EndpointSource(string endpointUrl, IEnumerable<StringPair> headers)
            {
                _endpointUrl = endpointUrl;
                _headers = headers;
            }

            public TaskYieldInstruction<ConnectionDetails> FetchConnectionDetails(TokenSourceFetchOptions options)
            {
                // Async methods can't return the (non-awaitable) instruction directly, so the actual
                // request lives in the helper below; the returned task carries any synchronous throw.
                return new TaskYieldInstruction<ConnectionDetails>(FetchConnectionDetailsAsync(options));
            }

            private async Task<ConnectionDetails> FetchConnectionDetailsAsync(TokenSourceFetchOptions options)
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

                if (!string.IsNullOrEmpty(options.AgentName) || !string.IsNullOrEmpty(options.AgentMetadata) || !string.IsNullOrEmpty(options.AgentDeployment))
                {
                    request.RoomConfig = new RoomConfig
                    {
                        Agents = new List<AgentDispatch>
                        {
                            new AgentDispatch
                            {
                                AgentName = NullIfEmpty(options.AgentName),
                                Metadata = NullIfEmpty(options.AgentMetadata),
                                Deployment = NullIfEmpty(options.AgentDeployment)
                            }
                        }
                    };
                }

                return request;
            }

            private static string NullIfEmpty(string value) =>
                string.IsNullOrEmpty(value) ? null : value;
        }
    }

    /// <summary>
    /// Marker interface for any source of LiveKit <see cref="ConnectionDetails"/>.
    /// Implementations are either <see cref="ITokenSourceFixed"/> or <see cref="ITokenSourceConfigurable"/>.
    /// </summary>
    public interface ITokenSource
    {
    }

    /// <summary>
    /// A token source whose connection details are fully determined at construction time and cannot be
    /// influenced by per-call options (e.g. literal credentials or a user-supplied callback).
    /// </summary>
    public interface ITokenSourceFixed : ITokenSource
    {
        public TaskYieldInstruction<ConnectionDetails> FetchConnectionDetails();
    }

    /// <summary>
    /// A token source that accepts per-call <see cref="TokenSourceFetchOptions"/> to parameterize the
    /// request (e.g. an HTTP endpoint that needs room/participant info per fetch).
    /// </summary>
    public interface ITokenSourceConfigurable : ITokenSource
    {
        public TaskYieldInstruction<ConnectionDetails> FetchConnectionDetails(TokenSourceFetchOptions options);
    }

    [Obsolete("Use TokenSource.Literal(...) instead")]
    public class TokenSourceLiteral : ITokenSourceFixed
    {
        private readonly ITokenSourceFixed _inner;

        public TokenSourceLiteral(string serverUrl, string participantToken)
        {
            _inner = TokenSource.Literal(serverUrl, participantToken);
        }

        public TaskYieldInstruction<ConnectionDetails> FetchConnectionDetails() => _inner.FetchConnectionDetails();
    }

    [Obsolete("Use TokenSource.Custom(...) instead")]
    public class TokenSourceCustom : ITokenSourceFixed
    {
        // v2.0.0 declared the delegate nested here; keep it so explicit
        // TokenSourceCustom.CustomTokenFunction references still compile.
        public delegate Task<ConnectionDetails> CustomTokenFunction();

        private readonly ITokenSourceFixed _inner;

        public TokenSourceCustom(CustomTokenFunction customTokenFunction)
        {
            // Lambda (not .Invoke) so a null delegate surfaces at fetch time via
            // IsError/Exception, matching v2.0.0 behavior, not as a ctor throw.
            _inner = TokenSource.Custom(() => customTokenFunction());
        }

        public TaskYieldInstruction<ConnectionDetails> FetchConnectionDetails() => _inner.FetchConnectionDetails();
    }

    [Obsolete("Use TokenSource.Endpoint(...) instead")]
    public class TokenSourceEndpoint : ITokenSourceConfigurable
    {
        private readonly ITokenSourceConfigurable _inner;

        public TokenSourceEndpoint(string endpointUrl, IEnumerable<StringPair> headers)
        {
            _inner = TokenSource.Endpoint(endpointUrl, headers);
        }

        public TaskYieldInstruction<ConnectionDetails> FetchConnectionDetails(TokenSourceFetchOptions options) => _inner.FetchConnectionDetails(options);
    }

    [Obsolete("Use TokenSource.DevelopmentTokenServer(...) instead")]
    public class TokenSourceSandbox : TokenSourceEndpoint
    {
        public TokenSourceSandbox(string sandboxId)
            : base(TokenSource.DevelopmentTokenServerUrl, new[] { new StringPair { key = "X-Sandbox-ID", value = sandboxId } }) {}
    }
}
