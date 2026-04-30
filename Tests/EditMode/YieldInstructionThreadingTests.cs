using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class YieldInstructionThreadingTests
    {
        // Test subclass that exposes the protected IsDone / IsError setters and a
        // payload field whose visibility we want to assert is bound to the IsDone
        // release semantics.
        private sealed class ProbeInstruction : YieldInstruction
        {
            public string Payload;
            public void CompleteWith(string payload)
            {
                Payload = payload;
                IsDone = true; // volatile write — must publish Payload to other threads
            }
            public new bool IsError { get => base.IsError; set => base.IsError = value; }
        }

        // Smoke test for the volatile IsDone field: a background thread sets
        // IsDone=true; the test thread spins on it. Without volatile semantics
        // (or on a weak memory model), the JIT could in principle hoist the
        // read and hang. The bounded wait turns a hang into a clear failure.
        [Test, Timeout(5_000)]
        public void IsDone_SetFromBackgroundThread_ObservedByForegroundSpin()
        {
            for (int trial = 0; trial < 200; trial++)
            {
                var instruction = new ProbeInstruction();
                var ready = new ManualResetEventSlim(false);
                var setter = Task.Run(() =>
                {
                    ready.Wait();
                    instruction.CompleteWith("payload-" + trial);
                });

                ready.Set();

                var spinDeadline = DateTime.UtcNow.AddMilliseconds(500);
                while (!instruction.IsDone)
                {
                    if (DateTime.UtcNow > spinDeadline)
                        Assert.Fail($"Trial {trial}: IsDone not observed within 500ms — volatile read may not be honored.");
                }

                // After observing IsDone == true via the volatile read, the Payload write
                // that happened-before the IsDone write on the setter thread must be visible.
                Assert.AreEqual("payload-" + trial, instruction.Payload,
                    $"Trial {trial}: IsDone observed but Payload was stale. Release/acquire pairing broken.");

                setter.Wait(TimeSpan.FromSeconds(1));
            }
        }

        // keepWaiting reads the same volatile field; a coroutine yielding on the
        // instruction needs to terminate once a background completion fires.
        [Test, Timeout(5_000)]
        public void KeepWaiting_FlipsToFalse_AfterBackgroundCompletion()
        {
            var instruction = new ProbeInstruction();
            Assert.IsTrue(instruction.keepWaiting);

            var setter = Task.Run(() =>
            {
                Thread.Sleep(20); // let foreground reach the spin
                instruction.CompleteWith("done");
            });

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (instruction.keepWaiting)
            {
                if (DateTime.UtcNow > deadline)
                    Assert.Fail("keepWaiting still true 2s after background completion.");
                Thread.Yield();
            }

            setter.Wait(TimeSpan.FromSeconds(1));
            Assert.IsFalse(instruction.keepWaiting);
            Assert.IsTrue(instruction.IsDone);
        }

        // IsError uses the same volatile-backed pattern. Set both from the background
        // and verify both are visible after IsDone is observed.
        [Test, Timeout(5_000)]
        public void IsError_VisibleAcrossThreadsOnceIsDoneIsObserved()
        {
            for (int trial = 0; trial < 100; trial++)
            {
                var instruction = new ProbeInstruction();
                var ready = new ManualResetEventSlim(false);
                var setter = Task.Run(() =>
                {
                    ready.Wait();
                    instruction.IsError = true;
                    instruction.CompleteWith("err");
                });

                ready.Set();

                var deadline = DateTime.UtcNow.AddMilliseconds(500);
                while (!instruction.IsDone)
                {
                    if (DateTime.UtcNow > deadline)
                        Assert.Fail($"Trial {trial}: IsDone not observed.");
                }

                Assert.IsTrue(instruction.IsError,
                    $"Trial {trial}: IsError write before IsDone was not visible.");
                setter.Wait(TimeSpan.FromSeconds(1));
            }
        }
    }
}
