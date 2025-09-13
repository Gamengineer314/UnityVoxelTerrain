using System;
using Unity.Collections;
using UnityEngine;
using Voxels.Collections;

namespace Voxels.Rendering {
    
    public class VoxelTerrain : MonoBehaviour {
        [SerializeField] private TextAsset voxelsAsset;
        public int maxHorizontalSize = 64;
        public int mergeNormalsThreshold = 256;

        [NonSerialized] public Bounds bounds;
        [NonSerialized] public VoxelColumns<char> voxels;

        internal GraphicsBuffer facesBuffer { get; private set; }
        internal GraphicsBuffer meshesBuffer { get; private set; }
        internal int meshCount { get; private set; }

        private TerrainMeshGenerator generator;
        private bool generating;
        internal bool Created => facesBuffer != null;


        private unsafe void Awake() {
            generator = new(maxHorizontalSize, mergeNormalsThreshold, 1024);
            if (voxelsAsset != null) VoxelUtils.AsVoxels(voxelsAsset, out bounds, out voxels);
        }

        private void OnDestroy() {
            if (Created) {
                facesBuffer.Dispose();
                meshesBuffer.Dispose();
            }
            else generator.Dispose();
            if (voxels.Created) voxels.Dispose();
        }


        /// <summary>
        /// Complete terrain generation now
        /// </summary>
        public void CompleteGenerate() {
            if (Created) throw new InvalidOperationException("Can't call CompleteGenerate : terrain already generated");
            if (!voxels.Created) throw new InvalidOperationException("Can't call CompleteGenerate : voxels not set");
            if (!generating) generator.Generate(voxels);
            generator.handle.Complete();
            FinishGenerate();
            generating = false;
        }

        private unsafe void FinishGenerate() {
            while (generator.meshes.Length % VoxelData.terrainCullingGroupSize != 0) generator.meshes.Add(default);
            meshCount = generator.meshes.Length;
            facesBuffer = new(GraphicsBuffer.Target.Structured, generator.faces.Length, sizeof(VoxelTerrainFace));
            facesBuffer.SetData(generator.faces.AsArray());
            meshesBuffer = new(GraphicsBuffer.Target.Structured, generator.meshes.Length, sizeof(VoxelMesh));
            meshesBuffer.SetData(generator.meshes.AsArray());
            generator.Dispose();
        }

        private void Update() {
            if (voxels.Created && !generating && !Created) {
                generator.Generate(voxels);
                generating = true;
            }
            if (generating && generator.handle.IsCompleted) {
                generating = false;
                generator.handle.Complete();
                FinishGenerate();
            }
        }
    }

}