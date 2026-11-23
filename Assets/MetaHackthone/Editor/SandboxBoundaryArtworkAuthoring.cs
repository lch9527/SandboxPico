using UnityEditor;
using UnityEngine;

namespace PhysicalAssembly.Editor
{
    public static class SandboxBoundaryArtworkAuthoring
    {
        public static void Apply(BuildingSceneLayout owner)
        {
            var factory=owner.GetComponent<SandboxFactoryLevel>();
            foreach(var layout in owner.LevelLayouts) {
                var area=layout.Course.BuildArea;
                if(!area) continue;
                Undo.RegisterFullObjectHierarchyUndo(area.gameObject,"Use Beautiful Corner boundary artwork");
                var volume=area.GetComponent<SandboxBuildVolume>();
                if(!volume) volume=Undo.AddComponent<SandboxBuildVolume>(area.gameObject);
                if(factory && factory.WorkbenchVisuals && factory.WorkbenchVisuals!=volume.Artwork) {
                    if(volume.Artwork) Undo.DestroyObjectImmediate(volume.Artwork.gameObject);
                    volume.Artwork=factory.WorkbenchVisuals;
                    Undo.SetTransformParent(volume.Artwork,area.transform,"Attach glowing workbench to building boundary");
                }
                if(!volume.Artwork) {
                    var prefab=Resources.Load<GameObject>(SandboxBuildVolume.ArtworkResource);
                    var art=(GameObject)PrefabUtility.InstantiatePrefab(prefab,area.transform);
                    Undo.RegisterCreatedObjectUndo(art,"Create glowing building boundary");
                    art.name="Beautiful Corner glowing workbench"; volume.Artwork=art.transform;
                }
                // Remove the old generated bands, leaving gameplay's BuildingArea intact.
                var legacy=area.GetComponent<MeshRenderer>(); if(legacy) Undo.DestroyObjectImmediate(legacy);
                var filter=area.GetComponent<MeshFilter>(); if(filter) Undo.DestroyObjectImmediate(filter);
                volume.Rebuild(); EditorUtility.SetDirty(volume);
            }
        }
    }
}
