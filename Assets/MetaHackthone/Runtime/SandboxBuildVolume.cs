using UnityEngine;

namespace PhysicalAssembly
{
    [ExecuteAlways, RequireComponent(typeof(BuildingArea))]
    public sealed class SandboxBuildVolume : MonoBehaviour
    {
        public const string ArtworkResource="SandboxBuildingBoundary";
        [Tooltip("Beautiful Corner artwork fitted to this building boundary. Resize the BuildingArea Size to change its extent.")]
        public Transform Artwork;
        public Renderer Tabletop;
        [Tooltip("Show the lightweight range edges while building. Off: only the lower perimeter is drawn; the boundary still limits where parts can go.")]
        public bool ShowWalls=true;
        Renderer[] renderers;
        bool[] authoredVisibility;
        bool AuthoredObjects => GetComponentInParent<SandboxFactoryLevel>(true) is SandboxFactoryLevel level && level.AuthoredObjects;
        Transform cachedArtwork;
        bool visible=true;
        bool stateApplied, appliedVisible, appliedWalls;

        void OnEnable()
        {
            if(Application.isPlaying) Rebuild();
            else FitArtwork();
        }
        void LateUpdate() => FitArtwork();

        public void Rebuild()
        {
            if(AuthoredObjects) { CacheRenderers(); stateApplied=false; SetVisible(visible); return; }
            // Old scenes may still contain the generated boundary-band renderer.
            // It must never render together with the replacement artwork.
            var legacy=GetComponent<MeshRenderer>();
            if(legacy) legacy.enabled=false;
            if(!Artwork) {
                var prefab=Resources.Load<GameObject>(ArtworkResource);
                if(!prefab) { Debug.LogError("Sandbox building boundary artwork is missing.",this); return; }
                Artwork=Instantiate(prefab,transform,false).transform;
                Artwork.name="Beautiful Corner glowing workbench";
            }
            FitArtwork(); CacheRenderers(); stateApplied=false; SetVisible(visible);
        }
        public void FitArtwork()
        {
            if(AuthoredObjects) return;
            if(!Artwork) return;
            var size=GetComponent<BuildingArea>().Size;
            if(Artwork.localPosition!=Vector3.zero) Artwork.localPosition=Vector3.zero;
            if(Artwork.localRotation!=Quaternion.identity) Artwork.localRotation=Quaternion.identity;
            if(Artwork.localScale!=size) Artwork.localScale=size;
        }
        void CacheRenderers()
        {
            if(cachedArtwork==Artwork && renderers!=null) return;
            cachedArtwork=Artwork;
            stateApplied=false;
            renderers=Artwork?Artwork.GetComponentsInChildren<Renderer>(true):System.Array.Empty<Renderer>();
            authoredVisibility=new bool[renderers.Length];
            for(int i=0;i<renderers.Length;i++) authoredVisibility[i]=renderers[i].enabled;
            if(!Tabletop && Artwork) {
                var surface=Artwork.Find("Assembly tabletop - shared footprint");
                if(surface) Tabletop=surface.GetComponent<Renderer>();
            }
        }
        public void SetVisible(bool value)
        {
            visible=value; CacheRenderers();
            if(stateApplied && appliedVisible==value && appliedWalls==ShowWalls)return;
            // Keep the lower perimeter during a run. The upper range edges
            // return when building resumes; no transparent wall faces are drawn.
            for(int i=0;i<renderers.Length;i++) {
                var renderer=renderers[i];
                if(renderer) renderer.enabled=(!AuthoredObjects || authoredVisibility[i]) && (renderer==Tabletop || (value && ShowWalls));
            }
            appliedVisible=value; appliedWalls=ShowWalls; stateApplied=true;
        }
    }
}
