using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PhysicalAssembly.Editor
{
    // Texture anti-aliasing for the sandbox levels. The player never moves (the camera is the Player
    // Start), so texture filtering is tuned for fixed viewing distances.
    public static class SandboxTextureQuality
    {
        public const int Anisotropy=8;
        public static readonly string[] Roots={
            "Assets/MetaHackthone/Generated/Campaign/Level1.prefab","Assets/MetaHackthone/Generated/Campaign/Level2.prefab",
            "Assets/MetaHackthone/Generated/Campaign/Level3.prefab","Assets/MetaHackthone/Generated/Campaign/Level4.prefab",
            "Assets/MetaHackthone/Generated/Campaign/Level5.prefab","Assets/MetaHackthone/Scenes/DownhillSandbox.unity"};
        // Quest 3 eye buffer at render scale 1: about 1680 px across about 100 degrees per eye.
        public const float QuestPixelsPerDegree=16.8f;

        // Importer-backed project textures (never package textures) used by the sandbox levels.
        public static IEnumerable<TextureImporter> LevelTextures() =>
            AssetDatabase.GetDependencies(Roots.Where(p=>AssetDatabase.LoadMainAssetAtPath(p)).ToArray(),true)
                .Where(p=>p.StartsWith("Assets/"))
                .Select(p=>AssetImporter.GetAtPath(p) as TextureImporter)
                .Where(i=>i!=null && i.textureShape==TextureImporterShape.Texture2D &&
                    (i.textureType==TextureImporterType.Default || i.textureType==TextureImporterType.NormalMap));

        public static bool IsFiltered(TextureImporter i) => i.mipmapEnabled && i.filterMode==FilterMode.Trilinear && i.anisoLevel>=4;

        [MenuItem("MetaHackthone/Rendering/Improve Sandbox Texture Filtering")]
        public static void Apply()
        {
            var log=new StringBuilder(); int changed=0;
            foreach(var importer in LevelTextures().ToArray()) {
                if(importer.mipmapEnabled && importer.filterMode==FilterMode.Trilinear && importer.anisoLevel==Anisotropy) continue;
                // Mipmaps + trilinear remove shimmer and mip seams; anisotropic keeps surfaces seen at an angle sharp.
                importer.mipmapEnabled=true; importer.filterMode=FilterMode.Trilinear; importer.anisoLevel=Anisotropy;
                importer.SaveAndReimport(); changed++;
                log.AppendLine("  "+importer.assetPath);
            }
            Debug.Log($"METAHACKTHONE_TEXTURE_FILTERING updated={changed} (mipmaps, trilinear, aniso {Anisotropy})\n{log}");
        }

        [MenuItem("MetaHackthone/Rendering/Report Texel Density from Player Start")]
        public static void Report()
        {
            var layout=Object.FindObjectsByType<SandboxLevelLayout>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).FirstOrDefault();
            if(!layout || !layout.BuildingRoot) { Debug.LogWarning("Open a sandbox level scene first."); return; }
            var eye=layout.PlayerStartPose(SandboxLevelLayout.DefaultPlayerDistance).position;
            var rows=new List<(float ratio,string line)>();
            foreach(var renderer in layout.GetComponentsInChildren<Renderer>()) {
                if(!renderer.TryGetComponent<MeshFilter>(out var filter) || !filter.sharedMesh || !filter.sharedMesh.isReadable) continue;
                float metersPerUv=MetersPerUv(filter.sharedMesh,renderer.transform);
                if(metersPerUv<=0) continue;
                foreach(var material in renderer.sharedMaterials.Where(m=>m)) {
                    var property=material.HasProperty("_BaseMap")?"_BaseMap":material.HasProperty("_MainTex")?"_MainTex":null;
                    var texture=property!=null?material.GetTexture(property) as Texture2D:null;
                    if(!texture) continue;
                    float tiling=material.GetTextureScale(property).x;
                    float texelsPerMeter=texture.width*Mathf.Max(.0001f,tiling)/metersPerUv;
                    float near=Vector3.Distance(eye,renderer.bounds.ClosestPoint(eye)), far=Vector3.Distance(eye,renderer.bounds.center)+renderer.bounds.extents.magnitude;
                    // One screen pixel covers this many metres at a given distance.
                    float metersPerPixel(float d)=>Mathf.Max(.01f,d)*Mathf.Deg2Rad/QuestPixelsPerDegree;
                    float nearRatio=texelsPerMeter*metersPerPixel(near), farRatio=texelsPerMeter*metersPerPixel(far);
                    int needed=Mathf.ClosestPowerOfTwo(Mathf.CeilToInt(texture.width/Mathf.Max(1,nearRatio)));
                    rows.Add((nearRatio,$"{renderer.name,-28} {AssetDatabase.GetAssetPath(texture).Split('/').Last(),-28} {near:F2}-{far:F2} m  texels/pixel {nearRatio:F2}-{farRatio:F2}  ({texture.width}px, full detail needs {Mathf.Min(needed,texture.width)}px)"));
                }
            }
            var text=new StringBuilder($"METAHACKTHONE_TEXEL_REPORT {layout.name} from Player Start {eye:F2}. Under ~1 texel/pixel is sharp; above ~1.5 the GPU samples a smaller mipmap (needs trilinear + anisotropic to avoid shimmer).\n");
            foreach(var row in rows.OrderByDescending(r=>r.ratio)) text.AppendLine(row.line);
            Debug.Log(text.ToString());
        }
        // Average world metres per UV unit (one texture repeat at tiling 1).
        static float MetersPerUv(Mesh mesh,Transform transform)
        {
            var vertices=mesh.vertices; var uvs=mesh.uv; var triangles=mesh.triangles;
            if(uvs.Length!=vertices.Length) return 0;
            double world=0, uv=0;
            for(int i=0;i+2<triangles.Length;i+=3) {
                Vector3 a=transform.TransformPoint(vertices[triangles[i]]), b=transform.TransformPoint(vertices[triangles[i+1]]), c=transform.TransformPoint(vertices[triangles[i+2]]);
                world+=Vector3.Cross(b-a,c-a).magnitude*.5;
                Vector2 ua=uvs[triangles[i]], ub=uvs[triangles[i+1]], uc=uvs[triangles[i+2]];
                uv+=Mathf.Abs((ub.x-ua.x)*(uc.y-ua.y)-(uc.x-ua.x)*(ub.y-ua.y))*.5;
            }
            return uv>1e-9?(float)System.Math.Sqrt(world/uv):0;
        }
    }
}
