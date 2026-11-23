using UnityEngine;

namespace PhysicalAssembly
{
    // Marks where the player's eyes (the headset camera) are and where they look when a sandbox level
    // starts. Keep it under the level's Building Area so it follows that area. Its position and yaw are
    // used at runtime; floor and gravity play no part.
    [SelectionBase, DisallowMultipleComponent]
    public sealed class SandboxPlayerStart : MonoBehaviour
    {
        // The camera preview is tagged EditorOnly, but that tag is not stripped from level prefabs
        // spawned at runtime, so hide it explicitly in Play mode and in builds.
        void Awake()
        {
            foreach(Transform child in transform)
                if(child.CompareTag("EditorOnly")) child.gameObject.SetActive(false);
        }
        // Locks the marker to a pure Y rotation, matching how it is read. Height is left as authored.
        public void KeepLevel()
        {
            var yaw=transform.localEulerAngles.y; var flat=Quaternion.Euler(0,yaw,0);
            if(Quaternion.Angle(transform.localRotation,flat)>.001f) transform.localRotation=flat;
        }
    }
}
