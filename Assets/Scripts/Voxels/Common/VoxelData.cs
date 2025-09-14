using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Voxels {

    /// <summary>
    /// Voxels global data
    /// </summary>
    public class VoxelData : MonoBehaviour {
        internal const int terrainCullingGroupSize = 64;
        internal const int maxFaceCount = 16384;

        internal static VoxelData Instance { get; private set; }

        [SerializeField] internal Material terrainMaterial;
        [SerializeField] internal ComputeShader terrainCulling;
        [SerializeField] private float quadsInterleaving = 0.05f; // Remove 1 pixel gaps between triangles

        public float QuadsInterleaving {
            get => quadsInterleaving;
            set {
                quadsInterleaving = value;
                terrainMaterial.SetFloat("quadsInterleaving", quadsInterleaving);
            }
        }

        // Global buffers
        internal GraphicsBuffer indicesBuffer { get; private set; } // All 16 bits indices
        internal GraphicsBuffer counterBuffer { get; private set; } // Buffer to store a counter

        // Shader IDs
        internal readonly int cameraPositionId = Shader.PropertyToID("cameraPosition");
        internal readonly int cameraFarPlaneId = Shader.PropertyToID("cameraFarPlane");
        internal readonly int cameraLeftPlaneId = Shader.PropertyToID("cameraLeftPlane");
        internal readonly int cameraRightPlaneId = Shader.PropertyToID("cameraRightPlane");
        internal readonly int cameraDownPlaneId = Shader.PropertyToID("cameraDownPlane");
        internal readonly int cameraUpPlaneId = Shader.PropertyToID("cameraUpPlane");
        internal readonly int meshesId = Shader.PropertyToID("meshes");
        internal readonly int commandsId = Shader.PropertyToID("commands");
        internal readonly int facesId = Shader.PropertyToID("faces");

        private void Awake() {
            if (Instance == null) Init();
        }

        private void OnDestroy() {
            if (Instance == this) Dispose();
        }


        /// <summary>
        /// Initialize global data
        /// </summary>
        internal void Init() {
            Instance = this;

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
            counterBuffer = new(GraphicsBuffer.Target.Structured, 1, sizeof(uint));

            terrainMaterial.SetFloat("quadsInterleaving", quadsInterleaving);
            terrainMaterial.SetFloat("seed", Random.value);
        }


        /// <summary>
        /// Dispose global data
        /// </summary>
        internal void Dispose() {
            indicesBuffer.Dispose();
            counterBuffer.Dispose();
        }
    }

}