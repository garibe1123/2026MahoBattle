using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public sealed class BattleTabBackgroundCaptureFeature : ScriptableRendererFeature
{
    private CapturePass capturePass;

    public override void Create()
    {
        capturePass = new CapturePass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (BattleShowBroadcastNoiseController.WantsBackgroundCapture(renderingData.cameraData.camera))
            renderer.EnqueuePass(capturePass);
    }

    private sealed class CapturePass : ScriptableRenderPass
    {
        public CapturePass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid()) return;
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            RTHandle output = BattleShowBroadcastNoiseController.PrepareBackgroundCapture(
                cameraData.camera, cameraData.cameraTargetDescriptor);
            if (output == null) return;

            // Capture before overlay canvases so the material never samples itself or the tab text.
            renderGraph.AddBlitPass(resources.activeColorTexture, renderGraph.ImportTexture(output),
                Vector2.one, Vector2.zero, passName: "Battle Tab Background Capture");
        }

#pragma warning disable 618, 672
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            RTHandle output = BattleShowBroadcastNoiseController.PrepareBackgroundCapture(
                renderingData.cameraData.camera, renderingData.cameraData.cameraTargetDescriptor);
            if (output == null) return;

            CommandBuffer commands = CommandBufferPool.Get("Battle Tab Background Capture");
            Blitter.BlitCameraTexture(commands, renderingData.cameraData.renderer.cameraColorTargetHandle, output, 0f, true);
            context.ExecuteCommandBuffer(commands);
            CommandBufferPool.Release(commands);
        }
#pragma warning restore 618, 672
    }
}
