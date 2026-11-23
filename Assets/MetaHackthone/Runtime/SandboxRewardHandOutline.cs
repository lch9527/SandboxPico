using System.Collections.Generic;
using UnityEngine;

namespace PhysicalAssembly
{
    public sealed class SandboxRewardHandOutline : MonoBehaviour
    {
        public static bool Requested;
        public static readonly List<SandboxPartHighlights.Draw> Draws=new();
        // A quiet teal edge on the receiving hand; the palm crescent is the louder cue.
        public Color Color=new(.38f,.86f,.82f,.75f);
        public float Width=.002f;
        SkinnedMeshRenderer source,proxy;
        MaterialPropertyBlock properties;
        public void Bind(SkinnedMeshRenderer renderer) { source=renderer; }
        void LateUpdate()
        {
            Draws.Clear();
            if(!Requested || !source || !source.enabled || !source.gameObject.activeInHierarchy)return;
            if(!proxy){var go=new GameObject("Reward hand outline");go.transform.SetParent(source.transform,false);proxy=go.AddComponent<SkinnedMeshRenderer>();proxy.sharedMesh=source.sharedMesh;proxy.bones=source.bones;proxy.rootBone=source.rootBone;proxy.localBounds=source.localBounds;proxy.enabled=false;properties=new MaterialPropertyBlock();}
            properties.SetColor("_Color",Color);properties.SetFloat("_Width",Width*(1+.15f*Mathf.Sin(Time.unscaledTime*2.5f)));proxy.SetPropertyBlock(properties);
            Draws.Add(new SandboxPartHighlights.Draw{mesh=proxy.sharedMesh,renderer=proxy,matrix=proxy.localToWorldMatrix,properties=properties});
        }
        void OnDisable(){Draws.Clear();}
        void OnDestroy(){Draws.Clear();if(proxy)Destroy(proxy.gameObject);}
    }
}
