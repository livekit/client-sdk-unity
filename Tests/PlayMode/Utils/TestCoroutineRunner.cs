using System.Collections;
using UnityEngine;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// Helper for running coroutines from non-MonoBehaviour test code.
    /// Creates a temporary GameObject with a MonoBehaviour to host coroutines.
    /// </summary>
    public class TestCoroutineRunner : MonoBehaviour
    {
        private static TestCoroutineRunner _instance;

        private static TestCoroutineRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("TestCoroutineRunner");
                    _instance = go.AddComponent<TestCoroutineRunner>();
                }
                return _instance;
            }
        }

        public static Coroutine Start(IEnumerator routine)
        {
            return Instance.StartCoroutine(routine);
        }

        public static void Stop(Coroutine coroutine)
        {
            if (_instance != null && coroutine != null)
                _instance.StopCoroutine(coroutine);
        }

        public static void Cleanup()
        {
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                _instance = null;
            }
        }
    }
}
