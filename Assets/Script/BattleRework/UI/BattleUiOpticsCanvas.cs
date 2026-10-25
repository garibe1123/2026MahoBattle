using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(62000)]
public sealed class BattleUiOpticsCanvas : MonoBehaviour
{
    private const float CapturePlaneDistance = 10000f;
    private static int captureLayer = -1;
    private static readonly HashSet<BattleUiOpticsCanvas> instances = new();
    private sealed class LayerState { internal int layer; }
    private readonly Dictionary<Transform, LayerState> layers = new();
    private readonly List<Transform> removed = new();
    private readonly UniversalRenderPipeline.SingleCameraRequest request = new();
    private Canvas source, captureRoot, output;
    internal static BattleUiOpticsCanvas ActiveCapture { get; private set; }
    private readonly List<BattleUiOpticsVisibility> visibility = new();
    private readonly HashSet<Graphic> missingVisibility = new();
    private Material hiddenMaterial;
    private bool sourceHidden;
    private Camera captureCamera;
    private Material material;
    private RenderTexture black, white;
    private RenderTexture protectedBlack, protectedWhite;
    private RawImage image;

    internal Material HiddenSourceMaterial => sourceHidden ? hiddenMaterial : null;
    internal Material ProtectionHiddenMaterial => hiddenMaterial;
    internal bool CapturingProtection { get; private set; }
    internal bool IsCapturing { get; private set; }
    internal bool PreserveNativePresenters { get; private set; }
    private bool renderNatively;

    internal void UseNativeRendering()
    {
        renderNatively = true;
        OnCanvasHierarchyChanged();
        Restore();
        ReleaseResources();
    }

    private void OnCanvasHierarchyChanged()
    {
        if (!renderNatively || source == null || ActiveCapture != null) return;
        foreach (Canvas parent in source.GetComponentsInParent<Canvas>(true))
        {
            if (parent == source || (!parent.overrideSorting && !parent.isRootCanvas)) continue;
            source.sortingLayerID = parent.sortingLayerID;
            source.sortingOrder = Mathf.Min(32767, parent.sortingOrder + 1);
            break;
        }
    }

    internal static void RefreshScreenTone()
    {
        foreach (BattleUiOpticsCanvas effect in instances)
        {
            if (effect == null) continue;
            effect.Restore();
            if (!BattleColorGradingController.TryGetUiOptics(out _, out _)) effect.ReleaseResources();
        }
    }

    internal static void Register(Graphic graphic)
    {
        if (!Application.isPlaying || graphic == null || graphic.canvas == null) return;
        if (ActiveCapture != null) return;
        Canvas root = graphic.canvas.rootCanvas;
        if (root.renderMode != RenderMode.ScreenSpaceOverlay ||
            root.name == "BattleCombatCornerVignetteCanvas" ||
            (root.sortingOrder < 500 && !root.name.StartsWith("Battle", System.StringComparison.Ordinal))) return;
        Canvas canvas = root;
        foreach (Canvas parent in graphic.GetComponentsInParent<Canvas>(true))
            if (parent.overrideSorting || parent == root) { canvas = parent; break; }
        if (!canvas.TryGetComponent(out BattleUiOpticsCanvas effect))
            effect = canvas.gameObject.AddComponent<BattleUiOpticsCanvas>();
        effect.RegisterGraphic(graphic);
        for (Transform node = graphic.transform; node != null; node = node.parent)
        {
            if (!effect.layers.ContainsKey(node))
                effect.layers.Add(node, new LayerState { layer = node.gameObject.layer });
            if (node == root.transform) break;
        }
    }

