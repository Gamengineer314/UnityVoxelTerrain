using System;
using System.Collections.Generic;
using Unity.Mathematics;
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
        internal bool Created => facesBuffer != null;


        public unsafe void SetTerrain(VoxelColumns<char> terrain, Bounds bounds) {
            if (Created) throw new Exception("Terrains can only be set once");
            this.bounds = bounds;
            TerrainMeshGenerator generator = new(maxHorizontalSize, mergeNormalsThreshold, 1024);
            generator.Generate(terrain);
            while (generator.meshes.Length % VoxelsData.terrainCullingGroupSize != 0) generator.meshes.Add(default);
            threadGroups = generator.meshes.Length / VoxelsData.terrainCullingGroupSize;
            facesBuffer = new(GraphicsBuffer.Target.Structured, generator.faces.Length, sizeof(VoxelFace));
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
            facesBuffer?.Dispose();
            meshesBuffer?.Dispose();
            commandsBuffer?.Dispose();
        }
    }

}