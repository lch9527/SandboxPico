using UnityEngine;
using UnityEngine.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PhysicalAssembly.MetaXR
{
    // Fixed Quest 3 panel-resolution target. The XR runtime retains its projection aspect ratio
    // and may round buffer dimensions; both dimensions must reach the target.
    public static class XRRenderQuality
    {
        public const int MsaaSamples=4;
        public const int TargetEyeWidth=2064, TargetEyeHeight=2208;
        // Used only if the runtime has not exposed its eye-buffer dimensions yet.
        public const float EyeResolutionScale=2208f/1760f;
        static float nativeEyeScale;

        public static float CalculateNativeEyeScale(int width,int height,float currentScale)
        {
            if(width<=0 || height<=0 || currentScale<=0) return EyeResolutionScale;
            return currentScale*Mathf.Max((float)TargetEyeWidth/width,(float)TargetEyeHeight/height);
        }
        // Eye-layer sharpening. Off: Normal re-crisped the edges MSAA smooths.
        public const OVRPlugin.LayerSharpenType Sharpen=OVRPlugin.LayerSharpenType.None;
        public const float ClarityResolutionScale=1.15f;
        // Foveation through the Meta SDK. (XRDisplaySubsystem.foveatedRenderingLevel does nothing here:
        // Unity's Foveated Rendering OpenXR feature is disabled, Meta's is enabled.) High saves about 1 ms.
        public const OVRManager.FoveatedRenderingLevel Foveation=OVRManager.FoveatedRenderingLevel.High;
        // Headset refresh rate; the app renders every frame (Application SpaceWarp was tried and dropped).
        public const float DisplayFrequency=90f;

        public static void Apply()
        {
            if(!XRSettings.isDeviceActive) return;
            // Dynamic resolution off: it allocated a 1.6x buffer and then drew into 44% of each side of it.
            var manager=OVRManager.instance;
            if(manager && manager.enableDynamicResolution) manager.enableDynamicResolution=false;
            // Measure once: repeated level loads must not compound the scale or use a pending resize.
            if(nativeEyeScale<=0)
                nativeEyeScale=CalculateNativeEyeScale(XRSettings.eyeTextureWidth,XRSettings.eyeTextureHeight,XRSettings.eyeTextureResolutionScale);
            // URP owns XR render-target scale and reapplies it each frame. Keep it in sync so it
            // cannot reset our target to the asset's 1x startup value on the next frame.
            var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if(pipeline) pipeline.renderScale=nativeEyeScale;
            XRSettings.eyeTextureResolutionScale=nativeEyeScale;
            XRSettings.renderViewportScale=1;
            ScalableBufferManager.ResizeBuffers(1,1);
            OVRManager.foveatedRenderingLevel=Foveation;
            // Let the system lower foveation again when there is GPU headroom.
            OVRManager.useDynamicFoveatedRendering=true;
            if(manager) manager.sharpenType=Sharpen;
            // URP mirrors its MSAA sample count into QualitySettings.antiAliasing.
            Debug.Log($"METAHACKTHONE_RENDER eyeScale={XRSettings.eyeTextureResolutionScale:F2} eyeTex={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} viewport={XRSettings.renderViewportScale:F2} dynamicResolution={(manager && manager.enableDynamicResolution)} msaa={QualitySettings.antiAliasing} foveation={OVRManager.foveatedRenderingLevel} spaceWarp={OVRManager.GetSpaceWarp()} hz={(OVRManager.display!=null?OVRManager.display.displayFrequency:0)} available={(OVRManager.display!=null?string.Join("/",OVRManager.display.displayFrequenciesAvailable):"")}");
        }
    }
}
