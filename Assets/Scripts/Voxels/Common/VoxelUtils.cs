using System;
using System.IO;
using Unity.Collections;
using UnityEngine;
using Voxels.Collections;

namespace Voxels {

    public static class VoxelUtils {
        /// <summary>
        /// Get bounds and voxel columns from an asset.
        /// The voxels reference the asset bytes.
        /// </summary>
        /// <param name="asset">The asset</param>
        /// <param name="bounds">The bounds</param>
        /// <param name="voxels">The voxels</param>
        public static unsafe void AsVoxels<T>(TextAsset asset, out Bounds bounds, out VoxelColumns<T> voxels) where T : unmanaged {
            byte[] bytes = asset.bytes;
            int offset = 0;

            bounds = new(
                new Vector3(
                    BitConverter.ToSingle(bytes),
                    BitConverter.ToSingle(bytes, sizeof(float)),
                    BitConverter.ToSingle(bytes, 2 * sizeof(float))),
                new Vector3(
                    BitConverter.ToSingle(bytes, 3 * sizeof(float)),
                    BitConverter.ToSingle(bytes, 4 * sizeof(float)),
                    BitConverter.ToSingle(bytes, 5 * sizeof(float))
                )
            );
            offset += 6 * sizeof(float);

            int sizeX = BitConverter.ToInt32(bytes, offset);
            int sizeZ = BitConverter.ToInt32(bytes, offset + sizeof(int));
            int nVoxels = BitConverter.ToInt32(bytes, offset + 2 * sizeof(int));
            offset += 3 * sizeof(int);
            NativeArray<Voxel<T>> voxelsRef = asset.GetData<byte>()
                .GetSubArray(offset, nVoxels * sizeof(Voxel<T>))
                .Reinterpret<Voxel<T>>(1);
            offset += nVoxels * sizeof(Voxel<T>);
            NativeArray<int> indicesRef = asset.GetData<byte>()
                .GetSubArray(offset, (sizeX * sizeZ + 1) * sizeof(int))
                .Reinterpret<int>(1);
            voxels = new(sizeX, sizeZ, voxelsRef, indicesRef);
        }


        /// <summary>
        /// Write bounds and voxel columns to an file
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        /// <param name="bounds">The bounds</param>
        /// <param name="voxels">The voxels</param>
        public static unsafe void WriteVoxels<T>(string filePath, Bounds bounds, VoxelColumns<T> voxels) where T : unmanaged {
            using FileStream file = File.OpenWrite(filePath);

            file.Write(BitConverter.GetBytes(bounds.center.x));
            file.Write(BitConverter.GetBytes(bounds.center.y));
            file.Write(BitConverter.GetBytes(bounds.center.z));
            file.Write(BitConverter.GetBytes(bounds.size.x));
            file.Write(BitConverter.GetBytes(bounds.size.y));
            file.Write(BitConverter.GetBytes(bounds.size.z));

            file.Write(BitConverter.GetBytes(voxels.sizeX));
            file.Write(BitConverter.GetBytes(voxels.sizeZ));
            file.Write(BitConverter.GetBytes(voxels.voxels.Length));
            file.Write(voxels.voxels.Reinterpret<byte>(sizeof(Voxel<T>)).ToArray());
            file.Write(voxels.startIndices.Reinterpret<byte>(sizeof(int)).ToArray());
        }
    }

}