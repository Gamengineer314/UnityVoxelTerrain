using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Random = UnityEngine.Random;
using UnityEngine.Rendering;


// Terrain renderer using indirect instanced rendering.
// Some optimizations inspired by https://youtu.be/40JzyaOYJeY?si=fmJFqzTBIclsLnIv
[BurstCompile]
public class TerrainRenderer : MonoBehaviour {
    private const int threadGroupSize = 64;
    private const float quadsInterleaving = 0.05f; // Remove small (1 pixel) gaps between triangles

    public Material material;
    public Camera mainCamera;
    public ComputeShader terrainCulling;

    private GraphicsBuffer squaresBuffer = null; // All rectangles (position, width, height, normal)
    private NativeList<Square> squares;
    private GraphicsBuffer meshDataBuffer = null; // All meshes information (position, size, rectangles indices)
    private NativeList<TerrainMeshData> meshData;
    private GraphicsBuffer commandsBuffer;
    private GraphicsBuffer counterBuffer = null; // Number of commands
    private GraphicsBuffer indicesBuffer; // Indices of a rectangles (each rectangles is an instance)
    private RenderParams renderParams;
    private int threadGroups;
    private bool rendering = false;

#if UNITY_EDITOR
    public enum SceneRender { Player, All, Nothing }
    public SceneRender sceneRender;
    private RenderParams allRenderParams;
    private GraphicsBuffer allCommandsBuffer = null;
    private int allNCommands;
#endif

    private readonly int cameraPositionUniform = Shader.PropertyToID("cameraPosition");
    private readonly int cameraFarPlaneUniform = Shader.PropertyToID("cameraFarPlane");
    private readonly int cameraLeftPlaneUniform = Shader.PropertyToID("cameraLeftPlane");
    private readonly int cameraRightPlaneUniform = Shader.PropertyToID("cameraRightPlane");
    private readonly int cameraDownPlaneUniform = Shader.PropertyToID("cameraDownPlane");
    private readonly int cameraUpPlaneUniform = Shader.PropertyToID("cameraUpPlane");


    private unsafe void Start() {
        squares = new NativeList<Square>(Allocator.Persistent);
        meshData = new NativeList<TerrainMeshData>(Allocator.Persistent);

        material.SetFloat("seed", Random.value);
        material.SetFloat("quadsInterleaving", quadsInterleaving);

        counterBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, sizeof(uint));

        // All 16 bits indices
        ushort[] indices = new ushort[98304];
        for (int i = 0; i < 16384; i++) {
            indices[6 * i] = (ushort)(4 * i);
            indices[6 * i + 1] = (ushort)(4 * i + 1);
            indices[6 * i + 2] = (ushort)(4 * i + 2);
            indices[6 * i + 3] = (ushort)(4 * i + 2);
            indices[6 * i + 4] = (ushort)(4 * i + 1);
            indices[6 * i + 5] = (ushort)(4 * i + 3);
        }
        indicesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, indices.Length, sizeof(ushort));
        indicesBuffer.SetData(indices);

        renderParams = new RenderParams(material) {
            worldBounds = new Bounds(new Vector3(WorldManager.horizontalSize, WorldManager.verticalSize, WorldManager.horizontalSize) / 2, new Vector3(WorldManager.horizontalSize, WorldManager.verticalSize, WorldManager.horizontalSize)),
            camera = mainCamera,
            matProps = new MaterialPropertyBlock()
        };

#if UNITY_EDITOR
        allRenderParams = new RenderParams(material) {
            worldBounds = new Bounds(new Vector3(WorldManager.horizontalSize, WorldManager.verticalSize, WorldManager.horizontalSize) / 2, new Vector3(WorldManager.horizontalSize, WorldManager.verticalSize, WorldManager.horizontalSize)),
            layer = LayerMask.NameToLayer("SceneOnly"),
            matProps = new MaterialPropertyBlock()
        };
        OnValidate();
