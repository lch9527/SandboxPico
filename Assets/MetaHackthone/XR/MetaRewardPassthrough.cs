using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAssembly.MetaXR
{
    // Multiply the passthrough compositor layer, leaving the reward, camera and color maps alone.
    public sealed class MetaRewardPassthrough : MonoBehaviour
    {
        sealed class LayerState
        {
            public OVRPassthroughLayer Layer;
            bool captured,originalOverride;
            Vector4 originalScale,originalOffset;
            public void Apply(float brightness)
            {
                if(!Layer) return;
                if(brightness>=1) { Restore(); return; }
                if(!captured) {
                    originalOverride=Layer.overridePerLayerColorScaleAndOffset;
                    originalScale=Layer.colorScale; originalOffset=Layer.colorOffset; captured=true;
                }
                var scale=originalOverride?originalScale:Vector4.one;
                var offset=originalOverride?originalOffset:Vector4.zero;
                scale.x*=brightness; scale.y*=brightness; scale.z*=brightness;
                offset.x*=brightness; offset.y*=brightness; offset.z*=brightness;
                Layer.colorScale=scale; Layer.colorOffset=offset;
                Layer.overridePerLayerColorScaleAndOffset=true;
            }
            public void Restore()
            {
                if(!captured) return;
                if(Layer) {
                    Layer.colorScale=originalScale; Layer.colorOffset=originalOffset;
                    Layer.overridePerLayerColorScaleAndOffset=originalOverride;
                }
                captured=false;
            }
        }
        readonly List<LayerState> layers=new();
        readonly Dictionary<Object,float> requests=new();
        public void Initialize(Transform rig)
        {
            Restore(); layers.Clear();
            if(rig) foreach(var layer in rig.GetComponentsInChildren<OVRPassthroughLayer>(true)) layers.Add(new LayerState {Layer=layer});
        }
        void OnEnable() { SandboxRewardPresentation.BrightnessChanged+=Apply; SandboxRewardPresentation.RoomBrightnessChanged+=Request; }
        void Apply(SandboxRewardPresentation owner,float brightness) => Request(owner,brightness);
        // Any owner may ask for a darker room; the darkest live request wins.
        void Request(Object owner,float brightness)
        {
            if(owner is null) return;
            if(brightness>=1 || !owner) requests.Remove(owner);
            else requests[owner]=Mathf.Clamp01(brightness);
            float combined=1;
            foreach(var request in requests) if(request.Key) combined=Mathf.Min(combined,request.Value);
            foreach(var layer in layers) layer.Apply(combined);
        }
        void Restore()
        {
            foreach(var layer in layers) layer.Restore();
            requests.Clear();
        }
        void OnDisable()
        {
            SandboxRewardPresentation.BrightnessChanged-=Apply; SandboxRewardPresentation.RoomBrightnessChanged-=Request;
            Restore();
        }
        void OnDestroy() => Restore();
    }
}
