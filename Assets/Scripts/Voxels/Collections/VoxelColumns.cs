using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

namespace Voxels.Collections {

    /// <summary>
    /// Array of voxels that contain generic data.
    /// The voxels are organized as sizeX * sizeZ columns of (y, data) pairs.
    /// </summary>
    /// <typeparam name="T">Voxel data type</typeparam>
    [BurstCompile]
    public readonly struct VoxelColumns<T> where T : unmanaged {
        public readonly int sizeX, sizeZ; // Size in the x and z dimensions
        private readonly NativeArray<Voxel<T>> voxels; // All columns
        private readonly NativeArray<int> startIndices; // [sizeX * sizeZ + 1] sized array giving the start index of each column

        public VoxelColumns(int sizeX, int sizeZ, NativeArray<Voxel<T>> voxels, NativeArray<int> startIndices) {
            this.sizeX = sizeX;
            this.sizeZ = sizeZ;
            this.voxels = voxels;
            this.startIndices = startIndices;
        }

        public void Dispose() {
            voxels.Dispose();
            startIndices.Dispose();
        }


        /// <summary>
        /// Get the data of a voxel
        /// </summary>
        /// <param name="x">x coordinate of the voxel</param>
        /// <param name="y">y coordinate of the voxel</param>
        /// <param name="z">z coordinate of the voxel</param>
        /// <returns>Data of the voxel if found, default otherwise</returns>
        public T GetVoxel(int x, int y, int z) {
            for (int i = startIndices[x + sizeX * z]; i < startIndices[x + sizeX * z + 1]; i++) {
                if (voxels[i].y == y) return voxels[i].data;
            }
            return default;
        }

        public T GetVoxel(int3 coords) => GetVoxel(coords.x, coords.y, coords.z);


        /// <summary>
        /// Get a column of voxels
        /// </summary>
        /// <param name="x">x coordinate of the column</param>
        /// <param name="z">z coordinate of the column</param>
        /// <returns>Enumerable of voxels</returns>
        public NativeArray<Voxel<T>> GetColumn(int x, int z) {
            int start = startIndices[x + sizeX * z];
            int length = startIndices[x + sizeX * z + 1] - start;
            return voxels.GetSubArray(start, length);
        }

        public NativeArray<Voxel<T>> GetColumn(int2 coords) => GetColumn(coords.x, coords.y);


        /// <summary>
        /// Get the lowest voxel in a column
        /// </summary>
        /// <param name="x">x coordinate of the column</param>
        /// <param name="z">z coordinate of the column</param>
        /// <returns>y coordinate of the voxel, int.MaxValue if no voxels in this column</returns>
        public int GetMin(int x, int z) {
            if (startIndices[x + sizeX * z] == startIndices[x + sizeX * z + 1]) return int.MaxValue;
            return voxels[startIndices[x + sizeX * z]].y;
        }

        public int GetMin(int2 coords) => GetMin(coords.x, coords.y);


        /// <summary>
        /// Get the highest voxel in a column
        /// </summary>
        /// <param name="x">x coordinate of the column</param>
        /// <param name="z">z coordinate of the column</param>
        /// <returns>y coordinate of the voxel, int.MinValue if no voxels in this column</returns>
        public int GetMax(int x, int z) {
            if (startIndices[x + sizeX * z] == startIndices[x + sizeX * z + 1]) return int.MinValue;
            return voxels[startIndices[x + sizeX * z + 1] - 1].y;
        }

        public int GetMax(int2 coords) => GetMax(coords.x, coords.y);
    }



    [BurstCompile]
    public static class VoxelColumns {
        /// <summary>
        /// Create an array of voxels from a height map
        /// </summary>
        /// <param name="voxels">Highest voxel in each column</param>
        /// <returns>The voxels</returns>
        public static VoxelColumns<T> FromHeightMap<T>(Native2DArray<Voxel<T>> voxels) where T : unmanaged {
            FromHeightMap(in voxels, out VoxelColumns<T> result);
            return result;
        }

        [BurstCompile]
        private static void FromHeightMap<T>(in Native2DArray<Voxel<T>> voxels, out VoxelColumns<T> result) where T : unmanaged {
            NativeList<Voxel<T>> allVoxels = new(Allocator.Persistent);
            NativeArray<int> startIndices = new(voxels.sizeX * voxels.sizeY + 1, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            for (int z = 0; z < voxels.sizeY; z++) {
                for (int x = 0; x < voxels.sizeX; x++) {
                    // Find lowest highest voxel in neighbor columns
                    int maxY = voxels[x, z].y;
                    int minNeighbor = maxY - 1;
                    if (x > 0) minNeighbor = math.min(minNeighbor, voxels[x - 1, z].y);
                    if (x < voxels.sizeX - 1) minNeighbor = math.min(minNeighbor, voxels[x + 1, z].y);
                    if (z > 0) minNeighbor = math.min(minNeighbor, voxels[x, z - 1].y);
                    if (z < voxels.sizeY - 1) minNeighbor = math.min(minNeighbor, voxels[x, z + 1].y);

                    // Add voxels
                    startIndices[x + voxels.sizeX * z] = allVoxels.Length;
                    for (int y = minNeighbor + 1; y <= maxY; y++) {
                        allVoxels.Add(new(y, voxels[x, z].data));
                    }
                }
            }
            startIndices[voxels.sizeX * voxels.sizeY] = allVoxels.Length;

            result = new(voxels.sizeX, voxels.sizeY, allVoxels.ToArray(Allocator.Persistent), startIndices);
            allVoxels.Dispose();
        }
    }



    /// <summary>
    /// (y, data) pair in a VoxelColumns struct
    /// </summary>
    /// <typeparam name="T">Data type</typeparam>
    public readonly struct Voxel<T> where T : unmanaged {
        public readonly int y;
        public readonly T data;

        public Voxel(int y, T data) {
            this.y = y;
            this.data = data;
        }
    }
    
}