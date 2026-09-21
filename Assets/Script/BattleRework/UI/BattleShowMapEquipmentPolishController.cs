using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Stage Map의 Room Type 아이콘과 Combat 장비 휠 입력을 담당합니다.
///
/// - Map Node 생성/선택/카메라 추적: BattleSpatialMapController.
/// - Map Node 상태/hover/selected 표현: BattleStageMapPurposefulUIController.
/// - 이 클래스: Combat / Elite / Shop / Event 런타임 아이콘, Tab+Wheel 장비 전환, 전환 사운드.
///
/// Tab/LB 소유권은 BattleInputRouter에 두고 Mouse wheel만 Input System device API로 읽습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32790)]
public sealed class BattleShowMapEquipmentPolishController : MonoBehaviour
{
    private const string MapContentName = "MapSelectionContent";
    private const string RoomIconName = "RoomTypeIcon";

    [Header("References")]
    [SerializeField] private BattleRunManager runManager;
    [SerializeField] private BattleEquipmentSystem equipmentSystem;
    [SerializeField] private BattleKineticLoadoutUI loadoutUI;
    [SerializeField] private BattleInputRouter inputRouter;

    [Header("Map Node Theme")]
    [SerializeField] private Color combatColor = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color eliteColor = new(1f, 0.18f, 0.48f, 1f);
    [SerializeField] private Color shopColor = new(0.15f, 0.88f, 0.92f, 1f);
    [SerializeField] private Color eventColor = new(0.72f, 0.46f, 1f, 1f);
    [SerializeField, Min(16f)] private float roomIconSize = 26f;

    [Header("Equipment Wheel")]
    [Tooltip("Tab을 누른 상태에서 휠 한 단계마다 이전/다음 Manual Weapon을 장착합니다.")]
    [SerializeField] private bool enableTabMouseWheel = true;
    [SerializeField, Range(0.05f, 1f)] private float swapSoundVolume = 0.28f;

    private RectTransform mapContent;
    private readonly Dictionary<BattleNodeType, Sprite> iconSprites = new();
    private readonly List<Texture2D> iconTextures = new();

    private AudioSource swapAudioSource;
    private AudioClip swapClip;
    private BattleEquipmentSystem subscribedEquipment;
    private BattleRunManager subscribedRunManager;
    private bool combatActive;
    private bool mapSelectionActive;
    private Coroutine mapResolveRoutine;
    private int lastEquippedSlot = -2;

    private void Awake()
    {
        ResolveReferences();
        EnsureSwapAudio();
        EnsureRoomIcons();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureSwapAudio();
        EnsureRoomIcons();
        SubscribeEquipment();
        SubscribeRunEvents();

        combatActive = IsCombat();
        mapSelectionActive = IsMapSelection();
        if (mapSelectionActive)
            QueueMapResolve();
    }

    private void OnDisable()
    {
        if (mapResolveRoutine != null)
            StopCoroutine(mapResolveRoutine);
        mapResolveRoutine = null;

        UnsubscribeRunEvents();
        UnsubscribeEquipment();
        mapSelectionActive = false;
    }

    private void OnDestroy()
    {
        UnsubscribeRunEvents();
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
        // Actual input sampling only. Run/Map state is event-driven.
        if (runManager == null || equipmentSystem == null || inputRouter == null)
        {
            ResolveReferences();
            SubscribeEquipment();
            SubscribeRunEvents();
        }

        if (enableTabMouseWheel && combatActive)
            UpdateTabWheelInput();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (loadoutUI == null)
            loadoutUI = FindFirstObjectByType<BattleKineticLoadoutUI>();
        if (inputRouter == null && Application.isPlaying)
            inputRouter = BattleInputRouter.ResolveOrCreate(this);
    }

    private bool IsCombat()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.Combat;
    }

    private bool IsMapSelection()
    {
        return runManager != null &&
               runManager.RunActive &&
               runManager.State == BattleRunState.SelectingNode;
    }

    private void SubscribeRunEvents()
    {
        if (subscribedRunManager == runManager)
            return;

        if (subscribedRunManager != null)
        {
            subscribedRunManager.StateChanged -= HandleRunStateChanged;
            subscribedRunManager.NextNodeSelectionRequested -= HandleNextNodeSelectionRequested;
        }

        subscribedRunManager = runManager;
        if (subscribedRunManager != null)
        {
            subscribedRunManager.StateChanged += HandleRunStateChanged;
            subscribedRunManager.NextNodeSelectionRequested += HandleNextNodeSelectionRequested;
        }
    }

    private void UnsubscribeRunEvents()
    {
        if (subscribedRunManager != null)
        {
            subscribedRunManager.StateChanged -= HandleRunStateChanged;
            subscribedRunManager.NextNodeSelectionRequested -= HandleNextNodeSelectionRequested;
        }

        subscribedRunManager = null;
    }

    private void HandleRunStateChanged(BattleRunState state)
    {
        combatActive =
            runManager != null &&
            runManager.RunActive &&
            state == BattleRunState.Combat;

        bool nextMap =
            runManager != null &&
            runManager.RunActive &&
            state == BattleRunState.SelectingNode;

        if (mapSelectionActive == nextMap)
            return;

        mapSelectionActive = nextMap;
        if (mapSelectionActive)
            QueueMapResolve();
    }

    private void HandleNextNodeSelectionRequested(IReadOnlyList<BattleNodeData> _)
    {
        mapSelectionActive = true;
        QueueMapResolve();
    }

    private void QueueMapResolve()
    {
        if (!isActiveAndEnabled)
            return;

        if (mapResolveRoutine != null)
            StopCoroutine(mapResolveRoutine);

        mapResolveRoutine = StartCoroutine(ResolveMapAfterLayout());
    }

    private IEnumerator ResolveMapAfterLayout()
    {
        yield return null;
        mapResolveRoutine = null;

        if (mapSelectionActive)
            ResolveMapNodes();
    }

    private void ResolveMapNodes()
    {
        if (mapContent == null || !mapContent.gameObject.activeInHierarchy)
            mapContent = FindRect(MapContentName);

        if (mapContent == null)
            return;

        RectTransform[] all = mapContent.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect == null ||
                !rect.name.StartsWith("StageNode_", System.StringComparison.Ordinal))
            {
                continue;
            }

            EnsureNodeIcon(rect);

            Text label = rect.Find("Label")?.GetComponent<Text>();
            BattleNodeType type = ResolveNodeType(label != null ? label.text : string.Empty);
            Image icon = rect.Find(RoomIconName)?.GetComponent<Image>();
            if (icon != null)
            {
                Sprite target = ResolveIcon(type);
                icon.sprite = target;
                icon.enabled = target != null;
                icon.color = ResolveTypeColor(type);
            }
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

    private void UpdateTabWheelInput()
    {
        if (!combatActive || equipmentSystem == null || BattlePauseController.IsPaused || inputRouter == null)
            return;
        if (!inputRouter.TabHeld || Mouse.current == null)
            return;

        float wheel = Mouse.current.scroll.ReadValue().y;
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
        loadoutUI?.SetSelectedIndexFromExternal(slotIndex, true);
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

        if (changed && combatActive && !BattlePauseController.IsPaused)
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
