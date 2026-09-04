using System;
using UnityEngine;

/// <summary>
/// Coroutine runner and application-pause relay for <see cref="AecMicrophoneSource"/>, attached
/// to the microphone GameObject. Stands in for the SDK's internal <c>MonoBehaviourContext</c>,
/// which sample code cannot reach.
/// </summary>
internal sealed class AecMicrophoneHost : MonoBehaviour
{
    public event Action<bool> Paused;

    private void OnApplicationPause(bool pause)
    {
        Paused?.Invoke(pause);
    }

    private void OnDestroy()
    {
        Paused = null;
    }
}
