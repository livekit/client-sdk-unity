using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace LiveKit
{
    public class TokenSource : MonoBehaviour
    {
        [SerializeField] private AuthConfig _config;

        private static readonly string SandboxUrl = "https://cloud-api.livekit.io/api/sandbox/connection-details";
        private static readonly HttpClient HttpClient = new HttpClient();

        public async Task<ConnectionDetails> FetchConnectionDetails()
        {
            if (_config == null)
                throw new InvalidOperationException("Auth configuration was not provided");
            if (!_config.IsValid)
                throw new InvalidOperationException("Auth configuration is invalid");

            if (_config is SandboxAuthConfig sandboxConfig)
                return await FetchConnectionDetailsFromSandbox(sandboxConfig);

            if (_config is LiteralAuthConfig literalConfig)
                return new ConnectionDetails
                {
                    serverUrl = literalConfig.ServerUrl,
                    participantToken = literalConfig.Token
                };

            throw new InvalidOperationException("Unknown auth type");
        }

        private async Task<ConnectionDetails> FetchConnectionDetailsFromSandbox(SandboxAuthConfig config)
        {
            var parts = new List<string>();
            AddJsonField(parts, "roomName", config.RoomName);
            AddJsonField(parts, "participantName", config.ParticipantName);
            AddJsonField(parts, "participantIdentity", config.ParticipantIdentity);
            AddJsonField(parts, "participantMetadata", config.ParticipantMetadata);
            AddJsonField(parts, "agentName", config.AgentName);
            AddJsonField(parts, "agentMetadata", config.AgentMetadata);
            var jsonBody = "{" + string.Join(",", parts) + "}";

            var request = new HttpRequestMessage(HttpMethod.Post, SandboxUrl);
            request.Headers.Add("X-Sandbox-ID", config.SandboxId);
            var content = new StringContent(jsonBody, System.Text.Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            request.Content = content;

            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Error from LiveKit Cloud sandbox: {response.StatusCode}, response: {response}");

            var jsonContent = await response.Content.ReadAsStringAsync();
            return JsonUtility.FromJson<ConnectionDetails>(jsonContent);
        }

        private static void AddJsonField(List<string> parts, string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
                parts.Add($"\"{key}\":\"{value}\"");
        }
    }

    [Serializable]
    public struct ConnectionDetails
    {
        public string serverUrl;
        public string roomName;
        public string participantName;
        public string participantToken;
    }
}
