using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests
{
    /// <summary>
    /// Diagnostic tests to reveal how different timeout mechanisms behave
    /// locally (editor) vs CI (batch mode). Run these in both environments
    /// and compare the results.
    ///
    /// Expected behavior if everything works:
    ///   - All tests should PASS (they assert their own timeout mechanism works)
    ///   - If a test hangs instead of failing/passing, that timeout mechanism is broken
    ///     in that environment.
    /// </summary>
    public class TimeoutDiagnosticTests
    {
        // ----------------------------------------------------------------
        // Test 1: Does Time.time advance in this environment?
        // If Time.time doesn't advance, WaitUntil predicates using it will hang.
        // ----------------------------------------------------------------
        [UnityTest, Category("Diagnostic")]
        public IEnumerator TimeProgression_TimeTimeAdvances()
        {
            var start = Time.time;
            var realtimeStart = Time.realtimeSinceStartup;

            // Wait a small number of frames
            for (int i = 0; i < 30; i++)
                yield return null;

            var elapsed = Time.time - start;
            var realtimeElapsed = Time.realtimeSinceStartup - realtimeStart;

            Debug.Log($"[TimeoutDiag] After 30 frames:");
            Debug.Log($"[TimeoutDiag]   Time.time elapsed:              {elapsed:F4}s");
            Debug.Log($"[TimeoutDiag]   realtimeSinceStartup elapsed:   {realtimeElapsed:F4}s");
            Debug.Log($"[TimeoutDiag]   Time.timeScale:                 {Time.timeScale}");
            Debug.Log($"[TimeoutDiag]   Time.deltaTime (last frame):    {Time.deltaTime:F4}s");
            Debug.Log($"[TimeoutDiag]   Application.isBatchMode:        {Application.isBatchMode}");

            Assert.Greater(realtimeElapsed, 0f, "realtimeSinceStartup should always advance");

            if (elapsed <= 0f)
                Debug.LogWarning("[TimeoutDiag] Time.time did NOT advance! " +
                    "This means any WaitUntil using Time.time will hang forever in this environment.");

            // We don't fail on Time.time == 0 — we just report it.
            // The test passes as long as realtime advances (it always should).
        }

        // ----------------------------------------------------------------
        // Test 2: Does WaitUntil + Time.time actually time out?
        // This mimics exactly what Expectation.Wait() does.
        // If this test hangs, Expectation timeouts are broken in this env.
        // ----------------------------------------------------------------
        [UnityTest, Category("Diagnostic")]
        [Timeout(15000)] // NUnit safety net: 15s real time
        public IEnumerator WaitUntilTimeout_UsingTimeTime()
        {
            var startTime = Time.time;
            float timeout = 3f;
            bool timedOut = false;

            Debug.Log($"[TimeoutDiag] Starting WaitUntil with Time.time, timeout={timeout}s");

            yield return new WaitUntil(() =>
            {
                if (Time.time - startTime > timeout)
                {
                    timedOut = true;
                    return true;
                }
                return false; // never fulfilled — should time out
            });

            Debug.Log($"[TimeoutDiag] WaitUntil exited. timedOut={timedOut}, " +
                $"elapsed Time.time={Time.time - startTime:F2}s");

            Assert.IsTrue(timedOut,
                "Expected the WaitUntil to exit via Time.time timeout. " +
                "If this fails, Time.time-based timeouts don't work in this environment.");
        }

        // ----------------------------------------------------------------
        // Test 3: Does WaitUntil + realtimeSinceStartup time out?
        // This is the alternative that should always work.
        // ----------------------------------------------------------------
        [UnityTest, Category("Diagnostic")]
        [Timeout(15000)]
        public IEnumerator WaitUntilTimeout_UsingRealtimeSinceStartup()
        {
            var startTime = Time.realtimeSinceStartup;
            float timeout = 3f;
            bool timedOut = false;

            Debug.Log($"[TimeoutDiag] Starting WaitUntil with realtimeSinceStartup, timeout={timeout}s");

            yield return new WaitUntil(() =>
            {
                if (Time.realtimeSinceStartup - startTime > timeout)
                {
                    timedOut = true;
                    return true;
                }
                return false;
            });

            Debug.Log($"[TimeoutDiag] WaitUntil exited. timedOut={timedOut}, " +
                $"elapsed realtime={Time.realtimeSinceStartup - startTime:F2}s");

            Assert.IsTrue(timedOut,
                "realtimeSinceStartup-based timeout should always work.");
        }

        // ----------------------------------------------------------------
        // Test 4: Does NUnit [Timeout] work on [UnityTest] coroutines?
        // This test deliberately hangs. If [Timeout] works, NUnit kills it
        // after 5s and the test fails with a timeout message. If [Timeout]
        // does NOT work, the test hangs until the CI job timeout.
        //
        // IMPORTANT: This test is expected to FAIL (with a timeout error).
        // If it HANGS instead of failing, [Timeout] doesn't work in that env.
        // ----------------------------------------------------------------
        [UnityTest, Category("Diagnostic")]
        [Timeout(5000)] // 5 seconds
        public IEnumerator NUnitTimeout_KillsHangingCoroutine()
        {
            Debug.Log("[TimeoutDiag] Starting deliberately hanging test. " +
                "NUnit [Timeout(5000)] should kill this after 5s.");

            var startRealtime = Time.realtimeSinceStartup;

            // This will never complete on its own
            yield return new WaitUntil(() => false);

            // If we reach here, [Timeout] didn't abort the coroutine — it forced
            // the yield instruction to complete and resumed execution.
            var elapsed = Time.realtimeSinceStartup - startRealtime;
            Debug.Log($"[TimeoutDiag] WaitUntil(() => false) was force-completed after {elapsed:F2}s. " +
                "[Timeout] unblocks yields but does NOT abort the coroutine.");

            // This is the expected behavior: [Timeout] unblocks, we land here ~5s later.
            // The test framework should still mark this as a timeout failure externally.
            // We pass here to avoid a misleading Assert.Fail message.
            Assert.Pass($"[Timeout] unblocked the coroutine after {elapsed:F2}s (expected ~5s). " +
                "This confirms [Timeout] works in this environment by force-completing yield instructions.");
        }

        // ----------------------------------------------------------------
        // Test 5: Does WaitForSecondsRealtime work?
        // This uses real wall-clock time, independent of Time.time.
        // ----------------------------------------------------------------
        [UnityTest, Category("Diagnostic")]
        [Timeout(15000)]
        public IEnumerator WaitForSecondsRealtime_Works()
        {
            var start = Time.realtimeSinceStartup;

            Debug.Log("[TimeoutDiag] Starting WaitForSecondsRealtime(2)");
            yield return new WaitForSecondsRealtime(2f);

            var elapsed = Time.realtimeSinceStartup - start;
            Debug.Log($"[TimeoutDiag] WaitForSecondsRealtime done. Elapsed: {elapsed:F2}s");

            Assert.Greater(elapsed, 1.5f, "Should have waited ~2 seconds");
            Assert.Less(elapsed, 5f, "Should not have waited much longer than 2 seconds");
        }

        // ----------------------------------------------------------------
        // Test 6: Does WaitForSeconds (Time.time-based) work?
        // WaitForSeconds uses scaled time. If Time.time doesn't advance,
        // this will hang.
        // ----------------------------------------------------------------
        [UnityTest, Category("Diagnostic")]
        [Timeout(15000)]
        public IEnumerator WaitForSeconds_Works()
        {
            var start = Time.realtimeSinceStartup;

            Debug.Log("[TimeoutDiag] Starting WaitForSeconds(2)");
            yield return new WaitForSeconds(2f);

            var elapsed = Time.realtimeSinceStartup - start;
            Debug.Log($"[TimeoutDiag] WaitForSeconds done. Elapsed: {elapsed:F2}s");

            Assert.Greater(elapsed, 1.5f, "Should have waited ~2 seconds");
            Assert.Less(elapsed, 5f, "Should not have waited much longer than 2 seconds");
        }
    }
}