    private void Awake()
    {
        instances.Add(this);
        source = GetComponent<Canvas>();
        captureRoot = source.rootCanvas;
        Canvas.preWillRenderCanvases += ReconcileCanvases;
        foreach (Transform node in GetComponentsInChildren<Transform>(true))
            layers[node] = new LayerState { layer = node.gameObject.layer };
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            Register(graphic);
        if (captureLayer < 0)
        {
            uint used = 1;
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                used |= 1u << renderer.gameObject.layer;
            foreach (CanvasRenderer renderer in FindObjectsByType<CanvasRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                used |= 1u << renderer.gameObject.layer;
            for (int layer = 31; layer > 0; layer--)
                if ((used & (1u << layer)) == 0) { captureLayer = layer; break; }
        }
    }

    private void RegisterGraphic(Graphic graphic)
    {
        missingVisibility.Remove(graphic);
        if (!graphic.TryGetComponent(out BattleUiOpticsVisibility modifier))
            modifier = graphic.gameObject.AddComponent<BattleUiOpticsVisibility>();
        if (!visibility.Contains(modifier)) visibility.Add(modifier);
        modifier.Bind(this);
    }

    internal void RemoveVisibility(BattleUiOpticsVisibility modifier, Graphic graphic, bool transferred = false)
    {
        visibility.Remove(modifier);
        if (!transferred && graphic != null) missingVisibility.Add(graphic);
        Restore();
    }

    private void SetSourceHidden(bool hidden)
    {
        if (sourceHidden == hidden) return;
        sourceHidden = hidden;
        foreach (BattleUiOpticsVisibility modifier in visibility)
            if (modifier != null) modifier.Refresh();
    }

    private void ReconcileCanvases()
    {
        if (ActiveCapture != null || !isActiveAndEnabled) return;
        for (int i = visibility.Count - 1; i >= 0; i--)
        {
            BattleUiOpticsVisibility modifier = visibility[i];
            if (modifier != null && modifier.SortingCanvas != source) modifier.RegisterCanvas();
        }
    }

    private void LateUpdate()
    {
        bool optics = BattleColorGradingController.TryGetUiOptics(out Vector4 settings, out Vector2 limits) &&
            source != null && source.sortingOrder >= 500 && !renderNatively && !BattleUiStyleController.IsPaper;
        PreserveNativePresenters = !optics;
        if (source == null || captureRoot == null || !source.isActiveAndEnabled ||
            !source.gameObject.activeInHierarchy ||
            !IsVisible() ||
            captureRoot.renderMode != RenderMode.ScreenSpaceOverlay ||
            !(RenderPipelineManager.currentPipeline is UniversalRenderPipeline) ||
            !optics)
        {
            Restore();
            ReleaseResources();
            return;
        }
        CreateResources();
        if (!EnsureResources()) { Restore(); return; }

        material.SetVector("_UiOptics", optics ? settings : Vector4.zero);
        material.SetVector("_PixelLimits", optics ? new Vector4(limits.x, limits.y, 0f, 0f) : Vector4.zero);
        material.SetVector("_ScreenSize", new Vector4(black.width, black.height, 1f / black.width, 1f / black.height));
        material.SetVector("_TonePattern", Vector4.zero);
        EnsureProtectionTextures(false);
        output.sortingLayerID = source.sortingLayerID;
        output.sortingOrder = source.sortingOrder;
        output.targetDisplay = source.targetDisplay;
        Capture(false);
        output.enabled = true;
    }

    private bool IsVisible()
    {
        missingVisibility.RemoveWhere(graphic => graphic == null);
        foreach (Graphic graphic in missingVisibility)
            if (graphic.isActiveAndEnabled) return false;
        bool visible = false;
        foreach (BattleUiOpticsVisibility modifier in visibility)
        {
            if (modifier == null || !modifier.Visible) continue;
            if (!modifier.isActiveAndEnabled) return false;
            if (modifier.NativePresenter && PreserveNativePresenters) continue;
            visible = true;
        }
        return visible;
    }

    private void CreateResources()
    {
        if (captureLayer < 0) return;
        if (material == null)
        {
            Shader shader = Resources.Load<Shader>("BattleUiOptics");
            Shader hiddenShader = Resources.Load<Shader>("BattleUiOpticsHidden");
            if (shader == null || hiddenShader == null) return;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            hiddenMaterial = new Material(hiddenShader) { hideFlags = HideFlags.HideAndDontSave };
            GameObject cameraObject = new("BattleUiOpticsCamera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.orthographic = true;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.cullingMask = 1 << captureLayer;
            captureCamera.allowHDR = true;
            captureCamera.allowMSAA = false;
            captureCamera.nearClipPlane = 0.1f;
            captureCamera.farClipPlane = CapturePlaneDistance * 2f;
            UniversalAdditionalCameraData data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.volumeLayerMask = 0;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.antialiasing = AntialiasingMode.None;
            GameObject outputObject = new("BattleUiOpticsOutput", typeof(RectTransform));
            outputObject.hideFlags = HideFlags.HideAndDontSave;
            output = outputObject.AddComponent<Canvas>();
            output.renderMode = RenderMode.ScreenSpaceOverlay;
            output.enabled = false;
            GameObject imageObject = new("Optics", typeof(RectTransform), typeof(CanvasRenderer));
            imageObject.transform.SetParent(output.transform, false);
            image = imageObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.material = material;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }

    private bool EnsureResources()
    {
        if (captureCamera == null || material == null) return false;
        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        if (black != null && black.width == width && black.height == height) return true;
        ReleaseTextures();
        black = CreateTexture(width, height, "BattleUiOpticsBlack");
        white = CreateTexture(width, height, "BattleUiOpticsWhite");
        image.texture = black;
        material.SetTexture("_WhiteTex", white);
        captureCamera.orthographicSize = height * 0.5f;
        captureCamera.aspect = (float)width / height;
        captureCamera.transform.position = new Vector3(width * 0.5f, height * 0.5f, -CapturePlaneDistance);
        return true;
    }

    private static RenderTexture CreateTexture(int width, int height, string textureName)
    {
        RenderTexture texture = new(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            name = textureName, hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
        };
        texture.Create();
        return texture;
    }

    private void EnsureProtectionTextures(bool enabled)
    {
        if (!enabled)
        {
            ReleaseTexture(ref protectedBlack);
            ReleaseTexture(ref protectedWhite);
            foreach (BattleUiOpticsVisibility modifier in visibility)
                if (modifier != null) modifier.ReleaseProtection();
            return;
        }
        if (protectedBlack == null)
        {
            protectedBlack = CreateTexture(black.width, black.height, "BattleUiToneProtectedBlack");
            protectedWhite = CreateTexture(black.width, black.height, "BattleUiToneProtectedWhite");
            material.SetTexture("_ProtectedBlack", protectedBlack);
            material.SetTexture("_ProtectedWhite", protectedWhite);
        }
    }

    private void Capture(bool screenTone)
    {
        Camera originalCamera = captureRoot.worldCamera;
        float originalDistance = captureRoot.planeDistance;
        RectTransform rect = (RectTransform)captureRoot.transform;
        Vector3 position = rect.localPosition, scale = rect.localScale;
        Quaternion rotation = rect.localRotation;
        Vector2 size = rect.sizeDelta;
        output.enabled = false;
        removed.Clear();
        foreach (var entry in layers)
        {
            if (entry.Key == null) { removed.Add(entry.Key); continue; }
            entry.Value.layer = entry.Key.gameObject.layer;
        }
        foreach (Transform node in removed) layers.Remove(node);
        bool captured = false;
        try
        {
            foreach (var entry in layers) entry.Key.gameObject.layer = captureLayer;
            ActiveCapture = this;
            IsCapturing = true;
            SetSourceHidden(false);
            RefreshCaptureMaterials();
            foreach (BattleUiOpticsVisibility modifier in visibility)
                if (modifier != null && modifier.NativePresenter && PreserveNativePresenters) modifier.Refresh();
            captureRoot.renderMode = RenderMode.ScreenSpaceCamera;
            captureRoot.worldCamera = captureCamera;
            captureRoot.planeDistance = CapturePlaneDistance;
            captureCamera.targetTexture = black;
            Canvas.ForceUpdateCanvases();
            // Two backgrounds recover coverage without changing any source shader or blend mode.
            captureCamera.backgroundColor = Color.black;
            request.destination = black;
            RenderPipeline.SubmitRenderRequest(captureCamera, request);
            captureCamera.backgroundColor = Color.white;
            request.destination = white;
            RenderPipeline.SubmitRenderRequest(captureCamera, request);
            if (screenTone)
            {
                CapturingProtection = true;
                foreach (BattleUiOpticsVisibility modifier in visibility)
                    if (modifier != null) modifier.Refresh();
                Canvas.ForceUpdateCanvases();
                captureCamera.backgroundColor = Color.black;
                request.destination = protectedBlack;
                RenderPipeline.SubmitRenderRequest(captureCamera, request);
                captureCamera.backgroundColor = Color.white;
                request.destination = protectedWhite;
                RenderPipeline.SubmitRenderRequest(captureCamera, request);
            }
            captured = true;
        }
        finally
        {
            IsCapturing = false;
            CapturingProtection = false;
            captureCamera.targetTexture = null;
            captureRoot.renderMode = RenderMode.ScreenSpaceOverlay;
            captureRoot.worldCamera = originalCamera;
            captureRoot.planeDistance = originalDistance;
            rect.localPosition = position;
            rect.localRotation = rotation;
            rect.localScale = scale;
            rect.sizeDelta = size;
            foreach (var entry in layers)
                if (entry.Key != null) entry.Key.gameObject.layer = entry.Value.layer;
            SetSourceHidden(captured);
            ActiveCapture = null;
            RefreshCaptureMaterials();
            foreach (BattleUiOpticsVisibility modifier in visibility)
                if (modifier != null) modifier.Refresh();
            Canvas.ForceUpdateCanvases();
        }
    }

    private void Restore()
    {
        IsCapturing = false;
        CapturingProtection = false;
        SetSourceHidden(false);
        if (output != null) output.enabled = false;
    }

    private static void RefreshCaptureMaterials()
    {
        foreach (BattleUiOpticsCanvas effect in instances)
            foreach (BattleUiOpticsVisibility modifier in effect.visibility)
                if (modifier != null) modifier.Refresh();
    }

    private void ReleaseTextures()
    {
        if (captureCamera != null) captureCamera.targetTexture = null;
        ReleaseTexture(ref black);
        ReleaseTexture(ref white);
        ReleaseTexture(ref protectedBlack);
        ReleaseTexture(ref protectedWhite);
    }

    private static void ReleaseTexture(ref RenderTexture texture)
    {
        if (texture != null) { texture.Release(); Destroy(texture); texture = null; }
    }

    private void ReleaseResources()
    {
        ReleaseTextures();
        if (material != null) Destroy(material);
        if (hiddenMaterial != null) Destroy(hiddenMaterial);
        if (captureCamera != null) Destroy(captureCamera.gameObject);
        if (output != null) Destroy(output.gameObject);
        material = hiddenMaterial = null;
        captureCamera = null;
        output = null;
        image = null;
        foreach (BattleUiOpticsVisibility modifier in visibility)
            if (modifier != null) modifier.ReleaseProtection();
    }

    private void OnDisable()
    {
        Restore();
        ReleaseResources();
    }

    private void OnDestroy()
    {
        Canvas.preWillRenderCanvases -= ReconcileCanvases;
        instances.Remove(this);
        Restore();
        ReleaseResources();
        foreach (BattleUiOpticsVisibility modifier in visibility)
            if (modifier != null) modifier.Release(this);
    }
}
