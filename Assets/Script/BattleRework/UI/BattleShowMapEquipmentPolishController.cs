using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Show/Map 입력과 전투 장비 전환의 마지막 보정 레이어.
///
/// - Show Focus의 사각형 Cutout을 TV Root가 아니라 실제 ScreenInner에 맞춥니다.
/// - Stage Map Hover는 PointerEnter 1프레임 효과가 아니라 커서가 노드 위에 있는 동안 계속 유지합니다.
/// - Combat / Elite / Shop / Event를 색뿐 아니라 전용 런타임 아이콘으로 구분합니다.
/// - 맵 카메라/포커스 반응은 선택 가능한 노드 위에 커서가 있을 때만 활성화합니다.
/// - 1~9 숫자키 전환은 BattleEquipmentSystem의 기존 입력을 다시 사용합니다.
/// - Tab을 누른 상태에서 Mouse Wheel로 이전/다음 Manual Weapon을 순환합니다.
/// - 실제 장착 슬롯이 바뀔 때 짧은 합성 UI 사운드를 재생합니다.
///
/// 기존 SO / Sprite / Scene 직렬화 데이터를 삭제하거나 교체하지 않습니다.
/// 장비 위치 변경은 기존 BattleEquipmentSystem.SwapSlots를 그대로 사용하므로
/// 대상 칸에 장비가 있으면 서로 자리를 맞바꿉니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32790)]
public sealed class BattleShowMapEquipmentPolishController : MonoBehaviour
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string MapContentName = "MapSelectionContent";
    private const string ScreenInnerName = "ScreenInner";
    private const string PointerHitAreaName = "MapPointerHitArea";
    private const string RoomIconName = "RoomTypeIcon";

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleKineticLoadoutUI loadoutUI;
    [SerializeField] private BattleShowFocusController showFocus;
    [SerializeField] private BattleHUD battleHud;
    [SerializeField] private BattleCameraController battleCamera;

    [Header("Map Node Theme")]
    [SerializeField] private Color inkColor = new(0.028f, 0.030f, 0.040f, 0.995f);
    [SerializeField] private Color paperColor = new(0.94f, 0.91f, 0.80f, 1f);
    [SerializeField] private Color combatColor = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color eliteColor = new(1f, 0.18f, 0.48f, 1f);
    [SerializeField] private Color shopColor = new(0.15f, 0.88f, 0.92f, 1f);
    [SerializeField] private Color eventColor = new(0.72f, 0.46f, 1f, 1f);
    [SerializeField] private Color mutedColor = new(0.34f, 0.37f, 0.44f, 0.72f);
    [SerializeField, Min(16f)] private float roomIconSize = 26f;
    [SerializeField, Min(0.03f)] private float mapResolveInterval = 0.10f;

    [Header("Equipment Wheel")]
    [Tooltip("Tab을 누른 상태에서 휠 한 단계마다 이전/다음 Manual Weapon을 장착합니다.")]
    [SerializeField] private bool enableTabMouseWheel = true;
    [SerializeField, Range(0.05f, 1f)] private float swapSoundVolume = 0.28f;

    private RectTransform mapContent;
    private RectTransform screenInner;
    private readonly List<RectTransform> mapNodes = new();
    private readonly Dictionary<BattleNodeType, Sprite> iconSprites = new();
    private readonly List<Texture2D> iconTextures = new();

    private FieldInfo showFocusRectField;
    private FieldInfo selectedIndexField;
    private FieldInfo directionMovedField;
    private FieldInfo boardWasShownField;
    private MethodInfo refreshLoadoutMethod;

    private AudioSource swapAudioSource;
    private AudioClip swapClip;
    private BattleEquipmentSystem subscribedEquipment;
    private int lastEquippedSlot = -2;
    private float nextMapResolve;

    private void Awake()
    {
        ResolveReferences();
        CacheReflection();
        EnsureSwapAudio();
        EnsureRoomIcons();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheReflection();
        EnsureSwapAudio();
        EnsureRoomIcons();
        SubscribeEquipment();
        nextMapResolve = 0f;
    }

    private void OnDisable()
    {
        UnsubscribeEquipment();
        battleHud?.SetMapCursorFocus(false);
        battleCamera?.SetMapCursorTracking(false, Vector2.zero);
    }

    private void OnDestroy()
    {
        UnsubscribeEquipment();

        if (swapClip != null)
            Destroy(swapClip);

        foreach (KeyValuePair<BattleNodeType, Sprite> pair in iconSprites)
            if (pair.Value != null)
                Destroy(pair.Value);
        iconSprites.Clear();

        for (int i = 0; i < iconTextures.Count; i++)
            if (iconTextures[i] != null)
                Destroy(iconTextures[i]);
        iconTextures.Clear();
    }

    private void Update()
    {
        ResolveReferences();
        CacheReflection();
        SubscribeEquipment();
        AlignShowFocusRect();

        if (enableTabMouseWheel)
            UpdateTabWheelInput();

        if (!IsMapSelection())
            return;

        if (Time.unscaledTime >= nextMapResolve)
        {
            nextMapResolve = Time.unscaledTime + Mathf.Max(0.03f, mapResolveInterval);
            ResolveMapNodes();
        }
    }

    private void LateUpdate()
    {
        if (!IsMapSelection())
            return;

        MaintainMapNodeHoverAndIcons();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (loadoutUI == null)
            loadoutUI = FindFirstObjectByType<BattleKineticLoadoutUI>();
        if (showFocus == null)
            showFocus = FindFirstObjectByType<BattleShowFocusController>();
        if (battleHud == null)
            battleHud = FindFirstObjectByType<BattleHUD>();
        if (battleCamera == null)
            battleCamera = FindFirstObjectByType<BattleCameraController>();
    }

    private void CacheReflection()
    {
        if (showFocus != null && showFocusRectField == null)
            showFocusRectField = typeof(BattleShowFocusController).GetField("tvFocusRect", PrivateInstance);

        if (loadoutUI == null)
            return;

        System.Type type = typeof(BattleKineticLoadoutUI);
        selectedIndexField ??= type.GetField("selectedIndex", PrivateInstance);
        directionMovedField ??= type.GetField("directionMoved", PrivateInstance);
        boardWasShownField ??= type.GetField("boardWasShown", PrivateInstance);
        refreshLoadoutMethod ??= type.GetMethod("RefreshAll", PrivateInstance);
    }

    private bool IsCombat()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.Combat;
    }

    private bool IsMapSelection()
    {
        return runManager != null && runManager.RunActive && runManager.State == BattleRunState.SelectingNode;
    }

    // ---------------------------------------------------------------------
    // Show Focus alignment
    // ---------------------------------------------------------------------

    private void AlignShowFocusRect()
    {
        if (showFocus == null || showFocusRectField == null)
            return;

        if (screenInner == null || !screenInner.gameObject.activeInHierarchy)
            screenInner = FindRect(ScreenInnerName);

        if (screenInner != null && screenInner.gameObject.activeInHierarchy)
            showFocusRectField.SetValue(showFocus, screenInner);
    }

    // ---------------------------------------------------------------------
    // Map hover + room icons
    // ---------------------------------------------------------------------

    private void ResolveMapNodes()
    {
        if (mapContent == null || !mapContent.gameObject.activeInHierarchy)
            mapContent = FindRect(MapContentName);

        mapNodes.Clear();
        if (mapContent == null)
            return;

        RectTransform[] all = mapContent.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect == null || !rect.name.StartsWith("StageNode_", System.StringComparison.Ordinal))
                continue;

            mapNodes.Add(rect);
            EnsureNodeIcon(rect);
        }
    }

    private void MaintainMapNodeHoverAndIcons()
    {
        if (mapContent == null || mapNodes.Count == 0)
            ResolveMapNodes();
        if (mapContent == null)
            return;

        Camera eventCamera = Camera.main;
        RectTransform hoveredNode = null;

        for (int i = 0; i < mapNodes.Count; i++)
        {
            RectTransform node = mapNodes[i];
            if (node == null || !node.gameObject.activeInHierarchy)
                continue;

            Button button = node.GetComponent<Button>();
            bool selectable = button != null && button.interactable;
            RectTransform hitRect = node.Find(PointerHitAreaName) as RectTransform;
            if (hitRect == null)
                hitRect = node;

            bool hovered = selectable && RectTransformUtility.RectangleContainsScreenPoint(
                hitRect,
                Input.mousePosition,
                eventCamera);

            if (hovered)
                hoveredNode = node;

            ApplyNodeVisual(node, selectable, hovered);
        }

        // 기존 SpatialMapController는 Map 패널 전체에 커서가 들어오면 Focus를 켭니다.
        // 여기서 마지막 정책을 덮어써 실제 선택 가능한 Node 위에 있을 때만 반응하게 합니다.
        bool hasHoveredNode = hoveredNode != null;
        battleHud?.SetMapCursorFocus(hasHoveredNode);

        if (battleCamera != null)
        {
            Vector2 normalized = hasHoveredNode
                ? ResolveNodeNormalizedPosition(hoveredNode)
                : Vector2.zero;
            battleCamera.SetMapCursorTracking(hasHoveredNode, normalized);
        }
    }

    private void ApplyNodeVisual(RectTransform node, bool selectable, bool hovered)
    {
        Image background = node.GetComponent<Image>();
        Outline outline = node.GetComponent<Outline>();
        Text label = node.Find("Label")?.GetComponent<Text>();
        Image icon = node.Find(RoomIconName)?.GetComponent<Image>();
        BattleNodeType type = ResolveNodeType(label != null ? label.text : string.Empty);
        Color accent = ResolveTypeColor(type);

        bool current = runManager != null && runManager.CurrentNode != null &&
                       node.name == $"StageNode_{runManager.CurrentNode.id}";

        if (background != null)
        {
            if (hovered)
                background.color = accent;
            else if (current)
                background.color = new Color(shopColor.r * 0.28f, shopColor.g * 0.28f, shopColor.b * 0.28f, 1f);
            else if (selectable)
                background.color = inkColor;
            else
                background.color = new Color(0.09f, 0.095f, 0.12f, 0.94f);
        }

        if (outline != null)
        {
            if (hovered)
            {
                outline.effectColor = paperColor;
                outline.effectDistance = new Vector2(6f, -6f);
            }
            else if (current)
            {
                outline.effectColor = shopColor;
                outline.effectDistance = new Vector2(3f, -3f);
            }
            else if (selectable)
            {
                outline.effectColor = accent;
                outline.effectDistance = new Vector2(3f, -3f);
            }
            else
            {
                outline.effectColor = new Color(mutedColor.r, mutedColor.g, mutedColor.b, 0.24f);
                outline.effectDistance = new Vector2(1f, -1f);
            }
        }

        if (icon != null)
        {
            icon.sprite = ResolveIcon(type);
            icon.enabled = icon.sprite != null;
            icon.color = hovered
                ? inkColor
                : current
                    ? shopColor
                    : selectable ? accent : mutedColor;
        }

        if (label != null)
        {
            string typeName = type.ToString().ToUpperInvariant();
            label.text = hovered ? typeName + "\nSELECT" : typeName;
            label.fontStyle = selectable || current ? FontStyle.Bold : FontStyle.Normal;
            label.color = hovered
                ? inkColor
                : current
                    ? shopColor
                    : selectable ? paperColor : mutedColor;
        }
    }

    private void EnsureNodeIcon(RectTransform node)
    {
        if (node == null)
            return;

        RectTransform iconRect = node.Find(RoomIconName) as RectTransform;
        Image icon;
        if (iconRect == null)
        {
            GameObject iconObject = new(RoomIconName);
            iconObject.transform.SetParent(node, false);
            iconRect = iconObject.AddComponent<RectTransform>();
            icon = iconObject.AddComponent<Image>();
            icon.raycastTarget = false;
        }
        else
        {
            icon = iconRect.GetComponent<Image>();
            if (icon == null)
                icon = iconRect.gameObject.AddComponent<Image>();
        }

        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.anchoredPosition = new Vector2(0f, 2f);
        iconRect.sizeDelta = Vector2.one * Mathf.Max(16f, roomIconSize);
        iconRect.localRotation = Quaternion.identity;
        iconRect.SetAsLastSibling();
        icon.raycastTarget = false;
        icon.preserveAspect = true;
    }

    private Vector2 ResolveNodeNormalizedPosition(RectTransform node)
    {
        if (mapContent == null || node == null)
            return Vector2.zero;

        Rect rect = mapContent.rect;
        Vector2 local = mapContent.InverseTransformPoint(node.position);
        return new Vector2(
            rect.width > 0.001f ? Mathf.Clamp(local.x / (rect.width * 0.5f), -1f, 1f) : 0f,
            rect.height > 0.001f ? Mathf.Clamp(local.y / (rect.height * 0.5f), -1f, 1f) : 0f);
    }

    private static BattleNodeType ResolveNodeType(string source)
    {
        if (!string.IsNullOrEmpty(source))
        {
            string upper = source.ToUpperInvariant();
            if (upper.Contains("ELITE")) return BattleNodeType.Elite;
            if (upper.Contains("SHOP")) return BattleNodeType.Shop;
            if (upper.Contains("EVENT")) return BattleNodeType.Event;
        }
        return BattleNodeType.Combat;
    }

    private Color ResolveTypeColor(BattleNodeType type)
    {
        return type switch
        {
            BattleNodeType.Elite => eliteColor,
            BattleNodeType.Shop => shopColor,
            BattleNodeType.Event => eventColor,
            _ => combatColor
        };
    }

    private Sprite ResolveIcon(BattleNodeType type)
    {
        EnsureRoomIcons();
        return iconSprites.TryGetValue(type, out Sprite sprite) ? sprite : null;
    }

    private void EnsureRoomIcons()
    {
        if (iconSprites.Count > 0)
            return;

        iconSprites[BattleNodeType.Combat] = CreateRoomIcon(BattleNodeType.Combat);
        iconSprites[BattleNodeType.Elite] = CreateRoomIcon(BattleNodeType.Elite);
        iconSprites[BattleNodeType.Shop] = CreateRoomIcon(BattleNodeType.Shop);
        iconSprites[BattleNodeType.Event] = CreateRoomIcon(BattleNodeType.Event);
    }

    private Sprite CreateRoomIcon(BattleNodeType type)
    {
        const int size = 32;
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false)
        {
            name = $"BattleMapIcon_{type}",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        iconTextures.Add(texture);

        Color32 clear = new(0, 0, 0, 0);
        Color32 white = new(255, 255, 255, 255);
        Color32[] pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        switch (type)
        {
            case BattleNodeType.Elite:
                DrawCrown(pixels, size, white);
                break;
            case BattleNodeType.Shop:
                DrawShopBag(pixels, size, white);
                break;
            case BattleNodeType.Event:
                DrawEventSpark(pixels, size, white);
                break;
            default:
                DrawCrossedSwords(pixels, size, white);
                break;
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size,
            0,
            SpriteMeshType.FullRect);
        sprite.name = $"BattleMapIcon_{type}_Sprite";
        return sprite;
    }

    private static void DrawCrossedSwords(Color32[] pixels, int size, Color32 color)
    {
        DrawLine(pixels, size, 8, 7, 24, 23, 2, color);
        DrawLine(pixels, size, 24, 7, 8, 23, 2, color);
        DrawLine(pixels, size, 7, 9, 12, 4, 2, color);
        DrawLine(pixels, size, 25, 9, 20, 4, 2, color);
        DrawLine(pixels, size, 18, 20, 24, 26, 1, color);
        DrawLine(pixels, size, 14, 20, 8, 26, 1, color);
    }

    private static void DrawCrown(Color32[] pixels, int size, Color32 color)
    {
        DrawLine(pixels, size, 6, 11, 10, 22, 2, color);
        DrawLine(pixels, size, 10, 22, 16, 14, 2, color);
        DrawLine(pixels, size, 16, 14, 22, 22, 2, color);
        DrawLine(pixels, size, 22, 22, 26, 11, 2, color);
        DrawLine(pixels, size, 6, 11, 26, 11, 2, color);
        DrawLine(pixels, size, 8, 8, 24, 8, 2, color);
    }

    private static void DrawShopBag(Color32[] pixels, int size, Color32 color)
    {
        DrawRect(pixels, size, 7, 7, 25, 21, 2, color);
        DrawLine(pixels, size, 11, 21, 11, 25, 2, color);
        DrawLine(pixels, size, 21, 21, 21, 25, 2, color);
        DrawLine(pixels, size, 11, 25, 21, 25, 2, color);
        DrawLine(pixels, size, 16, 10, 16, 18, 1, color);
        DrawLine(pixels, size, 12, 14, 20, 14, 1, color);
    }

    private static void DrawEventSpark(Color32[] pixels, int size, Color32 color)
    {
        DrawLine(pixels, size, 16, 5, 16, 27, 2, color);
        DrawLine(pixels, size, 5, 16, 27, 16, 2, color);
        DrawLine(pixels, size, 9, 9, 23, 23, 1, color);
        DrawLine(pixels, size, 23, 9, 9, 23, 1, color);
        DrawLine(pixels, size, 16, 10, 11, 16, 1, color);
        DrawLine(pixels, size, 16, 10, 21, 16, 1, color);
        DrawLine(pixels, size, 11, 16, 16, 22, 1, color);
        DrawLine(pixels, size, 21, 16, 16, 22, 1, color);
    }

    private static void DrawRect(Color32[] pixels, int size, int minX, int minY, int maxX, int maxY, int thickness, Color32 color)
    {
        DrawLine(pixels, size, minX, minY, maxX, minY, thickness, color);
        DrawLine(pixels, size, maxX, minY, maxX, maxY, thickness, color);
        DrawLine(pixels, size, maxX, maxY, minX, maxY, thickness, color);
        DrawLine(pixels, size, minX, maxY, minX, minY, thickness, color);
    }

    private static void DrawLine(Color32[] pixels, int size, int x0, int y0, int x1, int y1, int thickness, Color32 color)
    {
        int dx = Mathf.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;

        while (true)
        {
            PaintPoint(pixels, size, x0, y0, thickness, color);
            if (x0 == x1 && y0 == y1)
                break;
            int doubled = 2 * error;
            if (doubled >= dy)
            {
                error += dy;
                x0 += sx;
            }
            if (doubled <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private static void PaintPoint(Color32[] pixels, int size, int x, int y, int radius, Color32 color)
    {
        radius = Mathf.Max(0, radius - 1);
        for (int oy = -radius; oy <= radius; oy++)
        {
            for (int ox = -radius; ox <= radius; ox++)
            {
                int px = x + ox;
                int py = y + oy;
                if (px < 0 || py < 0 || px >= size || py >= size)
                    continue;
                pixels[py * size + px] = color;
            }
        }
    }

    // ---------------------------------------------------------------------
    // Equipment wheel + sound
    // ---------------------------------------------------------------------

    private void UpdateTabWheelInput()
    {
        if (!IsCombat() || equipmentSystem == null || BattlePauseController.IsPaused)
            return;
        if (!Input.GetKey(KeyCode.Tab))
            return;

        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) < 0.01f)
            return;

        int direction = wheel > 0f ? -1 : 1;
        int next = equipmentSystem.FindNextWeaponSlot(equipmentSystem.EquippedSlotIndex, direction);
        if (next < 0)
            return;

        if (equipmentSystem.EquipSlot(next))
            SyncLoadoutSelection(next);
    }

    private void SyncLoadoutSelection(int slotIndex)
    {
        if (loadoutUI == null)
            return;

        selectedIndexField?.SetValue(loadoutUI, slotIndex);
        directionMovedField?.SetValue(loadoutUI, true);
        boardWasShownField?.SetValue(loadoutUI, true);
        refreshLoadoutMethod?.Invoke(loadoutUI, null);
    }

    private void SubscribeEquipment()
    {
        if (equipmentSystem == subscribedEquipment)
            return;

        UnsubscribeEquipment();
        if (equipmentSystem == null)
            return;

        subscribedEquipment = equipmentSystem;
        lastEquippedSlot = equipmentSystem.EquippedSlotIndex;
        subscribedEquipment.EquippedSlotChanged += HandleEquippedSlotChanged;
    }

    private void UnsubscribeEquipment()
    {
        if (subscribedEquipment != null)
            subscribedEquipment.EquippedSlotChanged -= HandleEquippedSlotChanged;
        subscribedEquipment = null;
    }

    private void HandleEquippedSlotChanged(int slotIndex)
    {
        if (slotIndex < 0)
        {
            lastEquippedSlot = slotIndex;
            return;
        }

        bool changed = slotIndex != lastEquippedSlot;
        lastEquippedSlot = slotIndex;
        SyncLoadoutSelection(slotIndex);

        if (changed && IsCombat() && !BattlePauseController.IsPaused)
            PlaySwapSound(slotIndex);
    }

    private void EnsureSwapAudio()
    {
        if (swapAudioSource == null)
        {
            swapAudioSource = GetComponent<AudioSource>();
            if (swapAudioSource == null)
                swapAudioSource = gameObject.AddComponent<AudioSource>();
            swapAudioSource.playOnAwake = false;
            swapAudioSource.loop = false;
            swapAudioSource.spatialBlend = 0f;
            swapAudioSource.ignoreListenerPause = true;
            swapAudioSource.volume = Mathf.Clamp01(swapSoundVolume);
        }

        if (swapClip == null)
            swapClip = CreateSwapClip();
    }

    private AudioClip CreateSwapClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.085f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float phase = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleCount;
            float frequency = Mathf.Lerp(620f, 980f, Mathf.SmoothStep(0f, 1f, t));
            phase += 2f * Mathf.PI * frequency / sampleRate;
            float envelope = Mathf.Pow(1f - t, 2.4f);
            float click = i < 36 ? (1f - i / 36f) * 0.14f : 0f;
            samples[i] = (Mathf.Sin(phase) * 0.72f + Mathf.Sin(phase * 2.01f) * 0.16f + click) * envelope;
        }

        AudioClip clip = AudioClip.Create("EquipmentSwap_Pyong", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void PlaySwapSound(int slotIndex)
    {
        EnsureSwapAudio();
        if (swapAudioSource == null || swapClip == null)
            return;

        swapAudioSource.volume = Mathf.Clamp01(swapSoundVolume);
        swapAudioSource.pitch = 0.96f + Mathf.Clamp(slotIndex, 0, 8) * 0.018f;
        swapAudioSource.PlayOneShot(swapClip);
    }

    private static RectTransform FindRect(string objectName)
    {
        RectTransform[] all = UnityEngine.Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == objectName)
                return rect;
        }
        return null;
    }
}

public static class BattleShowMapEquipmentPolishAutoInstaller
{
#if UNITY_EDITOR
    private static bool installQueued;

    [InitializeOnLoadMethod]
    private static void InitializeEditorInstaller()
    {
        EditorApplication.hierarchyChanged -= QueueInstall;
        EditorApplication.hierarchyChanged += QueueInstall;
        QueueInstall();
    }

    private static void QueueInstall()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || installQueued)
            return;
        installQueued = true;
        EditorApplication.delayCall += EnsureEditorComponents;
    }

    private static void EnsureEditorComponents()
    {
        installQueued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null || EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded)
                continue;

            if (manager.GetComponent<BattleShowMapEquipmentPolishController>() != null)
                continue;

            Undo.AddComponent<BattleShowMapEquipmentPolishController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponents()
    {
        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattleShowMapEquipmentPolishController>() == null)
                manager.gameObject.AddComponent<BattleShowMapEquipmentPolishController>();
        }
    }
}
