#if LIVEKIT_UNITASK
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace LiveKit.PlayModeTests.UniTaskBridge
{
    public class RoomUniTaskTests
    {
        // Synthetic instruction used by the unit tests below — they verify the
        // AsUniTask extension's behavior directly against the public setter contract
        // (IsError then IsDone, mirroring the production completion order in
        // Room.cs / Participant.cs / Track.cs) without needing the FFI.
        private sealed class TestInstruction : YieldInstruction
        {
            public void Complete() => IsDone = true;
            public void CompleteWithError() { IsError = true; IsDone = true; }
        }

        // AsUniTask completes (without throwing) when IsDone transitions to true on success,
        // mirroring a direct await of the instruction.
        [UnityTest]
        public System.Collections.IEnumerator AsUniTask_CompletesOnSuccess() => UniTask.ToCoroutine(async () =>
        {
            var instruction = new TestInstruction();
            var task = instruction.AsUniTask();
            Assert.IsFalse(instruction.IsDone, "Sanity: instruction must not be done before Complete()");

            instruction.Complete();
            await task;

            Assert.IsTrue(instruction.IsDone, "UniTask should not resume before IsDone");
            Assert.IsFalse(instruction.IsError);
        });

        // On failure, AsUniTask faults with the instruction's error (here the base
        // LiveKitException) — parity with a direct await, whose GetResult throws.
        [UnityTest]
        public System.Collections.IEnumerator AsUniTask_ThrowsOnError() => UniTask.ToCoroutine(async () =>
        {
            var instruction = new TestInstruction();
            var task = instruction.AsUniTask();

            instruction.CompleteWithError();

            LiveKitException caught = null;
            try { await task; }
            catch (LiveKitException e) { caught = e; }

            Assert.IsNotNull(caught, "AsUniTask must fault when the instruction completes with an error");
        });

        // Cancellation has abandon-awaiter semantics: the UniTask faults with
        // OperationCanceledException, but the underlying request is not aborted.
        // The synthetic instruction is never completed — only the token fires.
        [UnityTest]
        public System.Collections.IEnumerator AsUniTask_Cancellation_ThrowsOperationCanceled() => UniTask.ToCoroutine(async () =>
        {
            var instruction = new TestInstruction();
            using var cts = new CancellationTokenSource();

            var task = instruction.AsUniTask(cts.Token);
            cts.Cancel();

            bool threw = false;
            try
            {
                await task;
            }
            catch (System.OperationCanceledException)
            {
                threw = true;
            }

            Assert.IsTrue(threw, "Expected OperationCanceledException when token was cancelled");
            Assert.IsFalse(instruction.IsDone, "Abandon-awaiter semantics: underlying instruction is untouched");
        });

        // End-to-end coverage of the FFI path is handled by the migrated Meet sample
        // (Samples~/Meet/Assets/Runtime/MeetManager.cs). An additional E2E test here
        // was tried and removed: FFI error logs arrive asynchronously and their delivery
        // window races UniTask's synchronous resume, so the LogAssert tracking was
        // brittle across test order. The unit tests above cover the extension's logic.
    }
}
#endif
