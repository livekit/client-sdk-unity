using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace LiveKit.PlayModeTests.Utils
{
    public static class TimeoutExtensions
    {
        public static IEnumerator WithTimeout(this CustomYieldInstruction instruction, float timeoutSeconds = 30f)
        {
            var startTime = Time.realtimeSinceStartup;
            while (instruction.keepWaiting)
            {
                if (Time.realtimeSinceStartup - startTime > timeoutSeconds)
                    Assert.Fail($"Yield timed out after {timeoutSeconds}s");
                yield return null;
            }
        }

        public static IEnumerator WithTimeout(this IEnumerator coroutine, float timeoutSeconds = 30f)
        {
            var startTime = Time.realtimeSinceStartup;
            while (coroutine.MoveNext())
            {
                if (Time.realtimeSinceStartup - startTime > timeoutSeconds)
                    Assert.Fail($"Yield timed out after {timeoutSeconds}s");
                yield return coroutine.Current;
            }
        }
    }
}
