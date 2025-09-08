using System;
using UnityEngine;
using Voxels;

namespace Voxels.Rendering {

    public class VoxelTerrainRenderer : MonoBehaviour {

        public VoxelTerrain terrain; // Terrain to render
        public Camera target; // Camera to render the terrain on

        private RenderParams renderParams;
        private bool rendering;


        private void LateUpdate() {
            if (!rendering && terrain.Created) StartRender();
            if (rendering) Render();
        }


        private void StartRender() {
            rendering = true;
            MaterialPropertyBlock properties = new();
            renderParams = new(VoxelsData.Instance.terrainMaterial) {
                //camera = target,
                worldBounds = terrain.bounds,
                matProps = properties
            };
            properties.SetBuffer("faces", terrain.facesBuffer);
        }


        private void Render() {
            VoxelsData voxels = VoxelsData.Instance;

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
            voxels.terrainCulling.SetBuffer(0, voxels.commandsId, terrain.commandsBuffer);
            terrain.commandsBuffer.SetCounterValue(0);
            voxels.terrainCulling.Dispatch(0, terrain.threadGroups, 1, 1);
            GraphicsBuffer.CopyCount(terrain.commandsBuffer, voxels.counterBuffer, 0);
            uint[] data = new uint[1];
            voxels.counterBuffer.GetData(data);

            // Draw calls
            Graphics.RenderPrimitivesIndexedIndirect(renderParams, MeshTopology.Triangles, voxels.indicesBuffer, terrain.commandsBuffer, commandCount: (int)data[0]);
        }
    }

}