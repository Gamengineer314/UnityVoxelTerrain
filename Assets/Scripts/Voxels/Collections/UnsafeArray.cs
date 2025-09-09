using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Voxels.Collections {

    /// <summary>
    /// Array of elements, similar to NativeArray, but without jobs checks
    /// </summary>
    /// <typeparam name="T">Type of elements to store in the array</typeparam>
    public readonly unsafe struct UnsafeArray<T> : IEnumerable<T>, IDisposable where T : unmanaged {
        public readonly int length;
        [NativeDisableUnsafePtrRestriction] public readonly void* ptr;
        private readonly Allocator allocator;


        public UnsafeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory) {
            this.length = length;
            this.allocator = allocator;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            ptr = UnsafeUtility.MallocTracked(length * sizeof(T), UnsafeUtility.AlignOf<T>(), allocator, 0);
#else
            ptr = UnsafeUtility.Malloc(length * sizeof(T), UnsafeUtility.AlignOf<T>(), allocator);
#endif
            if (options == NativeArrayOptions.ClearMemory) UnsafeUtility.MemClear(ptr, length * sizeof(T));
        }

        public UnsafeArray(NativeArray<T> array) {
            length = array.Length;
            ptr = array.GetUnsafePtr();
            allocator = Allocator.None;
        }


        public void Dispose() {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            UnsafeUtility.FreeTracked(ptr, allocator);
#else
            UnsafeUtility.Free(ptr, allocator);
#endif
        }


        public readonly ref T this[int index] {
            get {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (index < 0 || index >= length) throw new IndexOutOfRangeException($"Index {index} is out of range of UnsafeArray of size {length}");
#endif
                return ref ((T*)ptr)[index];
            }
        }


        public void Clear() {
            UnsafeUtility.MemClear(ptr, length * sizeof(T));
        }


        public Enumerator GetEnumerator() => new(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<T> {
            private readonly UnsafeArray<T> array;
            private int index;

            public Enumerator(UnsafeArray<T> array) {
                this.array = array;
                index = -1;
            }

            public readonly T Current => array[index];
            readonly object IEnumerator.Current => Current;
            public bool MoveNext() => ++index < array.length;
            public void Reset() => index = -1;
            public readonly void Dispose() { }
        }
    }
    
}