using UnityEngine;

namespace PhysicalAssembly
{
    // Camera-local desktop sky. Never touches the XR camera or global RenderSettings.
    public sealed class CarScenerySky : MonoBehaviour
    {
        Camera target;
        Skybox skybox;
        Material installed, previous;
        CameraClearFlags previousFlags;
        bool previousEnabled, created;
        public void Bind(Camera camera, Material material)
        {
            Restore();
            if(!camera || !material)return;
            target=camera;installed=material;previousFlags=camera.clearFlags;
            skybox=camera.GetComponent<Skybox>();created=!skybox;
            if(created)skybox=camera.gameObject.AddComponent<Skybox>();
            previous=skybox.material;previousEnabled=skybox.enabled;
            skybox.material=material;skybox.enabled=true;camera.clearFlags=CameraClearFlags.Skybox;
        }
        void OnDisable()=>Restore();
        void Restore()
        {
            if(target && skybox && skybox.material==installed) {
                skybox.material=previous;skybox.enabled=previousEnabled;
                if(target.clearFlags==CameraClearFlags.Skybox)target.clearFlags=previousFlags;
                if(created)Destroy(skybox);
            }
            target=null;skybox=null;installed=null;
        }
    }
}
