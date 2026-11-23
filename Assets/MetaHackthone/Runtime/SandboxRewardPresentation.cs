using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAssembly
{
    // Scoped material exposure: shared source assets and the XR camera remain untouched.
    public sealed class SandboxRewardPresentation : MonoBehaviour
    {
        // XR adapters consume the same fade without introducing an SDK dependency here.
        public static event System.Action<SandboxRewardPresentation,float> BrightnessChanged;
        // Other presentations (the first-entry intro) dim the room through the same XR bridge. 1 clears the request.
        public static event System.Action<Object,float> RoomBrightnessChanged;
        public static void RequestRoomBrightness(Object owner,float brightness) => RoomBrightnessChanged?.Invoke(owner,brightness);
        public static readonly List<SandboxPartHighlights.Draw> Draws=new();
        public float EnvironmentBrightness=.3f, FadeSeconds=.45f, OutlineWidth=.003f;
        public bool Focus;
        float amount;
        readonly List<(Renderer renderer,Material[] originals)> renderers=new();
        readonly Dictionary<Material,Material> copies=new();
        readonly List<(Material material,int property,Color color)> colors=new();
        readonly List<MeshRenderer> proxies=new();
        MaterialPropertyBlock properties;
        void Awake(){properties=new MaterialPropertyBlock();}
        static readonly string[] colorNames={"_BaseColor","_Color","_EmissionColor","_SpecColor","_DeepColor","_ShallowColor","_WaterColor","_FoamColor"};
        public float Amount=>amount;
        public void Initialize(Transform environment,Transform preview)
        {
            foreach(var renderer in environment.GetComponentsInChildren<Renderer>(true)) {
                if(!renderer.enabled || renderer.transform.IsChildOf(preview))continue;
                var originals=renderer.sharedMaterials;var replacements=new Material[originals.Length];
                for(int i=0;i<originals.Length;i++) {
                    var source=originals[i];if(!source)continue;
                    if(!copies.TryGetValue(source,out var copy)) {
                        copy=new Material(source){name=source.name+" (reward focus)"};copies.Add(source,copy);
                        foreach(var name in colorNames)if(copy.HasProperty(name)){int id=Shader.PropertyToID(name);colors.Add((copy,id,copy.GetColor(id)));}
                    }
                    replacements[i]=copy;
                }
                renderers.Add((renderer,originals));renderer.sharedMaterials=replacements;
            }
            foreach(var filter in preview.GetComponentsInChildren<MeshFilter>()) {
                var go=new GameObject("Reward outline");go.transform.SetParent(filter.transform,false);
                go.AddComponent<MeshFilter>().sharedMesh=filter.sharedMesh;var proxy=go.AddComponent<MeshRenderer>();proxy.enabled=false;proxies.Add(proxy);
            }
        }
        public void Advance(float delta)
        {
            amount=Mathf.MoveTowards(amount,Focus?1:0,Mathf.Max(0,delta)/Mathf.Max(.01f,FadeSeconds));
            float brightness=Mathf.Lerp(1,Mathf.Clamp01(EnvironmentBrightness),Mathf.SmoothStep(0,1,amount));
            foreach(var item in colors){var c=item.color;c.r*=brightness;c.g*=brightness;c.b*=brightness;item.material.SetColor(item.property,c);}
            BrightnessChanged?.Invoke(this,brightness);
        }
        void LateUpdate()
        {
            Draws.Clear();
            properties.SetColor("_Color",new Color(1,.78f,.08f,1));properties.SetFloat("_Width",OutlineWidth);
            foreach(var proxy in proxies)if(proxy && proxy.gameObject.activeInHierarchy){proxy.SetPropertyBlock(properties);Draws.Add(new SandboxPartHighlights.Draw{mesh=proxy.GetComponent<MeshFilter>().sharedMesh,renderer=proxy,matrix=proxy.localToWorldMatrix,properties=properties});}
        }
        public void Restore()
        {
            Draws.Clear();amount=0;
            BrightnessChanged?.Invoke(this,1);
            foreach(var entry in renderers)if(entry.renderer)entry.renderer.sharedMaterials=entry.originals;
            renderers.Clear();foreach(var copy in copies.Values)if(copy)Destroy(copy);copies.Clear();colors.Clear();
            foreach(var proxy in proxies)if(proxy)Destroy(proxy.gameObject);proxies.Clear();
        }
        void OnDisable()=>Restore();
        void OnDestroy()=>Restore();
    }
}
