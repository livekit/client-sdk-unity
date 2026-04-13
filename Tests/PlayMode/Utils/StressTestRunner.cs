using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace LiveKit.PlayModeTests.Utils
{
    /// <summary>
    /// Runs a UnityTest coroutine repeatedly to detect flakiness.
    /// Usage:
    /// <code>
    /// [UnityTest, Category("Stress"), Timeout(3600000)]
    /// public IEnumerator MyTest_100x()
    /// {
    ///     yield return StressTestRunner.Run(MyTest);
    /// }
    /// </code>
    /// Note: add a [Timeout] attribute large enough to cover all iterations,
    /// since Unity's default test timeout is 180s.
    /// </summary>
    public static class StressTestRunner
    {
        /// <summary>
        /// Runs <paramref name="test"/> the specified number of times, logging per-iteration
        /// results. Fails at the end if any iteration failed.
        /// </summary>
        /// <param name="test">Factory that returns a fresh test coroutine each call.</param>
        /// <param name="iterations">Number of times to run the test.</param>
        /// <param name="iterationTimeoutSeconds">
        /// Max seconds per iteration. If an iteration exceeds this, it is aborted and
        /// counted as failed. This prevents a single hanging iteration from blocking the
        /// entire stress run.
        /// </param>
        public static IEnumerator Run(
            Func<IEnumerator> test,
            int iterations = 20,
            float iterationTimeoutSeconds = 30f)
        {
            int passed = 0;
            int failed = 0;

            for (int i = 0; i < iterations; i++)
            {
                var iterationFailed = new bool[] { false };
                var startTime = Time.realtimeSinceStartup;
                yield return RunWithExceptionHandling(
                    test(), iterationFailed, startTime, iterationTimeoutSeconds);

                if (iterationFailed[0]) failed++;
                else passed++;

                Debug.Log($"[{i + 1}/{iterations}] {(iterationFailed[0] ? "FAIL" : "PASS")}" +
                          $" (pass: {passed}, fail: {failed})");
            }

            Debug.Log($"Stress test complete: {passed}/{iterations} passed, {failed} failed");
            Assert.Zero(failed, $"{failed}/{iterations} iterations failed");
        }

        /// <summary>
        /// Drives a coroutine (and any nested IEnumerator / CustomYieldInstruction yields)
        /// while catching exceptions and enforcing a per-iteration timeout.
        /// Sets failed[0] to true if any assertion, exception, or timeout occurs.
        /// </summary>
        static IEnumerator RunWithExceptionHandling(
            IEnumerator coroutine,
            bool[] failed,
            float startTime,
            float timeoutSeconds)
        {
            while (true)
            {
                if (Time.realtimeSinceStartup - startTime > timeoutSeconds)
                {
                    failed[0] = true;
                    yield break;
                }

                object current;
                try
                {
                    if (!coroutine.MoveNext()) yield break;
                    current = coroutine.Current;
                }
                catch (Exception)
                {
                    failed[0] = true;
                    yield break;
                }

                if (current is IEnumerator nested)
                {
                    yield return RunWithExceptionHandling(
                        nested, failed, startTime, timeoutSeconds);
                    if (failed[0]) yield break;
                }
                else if (current is CustomYieldInstruction customYield)
                {
                    // Drive CustomYieldInstruction manually so we can enforce
                    // the timeout instead of handing control to Unity.
                    while (customYield.keepWaiting)
                    {
                        if (Time.realtimeSinceStartup - startTime > timeoutSeconds)
                        {
                            failed[0] = true;
                            yield break;
                        }
                        yield return null;
                    }
                }
                else
                {
                    yield return current;
                }
            }
        }
    }
}
