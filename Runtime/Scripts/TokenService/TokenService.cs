using System;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEngine;

namespace LiveKit
{
    public class TokenService : MonoBehaviour
    {
        [SerializeField] private AuthConfig _config;

        private static readonly string SandboxUrl = "https://cloud-api.livekit.io/api/sandbox/connection-details";
        private static readonly HttpClient HttpClient = new HttpClient();

        public async Task<ConnectionDetails> FetchConnectionDetails()
        {
            if (_config == null)
                throw new InvalidOperationException("Auth configuration was not provided");

            var (roomName, participantName) = _config.ResolveNames();
            return await FetchConnectionDetails(roomName, participantName);
        }

        public async Task<ConnectionDetails> FetchConnectionDetails(string roomName, string participantName)
        {
            if (_config == null)
                throw new InvalidOperationException("Auth configuration was not provided");
            if (!_config.IsValid)
                throw new InvalidOperationException("Auth configuration is invalid");

            if (_config is SandboxAuth sandboxConfig)
                return await FetchConnectionDetailsFromSandbox(roomName, participantName, sandboxConfig.SandboxId);

            if (_config is HardcodedAuth hardcodedConfig)
                return new ConnectionDetails
                {
                    serverUrl = hardcodedConfig.ServerUrl,
                    roomName = roomName,
                    participantName = participantName,
                    participantToken = hardcodedConfig.Token
                };

            if (_config is LocalAuth localConfig)
                return localConfig.GenerateConnectionDetails(roomName, participantName);

            throw new InvalidOperationException("Unknown auth type");
        }

        private async Task<ConnectionDetails> FetchConnectionDetailsFromSandbox(string roomName, string participantName, string sandboxId)
        {
            var jsonBody = JsonUtility.ToJson(new SandboxRequest { roomName = roomName, participantName = participantName });

            Debug.Log($"Room name requested {roomName}");

            var request = new HttpRequestMessage(HttpMethod.Post, SandboxUrl);
            request.Headers.Add("X-Sandbox-ID", sandboxId);
            var content = new StringContent(jsonBody, System.Text.Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            request.Content = content;

            Debug.Log(jsonBody);

            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Error from LiveKit Cloud sandbox: {response.StatusCode}, response: {response}");

            var jsonContent = await response.Content.ReadAsStringAsync();
            Debug.Log(jsonContent);
            return JsonUtility.FromJson<ConnectionDetails>(jsonContent);
        }
    }

    [Serializable]
    struct SandboxRequest
    {
        public string roomName;
        public string participantName;
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