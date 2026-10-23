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
        BattleShowBroadcastNoiseController.CaptureStopped -= ReleaseCaptureResources;
        capturePass?.Dispose();
        capturePass = new CapturePass();
        BattleShowBroadcastNoiseController.CaptureStopped += ReleaseCaptureResources;
    }

    protected override void Dispose(bool disposing)
    {
        BattleShowBroadcastNoiseController.CaptureStopped -= ReleaseCaptureResources;
        capturePass?.Dispose();
        capturePass = null;
    }

    private void ReleaseCaptureResources()
    {
        capturePass?.Dispose();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (BattleShowBroadcastNoiseController.WantsBackgroundCapture(camera) ||
            BattleShowBroadcastNoiseController.TryGetCameraDistortion(camera, out _))
            renderer.EnqueuePass(capturePass);
        else
            capturePass?.ReleaseForCamera(camera);
    }

    private sealed class CapturePass : ScriptableRenderPass
    {
        private Material distortionMaterial;
        private RTHandle distortionTarget;
        private Camera distortionCamera;

        public CapturePass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Color);
        }

        public void Dispose()
        {
            distortionTarget?.Release();
            distortionTarget = null;
            CoreUtils.Destroy(distortionMaterial);
            distortionMaterial = null;
            distortionCamera = null;
        }

        public void ReleaseForCamera(Camera camera)
        {
            if (distortionCamera == camera) Dispose();
        }

        private bool ConfigureDistortion(Camera camera)
        {
            if (!BattleShowBroadcastNoiseController.TryGetCameraDistortion(camera, out Vector4 settings))
            {
                ReleaseForCamera(camera);
                return false;
            }
            if (distortionMaterial == null)
            {
                Shader shader = Resources.Load<Shader>("BattleTabCameraDistortion");
                if (shader != null) distortionMaterial = CoreUtils.CreateEngineMaterial(shader);
            }
            if (distortionMaterial == null) return false;
            distortionCamera = camera;
            distortionMaterial.SetVector("_CrtSettings", settings);
            return true;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer || !resources.activeColorTexture.IsValid()) return;
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            TextureHandle source = resources.activeColorTexture;
            if (ConfigureDistortion(cameraData.camera))
            {
                TextureDesc descriptor = renderGraph.GetTextureDesc(source);
                descriptor.name = "Battle Tab Camera Distortion";
                descriptor.clearBuffer = false;
                TextureHandle distorted = renderGraph.CreateTexture(descriptor);
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(
                    source, distorted, distortionMaterial, 0), passName: "Battle Tab Camera Distortion");
                resources.cameraColor = distorted;
                source = distorted;
            }
            RTHandle output = BattleShowBroadcastNoiseController.PrepareBackgroundCapture(
                cameraData.camera, cameraData.cameraTargetDescriptor);
            if (output == null) return;

            // Capture before overlay canvases so the material never samples itself or the tab text.
            renderGraph.AddBlitPass(source, renderGraph.ImportTexture(output),
                Vector2.one, Vector2.zero, passName: "Battle Tab Background Capture");
        }

#pragma warning disable 618, 672
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            bool distort = ConfigureDistortion(renderingData.cameraData.camera);
            RTHandle output = BattleShowBroadcastNoiseController.PrepareBackgroundCapture(
                renderingData.cameraData.camera, renderingData.cameraData.cameraTargetDescriptor);
            if (output == null && !distort) return;

            CommandBuffer commands = CommandBufferPool.Get("Battle Tab Background Capture");
            RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;
            if (distort)
            {
                RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.None;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
                RenderingUtils.ReAllocateHandleIfNeeded(ref distortionTarget, descriptor,
                    FilterMode.Bilinear, TextureWrapMode.Clamp, name: "BattleTabCameraDistortion");
                Blitter.BlitCameraTexture(commands, source, distortionTarget, distortionMaterial, 0);
                Blitter.BlitCameraTexture(commands, distortionTarget, source, 0f, true);
            }
            if (output != null) Blitter.BlitCameraTexture(commands, source, output, 0f, true);
            context.ExecuteCommandBuffer(commands);
            CommandBufferPool.Release(commands);
        }
#pragma warning restore 618, 672
    }
}
