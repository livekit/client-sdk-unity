using System;
using System.Collections;
using UnityEngine;

namespace LiveKit.Internal.Threading
{
    /// <summary>
    /// Global singleton used for accessing specific MonoBehaviour methods from
    /// non-MonoBehaviour contexts.
    /// </summary>
    internal class MonoBehaviourContext : MonoBehaviour
    {
        private static MonoBehaviourContext _instance;
        private const string OBJECT_NAME = "LiveKitSDK";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            _instance = FindObjectOfType<MonoBehaviourContext>();
            if (_instance == null)
            {
                GameObject obj = new GameObject(OBJECT_NAME);
                _instance = obj.AddComponent<MonoBehaviourContext>();
                DontDestroyOnLoad(obj);
            }
        }

        /// <summary>
        /// Invoked when the application is paused or resumed.
        /// </summary>
        internal static Action<bool> OnApplicationPauseEvent;

        /// <summary>
        /// Runs a coroutine from a non-MonoBehaviour context, invoking the callback when the
        /// coroutine completes.
        /// </summary>
        /// <remarks>
        /// If the singleton has already been destroyed (e.g. during play mode exit or
        /// application shutdown) the coroutine cannot be started on a MonoBehaviour, so it is
        /// drained synchronously instead. This keeps cleanup side effects working without
        /// throwing a <see cref="MissingReferenceException"/>.
        /// </remarks>
        internal static void RunCoroutine(IEnumerator coroutine, Action onComplete = null)
        {
            // Unity's overloaded == treats a destroyed object as null.
            if (_instance == null)
            {
                DrainCoroutine(coroutine);
                onComplete?.Invoke();
                return;
            }

            _instance.StartCoroutine(WrapCoroutine(coroutine, onComplete));
        }

        /// <summary>
        /// Synchronously runs a coroutine to completion, ignoring time-based yield instructions
        /// and recursing into nested enumerators. Used as a fallback when no MonoBehaviour is
        /// available to host the coroutine.
        /// </summary>
        private static void DrainCoroutine(IEnumerator coroutine)
        {
            if (coroutine == null) return;
            while (coroutine.MoveNext())
            {
                if (coroutine.Current is IEnumerator nested)
                    DrainCoroutine(nested);
            }
        }

        private static IEnumerator WrapCoroutine(IEnumerator coroutine, Action onComplete = null)
        {
            yield return coroutine;
            onComplete?.Invoke();
        }

        private void OnApplicationPause(bool pause)
        {
            OnApplicationPauseEvent?.Invoke(pause);
        }
    }
}