using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BattleRunResultUI : MonoBehaviour, IInputModal
{
    private BattleInputRouter inputRouter;
    private BattlePauseController pauseController;
    private GameObject canvasRoot;
    private Button confirmButton;
    private Action onConfirmed;
    private bool confirmed;
    private int shownFrame;

    private void Awake()
    {
        inputRouter = BattleInputRouter.ResolveOrCreate(this);
        pauseController = FindFirstObjectByType<BattlePauseController>();
    }

    public void Show(BattleSceneResult result, bool returnOnConfirm, Action onConfirmed)
    {
        if (canvasRoot != null)
            Destroy(canvasRoot);

        this.onConfirmed = onConfirmed;
        confirmed = false;
        shownFrame = Time.frameCount;
        // Remove Pause's modal before putting the result on top of the stack.
        if (BattlePauseController.IsPaused)
            pauseController?.Resume();

        EnsureEventSystem();
        RectTransform root = CreateRect(transform, "BattleRunResultCanvas");
        canvasRoot = root.gameObject;
        Canvas canvas = canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Keep acknowledgement above the broadcast presentation and reward modals.
        canvas.sortingOrder = BattleUiSortingContract.RewardModal + 10;
        CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRoot.AddComponent<GraphicRaycaster>();

        RectTransform dim = CreateRect(root, "Dim");
        dim.anchorMin = Vector2.zero;
        dim.anchorMax = Vector2.one;
        dim.sizeDelta = Vector2.zero;
        dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        RectTransform panel = CreateRect(dim, "Result");
        panel.sizeDelta = new Vector2(640f, 540f);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.color = new Color(0.035f, 0.04f, 0.065f, 1f);
        BattleUiPanelDecoration.Round(panelImage);
        BattleUiPanelDecoration.Attach(panel, BattleUiPanelDecoration.Purple, false, false);

        string reason = result.endReason switch
        {
            RunEndReason.Clear => "클리어",
            RunEndReason.Death => "사망",
            RunEndReason.Quit => "중도 포기",
            _ => "전투 종료"
        };
        Text title = CreateText(panel, reason, 38, new Vector2(0f, 198f), new Vector2(560f, 64f));
        title.name = "Title";
        Text metrics = CreateText(panel,
            $"인기  {result.popularity:N0}\n팬 포인트  {result.fanPoints:N0}\n시청자  {result.viewers:N0}\n좋아요  {result.likes:N0}",
            26, new Vector2(0f, 28f), new Vector2(480f, 216f));
        metrics.alignment = TextAnchor.MiddleLeft;
        metrics.lineSpacing = 1.35f;

        string note = returnOnConfirm
            ? "결과를 확인한 뒤 이전 화면으로 돌아갑니다."
            : string.IsNullOrWhiteSpace(result.returnSceneName)
                ? "복귀할 씬이 없어 현재 씬에 머뭅니다."
                : "현재 씬에 머뭅니다. 자동 복귀가 꺼져 있거나 복귀 씬을 사용할 수 없습니다.";
        CreateText(panel, note, 18, new Vector2(0f, -126f), new Vector2(560f, 64f));

        RectTransform buttonRect = CreateRect(panel, "Confirm");
        buttonRect.anchoredPosition = new Vector2(0f, -208f);
        buttonRect.sizeDelta = new Vector2(320f, 64f);
        Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
        buttonImage.color = new Color(0.15f, 0.88f, 0.92f, 1f);
        confirmButton = buttonRect.gameObject.AddComponent<Button>();
        confirmButton.targetGraphic = buttonImage;
        confirmButton.onClick.AddListener(Confirm);
        Text label = CreateText(buttonRect, returnOnConfirm ? "돌아가기" : "확인", 24, Vector2.zero, buttonRect.sizeDelta);
        label.color = Color.black;

        BattleUiStyleController.RegisterRoot(root);
        BattleUiStyleBurst.Play(panel);
        inputRouter?.PushModal(this);
        EventSystem.current.SetSelectedGameObject(confirmButton.gameObject);
    }

    private void Confirm()
    {
        if (confirmed || Time.frameCount <= shownFrame)
            return;

        confirmed = true;
        confirmButton.interactable = false;
        Action callback = onConfirmed;
        onConfirmed = null;
        inputRouter?.PopModal(this);
        if (canvasRoot != null)
            canvasRoot.SetActive(false);
        callback?.Invoke();
    }

    // Cancel does not skip result acknowledgement; use the selected confirm button.
    public void RequestClose() { }

    private void OnDisable()
    {
        inputRouter?.PopModal(this);
        if (canvasRoot != null)
            canvasRoot.SetActive(false);
    }

    private static RectTransform CreateRect(Transform parent, string name)
    {
        GameObject go = new(name, typeof(RectTransform));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        return rect;
    }

    private static Text CreateText(Transform parent, string value, int size, Vector2 position, Vector2 bounds)
    {
        RectTransform rect = CreateRect(parent, "Text");
        rect.anchoredPosition = position;
        rect.sizeDelta = bounds;
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = BattleFeedbackFont.Resolve();
        text.text = value;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            GameObject host = new("BattleResultEventSystem", typeof(EventSystem));
            eventSystem = host.GetComponent<EventSystem>();
        }

        StandaloneInputModule legacy = eventSystem.GetComponent<StandaloneInputModule>();
        if (legacy != null)
        {
            legacy.enabled = false;
            Destroy(legacy);
        }

        if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            eventSystem.gameObject.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }
}
