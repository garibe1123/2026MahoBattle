using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// PACK 슬롯의 '전체 프레임'을 실제 UI 선택 상태에만 허용합니다.
///
/// 규칙:
/// - 현재 장착 중이라는 이유만으로 전체 Yellow Outline을 표시하지 않습니다.
/// - Synergy Link라는 이유만으로 전체 Cyan Outline을 표시하지 않습니다.
/// - Hover / Drop Flash도 전체 프레임을 만들지 않습니다.
/// - Reward PACK에서 마우스로 클릭 선택했거나, 패드 커서로 실제 선택 중이거나,
///   패드로 집어 든 슬롯만 InteractionSelectionFrame을 표시합니다.
/// - 장착/시너지 정보는 기존 CellAccent / Grade 색상처럼 작은 보조 표시만 유지합니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(39950)]
public sealed class BattlePackSelectionFramePolicyController : MonoBehaviour
{
    private const int SlotCount = BattleEquipmentSystem.MaxSlotCount;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Header("Frame Policy")]
    [SerializeField] private Color neutralUnlockedFrame = new(0f, 0f, 0f, 0.92f);
    [SerializeField] private Color neutralLockedFrame = new(0f, 0f, 0f, 0.65f);
    [SerializeField] private Color selectedFrame = new(1f, 0.80f, 0.10f, 1f);
    [SerializeField] private Color pickedFrame = new(1f, 0.18f, 0.52f, 1f);

    private BattleRunManager runManager;
    private BattleEquipmentSystem equipmentSystem;
    private BattleInventoryInteractionController interaction;
    private RectTransform packRoot;

    private FieldInfo selectedRewardSlotField;
    private FieldInfo padSelectedSlotField;
    private FieldInfo padPickedSlotField;
    private FieldInfo padModeActiveField;

    private float nextResolveTime;

    private void Awake()
    {
        ResolveReferences();
        CacheInteractionFields();
        ResolvePack();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheInteractionFields();
        ResolvePack();
        nextResolveTime = 0f;
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime >= nextResolveTime)
        {
            nextResolveTime = Time.unscaledTime + 0.12f;
            ResolveReferences();
            CacheInteractionFields();
            ResolvePack();
        }

        ApplyFramePolicy();
    }

    private void ResolveReferences()
    {
        if (runManager == null)
            runManager = FindFirstObjectByType<BattleRunManager>();
        if (equipmentSystem == null)
            equipmentSystem = FindFirstObjectByType<BattleEquipmentSystem>();
        if (interaction == null)
            interaction = FindFirstObjectByType<BattleInventoryInteractionController>();
    }

    private void CacheInteractionFields()
    {
        if (selectedRewardSlotField != null)
            return;

        System.Type type = typeof(BattleInventoryInteractionController);
        selectedRewardSlotField = type.GetField("selectedRewardSlot", PrivateInstance);
        padSelectedSlotField = type.GetField("padSelectedSlot", PrivateInstance);
        padPickedSlotField = type.GetField("padPickedSlot", PrivateInstance);
        padModeActiveField = type.GetField("padModeActive", PrivateInstance);
    }

    private void ResolvePack()
    {
        if (packRoot != null)
            return;

        RectTransform[] all = UnityEngine.Object.FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            RectTransform rect = all[i];
            if (rect != null && rect.name == "BackpackMiniGrid")
            {
                packRoot = rect;
                break;
            }
        }
    }

    private void ApplyFramePolicy()
    {
        if (packRoot == null)
            return;

        bool reward = runManager != null && runManager.RunActive && runManager.State == BattleRunState.Reward;
        int mouseSelected = reward ? ReadInt(selectedRewardSlotField, -1) : -1;
        int padSelected = reward ? ReadInt(padSelectedSlotField, -1) : -1;
        int padPicked = reward ? ReadInt(padPickedSlotField, -1) : -1;
        bool padMode = reward && ReadBool(padModeActiveField, false);

        for (int i = 0; i < SlotCount; i++)
        {
            Transform slotTransform = packRoot.Find($"BackpackCells/BackpackCell_{i}");
            if (!(slotTransform is RectTransform slot))
                continue;

            // BattleKineticItemBarUI가 Equipped / Synergy 상태에 따라 전체 Outline을 다시 칠하더라도
            // 최종 단계에서 항상 중립 프레임으로 되돌립니다.
            Outline baseOutline = slot.GetComponent<Outline>();
            if (baseOutline != null)
            {
                bool unlocked = equipmentSystem != null && equipmentSystem.IsSlotUnlocked(i);
                baseOutline.effectColor = unlocked ? neutralUnlockedFrame : neutralLockedFrame;
                baseOutline.effectDistance = new Vector2(3f, -3f);
            }

            // Equipped 슬롯이 커지는 것도 '선택 프레임처럼 보이는' 원인이므로 제거합니다.
            slot.localScale = Vector3.one;

            Transform frameTransform = slot.Find("InteractionSelectionFrame");
            if (frameTransform == null)
                continue;

            bool mouseSelection = i == mouseSelected;
            bool pickedSelection = i == padPicked;
            bool padCursorSelection = padMode && i == padSelected;
            bool selected = reward && (mouseSelection || pickedSelection || padCursorSelection);

            if (frameTransform.gameObject.activeSelf != selected)
                frameTransform.gameObject.SetActive(selected);

            if (!selected)
                continue;

            Outline selectionOutline = frameTransform.GetComponent<Outline>();
            if (selectionOutline != null)
            {
                selectionOutline.effectColor = pickedSelection ? pickedFrame : selectedFrame;
                selectionOutline.effectDistance = pickedSelection
                    ? new Vector2(7f, -7f)
                    : new Vector2(5f, -5f);
            }
        }
    }

    private int ReadInt(FieldInfo field, int fallback)
    {
        if (interaction == null || field == null)
            return fallback;
        object value = field.GetValue(interaction);
        return value is int number ? number : fallback;
    }

    private bool ReadBool(FieldInfo field, bool fallback)
    {
        if (interaction == null || field == null)
            return fallback;
        object value = field.GetValue(interaction);
        return value is bool flag ? flag : fallback;
    }
}

public static class BattlePackSelectionFramePolicyAutoInstaller
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
        EditorApplication.delayCall += EnsureEditorComponent;
    }

    private static void EnsureEditorComponent()
    {
        installQueued = false;
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

            if (manager.GetComponent<BattlePackSelectionFramePolicyController>() != null)
                continue;

            Undo.AddComponent<BattlePackSelectionFramePolicyController>(manager.gameObject);
            EditorUtility.SetDirty(manager.gameObject);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeComponent()
    {
        BattleSceneManager[] managers = UnityEngine.Object.FindObjectsByType<BattleSceneManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        for (int i = 0; i < managers.Length; i++)
        {
            BattleSceneManager manager = managers[i];
            if (manager != null && manager.GetComponent<BattlePackSelectionFramePolicyController>() == null)
                manager.gameObject.AddComponent<BattlePackSelectionFramePolicyController>();
        }
    }
}
