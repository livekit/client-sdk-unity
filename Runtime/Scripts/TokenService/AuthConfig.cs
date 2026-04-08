using System;
using UnityEngine;

namespace LiveKit
{
    public abstract class AuthConfig : ScriptableObject
    {
        [SerializeField] private bool _randomRoomName = true;
        [SerializeField] private bool _randomParticipantName = true;
        [SerializeField] private string _roomName = "my-room";
        [SerializeField] private string _participantName = "participant";

        public abstract bool IsValid { get; }

        public (string roomName, string participantName) ResolveNames()
        {
            var roomName = _randomRoomName
                ? $"room-{Guid.NewGuid().ToString().Substring(0, 8)}"
                : _roomName;
            var participantName = _randomParticipantName
                ? $"participant-{Guid.NewGuid().ToString().Substring(0, 8)}"
                : _participantName;
            return (roomName, participantName);
        }
    }
}