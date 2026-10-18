using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class BattleShadowFloorMask : MonoBehaviour
{
    private readonly List<BattleDecorFloorSource> floors = new();
    private readonly Plane[] planes = new Plane[6];
    private readonly Vector3[] corners = new Vector3[4];
    private WorldLight owner;
    private RenderTexture mask;
    private CommandBuffer commands;

    private void Awake()
    {
        owner = GetComponent<WorldLight>();
        commands = new CommandBuffer { name = "Battle shadow floor alpha mask" };
    }

    private void OnEnable() => RenderPipelineManager.beginCameraRendering += RenderMask;
    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= RenderMask;
        if (mask != null)
        {
            mask.Release();
            Destroy(mask);
            mask = null;
        }
    }

    private void OnDestroy() => commands?.Release();

    private void RenderMask(ScriptableRenderContext context, Camera camera)
    {
        if (owner == null || owner != WorldLight.Instance || WorldLight.Mode != BattleShadowMode.Projected ||
            camera.cameraType == CameraType.Preview || BattleSpriteShadow.SharedMaterial == null)
            return;

        Plane ground = new(Vector3.forward, Vector3.zero);
        for (int i = 0; i < 4; i++)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3(i & 1, i >> 1, 0f));
            if (!ground.Raycast(ray, out float distance))
                return;
            corners[i] = ray.GetPoint(distance);
        }
        Bounds area = new(corners[0], Vector3.zero);
        for (int i = 1; i < 4; i++)
            area.Encapsulate(corners[i]);
        Vector4 rect = new(area.min.x, area.min.y, 1f / Mathf.Max(0.001f, area.size.x),
            1f / Mathf.Max(0.001f, area.size.y));
        int width = Mathf.Clamp(camera.pixelWidth, 64, 2048);
        int height = Mathf.Clamp(camera.pixelHeight, 64, 2048);
        if (mask == null || mask.width != width || mask.height != height)
        {
            if (mask != null)
            {
                mask.Release();
                Destroy(mask);
            }
            mask = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "BattleShadowFloorMask", filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            mask.Create();
        }

        GeometryUtility.CalculateFrustumPlanes(camera, planes);
        BattleDecorFloorSource.CopyActive(floors);
        commands.Clear();
        commands.SetRenderTarget(mask);
        commands.SetViewport(new Rect(0, 0, width, height));
        commands.ClearRenderTarget(false, true, Color.clear);
        commands.SetGlobalVector("_BattleShadowMaskRect", rect);
        for (int i = 0; i < floors.Count; i++)
        {
            SpriteRenderer floor = floors[i].Renderer;
            if (floor == null || floor.sortingOrder != BattleWorldSorting.FloorOrder ||
                floor.forceRenderingOff || floor.color.a <= 0.001f ||
                (camera.cullingMask & (1 << floor.gameObject.layer)) == 0 ||
                !GeometryUtility.TestPlanesAABB(planes, floor.bounds))
                continue;
            commands.DrawRenderer(floor, BattleSpriteShadow.SharedMaterial, 0, 1);
        }
        commands.SetGlobalTexture("_BattleShadowFloorMask", mask);
        commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        commands.SetViewport(camera.pixelRect);
        context.ExecuteCommandBuffer(commands);
    }
}
