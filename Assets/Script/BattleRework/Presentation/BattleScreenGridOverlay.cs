using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BattleScreenGridOverlay : MonoBehaviour
{
    private Camera targetCamera;
    private Canvas gridCanvas;
    private Material runtimeMaterial;
    private bool requested;
    private float contrast;

    private void Awake() => targetCamera = Camera.main;

    private void Start()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        SetEffect(requested, contrast);
    }

    public void SetEffect(bool value, float strength)
    {
        requested = value;
        contrast = Mathf.Clamp(strength, 0f, 0.08f);
        if (value && targetCamera != null && gridCanvas == null)
        {
            Shader shader = Resources.Load<Shader>("BattleScreenGrid");
            if (shader == null) return;
            runtimeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            GameObject root = new("BattleScreenGrid", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            for (int layer = 0; layer < 32; layer++)
                if ((targetCamera.cullingMask & (1 << layer)) != 0)
                {
                    root.layer = layer;
                    break;
                }
            gridCanvas = root.AddComponent<Canvas>();
            gridCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            gridCanvas.worldCamera = targetCamera;
            gridCanvas.planeDistance = targetCamera.nearClipPlane + 0.01f;
            gridCanvas.sortingOrder = short.MaxValue;
            int highestLayerValue = int.MinValue;
            foreach (SortingLayer layer in SortingLayer.layers)
                if (layer.value > highestLayerValue)
                {
                    highestLayerValue = layer.value;
                    gridCanvas.sortingLayerID = layer.id;
                }
            GameObject surface = new("Grid", typeof(RectTransform), typeof(CanvasRenderer));
            surface.layer = root.layer;
            surface.transform.SetParent(root.transform, false);
            RectTransform rect = (RectTransform)surface.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Image image = surface.AddComponent<Image>();
            image.material = runtimeMaterial;
            image.raycastTarget = false;
        }
        if (runtimeMaterial != null) runtimeMaterial.SetFloat("_Contrast", contrast);
        if (gridCanvas != null) gridCanvas.gameObject.SetActive(value && targetCamera != null);
    }

    private void OnDisable()
    {
        if (gridCanvas != null) gridCanvas.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
        if (gridCanvas != null) Destroy(gridCanvas.gameObject);
    }
}
