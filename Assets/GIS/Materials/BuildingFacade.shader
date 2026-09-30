

Shader "GIS/BuildingFacade"
{
    Properties
    {
        _BaseColor("Base Color (multiplied by vertex color)", Color) = (1,1,1,1)
        _WindowColor("Window Color", Color)                          = (0.18, 0.22, 0.30, 1)
        _FrameColor("Frame Color", Color)                            = (0.10, 0.10, 0.10, 1)
        _DoorColor("Door Color", Color)                              = (0.06, 0.05, 0.04, 1)
        _WindowWidth("Window Width (m)", Float)                      = 1.5
        _WindowHeight("Window Height (m)", Float)                    = 1.4
        _WindowSpacingH("Horizontal Window Spacing (m)", Float)      = 3.0
        _FloorHeight("Floor Height (m)", Float)                      = 3.2
        _FrameWidth("Frame Thickness (m)", Float)                    = 0.06
        _DoorWidth("Door Width (m)", Float)                          = 1.2
        _DoorHeight("Door Height (m)", Float)                        = 2.2
        _DoorMargin("Door Margin (m, suppresses neighbouring windows)", Float) = 0.2
        _RoofGapMeters("Roof Gap (m)", Float)                        = 1.0
        _WindowsEnabled("Windows Enabled (per-renderer)", Float)     = 1.0
        _DoorEnabled("Door Enabled (per-renderer)", Float)           = 1.0
        _Smoothness("Smoothness", Range(0,1))                        = 0.08
        _Metallic("Metallic", Range(0,1))                            = 0.0
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
                float2 uv3        : TEXCOORD2;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float2 uv          : TEXCOORD2;
                float  buildingH   : TEXCOORD3;
                float2 doorInfo    : TEXCOORD4;
                float4 vColor      : TEXCOORD5;
                float  fogFactor   : TEXCOORD6;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _WindowColor;
                float4 _FrameColor;
                float4 _DoorColor;
                float  _WindowWidth;
                float  _WindowHeight;
                float  _WindowSpacingH;
                float  _FloorHeight;
                float  _FrameWidth;
                float  _DoorWidth;
                float  _DoorHeight;
                float  _DoorMargin;
                float  _RoofGapMeters;
                float  _WindowsEnabled;
                float  _DoorEnabled;
                float  _Smoothness;
                float  _Metallic;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pi = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   ni = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = pi.positionCS;
                OUT.positionWS  = pi.positionWS;
                OUT.normalWS    = ni.normalWS;
                OUT.uv          = IN.uv;
                OUT.buildingH   = IN.uv2.x;
                OUT.doorInfo    = IN.uv3;
                OUT.vColor      = IN.color;
                OUT.fogFactor   = ComputeFogFactor(pi.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wallTint = _BaseColor.rgb * IN.vColor.rgb;

                float3 albedo;
                float upDot = normalize(IN.normalWS).y;

                if (upDot > 0.9)
                {

                    albedo = wallTint;
                }
                else
                {
                    float u = IN.uv.x;
                    float v = IN.uv.y;
                    float h = IN.buildingH;

                    if (h > 0.01 && v > h - _RoofGapMeters)
                    {

                        albedo = wallTint;
                    }
                    else if (_WindowsEnabled < 0.5)
                    {

                        albedo = wallTint;
                    }
                    else
                    {

                        float floorH    = max(_FloorHeight, 0.5);
                        float floorIdx  = floor(v / floorH);
                        float floorBot  = floorIdx * floorH;
                        float floorCenV = floorBot + floorH * 0.5;

                        float halfW = _WindowWidth  * 0.5;
                        float halfH = _WindowHeight * 0.5;

                        float colSpacing = max(_WindowSpacingH, 0.5);
                        float col        = floor(u / colSpacing);
                        float colCenterU = (col + 0.5) * colSpacing;


                        bool suppressWindow = false;
                        if (_DoorEnabled > 0.5 && IN.doorInfo.y > 0.5)
                        {
                            float doorCenterU   = IN.doorInfo.x;
                            float doorRectHalfW = _DoorWidth  * 0.5 + _DoorMargin;
                            float doorRectTop   = _DoorHeight       + _DoorMargin;
                            bool uOverlaps = abs(colCenterU - doorCenterU) < (halfW + doorRectHalfW);
                            bool vOverlaps = (floorCenV - halfH) < doorRectTop;
                            suppressWindow = uOverlaps && vOverlaps;
                        }

                        if (suppressWindow)
                        {
                            albedo = wallTint;
                        }
                        else
                        {
                            float dU = abs(u - colCenterU);
                            float dV = abs(v - floorCenV);

                            float aaU = max(fwidth(u), 1e-4);
                            float aaV = max(fwidth(v), 1e-4);

                            float maskU = 1.0 - smoothstep(halfW - aaU, halfW + aaU, dU);
                            float maskV = 1.0 - smoothstep(halfH - aaV, halfH + aaV, dV);
                            float windowMask = maskU * maskV;

                            float innerHalfW = max(halfW - _FrameWidth, 0.0);
                            float innerHalfH = max(halfH - _FrameWidth, 0.0);
                            float inU = 1.0 - smoothstep(innerHalfW - aaU, innerHalfW + aaU, dU);
                            float inV = 1.0 - smoothstep(innerHalfH - aaV, innerHalfH + aaV, dV);
                            float innerMask = inU * inV;

                            albedo = lerp(wallTint, _FrameColor.rgb, windowMask);
                            albedo = lerp(albedo,   _WindowColor.rgb, innerMask);
                        }
                    }


                    if (_DoorEnabled > 0.5 && IN.doorInfo.y > 0.5)
                    {
                        float doorCenterU = IN.doorInfo.x;
                        float doorHalfW   = _DoorWidth * 0.5;
                        float aaU = max(fwidth(u), 1e-4);
                        float aaV = max(fwidth(v), 1e-4);

                        float dDoorU = abs(u - doorCenterU);
                        float doorMaskU = 1.0 - smoothstep(doorHalfW - aaU, doorHalfW + aaU, dDoorU);
                        float doorMaskV = 1.0 - smoothstep(_DoorHeight - aaV, _DoorHeight + aaV, v);
                        float doorMask  = doorMaskU * doorMaskV;
                        albedo = lerp(albedo, _DoorColor.rgb, doorMask);
                    }
                }

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
