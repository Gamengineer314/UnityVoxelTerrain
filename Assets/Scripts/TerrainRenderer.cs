using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Random = UnityEngine.Random;


// Terrain renderer using indirect instanced rendering.
// Some optimizations inspired by https://youtu.be/40JzyaOYJeY?si=fmJFqzTBIclsLnIv
[BurstCompile]
public class TerrainRenderer : MonoBehaviour
{
    private const int threadGroupSize = 64;
    private const float quadsInterleaving = 0.05f; // Remove small (1 pixel) gaps between triangles

    public Material material;
    public Camera mainCamera;
    public ComputeShader terrainCulling;

    private GraphicsBuffer squaresBuffer = null; // All rectangles (position, width, height, normal)
    private NativeList<Square> squares;
    private GraphicsBuffer meshDataBuffer = null; // All meshes information (position, size, rectangles indices)
    private NativeList<TerrainMeshData> meshData;
    private GraphicsBuffer squaresIndicesBuffer = null; // Indices of the rectangles to render
    private GraphicsBuffer indicesBuffer; // Indices of a rectangles (each rectangles is an instance)
    private ushort[] quadIndices = new ushort[] { 0, 1, 2, 0, 2, 3 };
    private GraphicsBuffer commandsBuffer; // 0: player, (1: all)
    private GraphicsBuffer.IndirectDrawIndexedArgs[] commands = new GraphicsBuffer.IndirectDrawIndexedArgs[]
    {
        new GraphicsBuffer.IndirectDrawIndexedArgs { indexCountPerInstance = 6 },
        new GraphicsBuffer.IndirectDrawIndexedArgs { indexCountPerInstance = 6 }
    };
    private RenderParams renderParams;
    private int threadGroups;
    private bool rendering = false;

#if UNITY_EDITOR
    public enum SceneRender { Player, All, Nothing }
    public SceneRender sceneRender;
    private RenderParams allRenderParams;
    private GraphicsBuffer allSquaresIndicesBuffer = null;
#endif

    private int cameraPositionUniform = Shader.PropertyToID("cameraPosition");
    private int cameraFarPlaneUniform = Shader.PropertyToID("cameraFarPlane");
    private int cameraLeftPlaneUniform = Shader.PropertyToID("cameraLeftPlane");
    private int cameraRightPlaneUniform = Shader.PropertyToID("cameraRightPlane");
    private int cameraDownPlaneUniform = Shader.PropertyToID("cameraDownPlane");
    private int cameraUpPlaneUniform = Shader.PropertyToID("cameraUpPlane");


    private unsafe void Start()
    {
        squares = new NativeList<Square>(Allocator.Persistent);
        meshData = new NativeList<TerrainMeshData>(Allocator.Persistent);
        material.SetFloat("seed", Random.value);
        material.SetFloat("quadsInterleaving", quadsInterleaving);

        commandsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 3, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        terrainCulling.SetBuffer(0, "commands", commandsBuffer);
        indicesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, quadIndices.Length, sizeof(ushort));
        indicesBuffer.SetData(quadIndices);
        quadIndices = null;

        renderParams = new RenderParams(material);
        renderParams.worldBounds = new Bounds(new Vector3(WorldManager.horizontalSize, WorldManager.verticalSize, WorldManager.horizontalSize) / 2, new Vector3(WorldManager.horizontalSize, WorldManager.verticalSize, WorldManager.horizontalSize));
        renderParams.camera = mainCamera;
        renderParams.matProps = new MaterialPropertyBlock();

#if UNITY_EDITOR
        allRenderParams = new RenderParams(material);
        allRenderParams.worldBounds = new Bounds(new Vector3(WorldManager.horizontalSize, WorldManager.verticalSize, WorldManager.horizontalSize) / 2, new Vector3(WorldManager.horizontalSize, WorldManager.verticalSize, WorldManager.horizontalSize));
        allRenderParams.layer = LayerMask.NameToLayer("SceneOnly");
        allRenderParams.matProps = new MaterialPropertyBlock();
        OnValidate();
#endif
    }


    public void Dispose()
    {
        commandsBuffer.Dispose();
        indicesBuffer.Dispose();
        if (squaresBuffer != null) squaresBuffer.Dispose();
        if (meshDataBuffer != null) meshDataBuffer.Dispose();
        if (squaresIndicesBuffer != null) squaresIndicesBuffer.Dispose();
        if (squares.IsCreated) squares.Dispose();
        if (meshData.IsCreated) meshData.Dispose();
#if UNITY_EDITOR
        if (allSquaresIndicesBuffer != null) allSquaresIndicesBuffer.Dispose();
#endif
    }


    /// <summary>
    /// Add new meshes (before starting to render)
    /// </summary>
    /// <param name="meshes">Meshes to add</param>
    /// <param name="squares">Squares in meshes</param>
    public void AddMeshes(NativeList<VoxelMesh> meshes, NativeList<Square> squares)
    {
        uint startSquare = (uint)this.squares.Length;
        foreach (VoxelMesh mesh in meshes)
        {
            meshData.Add(new TerrainMeshData(mesh, startSquare));
            startSquare += mesh.squaresCount;
        }
        this.squares.AddRange(squares.AsArray());
    }


    /// <summary>
    /// Prepare render.
    /// Future LateUpdate will draw terrain.
    /// </summary>
    public unsafe void StartRender()
    {
        // Add empty meshes at the end to have a size multiple of 64 (number of threads per thread group in compute shader)
        while (meshData.Length % threadGroupSize != 0) meshData.Add(default);
        threadGroups = meshData.Length / threadGroupSize;

        // Create buffers
        squaresBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, squares.Length, sizeof(Square));
        squaresBuffer.SetData(squares.AsArray());
        material.SetBuffer("squares", squaresBuffer);
        squaresIndicesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, squares.Length, sizeof(uint));
        renderParams.matProps.SetBuffer("squaresIndices", squaresIndicesBuffer);
        terrainCulling.SetBuffer(0, "squaresIndices", squaresIndicesBuffer);
        meshDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, meshData.Length, sizeof(TerrainMeshData));
        meshDataBuffer.SetData(meshData.AsArray());
        terrainCulling.SetBuffer(0, "meshData", meshDataBuffer);
