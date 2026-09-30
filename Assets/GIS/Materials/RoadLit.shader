

Shader "GIS/RoadLit"
{
    Properties
    {
        _BaseColor("Base Color", Color)                          = (0.22, 0.22, 0.22, 1)
        _MarkingColor("Marking Color", Color)                    = (0.92, 0.92, 0.92, 1)
        _EdgeLineMeters("Edge Line Width (m)", Float)            = 0.15
        _CenterLineMeters("Lane Divider Width (m)", Float)       = 0.15
        _CenterDashOnMeters("Dash On (m)", Float)                = 3.0
        _CenterDashOffMeters("Dash Off (m)", Float)              = 6.0
        _CenterDashed("Dashed (0=solid, 1=dashed)", Float)       = 1.0
        _Smoothness("Smoothness", Range(0,1))                    = 0.15
        _Metallic("Metallic", Range(0,1))                        = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }
        LOD 100


        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS   : SV_POSITION;
                float3 positionWS    : TEXCOORD0;
                float3 normalWS      : TEXCOORD1;
                float2 uv            : TEXCOORD2;
                float2 widthAndLanes : TEXCOORD3;
                float  fogFactor     : TEXCOORD4;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _MarkingColor;
                float  _EdgeLineMeters;
                float  _CenterLineMeters;
                float  _CenterDashOnMeters;
                float  _CenterDashOffMeters;
                float  _CenterDashed;
                float  _Smoothness;
                float  _Metallic;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   ni = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS   = pi.positionCS;
                OUT.positionWS    = pi.positionWS;
                OUT.normalWS      = ni.normalWS;
                OUT.uv            = IN.uv;
                OUT.widthAndLanes = IN.uv2;
                OUT.fogFactor     = ComputeFogFactor(pi.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float u = IN.uv.x;
                float v = IN.uv.y;                                  // metres along length
                float w = max(IN.widthAndLanes.x, 0.001);          // road width in metres
                int laneCount = clamp((int)round(IN.widthAndLanes.y), 1, 4);

                float xMeters = u * w;                              // lateral position in metres
                float dEdge   = min(xMeters, w - xMeters);

                // Edge lines, anti-aliased against pixel footprint
                float aaX      = max(fwidth(xMeters), 1e-4);
                float edgeMask = 1.0 - smoothstep(_EdgeLineMeters - aaX,
                                                  _EdgeLineMeters + aaX, dEdge);

                // Dash period measured along the road length
                float aaV    = max(fwidth(v), 1e-4);
                float period = max(_CenterDashOnMeters + _CenterDashOffMeters, 0.01);
                float t      = fmod(v, period);
                float dashOn = 1.0 - smoothstep(_CenterDashOnMeters - aaV,
                                                _CenterDashOnMeters + aaV, t);
                float dashMask = lerp(1.0, dashOn, step(0.5, _CenterDashed));

                // Lane dividers at i * w / laneCount for i = 1 .. laneCount-1
                float dividerMask = 0.0;
                [unroll(3)]
                for (int i = 1; i < 4; i++)
                {
                    if (i >= laneCount) break;
                    float divX = (float)i * w / (float)laneCount;
                    float dDiv = abs(xMeters - divX);
                    float divM = 1.0 - smoothstep(_CenterLineMeters * 0.5 - aaX,
                                                  _CenterLineMeters * 0.5 + aaX, dDiv);
                    dividerMask = max(dividerMask, divM * dashMask);
                }

                float marking = max(edgeMask, dividerMask);
                float3 albedo = lerp(_BaseColor.rgb, _MarkingColor.rgb, marking);

                // Hand the shaded surface to URP's physically based lighting
                InputData inputData = (InputData)0;
                inputData.positionWS      = IN.positionWS;
                inputData.normalWS        = normalize(IN.normalWS);
                inputData.viewDirectionWS = normalize(GetWorldSpaceViewDir(IN.positionWS));
                inputData.shadowCoord     = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord        = IN.fogFactor;
                inputData.vertexLighting  = half3(0,0,0);
                inputData.bakedGI         = SampleSH(inputData.normalWS);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo     = albedo;
                surfaceData.metallic   = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.specular   = half3(0,0,0);
                surfaceData.normalTS   = half3(0,0,1);
                surfaceData.emission   = half3(0,0,0);
                surfaceData.occlusion  = 1.0;
                surfaceData.alpha      = 1.0;

                half4 col = UniversalFragmentPBR(inputData, surfaceData);
                col.rgb   = MixFog(col.rgb, IN.fogFactor);
                return col;
            }
            ENDHLSL
        }


        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   shadowVert
            #pragma fragment shadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct sAttr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct sVar  { float4 positionCS : SV_POSITION; };

            sVar shadowVert(sAttr IN)
            {
                sVar OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ldir = normalize(_LightPosition - posWS);
                #else
                    float3 ldir = _LightDirection;
                #endif

                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, ldir));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionCS = posCS;
                return OUT;
            }

            half4 shadowFrag(sVar IN) : SV_Target { return 0; }
            ENDHLSL
        }


        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   dOnlyVert
            #pragma fragment dOnlyFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct dAttr { float4 positionOS : POSITION; };
            struct dVar  { float4 positionCS : SV_POSITION; };

            dVar dOnlyVert(dAttr IN)
            {
                dVar OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }
            half4 dOnlyFrag(dVar IN) : SV_Target { return 0; }
            ENDHLSL
        }


        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex   dnVert
            #pragma fragment dnFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct dnAttr { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct dnVar  { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            dnVar dnVert(dnAttr IN)
            {
                dnVar OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }
            half4 dnFrag(dnVar IN) : SV_Target { return half4(normalize(IN.normalWS), 0); }
            ENDHLSL
        }
    }
}
