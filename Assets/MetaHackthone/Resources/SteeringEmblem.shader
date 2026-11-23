Shader "MetaHackthone/Steering Emblem"
{
    // The glowing ring emblem that used to be drawn on the workbench tabletop, now the visual of
    // the build rotation wheel. Drawn on a flat disc mesh (SteeringEmblemDisc) in the wheel's XZ
    // plane, so distances are the wheel's local metres and it scales with the wheel exactly like the old rim mesh.
    Properties
    {
        [HDR] _Color("Ring colour", Color) = (.08,1.7,2.2,1)
        _Radius("Main ring radius (metres)", Float) = .158
        [HideInInspector] _RippleAmplitude("Ripple amplitude (metres)", Float) = 0
        [HideInInspector] _RipplePhase("Ripple phase (radians)", Float) = 0
        [HideInInspector] _SweepPhase("Decorative sweep phase", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+21" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/CreekDiorama/Shaders/CreekAnalyticAA.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Radius;
            float _RippleAmplitude;
            float _RipplePhase;
            float _SweepPhase;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float2 local:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.local=i.positionOS.xz; o.positionCS=TransformObjectToHClip(i.positionOS.xyz);
                return o;
            }
            half4 frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float radial=length(i.local);
                float angle=atan2(i.local.y,i.local.x);
                // Four periodic waves keep the angle seam continuous. Only the rim and
                // its glow use this displacement; the central arrow stays fixed.
                float rimDistance=radial-_Radius-_RippleAmplitude*sin(4*angle-_RipplePhase);
                float ring=CreekBand(rimDistance,.005);
                // Differentiate atan2 analytically in XY: no false footprint at its wrap seam.
                float2 angularGradient=float2(-i.local.y,i.local.x)/max(dot(i.local,i.local),1e-8);
                float angularFootprint=(abs(dot(angularGradient,ddx(i.local)))+abs(dot(angularGradient,ddy(i.local))))*3.1831;
                float broken=1-CreekPeriodicFill(angle*3.1831+_SweepPhase,.22,angularFootprint);
                ring+=CreekBand(rimDistance-.0187,.005)*broken;
                ring+=CreekBand(rimDistance+.013,.002)*.35;
                // Direction arrow in the centre, same proportions as on the old tabletop emblem.
                // Tip toward the wheel's +Z, where the tabletop arrow pointed in Levels 1-5.
                float2 arrow=i.local/max(_Radius,.01);
                float shaft=max(abs(arrow.x)-.11,max(-.38-arrow.y,arrow.y-.18));
                float tip=max(max(.16-arrow.y,arrow.y-.57),(abs(arrow.x)-(.57-arrow.y)*.9)*.743294);
                float arrowMask=1-CreekAAStep(0,min(shaft,tip));
                float emblem=ring+exp2(-abs(rimDistance)*95)*.16+arrowMask*.8;
                return half4(_Color.rgb*emblem,saturate(emblem*.8));
            }
            ENDHLSL
        }
    }
}
