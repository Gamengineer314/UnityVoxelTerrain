using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;
using Voxels.Collections;
using Voxels.Rendering;
using Voxels;

public class WorldManager : MonoBehaviour {
    public const int horizontalSize = 4096; // Number of blocks in x and z dimensions
    public const int verticalSize = 512; // Number of blocks in y dimension
    public const int chunkSize = 64; // Size of a chunk in all dimensions
    public const int horizontalChunks = horizontalSize / chunkSize; // Number of chunks in x and z dimensions

    public TerrainGenerator terrainGenerator;
    public VoxelTerrainRenderer terrainRenderer;
    public VoxelTerrain terrain;


    private void Start() {
        // Generate terrain
        Stopwatch watch = Stopwatch.StartNew();
        VoxelColumns<char> voxels = terrainGenerator.GenerateTerrain();
        Debug.Log($"Terrain generated in {watch.ElapsedMilliseconds} ms");

        // Generate mesh
        watch.Restart();
        terrain.voxels = voxels;
        terrain.bounds = new Bounds(new Vector3(horizontalSize, verticalSize, horizontalSize) / 2, new Vector3(horizontalSize, verticalSize, horizontalSize));
        terrain.CompleteGenerate();
        Debug.Log($"Mesh generated in {watch.ElapsedMilliseconds} ms");
    }
}
