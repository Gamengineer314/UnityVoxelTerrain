using System;
using Unity.Jobs;
using UnityEngine;
using Voxels.Collections;

namespace Voxels.Rendering {

    public class VoxelTerrain : MonoBehaviour {
        public int maxHorizontalSize = 64;
        public int mergeNormalsThreshold = 256;

        public Bounds bounds { get; private set; }
        internal GraphicsBuffer facesBuffer { get; private set; }
        internal GraphicsBuffer meshesBuffer { get; private set; }
        internal GraphicsBuffer commandsBuffer { get; private set; }
        internal int threadGroups { get; private set; }
        internal VoxelColumns<char> voxels { get; private set; }

        private TerrainMeshGenerator generator;
        private bool generating;
        internal bool Created => facesBuffer != null;

        private void Awake() {
            generator = new(maxHorizontalSize, mergeNormalsThreshold, 1024);
        }


        /// <summary>
        /// Set the terrain
        /// </summary>
        /// <param name="voxels">Voxels in the terrain</param>
        /// <param name="bounds">Bounds of the terrain</param>
        /// <param name="background">Whether to run the mesh generation in the background</param>
        public void SetTerrain(VoxelColumns<char> voxels, Bounds bounds, bool background = false) {
            if (Created || generating) throw new Exception("Terrains can only be set once");
            this.voxels = voxels;
            this.bounds = bounds;
            generator.Generate(voxels);
            if (background) generating = true;
            else {
                generator.handle.Complete();
                FinishSetTerrain();
            }
        }

        private unsafe void FinishSetTerrain() {
            while (generator.meshes.Length % VoxelsData.terrainCullingGroupSize != 0) generator.meshes.Add(default);
            threadGroups = generator.meshes.Length / VoxelsData.terrainCullingGroupSize;
            facesBuffer = new(GraphicsBuffer.Target.Structured, generator.faces.Length, sizeof(VoxelTerrainFace));
            facesBuffer.SetData(generator.faces.AsArray());
            meshesBuffer = new(GraphicsBuffer.Target.Structured, generator.meshes.Length, sizeof(VoxelMesh));
            meshesBuffer.SetData(generator.meshes.AsArray());
            commandsBuffer = new(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Counter | GraphicsBuffer.Target.Structured, generator.meshes.Length, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            GraphicsBuffer.IndirectDrawIndexedArgs[] commands = new GraphicsBuffer.IndirectDrawIndexedArgs[generator.meshes.Length];
            for (int i = 0; i < generator.meshes.Length; i++) commands[i] = new() { instanceCount = 1 };
            commandsBuffer.SetData(commands);
            generator.Dispose();
        }

        private void OnDestroy() {
            if (Created) {
                facesBuffer.Dispose();
                meshesBuffer.Dispose();
                commandsBuffer.Dispose();
                voxels.Dispose();
            }
        }

        private void Update() {
            if (generating && generator.handle.IsCompleted) {
                generating = false;
                generator.handle.Complete();
                FinishSetTerrain();
            }
        }
    }

}