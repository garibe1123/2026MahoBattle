using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class BattleShadowFloorMask : MonoBehaviour
{
    private readonly List<BattleDecorFloorSource> floors = new();
    private readonly List<BattleSpriteShadow> targets = new();
    private readonly List<BattleSpriteShadow> receivers = new();
    private readonly List<BattleSpriteShadow> casters = new();
    private readonly Plane[] planes = new Plane[6];
    private readonly Vector3[] corners = new Vector3[4];
    private WorldLight owner;
    private RenderTexture mask;
    private RenderTexture receiverMask;
    private CommandBuffer commands;

    private void Awake()
    {
        owner = GetComponent<WorldLight>();
        commands = new CommandBuffer { name = "Battle shadow floor alpha mask" };
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += RenderMask;
        RenderPipelineManager.endCameraRendering += FinishReceiverComposites;
    }
    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= RenderMask;
        RenderPipelineManager.endCameraRendering -= FinishReceiverComposites;
        BattleSpriteShadow.CopyActive(targets);
        for (int i = 0; i < targets.Count; i++)
            targets[i].SetReceiverMask(false);
        if (owner == WorldLight.Instance || WorldLight.Instance == null)
        {
            Shader.SetGlobalTexture("_BattleShadowFloorMask", Texture2D.blackTexture);
            Shader.SetGlobalTexture("_BattleShadowReceiverMask", Texture2D.blackTexture);
        }
        if (mask != null)
        {
            mask.Release();
            Destroy(mask);
            mask = null;
        }
        if (receiverMask != null)
        {
            receiverMask.Release();
            Destroy(receiverMask);
            receiverMask = null;
        }
    }

    private void OnDestroy() => commands?.Release();

    private void FinishReceiverComposites(ScriptableRenderContext context, Camera camera)
    {
        for (int i = 0; i < targets.Count; i++)
            if (targets[i] != null)
                targets[i].SetReceiverMask(false);
    }

    private void RenderMask(ScriptableRenderContext context, Camera camera)
    {
        if (owner == null || owner != WorldLight.Instance)
            return;
        BattleSpriteShadow.CopyActive(targets);
        for (int i = 0; i < targets.Count; i++)
            targets[i].SetReceiverMask(false);
        Shader.SetGlobalTexture("_BattleShadowFloorMask", Texture2D.blackTexture);
        Shader.SetGlobalTexture("_BattleShadowReceiverMask", Texture2D.blackTexture);
#if UNITY_EDITOR
        owner.BeginShadowDebugPreview(camera);
        for (int i = 0; i < targets.Count; i++)
            targets[i].BeginReceiverDebug(camera);
#endif
        if (WorldLight.Mode != BattleShadowMode.Projected ||
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
        RenderReceiverMasks(camera, area, width, height);
        commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        commands.SetViewport(camera.pixelRect);
        context.ExecuteCommandBuffer(commands);
    }

    private void RenderReceiverMasks(Camera camera, Bounds area, int width, int height)
    {
        receivers.Clear();
        casters.Clear();
        for (int i = 0; i < targets.Count; i++)
        {
            if (!targets[i].SourceVisible)
                continue;
            SpriteRenderer source = targets[i].Source;
            if ((camera.cullingMask & (1 << source.gameObject.layer)) == 0)
                continue;
            if (GeometryUtility.TestPlanesAABB(planes, source.bounds))
                receivers.Add(targets[i]);
            SpriteRenderer projection = targets[i].Projection;
            if (projection != null && projection.enabled && GeometryUtility.TestPlanesAABB(planes, targets[i].ReceiverProjectionBounds))
                casters.Add(targets[i]);
        }
        if (receivers.Count == 0)
            return;
        if (receiverMask == null || receiverMask.width != width || receiverMask.height != height)
        {
            if (receiverMask != null)
            {
                receiverMask.Release();
                Destroy(receiverMask);
            }
            receiverMask = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "BattleShadowReceiverMask", filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
            };
            receiverMask.Create();
        }
        commands.SetRenderTarget(receiverMask);
        commands.SetViewport(new Rect(0, 0, width, height));
        commands.ClearRenderTarget(false, true, Color.clear);
        int columns = Mathf.CeilToInt(Mathf.Sqrt(receivers.Count));
        int rows = (receivers.Count + columns - 1) / columns;
        int cellWidth = width / columns;
        int cellHeight = height / rows;
        for (int i = 0; i < receivers.Count; i++)
        {
            BattleSpriteShadow receiver = receivers[i];
            Bounds bounds = receiver.Source.bounds;
            float minX = Mathf.Max(bounds.min.x - 0.01f, area.min.x);
            float minY = Mathf.Max(bounds.min.y - 0.01f, area.min.y);
            float maxX = Mathf.Min(bounds.max.x + 0.01f, area.max.x);
            float maxY = Mathf.Min(bounds.max.y + 0.01f, area.max.y);
            if (maxX <= minX || maxY <= minY)
                continue;
            Vector4 rect = new(minX, minY, 1f / (maxX - minX), 1f / (maxY - minY));
            int x = i % columns * cellWidth;
            int y = i / columns * cellHeight;
            commands.SetViewport(new Rect(x, y, cellWidth, cellHeight));
            commands.SetGlobalVector("_BattleShadowReceiverRect", rect);
#if UNITY_EDITOR
            receiver.RecordReceiverDebug(camera);
#endif
            bool hasCaster = false;
            for (int j = 0; j < casters.Count; j++)
            {
                BattleSpriteShadow caster = casters[j];
                if (!caster.CanCastOn(receiver, camera))
                    continue;
                commands.DrawRenderer(caster.Projection, BattleSpriteShadow.SharedMaterial, 0, 2);
#if UNITY_EDITOR
                receiver.RecordReceiverDebug(camera, caster);
#endif
                hasCaster = true;
            }
            float atlasY = SystemInfo.graphicsUVStartsAtTop ? (float)(height - y - cellHeight) / height : (float)y / height;
            receiver.SetReceiverMask(hasCaster, rect, new Vector4((float)x / width, atlasY,
                (float)cellWidth / width, (float)cellHeight / height));
        }
        commands.SetGlobalTexture("_BattleShadowReceiverMask", receiverMask);
#if UNITY_EDITOR
        owner.RenderShadowDebugPreview(camera, commands);
#endif
    }
}
