using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace LiveKit.PlayModeTests.Utils
{
    public class TestRoomContext : IDisposable
    {
        public readonly string RoomName = $"unity-test-{Guid.NewGuid()}";
        public List<Room> Rooms { get; private set; } = new List<Room>();
        public string? ConnectionError { get; private set; }

        private List<ConnectionOptions> _connectionOptions;
        private bool _disposed = false;

        public TestRoomContext() : this(ConnectionOptions.Default) {}
        public TestRoomContext(ConnectionOptions options) : this(new[] { options }) {}

        public TestRoomContext(IEnumerable<ConnectionOptions> options)
        {
            var withDefaults = options.Select((options, i) =>
            {
                options.Identity ??= $"unity-{i}";
                options.DisplayName ??= $"Unity {i}";
                return options;
            });
            _connectionOptions = withDefaults.ToList();
        }

        public struct ConnectionOptions
        {
            public string Identity;
            public string DisplayName;
            public string Metadata;
            public bool CanPublish;
            public bool CanPublishData;
            public List<string> CanPublishSources;
            public bool CanSubscribe;
            public bool CanUpdateOwnMetadata;
            public string ServerUrl;

            public static ConnectionOptions Default => new ConnectionOptions
            {
                // TODO: Once we migrate to C# 10.0, remove this properly in favor
                // of a parameterless constructor.
                CanPublish = true,
                CanPublishData = true,
                CanSubscribe = true,
            };
        }

        public IEnumerator ConnectAll()
        {
            foreach (var options in _connectionOptions)
            {
                var token = CreateToken(options);
                var room = new Room();
                var roomOptions = new RoomOptions();
                var connect = room.Connect(options.ServerUrl ?? _serverUrl, token, roomOptions);
                yield return connect;

                if (connect.IsError)
                {
                    // TODO: Expose error details once available through API
                    ConnectionError = $"Participant '{options.Identity}' failed to connect to test room";
                    yield break;
                }
                Rooms.Add(room);
            }
        }

        private void DisconnectAll()
        {
            foreach (var room in Rooms)
                room?.Disconnect();
            Rooms.Clear();
        }

        private string CreateToken(ConnectionOptions options)
        {
            var claims = new AccessToken.Claims
            {
                iss = _apiKey,
                sub = options.Identity,
                name = options.DisplayName,
                video = new AccessToken.VideoGrants
                {
                    room = RoomName,
                    roomJoin = true,
                    canPublish = options.CanPublish,
                    canSubscribe = options.CanSubscribe,
                    canPublishData = options.CanPublishData,
                    canPublishSources = options.CanPublishSources,
                    canUpdateOwnMetadata = options.CanUpdateOwnMetadata
                },
                metadata = options.Metadata
            };
            return AccessToken.Encode(claims, _apiSecret);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            if (disposing) DisconnectAll();
            _disposed = true;
        }

        private static string _serverUrl => ReadEnv("LK_TEST_URL", "ws://localhost:7880");
        private static string _apiKey => ReadEnv("LK_TEST_API_KEY", "devkey");
        private static string _apiSecret => ReadEnv("LK_TEST_API_SECRET", "secret");

        private static string ReadEnv(string key, string defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrEmpty(value))
                return defaultValue;
            return value.Trim();
        }
    }
}