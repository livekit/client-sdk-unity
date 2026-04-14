using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace LiveKit.EditModeTests
{
    public class ThreadSafeQueueTests
    {
        [Test]
        public void Enqueue_IncreasesCount()
        {
            var queue = new ThreadSafeQueue<int>();
            Assert.AreEqual(0, queue.Count);

            queue.Enqueue(1);
            Assert.AreEqual(1, queue.Count);

            queue.Enqueue(2);
            Assert.AreEqual(2, queue.Count);
        }

        [Test]
        public void Dequeue_ReturnsEnqueuedItem_FIFO()
        {
            var queue = new ThreadSafeQueue<string>();
            queue.Enqueue("first");
            queue.Enqueue("second");
            queue.Enqueue("third");

            Assert.AreEqual("first", queue.Dequeue());
            Assert.AreEqual("second", queue.Dequeue());
            Assert.AreEqual("third", queue.Dequeue());
        }

        [Test]
        public void Dequeue_WhenEmpty_ThrowsInvalidOperationException()
        {
            var queue = new ThreadSafeQueue<int>();
            Assert.Throws<InvalidOperationException>(() => queue.Dequeue());
        }

        [Test]
        public void Clear_SetsCountToZero()
        {
            var queue = new ThreadSafeQueue<int>();
            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);
            Assert.AreEqual(3, queue.Count);

            queue.Clear();
            Assert.AreEqual(0, queue.Count);
        }

        [Test]
        public void ConcurrentEnqueueDequeue_NoExceptions()
        {
            var queue = new ThreadSafeQueue<int>();
            const int itemsPerThread = 1000;
            const int threadCount = 4;
            var dequeued = new List<int>();
            var dequeueLock = new object();

            var producerTasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                producerTasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < itemsPerThread; i++)
                        queue.Enqueue(threadId * itemsPerThread + i);
                });
            }
            Task.WaitAll(producerTasks);

            Assert.AreEqual(threadCount * itemsPerThread, queue.Count);

            var consumerTasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                consumerTasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < itemsPerThread; i++)
                    {
                        var item = queue.Dequeue();
                        lock (dequeueLock) dequeued.Add(item);
                    }
                });
            }
            Task.WaitAll(consumerTasks);

            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual(threadCount * itemsPerThread, dequeued.Count);
        }
    }
}
