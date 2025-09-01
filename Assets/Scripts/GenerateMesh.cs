using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;


// Fast, multithreadable greedy mesher.
// Optimizations inspired by https://youtu.be/qnGoGq7DWMc?si=8qQ08B40-qbJCfp0
//[BurstCompile]
public struct GenerateMesh : IJob
{
    [ReadOnly] private int chunkStartX;
    [ReadOnly] private int chunkStartZ;
    [ReadOnly] private int chunkEndX;
    [ReadOnly] private int chunkEndZ;
    [ReadOnly] private int xChunks;
    [ReadOnly] private int maxChunkX;
    [ReadOnly] private int maxChunkZ;
    
    // IDs contains all block rows one after the other.
    // A row contains all blocks for an (x, z) coordinate in ascending order.
    // Even indices : y coordinates, odd indices : color ids.
    // Index of the start of each row in IDs : IDIndexes.
    // Index of a row in IDIndexes : (chunkX + chunkZ * horizontalChunks) + xInChunk + zInChunk * chunkSize
    // ID 0 : invisible block used to not render faces arround it.
    [ReadOnly] private NativeList<int> IDs;
    [ReadOnly] private NativeArray<int> IDIndexes;

    [WriteOnly] public NativeList<VoxelMesh> meshes;
    [WriteOnly] public NativeList<Square> squares;

    private int chunkX;
    private int chunkZ;
    private int startY;
    private int startXZIndex;
    private UnsafeArray<ulong> rows;
    private UnsafeArray<bool2> sides;
    private UnsafeArray<ulong> planes;
    private UnsafeArray<int> idToIndex;
    private UnsafeArray<int> indexToId;


    /// <summary>
    /// Generate an optimized mesh (greedy meshing) for the terrain from block IDs.
    /// The mesh will be split in chunks and different face orientations.
    /// </summary>
    /// <param name="chunkStartX">x start (in chunks) of the part of IDs to render</param>
    /// <param name="chunkStartZ">z start (in chunks) of the part of IDs to render</param>
    /// <param name="chunkSizeX">x size (in chunks) of the part of IDs to render</param>
    /// <param name="chunkSizeZ">z size (in chunks) of the part of IDs to render</param>
    /// <param name="IDs">Block IDs</param>
    /// <param name="IDIndexes">Start index for each (x, z) in IDs</param>
    public GenerateMesh(int chunkStartX, int chunkStartZ, int chunkSizeX, int chunkSizeZ, NativeList<int> IDs, NativeArray<int> IDIndexes)
    {
        this.chunkStartX = chunkStartX;
        this.chunkStartZ = chunkStartZ;
        chunkEndX = chunkStartX + chunkSizeX - 1;
        chunkEndZ = chunkStartZ + chunkSizeZ - 1;
        this.IDs = IDs;
        this.IDIndexes = IDIndexes;
        xChunks = WorldManager.horizontalChunks;
        maxChunkX = WorldManager.horizontalChunks - 1;
        maxChunkZ = WorldManager.horizontalChunks - 1;
        meshes = new NativeList<VoxelMesh>(Allocator.Persistent);
        squares = new NativeList<Square>(Allocator.Persistent);

        chunkX = default;
        chunkZ = default;
        startY = default;
        startXZIndex = default;
        rows = default;
        sides = default;
        planes = default;
        idToIndex = default;
        indexToId = default;
    }


