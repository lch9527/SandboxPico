using System.Linq;
using NUnit.Framework;
using PhysicalAssembly.Editor;

namespace PhysicalAssembly.Tests
{
    public sealed class SandboxTextureQualityTests
    {
        // Stone, wood and other level textures are seen from a fixed camera; without mipmaps,
        // trilinear filtering and anisotropy they shimmer in the headset.
        [Test] public void SandboxLevelTexturesUseMipmapsTrilinearAndAnisotropicFiltering()
        {
            var textures=SandboxTextureQuality.LevelTextures().ToArray();
            Assert.IsNotEmpty(textures);
            Assert.IsTrue(textures.Any(t=>t.assetPath.EndsWith("Limestone surface.png")),"Stone texture is part of the sandbox levels");
            Assert.IsTrue(textures.Any(t=>t.assetPath.EndsWith("Oak grain.png")),"Wood texture is part of the sandbox levels");
            var bad=textures.Where(t=>!SandboxTextureQuality.IsFiltered(t)).Select(t=>$"{t.assetPath} mip={t.mipmapEnabled} filter={t.filterMode} aniso={t.anisoLevel}").ToArray();
            Assert.IsEmpty(bad,"Run MetaHackthone/Rendering/Improve Sandbox Texture Filtering:\n"+string.Join("\n",bad));
        }
    }
}
