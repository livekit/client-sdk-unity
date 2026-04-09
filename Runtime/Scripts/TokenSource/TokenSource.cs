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

        private static readonly string SandboxUrl = "https://cloud-api.livekit.io/api/v2/sandbox/connection-details";
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
                    server_url = literalConfig.ServerUrl,
                    participant_token = literalConfig.Token
                };

            throw new InvalidOperationException("Unknown auth type");
        }

        private async Task<ConnectionDetails> FetchConnectionDetailsFromSandbox(SandboxAuthConfig config)
        {
            var jsonBody = BuildSandboxRequestJson(config);

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

        private static string BuildSandboxRequestJson(SandboxAuthConfig config)
        {
            var parts = new List<string>();
            AddJsonField(parts, "room_name", config.RoomName);
            AddJsonField(parts, "participant_name", config.ParticipantName);
            AddJsonField(parts, "participant_identity", config.ParticipantIdentity);
            AddJsonField(parts, "participant_metadata", config.ParticipantMetadata);

            if (config.ParticipantAttributes != null && config.ParticipantAttributes.Count > 0)
            {
                var attrParts = new List<string>();
                foreach (var attr in config.ParticipantAttributes)
                {
                    if (!string.IsNullOrEmpty(attr.key))
                        attrParts.Add($"\"{attr.key}\":\"{attr.value}\"");
                }
                if (attrParts.Count > 0)
                    parts.Add("\"participant_attributes\":{" + string.Join(",", attrParts) + "}");
            }

            if (!string.IsNullOrEmpty(config.AgentName) || !string.IsNullOrEmpty(config.AgentMetadata))
            {
                var agentParts = new List<string>();
                AddJsonField(agentParts, "agent_name", config.AgentName);
                AddJsonField(agentParts, "metadata", config.AgentMetadata);
                var agentDispatch = "{" + string.Join(",", agentParts) + "}";
                parts.Add($"\"room_config\":{{\"agents\":[{agentDispatch}]}}");
            }

            return "{" + string.Join(",", parts) + "}";
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
        public string server_url;
        public string participant_token;
    }
}
