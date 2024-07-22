using Unity.Mathematics;


// Mesh with all rectangles with the same normal and in the same chunk (rectangles must be stored in a seperate container)
public struct VoxelMesh
{
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
    public VoxelMesh(CubeNormal normal, int chunkX, int chunkZ, int startY)
    {
        this.normal = normal;
        position = new int3(chunkX * WorldManager.chunkSize, startY, chunkZ * WorldManager.chunkSize);
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
    public Square Add(int x, int y, int depth, int width, int height, int colorID)
    {
        (int squareMinX, int squareMinY, int squareMinZ, int squareMaxX, int squareMaxY, int squareMaxZ) = ((uint)normal % 3) switch
        {
            0 => (depth, y, x, depth, y + height, x + width), // x
            1 => (x, y, depth, x + width, y + height, depth), // z
            2 => (x, depth, y, x + width, depth, y + height), // y
            _ => default
        };
        if (squareMinX < minX) minX = squareMinX;
        if (squareMaxX > maxX) maxX = squareMaxX;
        if (squareMinY < minY) minY = squareMinY;
        if (squareMaxY > maxY) maxY = squareMaxY;
        if (squareMinZ < minZ) minZ = squareMinZ;
        if (squareMaxZ > maxZ) maxZ = squareMaxZ;
        squaresCount++;
        return new Square((uint)(squareMinX + position.x), (uint)(squareMinY + position.y), (uint)(squareMinZ + position.z), (uint)width, (uint)height, (uint)normal, (uint)colorID);
    }


    public float3 Center => (position + new float3(minX + maxX, minY + maxY, minZ + maxZ) / 2) * WorldManager.blockSize;

    public float3 Size => new float3(maxX - minX, maxY - minY, maxZ - minZ) / 2 * WorldManager.blockSize;
}


public enum CubeNormal
{
    xPositive = 0,
    zPositive = 1,
    yPositive = 2,
    xNegative = 3,
    zNegative = 4,
    yNegative = 5
}


public struct Square
{
    private uint data1; // x (13b), sizeX (6b), z (13b)
    private uint data2; // sizeZ (6b), y (9b), sizeY (6b), normal (3b), color (8b)

    public Square(uint x, uint y, uint z, uint width, uint height, uint normal, uint colorID)
    {
        (uint sizeX, uint sizeY, uint sizeZ) = (normal % 3) switch
        {
            0 => (0U, height - 1, width - 1), // x
            1 => (width - 1, height - 1, 0U), // z
            2 => (width - 1, 0U, height - 1), // y
            _ => default
        };
        data1 = x | (sizeX << 13) | (z << 19);
        data2 = sizeZ | (y << 6) | (sizeY << 15) | (normal << 21) | (colorID << 24);
    }
}


public struct TerrainMeshData
{
    private float3 center;
    private float3 size;
    private uint data1; // normal (3b), squareCount (29b)
    private uint data2; // startSquare (32b)

    public TerrainMeshData(float3 center, float3 size, uint normal, uint squareCount, uint startSquare)
    {
        this.center = center;
        this.size = size;
        data1 = normal | (squareCount << 3);
        data2 = startSquare;
    }

    public TerrainMeshData(VoxelMesh mesh, uint startSquare) : this(mesh.Center, mesh.Size, (uint)mesh.normal, mesh.squaresCount, startSquare) { }
}