using LiveKit.Internal;
using System.Threading;
using System.Threading.Tasks;

namespace LiveKit
{
    public class AsyncInstruction
    {
        public bool IsDone { protected set; get; }
        public bool IsError => string.IsNullOrWhiteSpace(ErrorMessage) == false;
        public string? ErrorMessage { protected set; get; }

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

        public async Task<(bool success, string? errorMessage)> AwaitWithSuccess()
        {
            await AwaitCompletion();
            return (IsError == false, ErrorMessage);
        }
    }
}