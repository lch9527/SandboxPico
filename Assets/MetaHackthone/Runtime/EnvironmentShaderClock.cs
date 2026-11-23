using UnityEngine;

namespace PhysicalAssembly
{
    // Landscape motion follows real time even while miniature car physics runs slowly.
    public sealed class EnvironmentShaderClock : MonoBehaviour
    {
        static readonly int Clock=Shader.PropertyToID("_CreekClock");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var go=new GameObject("Environment shader clock");
            DontDestroyOnLoad(go);go.AddComponent<EnvironmentShaderClock>();
        }
        void LateUpdate()=>Shader.SetGlobalVector(Clock,new Vector4(Time.unscaledTime,1,0,0));
        void OnDestroy()=>Shader.SetGlobalVector(Clock,Vector4.zero);
    }
}
