using System;
using System.Collections.Generic;

namespace LiveKit.PlayModeTests.Utils
{
    public static class AccessToken
    {
        [Serializable]
        public struct Claims
        {
            public long exp;
            public string iss;
            public long nbf;
            public string sub;

            public string name;
            public VideoGrants video;
            public SIPGrants sip;
            public string sha256;
            public string metadata;
        }

        [Serializable]
        public struct VideoGrants
        {
            public bool roomCreate;
            public bool roomList;
            public bool roomRecord;
            public bool roomAdmin;
            public bool roomJoin;
            public string room;
            public bool canPublish;
            public bool canSubscribe;
            public bool canPublishData;
            public List<string> canPublishSources;
            public bool canUpdateOwnMetadata;
            public bool ingressAdmin;
            public bool hidden;
            public bool recorder;
        }

        [Serializable]
        public struct SIPGrants
        {
            public bool admin;
            public bool call;
        }

        public static string Encode(Claims claims, string apiSecret)
        {
            if (string.IsNullOrEmpty(claims.iss))
                throw new ArgumentException("ISS field must be set to API key");

            if (claims.video.roomJoin && (string.IsNullOrEmpty(claims.sub) || string.IsNullOrEmpty(claims.video.room)))
                throw new ArgumentException("Token grants room join but doesn't have an identity or room");

            if (claims.nbf == 0 || claims.exp == 0)
            {
                var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                claims.nbf = currentTime;
                claims.exp = currentTime + DEFAULT_TTL;
            }
            return JsonWebToken.Encode(claims, apiSecret, JsonWebToken.Algorithm.HS256);
        }

        private static long DEFAULT_TTL = 30 * 60; // 30 minutes
    }
}