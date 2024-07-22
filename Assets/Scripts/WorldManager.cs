using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Stopwatch = System.Diagnostics.Stopwatch;

public class WorldManager : MonoBehaviour
{
    public const float blockSize = 1; // Size of a block
    public const int horizontalSize = 4096; // Number of blocks in x and z dimensions
    public const int verticalSize = 512; // Number of blocks in y dimension
    public const int chunkSize = 64; // Size of a chunk in all dimensions
    public const int horizontalChunks = horizontalSize / chunkSize; // Number of chunks in x and z dimensions

    private const int generateJobsX = 4;
    private const int generateJobsZ = 4;

    public TerrainGenerator terrainGenerator;
    public TerrainRenderer terrainRenderer;


    private void Start()
    {
        // Generate terrain
        Stopwatch watch = Stopwatch.StartNew();
        (NativeList<int> IDs, NativeArray<int> IDIndexes) = terrainGenerator.GenerateTerrain();
        Debug.Log($"Terrain generated in {watch.ElapsedMilliseconds}s");

        // Generate mesh
        watch.Restart();
        GenerateMesh[] jobs = new GenerateMesh[generateJobsX * generateJobsZ];
        NativeArray<JobHandle> handles = new NativeArray<JobHandle>(generateJobsX * generateJobsZ, Allocator.Temp);
        for (int jobZ = 0; jobZ < generateJobsZ; jobZ++)
        {
            for (int jobX = 0; jobX < generateJobsX; jobX++)
            {
                jobs[jobX + jobZ * generateJobsX] = new GenerateMesh(jobX * horizontalChunks / generateJobsX, jobZ * horizontalChunks / generateJobsZ, horizontalChunks / generateJobsX, horizontalChunks / generateJobsZ, IDs, IDIndexes);
                handles[jobX + jobZ * generateJobsX] = jobs[jobX + jobZ * generateJobsX].Schedule();
            }
        }
        JobHandle.CompleteAll(handles);
        IDs.Dispose();
        IDIndexes.Dispose();
        Debug.Log($"Mesh generated in {watch.ElapsedMilliseconds}s");

        // Start rendering
        foreach (GenerateMesh job in jobs)
        {
            terrainRenderer.AddMeshes(job.meshes, job.squares);
            job.meshes.Dispose();
            job.squares.Dispose();
        }
        terrainRenderer.StartRender();
        Debug.Log($"Rendering started in {watch.ElapsedMilliseconds}s");
    }


    private void OnApplicationQuit()
    {
        terrainRenderer.Dispose();
    }
}
