#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Voxels.Rendering;
using System.Linq;

namespace Voxels.Editor {

    [CustomEditor(typeof(VoxelTerrain)), CanEditMultipleObjects]
    internal class VoxelTerrainEditor : UnityEditor.Editor {
        internal static SceneRender GetRenderMode(VoxelTerrain terrain) // 0: All, 1: None, 2: Renderer
            => (SceneRender)EditorPrefs.GetInt($"VoxelTerrain_{terrain.GetInstanceID()}_renderMode", 0);
        internal static int GetRenderId(VoxelTerrain terrain) // ID of the renderer
            => EditorPrefs.GetInt($"VoxelTerrain_{terrain.GetInstanceID()}_renderId", 0);
        private static void SetRenderMode(VoxelTerrain terrain, SceneRender mode)
            => EditorPrefs.SetInt($"VoxelTerrain_{terrain.GetInstanceID()}_renderMode", (int)mode);
        private static void SetRenderId(VoxelTerrain terrain, int id)
            => EditorPrefs.SetInt($"VoxelTerrain_{terrain.GetInstanceID()}_renderId", id);


        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            EditorGUILayout.Space();

            VoxelTerrain terrain = (VoxelTerrain)target;
            VoxelTerrainRenderer[] renderers = FindObjectsOfType<VoxelTerrainRenderer>(true)
                .Where(renderer => renderer.terrain == terrain).ToArray();
            SceneRender renderMode = GetRenderMode(terrain);
            int renderId = GetRenderId(terrain);

            int renderIndex = -1;
            string[] options = new string[2 + renderers.Length];
            options[0] = "All";
            options[1] = "None";
            for (int i = 0; i < renderers.Length; i++) {
                options[2 + i] = renderers[i].name;
                if (renderers[i].GetInstanceID() == renderId) renderIndex = i;
            }
            if (renderMode == SceneRender.Renderer) {
                if (renderIndex == -1) renderIndex = 0;
                else renderIndex += 2;
            }
            else renderIndex = (int)renderMode;

            int chosenIndex = EditorGUILayout.Popup(new GUIContent("Scene Render"), renderIndex, options);
            if (chosenIndex < 2) renderMode = (SceneRender)chosenIndex;
            else {
                renderMode = SceneRender.Renderer;
                renderId = renderers[chosenIndex - 2].GetInstanceID();
            }

            SetRenderMode(terrain, renderMode);
            SetRenderId(terrain, renderId);
        }
    }



    internal enum SceneRender { All = 0, None = 1, Renderer = 2 }

}
#endif