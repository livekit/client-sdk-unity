using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace LiveKit
{
    public class TokenSourceHelper : MonoBehaviour
    {
        [SerializeField] private TokenSourceConfig _config;

        public Task<ConnectionDetails> FetchConnectionDetails() => FetchConnectionDetails(null);

        ITokenSource _tokenSource;

        public void Start()
        {
            if (_config == null)
                throw new InvalidOperationException("Token source configuration was not provided");
            if (!_config.IsValid)
                throw new InvalidOperationException("Token source configuration is invalid");

            switch (_config.TokenSourceType)
            {
                case TokenSourceType.Sandbox:
                    _tokenSource = new TokenSourceSandbox(_config.SandboxId);
                    break;

                case TokenSourceType.Endpoint:
                    _tokenSource = new TokenSourceEndpoint(_config.EndpointUrl, _config.EndpointHeaders);
                    break;

                case TokenSourceType.Literal:
                    _tokenSource = new TokenSourceLiteral(_config.ServerUrl, _config.Token);    
                    break;

                default:
                    throw new InvalidOperationException("Unknown token source type");
            }
        }

        /// <summary>
        /// Fetches connection details, merging per-call <paramref name="options"/> over the asset-backed
        /// <see cref="TokenSourceConfig"/>. For each field, a non-null value on <paramref name="options"/>
        /// overrides the config value. Ignored when the token source type is <see cref="TokenSourceType.Literal"/>.
        /// </summary>
        public async Task<ConnectionDetails> FetchConnectionDetails(TokenSourceFetchOptions options)   
        {
            switch (_tokenSource)
            {
                case ITokenSourceConfigurable configurable:
                    return await configurable.FetchConnectionDetails(Coalesce(_config, options));
        
                case ITokenSourceFixed fixedSource:
                    if (options != null)
                        Debug.Log("Token source helper has fixed config, fetch options are ignored.");
                    return await fixedSource.FetchConnectionDetails();

                default:
                    throw new InvalidOperationException("Unknown token source type");
            }                                                                                          
        }       

        private static TokenSourceFetchOptions Coalesce(TokenSourceConfig config, TokenSourceFetchOptions options)
        {
            Dictionary<string, string> participantAttributes = null;
            if (options?.ParticipantAttributes != null)
            {
                var attrs = options.ParticipantAttributes
                    .Where(kv => !string.IsNullOrEmpty(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                if (attrs.Count > 0)
                    participantAttributes = attrs;
            }
            else if (config.ParticipantAttributes != null && config.ParticipantAttributes.Count > 0)
            {
                var attrs = config.ParticipantAttributes
                    .Where(a => !string.IsNullOrEmpty(a.key))
                    .ToDictionary(a => a.key, a => a.value);
                if (attrs.Count > 0)
                    participantAttributes = attrs;
            }

            return new TokenSourceFetchOptions
            {
                RoomName = Coalesce(options?.RoomName, config.RoomName),
                ParticipantName = Coalesce(options?.ParticipantName, config.ParticipantName),
                ParticipantIdentity = Coalesce(options?.ParticipantIdentity, config.ParticipantIdentity),
                ParticipantMetadata = Coalesce(options?.ParticipantMetadata, config.ParticipantMetadata),
                ParticipantAttributes = participantAttributes,
                AgentName = Coalesce(options?.AgentName, config.AgentName),
                AgentMetadata = Coalesce(options?.AgentMetadata, config.AgentMetadata),
            };
        }

        private static string NullIfEmpty(string value) =>
            string.IsNullOrEmpty(value) ? null : value;

        private static string Coalesce(string primary, string fallback) =>
            NullIfEmpty(primary) ?? NullIfEmpty(fallback);
    }
}
