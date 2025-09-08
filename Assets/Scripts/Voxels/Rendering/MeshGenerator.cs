using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;
using Voxels.Collections;

namespace Voxels.Rendering {

    /// <summary>
    /// Mesh generator from voxel collections
    /// </summary>
    internal abstract class MeshGenerator<T, TMerger>
        where T : unmanaged
        where TMerger : unmanaged, IMerger<T> {
        private readonly TMerger merger;
        private readonly int maxHorizontalSize;
        private readonly int mergeNormalsThreshold;
        private readonly int jobHorizontalSize;
        private readonly bool seenFromAbove;
        
        public NativeList<VoxelMesh> meshes;
        public NativeList<VoxelFace> faces;
        protected int offset; // Face indices offset

        /// <summary>
        /// Create a mesh generator
        /// </summary>
        /// <param name="maxHorizontalSize">
        /// Max horizontal size for individual meshes.
        /// Multiple meshes can be generated from the same voxel collection if it exceeds this size.
        /// The generator will perform best if [maxHorizontalSize] is a multiple of 64.
        /// </param>
        /// <param name="mergeNormalsThreshold">
        /// Number of faces below which meshes at the same position with different normals must be merged together.
        /// Objects smaller than the threshold will use a single mesh but can't be partially culled based on normals.
        /// </param>
        /// <param name="jobHorizontalSize">
        /// Max horizontal size a generator job can process.
        /// Multiple jobs will be used to generate the meshes in parallel if a voxel collection exceeds this size.
        /// The generator will perform best if [jobHorizontalSize] is a multiple of [maxHorizontalSize]
        /// </param>
        /// <param name="seenFromAbove">
        /// Whether objects can only be seen from above and inside its horizontal bounds.
        /// This allows to remove faces below the objects and on their sides.
        /// </param>
        public MeshGenerator(
            TMerger merger,
            int maxHorizontalSize = 64,
            int mergeNormalsThreshold = 256,
            int jobHorizontalSize = int.MaxValue,
            bool seenFromAbove = false
        ) {
            if (mergeNormalsThreshold > VoxelsData.maxFaceCount) mergeNormalsThreshold = VoxelsData.maxFaceCount;
            this.merger = merger;
            this.maxHorizontalSize = maxHorizontalSize;
            this.mergeNormalsThreshold = mergeNormalsThreshold;
            this.jobHorizontalSize = jobHorizontalSize;
            this.seenFromAbove = seenFromAbove;
            meshes = new(Allocator.Persistent);
            faces = new(Allocator.Persistent);
            offset = 0;
        }

        public virtual void Dispose() {
            meshes.Dispose();
            faces.Dispose();
        }


        /// <summary>
        /// Clear generated meshes and faces to free memory
        /// <param name="keepOffset">Whether to keep the next face index as an offset for future meshes or reset it to 0</param>
        /// </summary>
        public virtual void Clear(bool keepOffset = false) {
            if (keepOffset) offset += faces.Length;
            else offset = 0;
            meshes.Clear();
            meshes.Capacity = 1;
            faces.Clear();
            faces.Capacity = 1;
        }


        /// <summary>
        /// Generate meshes from a voxel collection
        /// </summary>
        /// <param name="voxels">The voxels</param>
        public void Generate(VoxelColumns<T> voxels) {
            Native2DArray<MeshGeneratorJob> jobs = new(
                (int)math.ceil((float)voxels.sizeX / jobHorizontalSize),
                (int)math.ceil((float)voxels.sizeZ / jobHorizontalSize),
                Allocator.Temp, NativeArrayOptions.UninitializedMemory
            );
            Native2DArray<JobHandle> handles = new(jobs.sizeX, jobs.sizeY, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            // Create jobs
            for (int jobZ = 0; jobZ < jobs.sizeY; jobZ++) {
                for (int jobX = 0; jobX < jobs.sizeX; jobX++) {
                    int jobStartX = jobX * jobHorizontalSize, jobStartZ = jobZ * jobHorizontalSize;
                    jobs[jobX, jobZ] = new(voxels,
                        jobStartX, jobStartZ,
                        math.min(jobHorizontalSize, voxels.sizeX - jobStartX),
                        math.min(jobHorizontalSize, voxels.sizeZ - jobStartZ),
                        merger, maxHorizontalSize, mergeNormalsThreshold, seenFromAbove
                    );
                    handles[jobX, jobZ] = jobs[jobX, jobZ].Schedule();
                }
            }

            // Run jobs and process results
            JobHandle.CompleteAll(handles.Array);
            ProcessResults(voxels, jobs);
            foreach (MeshGeneratorJob job in jobs.Array) job.Dispose();
        }


        /// <summary>
        /// Add results of a mesh generation to the generated arrays
        /// </summary>
        /// <param name="jobs">Completed generator jobs</param>
        protected abstract void ProcessResults(VoxelColumns<T> voxels, Native2DArray<MeshGeneratorJob> jobs);



        [BurstCompile]
        protected unsafe struct MeshGeneratorJob : IJob {
            private const int chunkSize = 64;

            [ReadOnly] private readonly VoxelColumns<T> voxels; // All voxels
            private readonly int startX, startZ; // Start of the part to generate
            private readonly int sizeX, sizeZ; // Size of the part to generate
            private readonly TMerger merger; // Merge identifier
            private readonly int maxHorizontalSize;
            private readonly int mergeNormalsThreshold;
            private readonly bool seenFromAbove;

            public NativeList<MeshFace> faces; // Faces
            public NativeList<LinkedMeshPart> meshes; // Linked meshes
            public NativeList<LinkedMeshHead> heads; // Linked mesh heads

            private int3 currentChunkStart;
            private int3 currentChunkSize;
            private UnsafeArray<ulong> rows;
            private UnsafeArray<bool2> sides;
            private UnsafeArray<ulong> planes;
            private UnsafeArray<int> idToIndex;
            private UnsafeArray<int> indexToId;
            private int idCount;
            private fixed int currentHeads[6];

            public MeshGeneratorJob(
                VoxelColumns<T> voxels,
                int startX, int startZ, int sizeX, int sizeZ,
                TMerger merger,
                int maxHorizontalSize,
                int mergeNormalsThreshold,
                bool seenFromAbove
            ) {
                this.voxels = voxels;
                this.startX = startX;
                this.startZ = startZ;
                this.sizeX = sizeX;
                this.sizeZ = sizeZ;
                this.merger = merger;
                this.maxHorizontalSize = maxHorizontalSize;
                this.mergeNormalsThreshold = mergeNormalsThreshold;
                this.seenFromAbove = seenFromAbove;
                faces = new(Allocator.TempJob);
                meshes = new(Allocator.TempJob);
                heads = new(Allocator.TempJob);

                currentChunkStart = default;
                currentChunkSize = default;
                rows = default;
                sides = default;
                planes = default;
                idToIndex = default;
                indexToId = default;
                idCount = 0;
            }

            public void Dispose() {
                faces.Dispose();
                meshes.Dispose();
                heads.Dispose();
            }


            public void Execute() {
                // Find IDs and y ranges
                idToIndex = new UnsafeArray<int>(256, Allocator.Temp, NativeArrayOptions.ClearMemory);
                indexToId = new UnsafeArray<int>(256, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                int chunksPerMesh = (int)math.ceil((float)maxHorizontalSize / chunkSize);
                int nMeshesX = (int)math.ceil((float)sizeX / maxHorizontalSize);
                int nMeshesZ = (int)math.ceil((float)sizeZ / maxHorizontalSize);
                Native2DArray<int2> yRanges = new(nMeshesX * chunksPerMesh, nMeshesZ * chunksPerMesh, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int meshZ = 0; meshZ < nMeshesZ; meshZ++) {
                    int meshStartZ = startZ + meshZ * maxHorizontalSize;
                    int meshEndZ = math.min(meshStartZ + maxHorizontalSize, startZ + sizeZ);
                    for (int meshX = 0; meshX < nMeshesX; meshX++) {
                        int meshStartX = startX + meshX * maxHorizontalSize;
                        int meshEndX = math.min(meshStartX + maxHorizontalSize, startX + sizeX);
                        int nChunksX = (int)math.ceil((float)(meshEndX - meshStartX) / chunkSize);
                        int nChunksZ = (int)math.ceil((float)(meshEndZ - meshStartZ) / chunkSize);
                        for (int chunkZ = 0; chunkZ < nChunksZ; chunkZ++) {
                            int chunkStartZ = meshStartZ + chunkZ * chunkSize;
                            int chunkEndZ = math.min(chunkStartZ + chunkSize, startZ + sizeZ);
                            for (int chunkX = 0; chunkX < nChunksX; chunkX++) {
                                int chunkStartX = meshStartX + chunkX * chunkSize;
                                int chunkEndX = math.min(chunkStartX + chunkSize, startX + sizeX);
                                int min = int.MaxValue, max = int.MinValue;
                                for (int z = chunkStartZ; z < chunkEndZ; z++) {
                                    for (int x = chunkStartX; x < chunkEndX; x++) {
                                        min = math.min(min, voxels.GetMin(x, z));
                                        max = math.max(max, voxels.GetMax(x, z));
                                        foreach (Voxel<T> voxel in voxels.GetColumn(x, z)) {
                                            char id = merger.MergeIdentifier(voxel.data);
                                            if (idToIndex[id] == 0) {
                                                idToIndex[id] = idCount;
                                                indexToId[idCount] = id;
                                                idCount++;
                                            }
                                        }
                                    }
                                }
                                yRanges[meshX * chunksPerMesh + chunkX, meshZ * chunksPerMesh + chunkZ] = new(min, max);
                            }
                        }
                    }
                }

                // Generate all meshes
                rows = new UnsafeArray<ulong>(chunkSize * chunkSize * 3, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                sides = new UnsafeArray<bool2>(chunkSize * chunkSize * 3, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                planes = new UnsafeArray<ulong>(chunkSize * chunkSize * idCount * 6, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                for (int meshZ = 0; meshZ < nMeshesZ; meshZ++) {
                    int meshStartZ = startZ + meshZ * maxHorizontalSize;
                    int meshEndZ = math.min(meshStartZ + maxHorizontalSize, startZ + sizeZ);
                    for (int meshX = 0; meshX < nMeshesX; meshX++) {
                        int meshStartX = startX + meshX * maxHorizontalSize;
                        int meshEndX = math.min(meshStartX + maxHorizontalSize, startX + sizeX);
                        int nChunksX = (int)math.ceil((float)(meshEndX - meshStartX) / chunkSize);
                        int nChunksZ = (int)math.ceil((float)(meshEndZ - meshStartZ) / chunkSize);

                        // Initialize mesh
                        int startFace = faces.Length;
                        int startPart = meshes.Length;
                        int startHead = heads.Length;
                        for (int i = 0; i < 6; i++) {
                            currentHeads[i] = heads.Length;
                            heads.Add(new(-1, 0, int.MaxValue, int.MinValue));
                        }

                        // Generate all chunks
                        for (int chunkZ = 0; chunkZ < nChunksZ; chunkZ++) {
                            currentChunkStart.z = meshStartZ + chunkZ * chunkSize;
                            currentChunkSize.z = math.min(chunkSize, meshEndZ - currentChunkStart.z);
                            for (int chunkX = 0; chunkX < nChunksX; chunkX++) {
                                currentChunkStart.x = meshStartX + chunkX * chunkSize;
                                currentChunkSize.x = math.min(chunkSize, meshEndX - currentChunkStart.x);
                                int2 yRange = yRanges[meshX * chunksPerMesh + chunkX, meshZ * chunksPerMesh + chunkZ];
                                int nChunksY = (int)math.ceil((float)(yRange.y - yRange.x) / chunkSize);
                                for (int chunkY = 0; chunkY < nChunksY; chunkY++) {
                                    currentChunkStart.y = yRange.x + chunkY * chunkSize;
                                    currentChunkSize.y = math.min(chunkSize, yRange.y + 1 - currentChunkStart.y);

                                    // Generate one chunk
                                    rows.Clear();
                                    sides.Clear();
                                    planes.Clear();
                                    GenerateBinarySolidBlocks();
                                    GenerateBinaryPlanes();
                                    GenerateOptimizedMesh();
                                }
                            }
                        }

                        // Merge parts if too small and remove empty meshes
                        if (faces.Length - startFace < mergeNormalsThreshold) {
                            meshes[startPart] = new(startFace, faces.Length, -1);
                            int3 min = int.MaxValue, max = int.MinValue;
                            for (int i = 0; i < 6; i++) {
                                int3 partMin = heads[heads.Length - i - 1].min, partMax = heads[heads.Length - i - 1].max;
                                min = math.select(min, partMin, partMin < min);
                                max = math.select(max, partMax, partMax > max);
                            }
                            heads[^6] = new(startPart, faces.Length - startFace, min, max);
                            meshes.Length = startPart + 1;
                            heads.Length -= 5;
                        }
                        else {
                            for (int i = heads.Length - 1; i >= startHead; i--) {
                                if (heads[i].head == -1) heads.RemoveAtSwapBack(i);
                            }
                        }
                    }
                }

                yRanges.Dispose();
                idToIndex.Dispose();
                indexToId.Dispose();
                rows.Dispose();
                sides.Dispose();
                planes.Dispose();
            }


            // rows: bit rows containing 1 if the block is solid, 0 otherwise
            // sides: 2 bools, first is true if the block before the row is solid, 0 otherwise, second is true if the block after the row is solid, 0 otherwise
            // rows and sides contain chunkSize * chunkSize elements for each axis (x, z, y)
            private void GenerateBinarySolidBlocks() {
                // Rows and y sides
                for (int z = 0; z < currentChunkSize.z; z++) {
                    for (int x = 0; x < currentChunkSize.x; x++) {
                        bool2 ySide = new(false, false);
                        foreach (Voxel<T> voxel in voxels.GetColumn(currentChunkStart.x + x, currentChunkStart.z + z)) {
                            int y = voxel.y - currentChunkStart.y;
                            if (y >= 0 && y < currentChunkSize.y) {
                                rows[y + z * chunkSize] |= 1UL << x; // x
                                rows[x + z * chunkSize + chunkSize * chunkSize] |= 1UL << y; // y
                                rows[y + x * chunkSize + 2 * chunkSize * chunkSize] |= 1UL << z; // z
                            }
                            else if (y == -1) ySide.x = true;
                            else if (y == chunkSize) ySide.y = true;
                        }
                    }
                }

                // x and z sides
                if (currentChunkStart.x > 0) {
                    for (int z = 0; z < currentChunkSize.z; z++) {
                        foreach (Voxel<T> voxel in voxels.GetColumn(currentChunkStart.x - 1, currentChunkStart.z + z)) {
                            int y = voxel.y - currentChunkStart.y;
                            if (y >= 0 && y < currentChunkSize.y) sides[y + z * chunkSize].x = true;
                        }
                    }
                }
                if (currentChunkStart.x + currentChunkSize.x < voxels.sizeX) {
                    for (int z = 0; z < currentChunkSize.z; z++) {
                        foreach (Voxel<T> voxel in voxels.GetColumn(currentChunkStart.x + currentChunkSize.x, currentChunkStart.z + z)) {
                            int y = voxel.y - currentChunkStart.y;
                            if (y >= 0 && y < currentChunkSize.y) sides[y + z * chunkSize].y = true;
                        }
                    }
                }
                if (currentChunkStart.z > 0) {
                    for (int x = 0; x < currentChunkSize.x; x++) {
                        foreach (Voxel<T> voxel in voxels.GetColumn(currentChunkStart.x + x, currentChunkStart.z - 1)) {
                            int y = voxel.y - currentChunkStart.y;
                            if (y >= 0 && y < currentChunkSize.y) sides[y + x * chunkSize + 2 * chunkSize * chunkSize].x = true;
                        }
                    }
                }
                if (currentChunkStart.z + currentChunkSize.z < voxels.sizeZ) {
                    for (int x = 0; x < currentChunkSize.x; x++) {
                        foreach (Voxel<T> voxel in voxels.GetColumn(currentChunkStart.x + x, currentChunkStart.z + currentChunkSize.z)) {
                            int y = voxel.y - currentChunkStart.y;
                            if (y >= 0 && y < currentChunkSize.y) sides[y + x * chunkSize + 2 * chunkSize * chunkSize].y = true;
                        }
                    }
                }
            }


            // planes: 64 bits rows containing 1 if the face must be rendered, 0 otherwise
            // planes contains chunkSize (rows in one plane) * chunkSize (planes in one direction) * nbrIDs * 6 (x+, z+, y+, x-, z-, y-)
            private void GenerateBinaryPlanes() {
                GenerateAxisBinaryPlanes(0);
                GenerateAxisBinaryPlanes(1);
                GenerateAxisBinaryPlanes(2);
            }


            // Generate binary planes for one axis
            private void GenerateAxisBinaryPlanes(int axis) {
                int3 beforeX = currentChunkStart;
                for (int y = 0; y < currentChunkSize[VoxelNormals.HeightAxis(axis)]; y++) {
                    int3 pos = beforeX;
                    for (int x = 0; x < currentChunkSize[VoxelNormals.WidthAxis(axis)]; x++) {
                        ulong row = rows[x + y * chunkSize + axis * chunkSize * chunkSize];
                        bool2 side = sides[x + y * chunkSize + axis * chunkSize * chunkSize];

                        // Find faces to render in positive direction and add them to planes
                        ulong shiftedRow = row >> 1;
                        if (side.y) shiftedRow |= 1UL << 63;
                        ulong faceRow = row & ~shiftedRow;
                        while (faceRow != 0) {
                            int depth = math.tzcnt(faceRow);
                            faceRow &= ~(1UL << depth);
                            int3 posDepth = pos;
                            posDepth[axis] += depth;
                            if (seenFromAbove) { // Remove useless faces
                                int3 next = posDepth;
                                next[axis]++;
                                if (axis == 0 && next.x >= voxels.sizeX) continue;
                                if (axis == 2 && next.z >= voxels.sizeZ) continue;
                                if (next.y < voxels.GetMin(next.xz)) continue;
                            }
                            char id = merger.MergeIdentifier(voxels.GetVoxel(posDepth));
                            planes[y + depth * chunkSize + idToIndex[id] * chunkSize * chunkSize + 2 * axis * chunkSize * chunkSize * idCount] |= 1UL << x;
                        }

                        // Find faces to render in negative direction and add them to planes
                        shiftedRow = row << 1;
                        if (side.x) shiftedRow |= 1;
                        faceRow = row & ~shiftedRow;
                        while (faceRow != 0) {
                            int depth = math.tzcnt(faceRow);
                            faceRow &= ~(1UL << depth);
                            int3 posDepth = pos;
                            posDepth[axis] += depth;
                            if (seenFromAbove) { // Remove useless faces
                                int3 next = posDepth;
                                next[axis]--;
                                if (axis == 0 && next.x < 0) continue;
                                if (axis == 2 && next.z < 0) continue;
                                if (next.y < voxels.GetMin(next.xz)) continue;
                            }
                            char id = merger.MergeIdentifier(voxels.GetVoxel(posDepth));
                            planes[y + depth * chunkSize + idToIndex[id] * chunkSize * chunkSize + (2 * axis + 1) * chunkSize * chunkSize * idCount] |= 1UL << x;
                        }

                        pos[VoxelNormals.WidthAxis(axis)]++;
                    }
                    beforeX[VoxelNormals.HeightAxis(axis)]++;
                }
            }


            // Generate the mesh for each plane
            private void GenerateOptimizedMesh() {
                GenerateNormalOptimizedMesh(VoxelNormal.XPositive);
                GenerateNormalOptimizedMesh(VoxelNormal.ZPositive);
                GenerateNormalOptimizedMesh(VoxelNormal.YPositive);
                GenerateNormalOptimizedMesh(VoxelNormal.XNegative);
                GenerateNormalOptimizedMesh(VoxelNormal.ZNegative);
                GenerateNormalOptimizedMesh(VoxelNormal.YNegative);
            }


            // Generate the mesh for a normal
            private void GenerateNormalOptimizedMesh(VoxelNormal normal) {
                // Generate faces for each ID and depth
                int startFace = faces.Length;
                int3 min = int.MaxValue;
                int3 max = int.MinValue;
                for (int i = 0; i < idCount; i++) {
                    for (int depth = 0; depth < currentChunkSize[VoxelNormals.Axis(normal)]; depth++) {
                        GenerateOptimizedPlane(normal, depth, i, ref min, ref max);
                    }
                }

                // Add mesh part
                if (faces.Length > startFace) {
                    int headIndex = currentHeads[(int)normal];
                    int3 prevMin = heads[headIndex].min, prevMax = heads[headIndex].max;
                    int3 newMin = math.select(prevMin, min, min < prevMin), newMax = math.select(prevMax, max, max > prevMax);
                    int faceCount = heads[headIndex].faceCount + faces.Length - startFace;
                    if (faceCount > VoxelsData.maxFaceCount) { // Split mesh
                        meshes.Add(new(startFace, faces.Length - (faceCount - VoxelsData.maxFaceCount), heads[headIndex].head));
                        heads[headIndex] = new(meshes.Length - 1, VoxelsData.maxFaceCount, newMin, newMax);
                        meshes.Add(new(faces.Length - (faceCount - VoxelsData.maxFaceCount), faces.Length, -1));
                        currentHeads[(int)normal] = heads.Length;
                        heads.Add(new(meshes.Length - 1, faceCount - VoxelsData.maxFaceCount, newMin, newMax));
                    }
                    else {
                        meshes.Add(new(startFace, faces.Length, heads[headIndex].head));
                        heads[headIndex] = new(meshes.Length - 1, faceCount, newMin, newMax);
                    }
                }
            }


            // Generate the mesh for a plane
            private void GenerateOptimizedPlane(VoxelNormal normal, int depth, int idIndex, ref int3 min, ref int3 max) {
                int startIndex = (int)normal * chunkSize * chunkSize * idCount + idIndex * chunkSize * chunkSize + depth * chunkSize;
                int3 beforeX = currentChunkStart;
                beforeX[VoxelNormals.Axis(normal)] += depth;
                for (int y = 0; y < currentChunkSize[VoxelNormals.HeightAxis(normal)]; y++) {
                    int3 pos = beforeX;
                    ulong row = planes[startIndex + y];
                    int x = math.tzcnt(row);
                    pos[VoxelNormals.WidthAxis(normal)] += x;
                    while (x < currentChunkSize[VoxelNormals.WidthAxis(normal)]) {
                        int width = math.tzcnt(~(row >> x)); // Expand in x
                        ulong checkMask = (row << (64 - width - x)) >> (64 - width - x);
                        ulong deleteMask = ~checkMask;

                        int height = 1;
                        while (y + height < currentChunkSize[VoxelNormals.Axis(normal)]) { // Expand in y
                            ref ulong nextRow = ref planes[startIndex + y + height];
                            if ((nextRow & checkMask) != checkMask) break;
                            nextRow &= deleteMask;
                            height++;
                        }

                        // Add the face
                        int3 faceMin = pos;
                        if (VoxelNormals.Positive(normal)) faceMin[VoxelNormals.Axis(normal)]++;
                        int3 faceMax = pos;
                        faceMax[VoxelNormals.WidthAxis(normal)] += width;
                        faceMax[VoxelNormals.HeightAxis(normal)] += height;
                        min = math.select(min, faceMin, faceMin < min);
                        max = math.select(max, faceMax, faceMax > max);
                        voxels.GetVoxel(faceMin);
                        faces.Add(new(pos, (uint)width, (uint)height, normal));

                        int prevX = x;
                        x += width;
                        x += math.tzcnt(row >> x);
                        pos[VoxelNormals.WidthAxis(normal)] += x - prevX;
                    }
                    beforeX[VoxelNormals.HeightAxis(normal)]++;
                }
            }
        }


        protected readonly struct MeshFace {
            public readonly int3 pos; // Coordinates of the min voxel in the face
            private readonly uint data; // width (8b), height (8b), normal (3b)

            public MeshFace(int3 pos, uint width, uint height, VoxelNormal normal) {
                this.pos = pos;
                data = width | (height << 8) | ((uint)normal << 16);
            }

            public uint Width => data & 0b11111111;
            public uint Height => (data >> 8) & 0b11111111;
            public VoxelNormal Normal => (VoxelNormal)(data >> 16);
        }


        protected readonly struct LinkedMeshPart {
            public readonly int startFace, endFace; // Indices of the faces in this part of the mesh
            public readonly int next; // Index of the next part

            public LinkedMeshPart(int startFace, int endFace, int next) {
                this.startFace = startFace;
                this.endFace = endFace;
                this.next = next;
            }
        }


        protected readonly struct LinkedMeshHead {
            public readonly int head; // Index of the first part
            public readonly int faceCount;
            public readonly int3 min, max; // Current bounds

            public LinkedMeshHead(int head, int faceCount, int3 min, int3 max) {
                this.head = head;
                this.faceCount = faceCount;
                this.min = min;
                this.max = max;
            }
        }
    }



    /// <summary>
    /// Struct that allows merging faces for a greedy mesher
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal interface IMerger<T> {
        /// <summary>
        /// Returns an identifier for a voxel.
        /// Faces with the same identifier can be merged.
        /// The identifier can't be 0.
        /// </summary>
        char MergeIdentifier(T voxel);
    }

    internal struct IdentityMerger : IMerger<char> {
        public readonly char MergeIdentifier(char id) => id;
    }



    [BurstCompile]
    internal class TerrainMeshGenerator : MeshGenerator<char, IdentityMerger> {
        public TerrainMeshGenerator(
            int maxHorizontalSize = 64,
            int mergeNormalsThreshold = 256,
            int jobHorizontalSize = int.MaxValue
        ) : base(default, maxHorizontalSize, mergeNormalsThreshold, jobHorizontalSize, true) { }

        protected override void ProcessResults(VoxelColumns<char> voxels, Native2DArray<MeshGeneratorJob> jobs) {
            ProcessResults(in voxels, in jobs, ref faces, ref meshes, offset);
        }

        [BurstCompile]
        private static void ProcessResults(
            in VoxelColumns<char> voxels,
            in Native2DArray<MeshGeneratorJob> jobs,
            ref NativeList<VoxelFace> faces,
            ref NativeList<VoxelMesh> meshes,
            int offset
        ) {
            foreach (MeshGeneratorJob job in jobs.Array) {
                foreach (LinkedMeshHead head in job.heads) {
                    int startFace = faces.Length + offset;
                    VoxelNormal normal = VoxelNormal.None;
                    int i = head.head;
                    while (i != -1) {
                        LinkedMeshPart part = job.meshes[i];
                        for (int j = part.startFace; j < part.endFace; j++) {
                            MeshFace face = job.faces[j];
                            int3 pos = face.pos;
                            if (VoxelNormals.Positive(face.Normal)) pos[VoxelNormals.Axis(face.Normal)]++;
                            faces.Add(new((uint3)pos, face.Width, face.Height, face.Normal, voxels.GetVoxel(face.pos)));
                            if (normal == VoxelNormal.None) normal = face.Normal;
                            else if (normal != face.Normal) normal = VoxelNormal.Any;
                        }
                        i = part.next;
                    }
                    float3 min = head.min, max = head.max;
                    meshes.Add(new((min + max) / 2, (max - min) / 2, normal, (uint)(faces.Length - startFace), (uint)startFace));
                }
            }
        }
    }

}