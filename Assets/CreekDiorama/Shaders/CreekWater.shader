Shader "Creek Diorama/Living Water"
{
    Properties
    {
        _DeepColor("Deep lagoon", Color)=(.025,.30,.42,1)
        _ShallowColor("Turquoise shallows", Color)=(.10,.68,.68,1)
        _FoamColor("Ivory foam", Color)=(.83,.98,.94,1)
        _FlowSpeed("Current speed", Range(0,2))=.35
        _RippleScale("Ripple density", Range(5,100))=38
        _ShoreWidth("Shore foam width (metres)", Range(.001,.1))=.007
        _DepthRange("Color depth (metres)", Range(.01,.2))=.07
        [Toggle] _UseDepth("Optional camera depth (requires pipeline support)", Float)=0
        [Toggle] _UseOpaqueTexture("Optional scene color (requires opaque texture)", Float)=0
        [Toggle] _WorldSpace("Continuous flow across modular tiles", Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20" }
        Pass
        {
            Name "Water"
            Tags { "LightMode"="UniversalForward" }
            Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "CreekClock.hlsl"
            #include "CreekAnalyticAA.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor, _ShallowColor, _FoamColor;
            float _FlowSpeed, _RippleScale, _ShoreWidth, _DepthRange, _UseDepth, _WorldSpace, _UseOpaqueTexture;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 local:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V Vert(A v)
            {
                V o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p=v.positionOS.xyz;
                // A continuous horizontal sheet: sparse imported faces must not lift
                // independently and expose cuts. Ripple relief lives in the normal.
                o.positionWS=TransformObjectToWorld(p); o.positionCS=TransformWorldToHClip(o.positionWS);o.local=p;return o;
            }
            half4 Frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float t=CreekTime()*_FlowSpeed;
                float2 p=lerp(i.local.xz,i.positionWS.xz,_WorldSpace);
                // Continuous full-float waves: no hashed grid cells or thresholds that expose tile edges.
                float n=.5+.25*CreekFilteredSin(dot(p,float2(5.7,3.1))+t*.17)
                            +.25*CreekFilteredSin(dot(p,float2(-3.8,6.3))-t*.13);
                float a=p.x*_RippleScale+p.y*17+t*2+n*2;
                float b=p.y*_RippleScale*1.31-p.x*12-t*1.5;
                float3 normalOS=normalize(float3(-CreekFilteredSin(a+1.5707963)*.12,1,-CreekFilteredSin(b+1.5707963)*.12));
                float3 N=TransformObjectToWorldNormal(normalOS), Vw=GetWorldSpaceNormalizeViewDir(i.positionWS);
                // Keep main's foam averaging, with derivatives before the optional depth branch.
                float depth=.06, edge=0;
                float2 screenUV=GetNormalizedScreenSpaceUV(i.positionCS);
                if(_UseDepth>.5)
                {
                    float raw=SampleSceneDepth(screenUV);
                    #if !UNITY_REVERSED_Z
                    raw=lerp(UNITY_NEAR_CLIP_VALUE,1,raw);
                    #endif
                    float3 behind=ComputeWorldSpacePosition(screenUV,raw,UNITY_MATRIX_I_VP);
                    depth=max(0,i.positionWS.y-behind.y);
                    edge=1-CreekAASmooth(_ShoreWidth*.12,_ShoreWidth*(.7+n*.7),depth);
                }
                float basin=saturate(depth/_DepthRange)*.65+(.5+.5*CreekFilteredSin(dot(p,float2(2.1,1.7))))*.35;
                half3 col=lerp(_ShallowColor.rgb,_DeepColor.rgb,saturate(basin));
                // Soft moving caustic filaments beneath the surface.
                float ridge=sin(a*.72+sin(b*.45))+sin(b*.85+sin(a*.38));
                float resolved=CreekPhaseVisibility(a)*CreekPhaseVisibility(b);
                float caustic=CreekBand(ridge,.075)*(.3+.7*n)*resolved;
                col+=half3(.035,.14,.12)*caustic;
                float fresnel=pow(1-saturate(dot(N,Vw)),4);
                col=lerp(col,half3(.22,.53,.66),fresnel*.65);
                Light sun=GetMainLight();
                // Highlights, restyled (2026-11-16). The sharp specular lobe broke into pale blocks on the ripples and
                // the glints drew four-pointed stars: both read as noise on painted water. Now there is one broad,
                // quiet sheen where the sun reflects, and long soft lines that ride downstream with the current.
                float3 calm=normalize(lerp(TransformObjectToWorldNormal(float3(0,1,0)),N,.3));
                float sheen=pow(saturate(dot(calm,normalize(sun.direction+Vw))),26);
                col+=sun.color*sheen*.14;
                float2 flow=normalize(float2(1,.45));
                float2 along=float2(dot(p,flow),dot(p,float2(-flow.y,flow.x)));
                float lane=along.y*23+CreekFilteredSin(along.x*2.3-t*.12)*.6;
                float stretch=.5+.5*CreekFilteredSin(along.x*3.7-t*.23+CreekFilteredSin(lane*.16));
                float lines=pow(saturate(.5+.5*CreekFilteredSin(lane-t*.10)),6)*stretch;
                lines*=CreekPhaseVisibility(lane);
                col=lerp(col,_FoamColor.rgb,lines*.14);
                float grain=.55+.20*CreekFilteredSin(dot(p,float2(53,37))+t*.6)
                                +.15*CreekFilteredSin(dot(p,float2(-31,59))-t*.4);
                float foam=edge*(.5+.5*grain);
                col=lerp(col,_FoamColor.rgb,saturate(foam));
                if(_UseOpaqueTexture>.5) col=lerp(col,SampleSceneColor(screenUV+N.xz*.0015),.06*(1-foam));
                return half4(col,1);
            }
            ENDHLSL
        }
    }
}
