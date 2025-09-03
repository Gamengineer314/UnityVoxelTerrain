using Unity.Mathematics;


// Mesh with all rectangles with the same normal and in the same chunk (rectangles must be stored in a seperate container)
public struct VoxelMesh {
    public readonly CubeNormal normal;
    public readonly int3 position;
    public uint squaresCount;
    private int minX;
    private int minY;
    private int minZ;
    private int maxX;
    private int maxY;
    private int maxZ;


    /// <summary>
    /// Create a new VoxelMesh
    /// </summary>
    /// <param name="normal">Normal of all rectangles</param>
    /// <param name="chunkX">x index of the chunk the mesh is in</param>
    /// <param name="chunkZ">z index of the chunk the mesh is in</param>
    /// <param name="startY">Start y index of the chunk the mesh is in</param>
    public VoxelMesh(CubeNormal normal, int chunkX, int chunkZ, int startY) {
        this.normal = normal;
        position = new int3(chunkX * WorldManager.chunkSize, startY, chunkZ * WorldManager.chunkSize);
        if (CubeNormals.Positive(normal)) position[CubeNormals.Axis(normal)]++;
        squaresCount = 0;
        minX = WorldManager.chunkSize;
        minY = WorldManager.chunkSize;
        minZ = WorldManager.chunkSize;
        maxX = 0;
        maxY = 0;
        maxZ = 0;
    }


    /// <summary>
    /// Add a rectangle to the mesh
    /// </summary>
    /// <param name="x">x start index of the rectangle in the plane</param>
    /// <param name="y">y start index of the rectangle in the plane</param>
    /// <param name="depth">Index of the plane</param>
    /// <param name="width">Width of the rectangle</param>
    /// <param name="height">Height of the rectangle</param>
    /// <param name="colorID">Color ID of the rectangle</param>
    /// <returns></returns>
    public Square Add(int x, int y, int depth, int width, int height, int colorID) {
        int3 min = 0;
        min[CubeNormals.WidthAxis(normal)] += x;
        min[CubeNormals.HeightAxis(normal)] += y;
        min[CubeNormals.Axis(normal)] += depth;
        int3 max = min;
        max[CubeNormals.WidthAxis(normal)] += width;
        max[CubeNormals.HeightAxis(normal)] += height;
        if (min.x < minX) minX = min.x;
        if (max.x > maxX) maxX = max.x;
        if (min.y < minY) minY = min.y;
        if (max.y > maxY) maxY = max.y;
        if (min.z < minZ) minZ = min.z;
        if (max.z > maxZ) maxZ = max.z;
        squaresCount++;
        return new Square((uint)(min.x + position.x), (uint)(min.y + position.y), (uint)(min.z + position.z), (uint)width, (uint)height, (uint)normal, (uint)colorID);
    }


    public readonly float3 Center => (position + new float3(minX + maxX, minY + maxY, minZ + maxZ) / 2) * WorldManager.blockSize;

    public readonly float3 Size => new float3(maxX - minX, maxY - minY, maxZ - minZ) / 2 * WorldManager.blockSize;
}


public enum CubeNormal {
    xPositive = 0,
    xNegative = 1,
    yPositive = 2,
    yNegative = 3,
    zPositive = 4,
    zNegative = 5
}

// Normals helper methods
public static class CubeNormals {
    // x: 0, y: 1, z: 2
    public static int Axis(CubeNormal normal) => (int)((uint)normal >> 1);

    // x: 1, y: 0, z: 1
    public static int WidthAxis(CubeNormal normal) => WidthAxis(Axis(normal));
    public static int WidthAxis(int axis) => 1 & ~axis;

    // x: 2, y: 2, z: 0
    public static int HeightAxis(CubeNormal normal) => HeightAxis(Axis(normal));
    public static int HeightAxis(int axis) => 2 & ~axis;

    public static bool Positive(CubeNormal normal) => ((int)normal & 1) == 0;
}


public readonly struct Square {
    public readonly uint data1; // x (13b), z (13b)
    public readonly uint data2; // y (9b), width (6b), height (6b), normal (3b), color (8b)

    public Square(uint x, uint y, uint z, uint w, uint h, uint normal, uint color) {
        data1 = x | (z << 13);
        data2 = y | ((w - 1) << 9) | ((h - 1) << 15) | (normal << 21) | (color << 24);
    }

    public uint X => data1 & 0b1111111111111;
    public uint Y => data2 & 0b111111111;
    public uint Z => (data1 >> 13) & 0b1111111111111;
    public uint Width => ((data2 >> 9) & 0b111111) + 1;
    public uint Height => ((data2 >> 15) & 0b111111) + 1;
    public uint Normal => (data2 >> 21) & 0b111;
    public uint Color => (data2 >> 24) & 0b11111111;
}


public readonly struct TerrainMeshData {
    public readonly float3 center;
    public readonly uint data1; // normal (3b), squareCount (29b)
    public readonly float3 size;
    public readonly uint data2; // startSquare (32b)

    public TerrainMeshData(float3 center, float3 size, uint normal, uint squareCount, uint startSquare) {
        this.center = center;
        this.size = size;
        data1 = normal | (squareCount << 3);
        data2 = startSquare;
    }

    public TerrainMeshData(VoxelMesh mesh, uint startSquare) : this(mesh.Center, mesh.Size, (uint)mesh.normal, mesh.squaresCount, startSquare) { }

    public uint SquareCount => data1 >> 3;
    public uint StartSquare => data2;
    public uint Normal => data1 & 0b111;
}