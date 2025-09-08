Shader "Unlit/VoxelShader" {
    SubShader {
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
            #include "ShaderParams.cginc"


            struct v2f {
                float4 vertex : SV_POSITION;
                float4 voxelData : TEXCOORD0; // x,y,z : voxel pos, w : light level
                nointerpolation fixed4 color : COLOR; // x,y,z: color, w: random variation ammount
            };

            #define mask2Bits 3u             // 0b11
            #define mask3Bits 7u             // 0b111
            #define mask4Bits 15u            // 0b1111
            #define mask6Bits 63u            // 0b111111
            #define mask9Bits 511u           // 0b1111111111
            #define mask13Bits 8191u         // 0b1111111111111

            static const uint faceLightLevels[] = {
                12, // x+
                12, // x-
                15, // y+
                9,  // y-
                12, // z+
                12, // z-
            };

            struct Face {
                uint data1; // x (13b), z (13b)
                uint data2; // y (9b), width (6b), height (6b), normal (3b), color (8b)
            };

            StructuredBuffer<Face> faces;

            uniform float quadsInterleaving; // Remove 1 pixel gaps between triangles


            // Random value between 0 and 1
            uniform float seed;
            float random(uint3 pos) {
                float3 vec = frac(float3(pos) * 0.1031 + seed);
                vec += dot(vec, vec.yzx + 33.33);
                return frac((vec.x + vec.y) * vec.z);
            }


            v2f vert(uint vertexID: SV_VertexID) {
                // Get face
                uint faceID = vertexID >> 2;
                uint faceData1 = faces[faceID].data1;
                uint faceData2 = faces[faceID].data2;
                vertexID &= mask2Bits;

                // Unpack data
                float3 cubePos = float3(faceData1 & mask13Bits, faceData2 & mask9Bits, faceData1 >> 13);
                uint normalID = (faceData2 >> 21) & mask3Bits;
                float width = ((faceData2 >> 9) & mask6Bits) + 1;
                float height = ((faceData2 >> 15) & mask6Bits) + 1;
                uint normalAxis = normalID >> 1;

                // Position
                float pos[3] = { cubePos.x, cubePos.y, cubePos.z };
                float interleaving = distance(_WorldSpaceCameraPos, cubePos) * quadsInterleaving * 0.001f;
                pos[1u & ~normalAxis] += -interleaving + ((uint(vertexID) & 1u) ^ uint(normalAxis != 0) ^ (normalID & 1u)) * (width + 2 * interleaving);
                pos[2u & ~normalAxis] += -interleaving + (vertexID >> 1) * (height + 2 * interleaving);
                float normal[3] = { 0, 0, 0 };
                normal[normalAxis] = -2 * float(normalID & 1u) + 1;

                // Output
                v2f o;
                o.vertex = mul(UNITY_MATRIX_VP, float4(pos[0], pos[1], pos[2], 1));
                o.voxelData = float4(pos[0] - normal[0] * 0.5f, pos[1] - normal[1] * 0.5f, pos[2] - normal[2] * 0.5f, faceLightLevels[normalID]);
                o.color = colors[faceData2 >> 24];
                return o;
            }


            fixed4 frag(v2f i) : SV_Target {
                uint3 pos = (uint3)i.voxelData.xyz;
                float lightLevel = i.voxelData.w;
                fixed4 color = i.color;
                color *= lightLevel / 15; // Light (depending on face directions, better lighting could be added)
                color *= 1 + color.w * ((round(random(pos) * discretization) / discretization) - 0.5); // Random slight color variation
                color.w = 1;
                return color;
            }
            ENDCG
        }
    }
}