#if UNITY_EDITOR
        NativeArray<uint> allSquaresIndices = new NativeArray<uint>(squares.Length, Allocator.Temp);
        for (int i = 0; i < squares.Length; i++) allSquaresIndices[i] = (uint)i;
        allSquaresIndicesBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, squares.Length, sizeof(uint));
        allSquaresIndicesBuffer.SetData(allSquaresIndices);
        allRenderParams.matProps.SetBuffer("squaresIndices", allSquaresIndicesBuffer);
        commands[1] = new GraphicsBuffer.IndirectDrawIndexedArgs { indexCountPerInstance = 6, instanceCount = (uint)squares.Length };
        allSquaresIndices.Dispose();
#endif
        squares.Dispose();
        meshData.Dispose();
        rendering = true;
    }


#if UNITY_EDITOR
    private void OnValidate()
    {
        // Change scene view
        if (sceneRender == SceneRender.Player)
        {
            renderParams.camera = null;
            renderParams.layer = LayerMask.NameToLayer("PlayerAndScene");
        }
        else renderParams.camera = mainCamera;
    }
#endif


    // LateUpdate to render after camera moved
    private void LateUpdate()
    {
        if (rendering)
        {
            DrawMeshes();
        }
    }


    private unsafe void DrawMeshes()
    {
        // Set camera data
        terrainCulling.SetVector(cameraPositionUniform, mainCamera.transform.position);
        Plane[] cameraPlanes = GeometryUtility.CalculateFrustumPlanes(mainCamera);
        terrainCulling.SetVector(cameraFarPlaneUniform, new Vector4(cameraPlanes[5].normal.x, cameraPlanes[5].normal.y, cameraPlanes[5].normal.z, cameraPlanes[5].distance));
        terrainCulling.SetVector(cameraLeftPlaneUniform, new Vector4(cameraPlanes[0].normal.x, cameraPlanes[0].normal.y, cameraPlanes[0].normal.z, cameraPlanes[0].distance));
        terrainCulling.SetVector(cameraRightPlaneUniform, new Vector4(cameraPlanes[1].normal.x, cameraPlanes[1].normal.y, cameraPlanes[1].normal.z, cameraPlanes[1].distance));
        terrainCulling.SetVector(cameraDownPlaneUniform, new Vector4(cameraPlanes[2].normal.x, cameraPlanes[2].normal.y, cameraPlanes[2].normal.z, cameraPlanes[2].distance));
        terrainCulling.SetVector(cameraUpPlaneUniform, new Vector4(cameraPlanes[3].normal.x, cameraPlanes[3].normal.y, cameraPlanes[3].normal.z, cameraPlanes[3].distance));

        // Frustrum culling in compute shader then draw
        commandsBuffer.SetData(commands, 0, 0, 2);
        terrainCulling.Dispatch(0, threadGroups, 1, 1);
        Graphics.RenderPrimitivesIndexedIndirect(renderParams, MeshTopology.Triangles, indicesBuffer, commandsBuffer, startCommand:0, commandCount:1);
#if UNITY_EDITOR
        if (sceneRender == SceneRender.All) Graphics.RenderPrimitivesIndexedIndirect(allRenderParams, MeshTopology.Triangles, indicesBuffer, commandsBuffer, startCommand:1, commandCount:1);
#endif
    }
}
