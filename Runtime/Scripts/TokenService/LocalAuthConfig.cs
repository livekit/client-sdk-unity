using UnityEngine;

namespace LiveKit
{
    [CreateAssetMenu(fileName = "TokenService", menuName = "LiveKit/Local Auth")]
    public class LocalAuthConfig : AuthConfig
    {
        [SerializeField] private string _liveKitUrl;
        [SerializeField] private string _apiKey;
        [SerializeField] private string _apiSecret;

        public string LiveKitUrl => _liveKitUrl;
        public string ApiKey => _apiKey;
        public string ApiSecret => _apiSecret;

        public override bool IsValid =>
            !string.IsNullOrEmpty(LiveKitUrl) && LiveKitUrl.StartsWith("ws") &&
            !string.IsNullOrEmpty(ApiKey) &&
            !string.IsNullOrEmpty(ApiSecret);

        public ConnectionDetails GenerateConnectionDetails(string roomName, string participantName)
        {
            var claims = new AccessToken.Claims
            {
                iss = _apiKey,
                sub = participantName,
                name = participantName,
                video = new AccessToken.VideoGrants
                {
                    room = roomName,
                    roomJoin = true,
                    canPublish = true,
                    canSubscribe = true,
                    canPublishData = true,
                }
            };
            var token = AccessToken.Encode(claims, _apiSecret);
            return new ConnectionDetails
            {
                serverUrl = _liveKitUrl,
                roomName = roomName,
                participantName = participantName,
                participantToken = token
            };
        }
    }
}
