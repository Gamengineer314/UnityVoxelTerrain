using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Voxels.Collections;

// Example terrain generator
[BurstCompile]
public class TerrainGenerator : MonoBehaviour {
    private const float amplitude = 80;
    private const float blockPeriod = 500;
    private const int idHeight = 50;

    public VoxelColumns<char> GenerateTerrain() {
        Native2DArray<Voxel<char>> heightMap = new(WorldManager.horizontalSize, WorldManager.horizontalSize, Allocator.Persistent);
        GenerateHeightMap(ref heightMap);
        VoxelColumns<char> voxels = VoxelColumns.FromHeightMap(heightMap);
        heightMap.Dispose();
        return voxels;
    }

    [BurstCompile]
    private static void GenerateHeightMap(ref Native2DArray<Voxel<char>> heightMap) {
        // Sine height map
        for (int z = 0; z < WorldManager.horizontalSize; z++) {
            for (int x = 0; x < WorldManager.horizontalSize; x++) {
                int height = 1 + (int)(amplitude * (math.sin(2 * math.PI * x / blockPeriod) * math.sin(2 * math.PI * z / blockPeriod) + 1));
                heightMap[x, z] = new(height, (char)(height / idHeight + 1));
            }
        }
    }
}
