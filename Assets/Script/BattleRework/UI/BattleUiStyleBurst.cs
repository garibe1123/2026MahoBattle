using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleUiStyleBurst : MaskableGraphic
{
    private static BattleUiStyleBurst instance;
    private Vector2 screenAnchor;
    private float started;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance() => instance = null;

    public static void Play(RectTransform source)
    {
        if (!BattleUiStyleController.IsStylish || source == null) return;
        if (instance == null)
        {
            GameObject root = new("BattleUiStyleBurst", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler));
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            instance = root.AddComponent<BattleUiStyleBurst>();
            instance.raycastTarget = false;
        }
        Canvas sourceCanvas = source.GetComponentInParent<Canvas>();
        Camera camera = sourceCanvas != null && sourceCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? sourceCanvas.worldCamera : null;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, source.TransformPoint(source.rect.center));
        instance.screenAnchor = new Vector2(screen.x / Mathf.Max(1, Screen.width), screen.y / Mathf.Max(1, Screen.height));
        instance.started = Time.unscaledTime;
        instance.gameObject.SetActive(true);
        instance.SetVerticesDirty();
    }

    private void Update()
    {
        BattleUiStyleSettings style = BattleUiStyleController.Settings;
        if (!BattleUiStyleController.IsStylish || style == null ||
            Time.unscaledTime - started >= style.stylishConfirmDuration)
        {
            gameObject.SetActive(false);
            return;
        }
        SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        started = float.NegativeInfinity;
        base.OnDisable();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        BattleUiStyleSettings style = BattleUiStyleController.Settings;
        if (!BattleUiStyleController.IsStylish || style == null) return;
        float progress = Mathf.Clamp01((Time.unscaledTime - started) / Mathf.Max(0.01f, style.stylishConfirmDuration));
        Rect rect = rectTransform.rect;
        Vector2 center = rect.min + Vector2.Scale(screenAnchor, rect.size);
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI / 6f;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 tangent = new(-direction.y, direction.x);
            Color color = i % 3 == 0 ? style.highlight : i % 3 == 1 ? style.stylishNeon : style.status;
            color.a *= 1f - progress;
            float radius = Mathf.Lerp(20f, 150f, 1f - Mathf.Pow(1f - progress, 3f));
            AddQuad(vh, center + direction * radius, direction * (18f * (1f - progress)), tangent * 2f, color);
            Vector2 confetti = center + direction * (radius * 0.72f) + Vector2.down * (progress * progress * 35f);
            float spin = angle + progress * 5f;
            Vector2 axis = new(Mathf.Cos(spin), Mathf.Sin(spin));
            AddQuad(vh, confetti, axis * 5f, new Vector2(-axis.y, axis.x) * 2.5f, color);
        }
    }

    private static void AddQuad(VertexHelper vh, Vector2 center, Vector2 along, Vector2 across, Color color)
    {
        int first = vh.currentVertCount;
        vh.AddVert(center - along - across, color, Vector2.zero);
        vh.AddVert(center - along + across, color, Vector2.zero);
        vh.AddVert(center + along + across, color, Vector2.zero);
        vh.AddVert(center + along - across, color, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }
}
