using UnityEngine;
using System.Linq;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PhysicalAssembly
{
    public sealed class SandboxOutlineFeature : ScriptableRendererFeature
    {
        Material material;
        OutlinePass pass;
        public override void Create()
        {
            CoreUtils.Destroy(material);
            var shader=Resources.Load<Shader>("SandboxStencilOutline");
            if(shader) material=CoreUtils.CreateEngineMaterial(shader);
            // Water is transparent and does not write depth. Composite the outline
            // after it, retaining depth testing against opaque scene geometry.
            pass=new OutlinePass {renderPassEvent=RenderPassEvent.AfterRenderingTransparents};
        }
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData renderingData)
        {
            if(!material || (SandboxPartHighlights.Draws.Count==0 && SandboxRewardHandOutline.Draws.Count==0 && SandboxRewardPresentation.Draws.Count==0) || renderingData.cameraData.cameraType==CameraType.Preview) return;
            pass.Material=material; renderer.EnqueuePass(pass);
        }
        protected override void Dispose(bool disposing) { CoreUtils.Destroy(material); }
        sealed class OutlinePass : ScriptableRenderPass
        {
            public Material Material;
            sealed class Data { public Material material; public SandboxPartHighlights.Draw[] draws; }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frame)
            {
                var resources=frame.Get<UniversalResourceData>();
                var camera=frame.Get<UniversalCameraData>();
                using(var builder=graph.AddRasterRenderPass<Data>("Sandbox stencil outlines",out var data)) {
                    data.material=Material; data.draws=SandboxPartHighlights.Draws.Concat(SandboxRewardHandOutline.Draws).Concat(SandboxRewardPresentation.Draws).ToArray();
                    builder.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.ReadWrite);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture,AccessFlags.ReadWrite);
                    if(camera.xr.enabled) {
                        builder.EnableFoveatedRasterization(camera.xr.supportsFoveatedRendering && resources.isActiveTargetBackBuffer);
                        builder.SetExtendedFeatureFlags(ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
                    }
                    builder.SetRenderFunc((Data d,RasterGraphContext context)=> {
                        // Mask all parts first so overlapping silhouettes never draw inside another part.
                        for(int pass=0;pass<4;pass++) foreach(var draw in d.draws) {
                            if(!draw.mesh || !draw.renderer) continue;
                            for(int sub=0;sub<draw.mesh.subMeshCount;sub++) context.cmd.DrawRenderer(draw.renderer,d.material,sub,pass);
                        }
                    });
                }
            }
        }
    }
}
