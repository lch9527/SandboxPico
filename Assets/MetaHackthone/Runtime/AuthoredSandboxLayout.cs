using UnityEngine;

namespace PhysicalAssembly
{
    // Scene data is authoritative. Opening/playing a level never regenerates it.
    public sealed class AuthoredSandboxLayout : MonoBehaviour
    {
        public DesignLevelId EditorLevel;
        public GravityGarageCourse Level1;
        public GravityGarage3DCourse Level2;
        public void Show(DesignLevelId level)
        {
            if(!Level1 || !Level2) throw new System.InvalidOperationException("Both authored sandbox courses must be assigned.");
            Level1.gameObject.SetActive(level==DesignLevelId.GravityGarage);
            Level2.gameObject.SetActive(level==DesignLevelId.WaterfallValley);
            var layout=GetComponent<BuildingSceneLayout>();
            if(layout && layout.LevelLayouts!=null) foreach(var binding in layout.LevelLayouts) {
                bool selected=binding.CourseId==(SandboxCourseId)level;
                binding.gameObject.SetActive(selected);
                if(selected) binding.Bind(layout);
            }
            if(layout && layout.Spawns!=null) foreach(var spawn in layout.Spawns)
                if(spawn) spawn.gameObject.SetActive(DesignLevelCatalog.PartAvailable(level,spawn.Kind));
        }
    }
}
