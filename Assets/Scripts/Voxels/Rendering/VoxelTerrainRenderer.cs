using UnityEngine;

namespace Voxels.Rendering {

    public class VoxelTerrainRenderer : MonoBehaviour {
        public VoxelTerrain terrain; // Terrain to render
        public Camera target; // Camera to render the terrain on

        private RenderParams renderParams;
        private GraphicsBuffer commandsBuffer;
        private bool rendering;


        private void LateUpdate() {
            if (!rendering && terrain.Created) StartRender();
            if (rendering) {
                int count = PrepareDraw(terrain, target, terrain.facesBuffer, commandsBuffer);
                Graphics.RenderPrimitivesIndexedIndirect(renderParams, MeshTopology.Triangles, VoxelData.Instance.indicesBuffer, commandsBuffer, count);
            }
        }


        private void StartRender() {
            rendering = true;
            renderParams = new(VoxelData.Instance.terrainMaterial) {
                camera = target,
                worldBounds = terrain.bounds
            };
            commandsBuffer = CreateCommands(terrain.meshCount);
        }


        /// <summary>
        /// Create a commands buffer
        /// </summary>
        /// <param name="meshCount">Maximum number of meshes that can be rendered</param>
        /// <returns>The buffer</returns>
        internal static GraphicsBuffer CreateCommands(int meshCount) {
            GraphicsBuffer buffer = new(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Counter | GraphicsBuffer.Target.Structured, meshCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            GraphicsBuffer.IndirectDrawIndexedArgs[] commands = new GraphicsBuffer.IndirectDrawIndexedArgs[meshCount];
            for (int i = 0; i < meshCount; i++) commands[i] = new() { instanceCount = 1 };
            buffer.SetData(commands);
            return buffer;
        }


        /// <summary>
        /// Prepare a draw call
        /// </summary>
        /// <param name="terrain">The terrain to draw</param>
        /// <param name="target">The target camera</param>
        /// <param name="commandsBuffer">The commands buffer to use</param>
        /// <returns>Number of commands to draw</returns>
        internal static int PrepareDraw(VoxelTerrain terrain, Camera target, GraphicsBuffer facesBuffer, GraphicsBuffer commandsBuffer) {
            VoxelData voxels = VoxelData.Instance;
            voxels.terrainMaterial.SetBuffer(voxels.facesId, facesBuffer);

            // Set camera data
            voxels.terrainCulling.SetVector(voxels.cameraPositionId, target.transform.position);
            Plane[] cameraPlanes = GeometryUtility.CalculateFrustumPlanes(target);
            voxels.terrainCulling.SetVector(voxels.cameraFarPlaneId, new Vector4(cameraPlanes[5].normal.x, cameraPlanes[5].normal.y, cameraPlanes[5].normal.z, cameraPlanes[5].distance));
            voxels.terrainCulling.SetVector(voxels.cameraLeftPlaneId, new Vector4(cameraPlanes[0].normal.x, cameraPlanes[0].normal.y, cameraPlanes[0].normal.z, cameraPlanes[0].distance));
            voxels.terrainCulling.SetVector(voxels.cameraRightPlaneId, new Vector4(cameraPlanes[1].normal.x, cameraPlanes[1].normal.y, cameraPlanes[1].normal.z, cameraPlanes[1].distance));
            voxels.terrainCulling.SetVector(voxels.cameraDownPlaneId, new Vector4(cameraPlanes[2].normal.x, cameraPlanes[2].normal.y, cameraPlanes[2].normal.z, cameraPlanes[2].distance));
            voxels.terrainCulling.SetVector(voxels.cameraUpPlaneId, new Vector4(cameraPlanes[3].normal.x, cameraPlanes[3].normal.y, cameraPlanes[3].normal.z, cameraPlanes[3].distance));

            // Frustrum culling
            voxels.terrainCulling.SetBuffer(0, voxels.meshesId, terrain.meshesBuffer);
            voxels.terrainCulling.SetBuffer(0, voxels.commandsId, commandsBuffer);
            commandsBuffer.SetCounterValue(0);
            voxels.terrainCulling.Dispatch(0, terrain.meshCount / VoxelData.terrainCullingGroupSize, 1, 1);
            GraphicsBuffer.CopyCount(commandsBuffer, voxels.counterBuffer, 0);
            uint[] data = new uint[1];
            voxels.counterBuffer.GetData(data);
            return (int)data[0];
        }


        private void OnDestroy() {
            commandsBuffer?.Dispose();
        }
    }

}