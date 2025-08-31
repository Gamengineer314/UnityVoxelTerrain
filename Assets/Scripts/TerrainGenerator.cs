using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;

// Example terrain generator
[BurstCompile]
public class TerrainGenerator : MonoBehaviour {
    private const float amplitude = 80;
    private const float blockPeriod = 500;
    private const int idHeight = 50;

    public (NativeList<int> IDs, NativeArray<int> IDIndexes) GenerateTerrain() {
        NativeArray<int> heightMap = new(WorldManager.horizontalSize * WorldManager.horizontalSize, Allocator.Temp);
        NativeArray<int> ids = new(WorldManager.horizontalSize * WorldManager.horizontalSize, Allocator.Temp);
        GenerateHeightMap(ref heightMap, ref ids);
        GenerateIDs(in heightMap, in ids, out NativeList<int> IDs, out NativeArray<int> IDIndexes);
        return (IDs, IDIndexes);
    }


    [BurstCompile]
    private static void GenerateHeightMap(ref NativeArray<int> heightMap, ref NativeArray<int> ids) {
        // Sine height map
        int index = 0;
        for (int z = 0; z < WorldManager.horizontalSize; z++) {
            for (int x = 0; x < WorldManager.horizontalSize; x++) {
                int height = 1 + (int)(amplitude * (math.sin(2 * math.PI * x / blockPeriod) * math.sin(2 * math.PI * z / blockPeriod) + 1));
                heightMap[index] = height;
                ids[index] = height / idHeight + 1;
                index++;
            }
        }
    }


    [BurstCompile]
    private static void GenerateIDs(in NativeArray<int> heightMap, in NativeArray<int> ids, out NativeList<int> IDs, out NativeArray<int> IDIndexes) {
        IDs = new NativeList<int>(Allocator.Persistent);
        IDIndexes = new NativeArray<int>(WorldManager.horizontalSize * WorldManager.horizontalSize + 1, Allocator.Persistent);
        for (int chunkZ = 0; chunkZ < WorldManager.horizontalChunks; chunkZ++) {
            for (int chunkX = 0; chunkX < WorldManager.horizontalChunks; chunkX++) {
                for (int zInChunk = 0; zInChunk < WorldManager.chunkSize; zInChunk++) {
                    for (int xInChunk = 0; xInChunk < WorldManager.chunkSize; xInChunk++) {
                        int x = chunkX * WorldManager.chunkSize + xInChunk;
                        int z = chunkZ * WorldManager.chunkSize + zInChunk;
                        int y = heightMap[x + z * WorldManager.horizontalSize];
                        IDIndexes[(chunkX + chunkZ * WorldManager.horizontalChunks) * WorldManager.chunkSize * WorldManager.chunkSize + xInChunk + zInChunk * WorldManager.chunkSize] = IDs.Length;

                        // Add zeros below the block to not render invisible faces, then add the block
                        int minSurroundingY = y - 1;
                        if (x > 0) minSurroundingY = math.min(minSurroundingY, heightMap[x - 1 + z * WorldManager.horizontalSize]);
                        if (x < WorldManager.horizontalSize - 1) minSurroundingY = math.min(minSurroundingY, heightMap[x + 1 + z * WorldManager.horizontalSize]);
                        if (z > 0) minSurroundingY = math.min(minSurroundingY, heightMap[x + (z - 1) * WorldManager.horizontalSize]);
                        if (z < WorldManager.horizontalSize - 1) minSurroundingY = math.min(minSurroundingY, heightMap[x + (z + 1) * WorldManager.horizontalSize]);
                        for (int belowY = minSurroundingY; belowY < y; belowY++) {
                            IDs.Add(belowY);
                            IDs.Add(0);
                        }
                        IDs.Add(y);
                        IDs.Add(ids[x + z * WorldManager.horizontalSize]);
                    }
                }
            }
        }
    }
}
