using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 전투/Reward의 인벤토리 HUD를 하나의 좌측 하단 3x3 PACK으로 통일합니다.
///
/// - 기존 중앙 하단 EquipmentDock / RewardLoadoutStrip은 항상 숨깁니다.
/// - BackpackMiniGrid를 약 30~35% 확대합니다.
/// - 셀/아이콘/헤더/잠금 마크도 함께 확대합니다.
/// - Reward의 TRASH는 확대된 PACK 오른쪽으로 이동합니다.
///
/// 장비 SO / Sprite / Scene 직렬화 데이터는 변경하지 않습니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32780)]
public sealed class BattleInventoryHudLayoutPolishController : MonoBehaviour
{
    private const string PackName = "BackpackMiniGrid";
    private const string EquipmentDockName = "EquipmentDock";
    private const string RewardStripName = "RewardLoadoutStrip";
    private const string TrashName = "InventoryTrash";

    private const float CellSize = 78f;
    private const float CellGap = 7f;
    private const float GridTotal = CellSize * 3f + CellGap * 2f;

    [Header("PACK Layout")]
    [SerializeField] private Vector2 packSize = new(304f, 326f);
    [SerializeField] private Vector2 packScreenPosition = new(22f, 22f);
    [SerializeField] private Vector2 gridOffset = new(20f, 18f);
    [SerializeField] private Vector2 headerSize = new(126f, 40f);
    [SerializeField] private Vector2 iconSize = new(64f, 64f);

    [Header("Reward Trash")]
    [SerializeField] private Vector2 rewardTrashPosition = new(344f, 30f);
    [SerializeField] private Vector2 rewardTrashSize = new(184f, 72f);

    private BattleRunManager runManager;
    private RectTransform packRoot;
    private RectTransform packCells;
    private RectTransform trashRoot;
    private float nextResolveTime;

    private void Awake()
    {
        ResolveReferences();
        ResolveUi();
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveUi();
        nextResolveTime = 0f;
    }