#endif
    }


    public void Dispose() {
        squaresBuffer?.Dispose();
        meshDataBuffer?.Dispose();
        commandsBuffer.Dispose();
        counterBuffer.Dispose();
        indicesBuffer.Dispose();
        if (squares.IsCreated) squares.Dispose();
        if (meshData.IsCreated) meshData.Dispose();
#if UNITY_EDITOR
        allCommandsBuffer?.Dispose();
#endif
    }


    /// <summary>
    /// Add new meshes (before starting to render)
    /// </summary>
    /// <param name="meshes">Meshes to add</param>
    /// <param name="squares">Squares in meshes</param>
    public void AddMeshes(NativeList<VoxelMesh> meshes, NativeList<Square> squares) {
        uint startSquare = (uint)this.squares.Length;
        foreach (VoxelMesh mesh in meshes) {
            meshData.Add(new TerrainMeshData(mesh, startSquare));
            startSquare += mesh.squaresCount;
        }
        this.squares.AddRange(squares.AsArray());
    }


    /// <summary>
    /// Prepare render.
    /// Future LateUpdate will draw terrain.
    /// </summary>
    public unsafe void StartRender() {
        // Add empty meshes at the end to have a size multiple of 64 (number of threads per thread group in compute shader)
        while (meshData.Length % threadGroupSize != 0) meshData.Add(default);
        threadGroups = meshData.Length / threadGroupSize;

        // Create buffers
        squaresBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, squares.Length, sizeof(Square));
        squaresBuffer.SetData(squares.AsArray());
        material.SetBuffer("squares", squaresBuffer);
        meshDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, meshData.Length, sizeof(TerrainMeshData));
        meshDataBuffer.SetData(meshData.AsArray());
        terrainCulling.SetBuffer(0, "meshData", meshDataBuffer);
        commandsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Counter, meshData.Length, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        GraphicsBuffer.IndirectDrawIndexedArgs[] commands = new GraphicsBuffer.IndirectDrawIndexedArgs[meshData.Length];
        for (int i = 0; i < meshData.Length; i++) commands[i] = new() { instanceCount = 1 };
        commandsBuffer.SetData(commands);
#if UNITY_EDITOR
        allCommandsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, meshData.Length, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        for (int i = 0; i < meshData.Length; i++) {
            commands[i].indexCountPerInstance = 6 * meshData[i].SquareCount;
            commands[i].baseVertexIndex = 4 * meshData[i].StartSquare;
        }
        allCommandsBuffer.SetData(commands);
        allNCommands = meshData.Length;
#endif
        terrainCulling.SetBuffer(0, "commands", commandsBuffer);
        squares.Dispose();
        meshData.Dispose();
        rendering = true;
    }


#if UNITY_EDITOR
    private void OnValidate() {
        // Change scene view
        if (sceneRender == SceneRender.Player) {
            renderParams.camera = null;
            renderParams.layer = LayerMask.NameToLayer("PlayerAndScene");
        }
        else renderParams.camera = mainCamera;
    }
#endif


    // LateUpdate to render after camera moved
    private void LateUpdate() {
        if (rendering) {
            DrawMeshes();
        }
    }


    private unsafe void DrawMeshes() {
        // Set camera data
        terrainCulling.SetVector(cameraPositionUniform, mainCamera.transform.position);
        Plane[] cameraPlanes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
        terrainCulling.SetVector(cameraFarPlaneUniform, new Vector4(cameraPlanes[5].normal.x, cameraPlanes[5].normal.y, cameraPlanes[5].normal.z, cameraPlanes[5].distance));
        terrainCulling.SetVector(cameraLeftPlaneUniform, new Vector4(cameraPlanes[0].normal.x, cameraPlanes[0].normal.y, cameraPlanes[0].normal.z, cameraPlanes[0].distance));
        terrainCulling.SetVector(cameraRightPlaneUniform, new Vector4(cameraPlanes[1].normal.x, cameraPlanes[1].normal.y, cameraPlanes[1].normal.z, cameraPlanes[1].distance));
        terrainCulling.SetVector(cameraDownPlaneUniform, new Vector4(cameraPlanes[2].normal.x, cameraPlanes[2].normal.y, cameraPlanes[2].normal.z, cameraPlanes[2].distance));
        terrainCulling.SetVector(cameraUpPlaneUniform, new Vector4(cameraPlanes[3].normal.x, cameraPlanes[3].normal.y, cameraPlanes[3].normal.z, cameraPlanes[3].distance));

        // Frustrum culling
        commandsBuffer.SetCounterValue(0);
        terrainCulling.Dispatch(0, threadGroups, 1, 1);
        GraphicsBuffer.CopyCount(commandsBuffer, counterBuffer, 0);
        uint[] data = new uint[1];
        counterBuffer.GetData(data);

        // Draw calls
        Graphics.RenderPrimitivesIndexedIndirect(renderParams, MeshTopology.Triangles, indicesBuffer, commandsBuffer, commandCount: (int)data[0]);
#if UNITY_EDITOR
        if (sceneRender == SceneRender.All) Graphics.RenderPrimitivesIndexedIndirect(allRenderParams, MeshTopology.Triangles, indicesBuffer, allCommandsBuffer, commandCount: allNCommands);
#endif
    }
}