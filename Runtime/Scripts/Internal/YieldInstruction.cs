using UnityEngine;

namespace LiveKit
{
    public class YieldInstruction : CustomYieldInstruction
    {
        public bool IsDone { protected set; get; }
        public bool IsError { protected set; get; }

        public override bool keepWaiting => !IsDone;
    }
}
