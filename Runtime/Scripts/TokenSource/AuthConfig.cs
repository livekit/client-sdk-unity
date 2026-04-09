using UnityEngine;

namespace LiveKit
{
    public abstract class AuthConfig : ScriptableObject
    {
        public abstract bool IsValid { get; }
    }
}