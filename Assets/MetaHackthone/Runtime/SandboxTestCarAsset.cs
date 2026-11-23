using UnityEngine;
namespace PhysicalAssembly
{
    [CreateAssetMenu(menuName = "MetaHackthone/Factory/Test car")]
    public sealed class SandboxTestCarAsset : ScriptableObject
    {
        public AssemblySaveV1 Assembly = new();
    }
}
