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
                float4 blockData : TEXCOORD0; // x,y,z : block pos, w : light level
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

            struct Square {
                uint data1; // x (13b), z (13b)
                uint data2; // y (9b), width (6b), height (6b), normal (3b), color (8b)
            };

            StructuredBuffer<Square> squares;

            uniform float quadsInterleaving; // Size increase to remove small (1 pixel) gaps between triangles


            // Random value between 0 and 1
            uniform float seed;
            float random(uint3 block) {
                float3 vec = frac(float3(block) * 0.1031 + seed);
                vec += dot(vec, vec.yzx + 33.33);
                return frac((vec.x + vec.y) * vec.z);
            }


            v2f vert(uint vertexID: SV_VertexID) {
                // Get square
                uint squareID = vertexID >> 2;
                uint squareData1 = squares[squareID].data1;
                uint squareData2 = squares[squareID].data2;
                vertexID &= mask2Bits;

                // Unpack data
                float3 cubePos = float3(squareData1 & mask13Bits, squareData2 & mask9Bits, squareData1 >> 13);
                uint normalID = (squareData2 >> 21) & mask3Bits;
                float width = ((squareData2 >> 9) & mask6Bits) + 1;
                float height = ((squareData2 >> 15) & mask6Bits) + 1;
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
                o.blockData = float4(pos[0] - normal[0] * 0.5f, pos[1] - normal[1] * 0.5f, pos[2] - normal[2] * 0.5f, faceLightLevels[normalID]);
                o.color = colors[squareData2 >> 24];
                return o;
            }


            fixed4 frag(v2f i) : SV_Target {
                uint3 blockPos = (uint3)i.blockData.xyz;
                float lightLevel = i.blockData.w;
                fixed4 color = i.color;
                color *= lightLevel / 15; // Light (depending on face directions, better lighting could be added)
                color *= 1 + color.w * ((round(random(blockPos) * discretization) / discretization) - 0.5); // Random slight color variation
                color.w = 1;
                return color;
            }
            ENDCG
        }
    }
}
