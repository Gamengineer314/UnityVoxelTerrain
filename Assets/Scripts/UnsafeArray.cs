using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

/// <summary>
/// Array of elements, similar to NativeArray, but without jobs checks
/// </summary>
/// <typeparam name="T">Type of elements to store in the array</typeparam>
public unsafe struct UnsafeArray<T> : IDisposable where T : unmanaged
{
    public readonly int length;
    [NativeDisableUnsafePtrRestriction] public readonly void* ptr;
    private readonly Allocator allocator;


    public UnsafeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
    {
        this.length = length;
        this.allocator = allocator;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        ptr = UnsafeUtility.MallocTracked(length * sizeof(T), UnsafeUtility.AlignOf<T>(), allocator, 0);
#else
        ptr = UnsafeUtility.Malloc(length * sizeof(T), UnsafeUtility.AlignOf<T>(), allocator);
#endif
        if (options == NativeArrayOptions.ClearMemory) UnsafeUtility.MemClear(ptr, length * sizeof(T));
    }


    public void Dispose()
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        UnsafeUtility.FreeTracked(ptr, allocator);
#else
        UnsafeUtility.Free(ptr, allocator);
#endif
    }


    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (index < 0 || index >= length) throw new IndexOutOfRangeException($"Index {index} is out of range of UnsafeArray of size {length}");
#endif
            return ref ((T*)ptr)[index];
        }
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        UnsafeUtility.MemClear(ptr, length * sizeof(T));
    }
}
