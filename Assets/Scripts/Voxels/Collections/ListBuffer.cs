using System.Collections.Generic;
using UnityEngine;

namespace Voxels.Collections {

    /// <summary>
    /// GraphicsBuffer that allows adding items.
    /// Capacity is doubled when needed.
    /// <typeparam name="T">Type of the elements in the buffer</typeparam>
    /// </summary>
    public unsafe class ListBuffer<T> where T : unmanaged {
        public GraphicsBuffer buffer { get; private set; }
        private int length;
        private int capacity;
        private readonly GraphicsBuffer.Target target;

        /// <summary>
        /// Create a new ListBuffer with initial capacity
        /// </summary>
        /// <param name="target"></param>
        /// <param name="initialCapacity"></param>
        public ListBuffer(GraphicsBuffer.Target target, int initialCapacity) {
            buffer = new(target, initialCapacity, sizeof(T));
            length = 0;
            capacity = initialCapacity;
            this.target = target;
        }


        public int Length {
            get => length;
            set {
                length = value;
                if (length > capacity) {
                    capacity <<= 1;
                    Resize();
                }
            }
        }

        private void Resize() {
            GraphicsBuffer newBuffer = new(target, capacity, sizeof(T));
            T[] data = new T[length];
            buffer.GetData(data);
            newBuffer.SetData(data);
            buffer = newBuffer;
        }

        public void Add(T element) {
            Length++;
            buffer.SetData(new T[] { element }, 0, length - 1, 1);
        }
    }

}