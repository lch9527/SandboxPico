using UnityEditor;
using UnityEngine;

namespace PhysicalAssembly.Editor
{
    public static class SandboxFactoryGoalGizmo
    {
        // Draw on the collider so clicking selects the goal, not the level root.
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        static void DrawGoal(BoxCollider box, GizmoType gizmoType)
        {
            var factory = box.GetComponentInParent<SandboxFactoryLevel>();
            if (!factory || factory.Finish != box) return;

            var matrix = Gizmos.matrix;
            var color = Gizmos.color;
            try {
                Gizmos.matrix = box.transform.localToWorldMatrix;
                bool selected = (gizmoType & GizmoType.Selected) != 0;
                Gizmos.color = new Color(0, 1, 0, selected ? .18f : .08f);
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(box.center, box.size);
            } finally {
                Gizmos.matrix = matrix;
                Gizmos.color = color;
            }
        }
    }
}
