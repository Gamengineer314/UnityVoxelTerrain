using Unity.Mathematics;

namespace Voxels.Rendering {

    /// <summary>
    /// Face of a voxel (packed to be usable in GPU buffers)
    /// </summary>
    internal readonly struct VoxelFace {
        public readonly uint data1; // x (13b), z (13b)
        public readonly uint data2; // y (9b), width (6b), height (6b), normal (3b), color (8b)

        public VoxelFace(uint3 pos, uint width, uint height, VoxelNormal normal, uint color) {
            data1 = pos.x | (pos.z << 13);
            data2 = pos.y | ((width - 1) << 9) | ((height - 1) << 15) | ((uint)normal << 21) | (color << 24);
        }

        public uint X => data1 & 0b1111111111111;
        public uint Y => data2 & 0b111111111;
        public uint Z => (data1 >> 13) & 0b1111111111111;
        public uint Width => ((data2 >> 9) & 0b111111) + 1;
        public uint Height => ((data2 >> 15) & 0b111111) + 1;
        public VoxelNormal Normal => (VoxelNormal)((data2 >> 21) & 0b111);
        public uint Color => data2 >> 24;
    }


    /// <summary>
    /// Voxel mesh (packed to be usable in GPU buffers).
    /// Faces must be stored in a separate container.
    /// </summary>
    internal readonly struct VoxelMesh {
        public readonly float3 center;
        public readonly uint data1; // normal (3b), faceCount (29b)
        public readonly float3 size;
        public readonly uint data2; // startFace (32b)

        public VoxelMesh(float3 center, float3 size, VoxelNormal normal, uint faceCount, uint startFace) {
            this.center = center;
            this.size = size;
            data1 = (uint)normal | (faceCount << 3);
            data2 = startFace;
        }

        public uint FaceCount => data1 >> 3;
        public uint StartFace => data2;
        public uint Normal => data1 & 0b111;
    }


    /// <summary>
    /// Normals for a cube
    /// </summary>
    public enum VoxelNormal {
        XPositive = 0,
        XNegative = 1,
        YPositive = 2,
        YNegative = 3,
        ZPositive = 4,
        ZNegative = 5,
        Any = 6,
        None = 7
    }


    /// <summary>
    /// Normals helper functions
    /// </summary>
    public static class VoxelNormals {
        /// <summary>
        /// Axis of a normal
        /// </summary>
        public static int Axis(VoxelNormal normal) => (int)((uint)normal >> 1);

        /// <summary>
        /// Whether a normal is positive or negative
        /// <returns></returns>
        public static bool Positive(VoxelNormal normal) => ((int)normal & 1) == 0;

        // x: 1, y: 0, z: 1
        internal static int WidthAxis(VoxelNormal normal) => WidthAxis(Axis(normal));
        internal static int WidthAxis(int axis) => 1 & ~axis;

        // x: 2, y: 2, z: 0
        internal static int HeightAxis(VoxelNormal normal) => HeightAxis(Axis(normal));
        internal static int HeightAxis(int axis) => 2 & ~axis;
    }
    
}