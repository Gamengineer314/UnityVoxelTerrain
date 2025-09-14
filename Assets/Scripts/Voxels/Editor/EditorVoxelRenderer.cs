#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Voxels.Rendering;
using System.Collections.Generic;

namespace Voxels.Editor {

    [InitializeOnLoad]
    internal class EditorVoxelRenderer : EditorWindow {
        private static Dictionary<VoxelTerrain, GraphicsBuffer> commandsBuffers = new();

        static EditorVoxelRenderer() {
            SceneView.duringSceneGui -= EditorRender;
            SceneView.duringSceneGui += EditorRender;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
        }

        private static void Dispose() {
            foreach (GraphicsBuffer buffer in commandsBuffers.Values) buffer.Dispose();
        }


        private static void EditorRender(SceneView view) {
            // Get global data
            if (VoxelData.Instance == null) {
                if (Application.isEditor) {
                    VoxelData voxels = FindObjectOfType<VoxelData>();
                    if (voxels == null) return;
                    voxels.Init();
                    AssemblyReloadEvents.beforeAssemblyReload += voxels.Dispose;
                }
                else return;
            }

            // Draw terrains
            Camera sceneCamera = SceneView.currentDrawingSceneView.camera;
            RenderTerrains(sceneCamera);
        }


        private static void RenderTerrains(Camera sceneCamera) {
            VoxelData voxels = VoxelData.Instance;
            RenderParams renderParams = new(voxels.terrainMaterial) { camera = sceneCamera };

            VoxelTerrain[] terrains = FindObjectsOfType<VoxelTerrain>();
            foreach (VoxelTerrain terrain in terrains) {
                if (!terrain.Created) continue;
                if (!commandsBuffers.ContainsKey(terrain)) commandsBuffers[terrain] = VoxelTerrainRenderer.CreateCommands(terrain.meshCount);
                GraphicsBuffer commandsBuffer = commandsBuffers[terrain];

                SceneRender renderMode = VoxelTerrainEditor.GetRenderMode(terrain);
                if (renderMode == SceneRender.None) continue;
                int count;
                if (renderMode == SceneRender.All) {
                    count = VoxelTerrainRenderer.PrepareDraw(terrain, sceneCamera, terrain.facesBuffer, commandsBuffer);
                }
                else {
                    int renderId = VoxelTerrainEditor.GetRenderId(terrain);
                    VoxelTerrainRenderer renderer = (VoxelTerrainRenderer)EditorUtility.InstanceIDToObject(renderId);
                    count = VoxelTerrainRenderer.PrepareDraw(terrain, renderer.target, terrain.facesBuffer, commandsBuffer);
                }

                renderParams.worldBounds = terrain.bounds;
                Graphics.RenderPrimitivesIndexedIndirect(renderParams, MeshTopology.Triangles, voxels.indicesBuffer, commandsBuffer, count);
            }
        }
    }

}
#endif