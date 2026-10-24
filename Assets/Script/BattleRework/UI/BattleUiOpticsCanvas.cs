using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class BattleUiOpticsCanvas : MonoBehaviour
{
    private const float CapturePlaneDistance = 10000f;
    private static int captureLayer = -1;
    private sealed class LayerState { internal int layer; }
    private readonly Dictionary<Transform, LayerState> layers = new();
    private readonly List<Transform> removed = new();
    private readonly UniversalRenderPipeline.SingleCameraRequest request = new();
    private Canvas source, output;
    private readonly List<BattleUiOpticsVisibility> visibility = new();
    private Material hiddenMaterial;
    private bool sourceHidden;
    private CanvasGroup[] sourceGroups;
    private Camera captureCamera;
    private Material material;
    private RenderTexture black, white;
    private RawImage image;

    internal Material HiddenSourceMaterial => sourceHidden ? hiddenMaterial : null;

    internal static void Register(Graphic graphic)
    {
        if (!Application.isPlaying || graphic == null || graphic.canvas == null) return;
        Canvas canvas = graphic.canvas.rootCanvas;
        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay || canvas.sortingOrder < 500) return;
        if (!canvas.TryGetComponent(out BattleUiOpticsCanvas effect))
            effect = canvas.gameObject.AddComponent<BattleUiOpticsCanvas>();
        effect.RegisterGraphic(graphic);
        for (Transform node = graphic.transform; node != null; node = node.parent)
        {
            if (!effect.layers.ContainsKey(node))
                effect.layers.Add(node, new LayerState { layer = node.gameObject.layer });
            if (node == canvas.transform) break;
        }
    }

    private void Awake()
    {
        source = GetComponent<Canvas>();
        sourceGroups = GetComponentsInParent<CanvasGroup>(true);
        foreach (Transform node in GetComponentsInChildren<Transform>(true))
            layers[node] = new LayerState { layer = node.gameObject.layer };
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            RegisterGraphic(graphic);
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
        CreateResources();
    }

    private void RegisterGraphic(Graphic graphic)
    {
        if (!graphic.TryGetComponent(out BattleUiOpticsVisibility modifier))
            modifier = graphic.gameObject.AddComponent<BattleUiOpticsVisibility>();
        if (!visibility.Contains(modifier)) visibility.Add(modifier);
        modifier.Bind(this);
    }

    private void SetSourceHidden(bool hidden)
    {
        if (sourceHidden == hidden) return;
        sourceHidden = hidden;
        foreach (BattleUiOpticsVisibility modifier in visibility)
            if (modifier != null) modifier.Refresh();
    }

    private void LateUpdate()
    {
        if (source == null || !source.isActiveAndEnabled ||
            !IsVisible() ||
            source.renderMode != RenderMode.ScreenSpaceOverlay ||
            !(RenderPipelineManager.currentPipeline is UniversalRenderPipeline) ||
            !BattleColorGradingController.TryGetUiOptics(out Vector4 settings, out Vector2 limits))
        {
            Restore();
            return;
        }
        if (!EnsureResources()) { Restore(); return; }

        material.SetVector("_UiOptics", settings);
        material.SetVector("_PixelLimits", new Vector4(limits.x, limits.y, 0f, 0f));
        material.SetVector("_ScreenSize", new Vector4(black.width, black.height, 1f / black.width, 1f / black.height));
        output.sortingLayerID = source.sortingLayerID;
        output.sortingOrder = source.sortingOrder;
        output.targetDisplay = source.targetDisplay;
        Capture();
        output.enabled = true;
    }

    private bool IsVisible()
    {
        foreach (CanvasGroup group in sourceGroups)
            if (group != null && group.isActiveAndEnabled && group.alpha <= 0f) return false;
        return true;
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
            GameObject cameraObject = new("BattleUiOpticsCamera", typeof(Camera), typeof(UniversalAdditionalCameraData));
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            captureCamera = cameraObject.GetComponent<Camera>();
            captureCamera.enabled = false;
            captureCamera.orthographic = true;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.cullingMask = 1 << captureLayer;
            captureCamera.allowHDR = true;
            captureCamera.allowMSAA = false;
            captureCamera.nearClipPlane = 0.1f;
            captureCamera.farClipPlane = CapturePlaneDistance * 2f;
            UniversalAdditionalCameraData data = cameraObject.GetComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.volumeLayerMask = 0;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.antialiasing = AntialiasingMode.None;
            GameObject outputObject = new("BattleUiOpticsOutput", typeof(RectTransform), typeof(Canvas));
            outputObject.hideFlags = HideFlags.HideAndDontSave;
            output = outputObject.GetComponent<Canvas>();
            output.renderMode = RenderMode.ScreenSpaceOverlay;
            output.enabled = false;
            GameObject imageObject = new("Optics", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            imageObject.transform.SetParent(output.transform, false);
            image = imageObject.GetComponent<RawImage>();
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

    private void Capture()
    {
        Camera originalCamera = source.worldCamera;
        float originalDistance = source.planeDistance;
        RectTransform rect = (RectTransform)source.transform;
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
        try
        {
            foreach (var entry in layers) entry.Key.gameObject.layer = captureLayer;
            SetSourceHidden(false);
            source.renderMode = RenderMode.ScreenSpaceCamera;
            source.worldCamera = captureCamera;
            source.planeDistance = CapturePlaneDistance;
            captureCamera.targetTexture = black;
            Canvas.ForceUpdateCanvases();
            // Two backgrounds recover coverage without changing any source shader or blend mode.
            captureCamera.backgroundColor = Color.black;
            request.destination = black;
            RenderPipeline.SubmitRenderRequest(captureCamera, request);
            captureCamera.backgroundColor = Color.white;
            request.destination = white;
            RenderPipeline.SubmitRenderRequest(captureCamera, request);
        }
        finally
        {
            captureCamera.targetTexture = null;
            source.renderMode = RenderMode.ScreenSpaceOverlay;
            source.worldCamera = originalCamera;
            source.planeDistance = originalDistance;
            rect.localPosition = position;
            rect.localRotation = rotation;
            rect.localScale = scale;
            rect.sizeDelta = size;
            foreach (var entry in layers)
                if (entry.Key != null) entry.Key.gameObject.layer = entry.Value.layer;
            SetSourceHidden(true);
            Canvas.ForceUpdateCanvases();
        }
    }

    private void Restore()
    {
        SetSourceHidden(false);
        if (output != null) output.enabled = false;
    }

    private void ReleaseTextures()
    {
        if (captureCamera != null) captureCamera.targetTexture = null;
        if (black != null) { black.Release(); Destroy(black); black = null; }
        if (white != null) { white.Release(); Destroy(white); white = null; }
    }

    private void OnDisable() => Restore();

    private void OnDestroy()
    {
        Restore();
        ReleaseTextures();
        if (material != null) Destroy(material);
        if (hiddenMaterial != null) Destroy(hiddenMaterial);
        if (captureCamera != null) Destroy(captureCamera.gameObject);
        if (output != null) Destroy(output.gameObject);
        foreach (BattleUiOpticsVisibility modifier in visibility)
            if (modifier != null) modifier.Release(this);
    }
}
