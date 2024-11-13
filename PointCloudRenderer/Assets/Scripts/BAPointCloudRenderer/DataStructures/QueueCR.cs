using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BAPointCloudRenderer.DataStructures
{
    /// <summary>
    /// A thredsafe queue
    /// </summary>
    /// <typeparam name="T">Value-Type</typeparam>
    class QueueCR<T> : IEnumerable<T>
    {
        private Queue<T> queue;

        public QueueCR()
        {
            queue = new Queue<T>();
        }

        public void Enqueue(T element)
        {
            queue.Enqueue(element);
        }

        public T Dequeue()
        {
             return queue.Dequeue();
        }

        public bool TryDequeue(out T result) {
            if (queue.Count == 0) {
                result = default(T);
                return false;
            } else {
                result = queue.Dequeue();
                return true;
            }
        }

        public bool IsEmpty()
        {
            return queue.Count == 0;
        }

        public void Clear() {

            queue.Clear();

        }

        public IEnumerator<T> GetEnumerator()
        {
            return new Queue<T>(queue).GetEnumerator();

        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count {
            get {
                return queue.Count;
            }
        }
    }
}
