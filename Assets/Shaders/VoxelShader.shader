Shader "Unlit/VoxelShader"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawIndexedArgs
            #include "UnityIndirect.cginc"
            #include "ShaderParams.cginc"


            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 blockData : TEXCOORD0; // x,y,z : block pos, w : light level
                nointerpolation fixed4 color : COLOR; // x,y,z: color, w: random variation ammount
            };

            #define mask3Bits 7             // 0b111
            #define mask4Bits 15            // 0b1111
            #define mask6Bits 63            // 0b111111
            #define mask9Bits 511           // 0b1111111111
            #define mask13Bits 8191         // 0b1111111111111

            static const float3 cubeVertices[] = {
                float3(1,-1,-1), float3(1,1,-1), float3(1,1,1), float3(1,-1,1),      // x+
                float3(1,-1,1), float3(1,1,1), float3(-1,1,1), float3(-1,-1,1),      // z+
                float3(-1,1,-1), float3(-1,1,1), float3(1,1,1), float3(1,1,-1),      // y+
                float3(-1,-1,1), float3(-1,1,1), float3(-1,1,-1), float3(-1,-1,-1),  // x-
                float3(-1,-1,-1), float3(-1,1,-1), float3(1,1,-1), float3(1,-1,-1),  // z-
                float3(1,-1,-1), float3(1,-1,1), float3(-1,-1,1), float3(-1,-1,-1),  // y-
            };

            static const float3 normals[] = {
                float3(1,0,0),  // x+
                float3(0,0,1),  // z+
                float3(0,1,0),  // y+
                float3(-1,0,0), // x-
                float3(0,0,-1), // z-
                float3(0,-1,0), // y-
            };

            static const uint faceLightLevels[] = {
                12, // x+
                12, // z+
                15, // y+
                12, // x-
                12, // z-
                9,  // y-
            };

            ByteAddressBuffer squares; // x (13b), sizeX (6b), z (13b), sizeZ (6b), y (9b), sizeY (6b), normal (3b), colorID (8b)
            ByteAddressBuffer squaresIndices;

            uniform float quadsInterleaving; // Size increase to remove small (1 pixel) gaps between triangles


            // Random value between 0 and 1
            uniform float seed;
            float random(uint3 block)
            {
                float3 vec = frac(float3(block) * 0.1031 + seed);
                vec += dot(vec, vec.yzx + 33.33);
                return frac((vec.x + vec.y) * vec.z);
            }


            v2f vert(uint vertexID: SV_VertexID, uint instanceID : SV_InstanceID)
            {
                // Get square
                uint squareIndex = squaresIndices.Load(instanceID * 4) * 8;
                uint squareData1 = squares.Load(squareIndex);
                uint squareData2 = squares.Load(squareIndex + 4);

                // Unpack data
                uint normal = (squareData2 >> 21) & mask3Bits;
                float3 cubePos = float3(squareData1 & mask13Bits, (squareData2 >> 6) & mask9Bits, squareData1 >> 19);
                float3 size = float3((squareData1 >> 13) & mask6Bits, (squareData2 >> 15) & mask6Bits, squareData2 & mask6Bits);

                // Position
                float3 vertex = cubeVertices[normal * 4 + vertexID];
                float3 worldPos = cubePos + (vertex * 0.5 + 0.5) * (size + 1);
                float3 normalVector = normals[normal];
                worldPos += vertex * (1 - abs(normalVector)) * quadsInterleaving * distance(_WorldSpaceCameraPos, worldPos) * quadsInterleaving / 1000; // Slight size increase
                
                // Create output
                v2f o;
                o.vertex = mul(UNITY_MATRIX_VP, float4(worldPos, 1));
                o.color = colors[squareData2 >> 24];
                o.blockData = float4(worldPos - normalVector * 0.5, faceLightLevels[normal]);
                return o;
            }


            fixed4 frag(v2f i) : SV_Target
            {
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
