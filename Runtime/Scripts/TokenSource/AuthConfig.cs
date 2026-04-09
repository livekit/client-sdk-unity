using System;
using UnityEngine;

namespace LiveKit
{
    public abstract class AuthConfig : ScriptableObject
    {
        [SerializeField] private bool _randomRoomName = true;
        [SerializeField] private bool _randomParticipantName = true;
        [SerializeField] private string _roomName = "my-room";
        [SerializeField] private string _participantName = "unity-participant";

        public abstract bool IsValid { get; }

        public (string roomName, string participantName) ResolveNames()
        {
            var roomName = _randomRoomName ? null : _roomName;
            var participantName = _randomParticipantName ? null : _participantName;
            return (roomName, participantName);
        }
    }
}