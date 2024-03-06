using LiveKit.Internal;
using System.Threading;
using System.Threading.Tasks;

namespace LiveKit
{
    public class AsyncInstruction
    {
        public bool IsDone { protected set; get; }
        public bool IsError { protected set; get; }

        public bool keepWaiting => !IsDone;

        private CancellationToken _token;
        protected CancellationToken Token => _token;


        internal AsyncInstruction(CancellationToken token)
        {
            _token = token;
        }

        public async Task AwaitCompletion()
        {
            while (!IsDone)
            {
                _token.ThrowIfCancellationRequested();
                await Task.Delay(Constants.TASK_DELAY);
            }
        }

        public async Task<bool> AwaitWithSuccess()
        {
            await AwaitCompletion();
            return IsError == false;
        }
    }
}