    public unsafe void Execute()
    {
        // Find IDs in area and y range for each (x, z) chunk
        NativeArray<bool> containedIDs = new(256, Allocator.Temp);
        int idCount = 0;
        NativeArray<int> minY = new((chunkEndX - chunkStartX + 1) * (chunkEndZ - chunkStartZ + 1), Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        NativeArray<int> maxY = new((chunkEndX - chunkStartX + 1) * (chunkEndZ - chunkStartZ + 1), Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        for (int chunkZ = chunkStartZ; chunkZ <= chunkEndZ; chunkZ++)
        {
            for (int chunkX = chunkStartX; chunkX <= chunkEndX; chunkX++)
            {
                int chunkMinY = WorldManager.verticalSize;
                int chunkMaxY = 0;
                for (int i = IDIndexes[(chunkX + chunkZ * xChunks) * WorldManager.chunkSize * WorldManager.chunkSize]; i < IDIndexes[(chunkX + chunkZ * xChunks + 1) * WorldManager.chunkSize * WorldManager.chunkSize]; i += 2)
                {
                    if (IDs[i] < chunkMinY) chunkMinY = IDs[i];
                    if (IDs[i] > chunkMaxY) chunkMaxY = IDs[i];
                }
                minY[chunkX - chunkStartX + (chunkEndZ - chunkStartZ + 1) * (chunkZ - chunkStartZ)] = chunkMinY;
                maxY[chunkX - chunkStartX + (chunkEndZ - chunkStartZ + 1) * (chunkZ - chunkStartZ)] = chunkMaxY;

                for (int i = 1 + IDIndexes[(chunkX + chunkZ * xChunks) * WorldManager.chunkSize * WorldManager.chunkSize]; i < IDIndexes[(chunkX + chunkZ * xChunks + 1) * WorldManager.chunkSize * WorldManager.chunkSize]; i += 2)
                {
                    if (IDs[i] != 0 && !containedIDs[IDs[i]])
                    {
                        containedIDs[IDs[i]] = true;
                        idCount++;
                    }
                }
            }
        }

        // Map contained IDs to smallest range possible
        idToIndex = new UnsafeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        indexToId = new UnsafeArray<int>(idCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        int index = 0;
        for (int id = 0; id < 256; id++)
        {
            if (containedIDs[id])
            {
                idToIndex[id] = index;
                indexToId[index] = id;
                index++;
            }
        }
        containedIDs.Dispose();

        // Generate all chunks
        rows = new UnsafeArray<ulong>(WorldManager.chunkSize * WorldManager.chunkSize * 3, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        sides = new UnsafeArray<bool2>(WorldManager.chunkSize * WorldManager.chunkSize * 3, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        planes = new UnsafeArray<ulong>(WorldManager.chunkSize * WorldManager.chunkSize * idCount * 6, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
        for (chunkZ = chunkStartZ; chunkZ <= chunkEndZ; chunkZ++)
        {
            for (chunkX = chunkStartX; chunkX <= chunkEndX; chunkX++)
            {
                startXZIndex = (chunkX + chunkZ * xChunks) * WorldManager.chunkSize * WorldManager.chunkSize;
                int xzStartY = minY[chunkX - chunkStartX + (chunkEndZ - chunkStartZ + 1) * (chunkZ - chunkStartZ)];
                int sizeY = maxY[chunkX - chunkStartX + (chunkEndZ - chunkStartZ + 1) * (chunkZ - chunkStartZ)] - xzStartY + 1;
                for (int chunkY = 0; chunkY < Mathf.CeilToInt((float)sizeY / WorldManager.chunkSize); chunkY++)
                {
                    // Generate one chunk
                    rows.Clear();
                    sides.Clear();
                    planes.Clear();
                    startY = xzStartY + chunkY * WorldManager.chunkSize;
                    GenerateBinarySolidBlocks();
                    GenerateBinaryPlanes();
                    GenerateOptimizedMesh();
                }
            }
        }
        minY.Dispose();
        maxY.Dispose();
        idToIndex.Dispose();
        indexToId.Dispose();
        rows.Dispose();
        sides.Dispose();
        planes.Dispose();
    }


    // rows: bit rows containing 1 if the block is solid, 0 otherwise
    // sides: 2 bools, first is true if the block before the row is solid, 0 otherwise, second is true if the block after the row is solid, 0 otherwise
    // rows and sides contain chunkSize * chunkSize elements for each axis (x, z, y)
    private void GenerateBinarySolidBlocks() {
        // Rows and y sides
        int xzIndex = startXZIndex;
        for (int z = 0; z < WorldManager.chunkSize; z++) // Iter chunk z
        {
            for (int x = 0; x < WorldManager.chunkSize; x++) // Iter chunk x
            {
                bool2 ySide = new bool2(false, false);
                for (int i = IDIndexes[xzIndex]; i < IDIndexes[xzIndex + 1]; i += 2) // Iter world y (only solid blocks in chunk)
                {
                    int y = IDs[i] - startY;
                    if (y >= 0 && y < WorldManager.chunkSize) {
                        rows[y + z * WorldManager.chunkSize] |= 1UL << x; // x
                        rows[x + z * WorldManager.chunkSize + WorldManager.chunkSize * WorldManager.chunkSize] |= 1UL << y; // z
                        rows[y + x * WorldManager.chunkSize + 2 * WorldManager.chunkSize * WorldManager.chunkSize] |= 1UL << z; // y
                    }
                    else if (y == -1) ySide.x = true;
                    else if (y == WorldManager.chunkSize) ySide.y = true;
                }
                sides[x + z * WorldManager.chunkSize + WorldManager.chunkSize * WorldManager.chunkSize] = ySide;
                xzIndex++;
            }
        }

        // x and z sides
        if (chunkX > 0) {
            int otherStartXZIndex = (chunkX - 1 + chunkZ * xChunks) * WorldManager.chunkSize * WorldManager.chunkSize;
            for (int z = 0; z < WorldManager.chunkSize; z++)
                GenerateXZSides(otherStartXZIndex + WorldManager.chunkSize - 1 + z * WorldManager.chunkSize, false, z * WorldManager.chunkSize);
        }
        else {
            for (int z = 0; z < WorldManager.chunkSize; z++)
                GenerateXZSides(false, z * WorldManager.chunkSize);
        }
        if (chunkX < maxChunkX) {
            int otherStartXZIndex = (chunkX + 1 + chunkZ * xChunks) * WorldManager.chunkSize * WorldManager.chunkSize;
            for (int z = 0; z < WorldManager.chunkSize; z++)
                GenerateXZSides(otherStartXZIndex + z * WorldManager.chunkSize, true, z * WorldManager.chunkSize);
        }
        else {
            for (int z = 0; z < WorldManager.chunkSize; z++)
                GenerateXZSides(true, z * WorldManager.chunkSize);
        }
        if (chunkZ > 0) {
            int otherStartXZIndex = (chunkX + (chunkZ - 1) * xChunks) * WorldManager.chunkSize * WorldManager.chunkSize;
            for (int x = 0; x < WorldManager.chunkSize; x++)
                GenerateXZSides(otherStartXZIndex + x + (WorldManager.chunkSize - 1) * WorldManager.chunkSize, false, x * WorldManager.chunkSize + 2 * WorldManager.chunkSize * WorldManager.chunkSize);
        }
        else {
            for (int x = 0; x < WorldManager.chunkSize; x++)
                GenerateXZSides(false, x * WorldManager.chunkSize + 2 * WorldManager.chunkSize * WorldManager.chunkSize);
        }
        if (chunkZ < maxChunkZ) {
            int otherStartXZIndex = (chunkX + (chunkZ + 1) * xChunks) * WorldManager.chunkSize * WorldManager.chunkSize;
            for (int x = 0; x < WorldManager.chunkSize; x++)
                GenerateXZSides(otherStartXZIndex + x, true, x * WorldManager.chunkSize + 2 * WorldManager.chunkSize * WorldManager.chunkSize);
        }
        else {
            for (int x = 0; x < WorldManager.chunkSize; x++)
                GenerateXZSides(true, x * WorldManager.chunkSize + 2 * WorldManager.chunkSize * WorldManager.chunkSize);
        }
    }


    // Generate side row at (x, z)
    private void GenerateXZSides(int xzIndex, bool after, int startIndex)
    {
        int i = IDIndexes[xzIndex];
        for (int y = 0; y < WorldManager.chunkSize; y++)
        {
            if (y == IDs[i] - startY)
            {
                sides[startIndex + y] = new bool2(!after || sides[startIndex + y].x, after || sides[startIndex + y].y);
                i += 2;
                if (i >= IDIndexes[xzIndex + 1]) return;
            }
        }
    }
    
    // Generate filled side row at (x, z)
    private void GenerateXZSides(bool after, int startIndex) {
        for (int y = 0; y < WorldManager.chunkSize; y++) {
            sides[startIndex + y] = new bool2(!after || sides[startIndex + y].x, after || sides[startIndex + y].y);
        }
    }


    // planes: 64 bits rows containing 1 if the face must be rendered, 0 otherwise
    // planes contains chunkSize (rows in one plane) * chunkSize (planes in one direction) * nbrIDs * 6 (x+, z+, y+, x-, z-, y-)
    private void GenerateBinaryPlanes() {
        GenerateAxisBinaryPlanes(0);
        GenerateAxisBinaryPlanes(1);
        GenerateAxisBinaryPlanes(2);
    }


    // Generate binary planes for one axis
    private void GenerateAxisBinaryPlanes(int axis)
    {
        int3 beforeX = new(0, startY, 0);
        for (int y = 0; y < WorldManager.chunkSize; y++) // Iter plane rows
        {
            int3 pos = beforeX;
            for (int x = 0; x < WorldManager.chunkSize; x++) // Iter plane columns
            {
                ulong row = rows[x + y * WorldManager.chunkSize + axis * WorldManager.chunkSize * WorldManager.chunkSize];
                bool2 side = sides[x + y * WorldManager.chunkSize + axis * WorldManager.chunkSize * WorldManager.chunkSize];

                // Find faces to render in positive direction and add them to planes
                ulong shiftedRow = row >> 1;
                if (side.y) shiftedRow |= 1UL << 63;
                ulong faceRow = row & ~shiftedRow;
                while (faceRow != 0)
                {
                    int depth = math.tzcnt(faceRow);
                    faceRow &= ~(1UL << depth);
                    int3 posDepth = pos;
                    posDepth[axis] += depth;
                    int id = GetID(posDepth);
                    if (id != 0)
                    {
                        planes[y + depth * WorldManager.chunkSize + idToIndex[id] * WorldManager.chunkSize * WorldManager.chunkSize + 2 * axis * WorldManager.chunkSize * WorldManager.chunkSize * indexToId.length]
                            |= 1UL << x;
                    }
                }

                // Find faces to render in negative direction and add them to planes
                shiftedRow = row << 1;
                if (side.x) shiftedRow |= 1;
                faceRow = row & ~shiftedRow;
                while (faceRow != 0)
                {
                    int depth = math.tzcnt(faceRow);
                    faceRow &= ~(1UL << depth);
                    int3 posDepth = pos;
                    posDepth[axis] += depth;
                    int id = GetID(posDepth);
                    if (id != 0)
                    {
                        planes[y + depth * WorldManager.chunkSize + idToIndex[id] * WorldManager.chunkSize * WorldManager.chunkSize + (2 * axis + 1) * WorldManager.chunkSize * WorldManager.chunkSize * indexToId.length]
                            |= 1UL << x;
                    }
                }

                pos[CubeNormals.WidthAxis(axis)]++;
            }
            beforeX[CubeNormals.HeightAxis(axis)]++;
        }
    }


    private int GetID(int3 pos)
    {
        for (int i = IDIndexes[startXZIndex + pos.x + pos.z * WorldManager.chunkSize]; i < IDIndexes[startXZIndex + pos.x + pos.z * WorldManager.chunkSize + 1]; i += 2)
        {
            if (IDs[i] == pos.y) return IDs[i + 1];
        }
        return default;
    }


    // Generate the mesh for each plane
    private void GenerateOptimizedMesh()
    {
        GenerateNormalOptimizedMesh(CubeNormal.xPositive);
        GenerateNormalOptimizedMesh(CubeNormal.zPositive);
        GenerateNormalOptimizedMesh(CubeNormal.yPositive);
        GenerateNormalOptimizedMesh(CubeNormal.xNegative);
        GenerateNormalOptimizedMesh(CubeNormal.zNegative);
        GenerateNormalOptimizedMesh(CubeNormal.yNegative);
    }


    // Generate the mesh for a normal
    private void GenerateNormalOptimizedMesh(CubeNormal normal)
    {
        VoxelMesh mesh = new VoxelMesh(normal, chunkX, chunkZ, startY);
        for (int i = 0; i < indexToId.length; i++)
        {
            for (int depth = 0; depth < WorldManager.chunkSize; depth++)
            {
                GenerateOptimizedPlane(normal, depth, i, ref mesh);
            }
        }
        if (mesh.squaresCount != 0) meshes.Add(mesh);
    }


    // Generate the mesh for a plane
    private void GenerateOptimizedPlane(CubeNormal normal, int depth, int idIndex, ref VoxelMesh mesh)
    {
        int startIndex =
            (int)normal * WorldManager.chunkSize * WorldManager.chunkSize * indexToId.length
            + idIndex * WorldManager.chunkSize * WorldManager.chunkSize
            + depth * WorldManager.chunkSize;
        for (int y = 0; y < WorldManager.chunkSize; y++) // Iter plane rows
        {
            ulong row = planes[startIndex + y];
            int x = math.tzcnt(row);
            while (x < WorldManager.chunkSize) // Iter plane columns (skip zeros)
            {
                int width = math.tzcnt(~(row >> x)); // Expand in x
                ulong checkMask = (row << (64 - width - x)) >> (64 - width - x);
                ulong deleteMask = ~checkMask;

                int height = 1;
                while (y + height < WorldManager.chunkSize) // Expand in y
                {
                    ref ulong nextRow = ref planes[startIndex + y + height];
                    if ((nextRow & checkMask) != checkMask) break;
                    nextRow &= deleteMask;
                    height++;
                }

                // Add the rectangle
                squares.Add(mesh.Add(x, y, depth, width, height, indexToId[idIndex]));

                x += width;
                x += math.tzcnt(row >> x);
            }
        }
    }
}