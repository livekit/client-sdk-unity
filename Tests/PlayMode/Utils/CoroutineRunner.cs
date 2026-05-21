using UnityEngine;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// Empty MonoBehaviour used by tests to host long-running coroutines
    /// (e.g. <see cref="RtcVideoSource.Update"/> and <see cref="VideoStream.Update"/>)
    /// that the test itself cannot host because <c>[UnityTest]</c> bodies must yield
    /// back to the test runner.
    /// </summary>
    public class CoroutineRunner : MonoBehaviour { }
}
