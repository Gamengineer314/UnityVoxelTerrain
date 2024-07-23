# Optimized voxel terrain rendering in Unity

## Features :
- Fast, multithreadable greedy mesher (using Unity's jobs and Burst compiler)
- Frustrum culling in a compute shader
- Packed mesh data (8 bytes per rectangle + 4 bytes per rendered rectangle + 32 bytes per mesh)
- Indirect rendering (using Graphics.RenderPrimitivesIndexedIndirect) for minimal CPU-GPU interactions
- Random slight color variation for each voxel

If you find other improvements that could be made, don't hesitate to add them :)
