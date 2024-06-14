using System.Threading;
using System.Collections.Generic;

namespace LiveKit
{

    public class ThreadSafeQueue<T>
    {
        private readonly Queue<T> _queue = new Queue<T>();
        private readonly object _lockObject = new object();

        public void Enqueue(T item)
        {
            lock (_lockObject)
            {
                _queue.Enqueue(item);
            }
        }

        public T Dequeue()
        {
            lock (_lockObject)
            {
                return _queue.Dequeue();
            }
        }

        public int Count
        {
            get
            {
                lock (_lockObject)
                {
                    return _queue.Count;
                }
            }
        }
    }
}