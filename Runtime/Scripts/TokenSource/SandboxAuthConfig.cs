using UnityEngine;

namespace LiveKit
{
    [CreateAssetMenu(fileName = "TokenSource", menuName = "LiveKit/Sandbox Auth")]
    public class SandboxAuthConfig : AuthConfig
    {
        [SerializeField] private string _sandboxId;

        public string SandboxId => _sandboxId?.Trim('"');

        public override bool IsValid =>
            !string.IsNullOrEmpty(SandboxId);
    }
}