using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Voxels.Collections {

    /// <summary>
    /// 2D array stored in a 1D NativeArray
    /// </summary>
    /// <typeparam name="T">Type of the elements in the array</typeparam>
    public struct Native2DArray<T> where T : struct {
        private NativeArray<T> array;
        public readonly int sizeX, sizeY; // Size in the x and y dimensions

        public Native2DArray(int sizeX, int sizeY, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory) {
            array = new(sizeX * sizeY, allocator, options);
            this.sizeX = sizeX;
            this.sizeY = sizeY;
        }

        public void Dispose() => array.Dispose();

        public T this[int x, int y] {
            get {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (x < 0 || x >= sizeX) throw new IndexOutOfRangeException($"X coordinate {x} is out of range of Native2DArray of sizeX {sizeX}");
                if (y < 0 || y >= sizeY) throw new IndexOutOfRangeException($"Y coordinate {y} is out of range of Native2DArray of sizeY {sizeY}");
#endif
                return array[x + sizeX * y];
            }
            set {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (x < 0 || x >= sizeX) throw new IndexOutOfRangeException($"X coordinate {x} is out of range of Native2DArray of sizeX {sizeX}");
                if (y < 0 || y >= sizeY) throw new IndexOutOfRangeException($"Y coordinate {y} is out of range of Native2DArray of sizeY {sizeY}");
#endif
                array[x + sizeX * y] = value;
            }
        }

        public T this[int2 coords] {
            get => this[coords.x, coords.y];
            set => this[coords.x, coords.y] = value;
        }

        public readonly NativeArray<T> Array => array;
    }

}