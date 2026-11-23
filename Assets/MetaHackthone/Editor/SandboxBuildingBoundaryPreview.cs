using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PhysicalAssembly.Editor
{
    // Scene-view authoring aid: visible without selecting or expanding the component.
    // The runtime boundary remains owned by SandboxBuildVolume.
    [InitializeOnLoad]
    public static class SandboxBuildingBoundaryPreview
    {
        static readonly Color Cyan=new(.05f,.9f,1f,1f);
        static GUIStyle labelStyle;

        static SandboxBuildingBoundaryPreview() => SceneView.duringSceneGui+=Draw;

        static void Draw(SceneView view)
        {
            if(Application.isPlaying || !view.drawGizmos || Event.current.type!=EventType.Repaint) return;
            foreach(var course in Object.FindObjectsByType<DownhillCourse>(FindObjectsSortMode.None)) {
                var area=course.BuildArea;
                if(!area || !area.isActiveAndEnabled || SceneVisibilityManager.instance.IsHidden(area.gameObject)) continue;
                DrawBoundary(area);
            }
        }

        static void DrawBoundary(BuildingArea area)
        {
            var previousDepth=Handles.zTest;
            bool selected=Selection.activeGameObject==area.gameObject;
            var volume=area.GetComponent<SandboxBuildVolume>();
            bool hasArtwork=volume && volume.Artwork;
            try {
                if(!hasArtwork) using(new Handles.DrawingScope(Cyan,area.transform.localToWorldMatrix)) {
                    // Faint back edges keep the complete extent readable behind the table.
                    Handles.zTest=CompareFunction.Always;
                    Handles.color=new Color(Cyan.r,Cyan.g,Cyan.b,.25f);
                    Handles.DrawWireCube(Vector3.zero,area.Size);
                    Handles.zTest=CompareFunction.LessEqual;
                    for(int axis=0;axis<3;axis++) for(int sign=-1;sign<=1;sign+=2) {
                        var normal=Vector3.zero; normal[axis]=area.Size[axis]*.5f*sign;
                        var u=Vector3.zero; u[(axis+1)%3]=area.Size[(axis+1)%3]*.5f;
                        var v=Vector3.zero; v[(axis+2)%3]=area.Size[(axis+2)%3]*.5f;
                        var corners=new[]{normal-u-v,normal+u-v,normal+u+v,normal-u+v};
                        Handles.DrawSolidRectangleWithOutline(corners,new Color(Cyan.r,Cyan.g,Cyan.b,selected?.055f:.025f),Color.clear);
                        Handles.color=Cyan;
                        Handles.DrawAAPolyLine(selected?3f:2f,new[]{corners[0],corners[1],corners[2],corners[3],corners[0]});
                    }
                }
                labelStyle??=new GUIStyle(EditorStyles.miniBoldLabel) { normal={textColor=Cyan} };
                Handles.zTest=CompareFunction.Always;
                Handles.Label(area.transform.TransformPoint(new Vector3(-area.Size.x*.5f,area.Size.y*.5f,0)),"Sandbox building boundary",labelStyle);
            } finally { Handles.zTest=previousDepth; }
        }
    }
}
