using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace LiveKit.EditModeTests
{
    public class MonoBehaviourContextTests
    {
        // Regression test for the MissingReferenceException thrown when the scene is stopped:
        // Unity tears down the DontDestroyOnLoad LiveKitSDK GameObject (backing the
        // MonoBehaviourContext singleton) before MeetManager.OnDestroy() runs, then
        // MicrophoneSource.Stop() calls RunCoroutine on the destroyed instance.
        //
        // The real play-mode-exit teardown ordering can't be controlled from a test, but the
        // throwing call (_instance.StartCoroutine) fires synchronously the moment its host is
        // destroyed. So we reproduce the precondition deterministically: point _instance at a
        // destroyed object via DestroyImmediate, then call RunCoroutine. Against the old code
        // this throws MissingReferenceException; against the fix it drains the coroutine
        // synchronously and invokes onComplete.
        [Test]
        public void RunCoroutine_AfterInstanceDestroyed_DrainsSynchronously_DoesNotThrow()
        {
            var instanceField = typeof(MonoBehaviourContext)
                .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(instanceField, "Expected private static MonoBehaviourContext._instance field");

            // Save the global singleton so destroying our stand-in can't disturb other tests.
            var original = instanceField.GetValue(null);
            try
            {
                var go = new GameObject("temp-monobehaviourcontext");
                var ctx = go.AddComponent<MonoBehaviourContext>();
                instanceField.SetValue(null, ctx);

                // Synchronous destroy: _instance now reports == null via Unity's overloaded ==.
                UnityEngine.Object.DestroyImmediate(go);

                bool ran = false;
                bool completed = false;

                Assert.DoesNotThrow(
                    () => MonoBehaviourContext.RunCoroutine(Body(() => ran = true), () => completed = true),
                    "RunCoroutine must not throw when the singleton has already been destroyed");

                Assert.IsTrue(ran, "coroutine body should have been drained synchronously");
                Assert.IsTrue(completed, "onComplete should fire after the drained coroutine");
            }
            finally
            {
                instanceField.SetValue(null, original);
            }
        }

        private static IEnumerator Body(Action onStep)
        {
            onStep();
            yield return null;
        }
    }
}