    private void Update()
    {
        ResolveReferences();

        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.20f;
            ResolveUi();
            ApplyPackLayout();
        }
    }

    private void LateUpdate()
    {
        // BattleHUD / Reward UI가 같은 프레임에서 다시 켜도 최종적으로 중복 Bar를 숨깁니다.
        HideDuplicateBars();

        if (packRoot == null)
            ResolveUi();

        ApplyPackLayout();
        ApplyRewardTrashLayout();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
    }

    private void ResolveUi()
    {
        if (packRoot == null)
            packRoot = FindRect(PackName);

        if (packRoot != null && packCells == null)
            packCells = packRoot.Find("BackpackCells") as RectTransform;

        if (trashRoot == null)
            trashRoot = FindRect(TrashName);
    }

    private void HideDuplicateBars()
    {
        HideByName(EquipmentDockName);
        HideByName(RewardStripName);
    }

    private static void HideByName(string objectName)
    {
        RectTransform rect = FindRect(objectName);
        if (rect != null && rect.gameObject.activeSelf)
            rect.gameObject.SetActive(false);
    }

    private void ApplyPackLayout()
    {
        if (packRoot == null)
            return;

        packRoot.anchorMin = packRoot.anchorMax = Vector2.zero;
        packRoot.pivot = Vector2.zero;
        packRoot.sizeDelta = packSize;
        packRoot.anchoredPosition = packScreenPosition;
        packRoot.localScale = Vector3.one;
        packRoot.localRotation = Quaternion.Euler(0f, 0f, -1.15f);

        RectTransform header = packRoot.Find("PackHeaderTag") as RectTransform;
        if (header != null)
        {
            header.sizeDelta = headerSize;
            header.anchoredPosition = new Vector2(10f, -5f);
        }

        Text[] headerTexts = packRoot.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < headerTexts.Length; i++)
        {
            Text text = headerTexts[i];
            if (text == null)
                continue;

            if (text.text == "PACK")
                text.fontSize = 18;
            else if (text.text != null && text.text.Contains("/ 9"))
                text.fontSize = 12;
            else if (text.text != null && (text.text.Contains("TAB") || text.text.Contains("LB")))
                text.fontSize = 9;
        }

        if (packCells == null)
            packCells = packRoot.Find("BackpackCells") as RectTransform;
        if (packCells == null)
            return;

        packCells.anchorMin = packCells.anchorMax = Vector2.zero;
        packCells.pivot = Vector2.zero;
        packCells.sizeDelta = new Vector2(GridTotal, GridTotal);
        packCells.anchoredPosition = gridOffset;

        for (int i = 0; i < BattleEquipmentSystem.MaxSlotCount; i++)
        {
            RectTransform slot = packCells.Find($"BackpackCell_{i}") as RectTransform;
            if (slot == null)
                continue;

            int x = i % BattleEquipmentSystem.GridSize;
            int y = i / BattleEquipmentSystem.GridSize;

            slot.sizeDelta = new Vector2(CellSize, CellSize);
            slot.anchorMin = slot.anchorMax = Vector2.zero;
            slot.pivot = new Vector2(0.5f, 0.5f);
            slot.anchoredPosition = new Vector2(
                x * (CellSize + CellGap) + CellSize * 0.5f,
                GridTotal - (y * (CellSize + CellGap) + CellSize * 0.5f));

            Transform iconTransform = slot.Find("Icon");
            if (iconTransform is RectTransform iconRect)
            {
                iconRect.sizeDelta = iconSize;
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = Vector2.zero;
            }

            RectTransform accent = slot.Find("CellAccent") as RectTransform;
            if (accent != null)
            {
                accent.sizeDelta = new Vector2(6f, CellSize - 10f);
                accent.anchoredPosition = new Vector2(4f, 0f);
            }

            Transform lockMark = slot.Find($"LockMark_{i}");
            if (lockMark != null)
            {
                RectTransform[] lockParts = lockMark.GetComponentsInChildren<RectTransform>(true);
                for (int p = 0; p < lockParts.Length; p++)
                {
                    RectTransform part = lockParts[p];
                    if (part != null && part.name == "LockSlash")
                        part.sizeDelta = new Vector2(50f, 5f);
                }
            }

            Text[] slotTexts = slot.GetComponentsInChildren<Text>(true);
            for (int t = 0; t < slotTexts.Length; t++)
            {
                Text text = slotTexts[t];
                if (text != null)
                    text.fontSize = Mathf.Max(text.fontSize, 10);
            }
        }
    }

    private void ApplyRewardTrashLayout()
    {
        if (trashRoot == null)
            return;

        bool reward = runManager != null &&
                      runManager.RunActive &&
                      runManager.State == BattleRunState.Reward;
        if (!reward)
            return;

        trashRoot.anchorMin = trashRoot.anchorMax = Vector2.zero;
        trashRoot.pivot = Vector2.zero;
        trashRoot.sizeDelta = rewardTrashSize;
        trashRoot.anchoredPosition = rewardTrashPosition;
        trashRoot.localRotation = Quaternion.Euler(0f, 0f, -1.6f);

        Text label = trashRoot.GetComponentInChildren<Text>(true);
        if (label != null)
            label.fontSize = 17;
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

#if UNITY_EDITOR
[InitializeOnLoad]
internal static class BattleInventoryHudLayoutPolishEditorInstaller
{
    private static bool queued;

    static BattleInventoryHudLayoutPolishEditorInstaller()
    {
        EditorApplication.hierarchyChanged -= QueueInstall;
        EditorApplication.hierarchyChanged += QueueInstall;
        QueueInstall();
    }

    private static void QueueInstall()
    {
        if (queued || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        queued = true;
        EditorApplication.delayCall += Install;
    }

    private static void Install()
    {
        queued = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        BattleSceneManager[] managers = Resources.FindObjectsOfTypeAll<BattleSceneManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null ||
                EditorUtility.IsPersistent(manager) ||
                !manager.gameObject.scene.IsValid() ||
                !manager.gameObject.scene.isLoaded)
                continue;

            if (manager.GetComponent<BattleInventoryHudLayoutPolishController>() != null)
                continue;

            Undo.AddComponent<BattleInventoryHudLayoutPolishController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
}
#endif

internal static class BattleInventoryHudLayoutPolishRuntimeInstaller
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager == null)
                continue;

            if (manager.GetComponent<BattleInventoryHudLayoutPolishController>() == null)
                manager.gameObject.AddComponent<BattleInventoryHudLayoutPolishController>();
        }
    }
}
