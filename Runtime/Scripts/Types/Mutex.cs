namespace Livekit.Types
{
    public class Mutex<T>
    {
        private readonly object lockObject = new();
        private T value;

        public Mutex(T value)
        {
            this.value = value;
        }

        public Guard Lock()
        {
            System.Threading.Monitor.Enter(lockObject);
            return new Guard(this);
        }

        public readonly ref struct Guard 
        {
            private readonly Mutex<T> mutex;

            internal Guard(Mutex<T> mutex)
            {
                this.mutex = mutex;
            }

            public ref T Value => ref mutex.value;

            public void Dispose()
            {
                System.Threading.Monitor.Exit(mutex.lockObject);
            }
        }
    }
}