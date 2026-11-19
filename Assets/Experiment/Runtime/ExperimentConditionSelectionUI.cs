using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>Operator-facing condition selection shown before the Android VR session starts.</summary>
public sealed class ExperimentConditionSelectionUI : MonoBehaviour
{
    public ExperimentSessionController session;
    public bool useBlindedLabels;

    private readonly Dictionary<ExperimentCondition, Button> conditionButtons =
        new Dictionary<ExperimentCondition, Button>();
    private GameObject root;
    private Text title;
    private Text status;
    private Button startButton;
    private Button nextButton;
    private Button repeatButton;
    private Button returnButton;
    private ExperimentCondition? pendingCondition;

    private void Awake()
    {
        if (session == null) session = GetComponent<ExperimentSessionController>();
        if (session == null) session = FindAnyObjectByType<ExperimentSessionController>();
        BuildCanvas();
    }

    private void OnEnable()
    {
        if (session == null) session = GetComponent<ExperimentSessionController>();
        if (session != null) session.StateChanged += HandleStateChanged;
    }

    private void Start() => ShowConditionSelection();

    private void OnDisable()
    {
        if (session != null) session.StateChanged -= HandleStateChanged;
    }

    private void BuildCanvas()
    {
        if (root != null) return;
        EnsureEventSystem();

        root = new GameObject("Experiment Condition UI", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject backdrop = CreateUiObject("Backdrop", root.transform, typeof(Image));
        Stretch(backdrop.GetComponent<RectTransform>());
        backdrop.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.055f, 0.96f);

        GameObject panel = CreateUiObject("Panel", backdrop.transform, typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.08f, 0.08f);
        panelRect.anchorMax = new Vector2(0.92f, 0.92f);
        panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.08f, 0.105f, 0.15f, 0.98f);
        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(48, 48, 56, 56);
        layout.spacing = 24f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        panel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        title = CreateText(panel.transform, "SELECT EXPERIMENT CONDITION", 46, FontStyle.Bold, 100f);
        status = CreateText(panel.transform, string.Empty, 27, FontStyle.Normal, 210f);

        CreateConditionButton(panel.transform, ExperimentCondition.Baseline);
        CreateConditionButton(panel.transform, ExperimentCondition.ResearcherHaptic);
        CreateConditionButton(panel.transform, ExperimentCondition.ParticipantHaptic);

        startButton = CreateButton(panel.transform, "START EXPERIMENT", 112f,
            () =>
            {
                if (pendingCondition.HasValue && session.StartCondition(pendingCondition.Value))
                    root.SetActive(false);
            });
        nextButton = CreateButton(panel.transform, "NEXT SCENARIO", 112f, () => session.PlayNext());
        repeatButton = CreateButton(panel.transform, "REPEAT SAME CONDITION", 112f,
            () =>
            {
                if (session.RepeatSelectedCondition()) root.SetActive(false);
            });
        returnButton = CreateButton(panel.transform, "RETURN TO CONDITION SELECTION", 112f,
            () => session.ReturnToConditionSelection());
    }

    private void CreateConditionButton(Transform parent, ExperimentCondition condition)
    {
        Button button = CreateButton(parent, ConditionLabel(condition), 112f,
            () => SelectCondition(condition));
        conditionButtons.Add(condition, button);
    }

    private void SelectCondition(ExperimentCondition condition)
    {
        pendingCondition = condition;
        RefreshConditionReadiness();
        status.text = $"Selected: {ConditionLabel(condition)}\n3 scenarios: Dental Drill, Toothbrush, Food / Apple";
        startButton.interactable = session.CanStartCondition(condition, out _);
    }

    private void HandleStateChanged(ExperimentSessionState next)
    {
        if (root == null) return;
        switch (next)
        {
            case ExperimentSessionState.ConditionSelection:
                ShowConditionSelection();
                break;
            case ExperimentSessionState.Running:
                root.SetActive(false);
                break;
            case ExperimentSessionState.BetweenTrials:
                ShowBetweenTrials();
                break;
            case ExperimentSessionState.Completed:
                ShowCompleted();
                break;
            case ExperimentSessionState.Error:
                ShowError();
                break;
        }
    }

    private void ShowConditionSelection()
    {
        if (root == null) return;
        root.SetActive(true);
        title.text = "SELECT EXPERIMENT CONDITION";
        pendingCondition = null;
        SetConditionButtonsVisible(true);
        startButton.gameObject.SetActive(true);
        startButton.interactable = false;
        nextButton.gameObject.SetActive(false);
        repeatButton.gameObject.SetActive(false);
        returnButton.gameObject.SetActive(false);
        RefreshConditionReadiness();
    }

    private void RefreshConditionReadiness()
    {
        var lines = new List<string>();
        foreach (KeyValuePair<ExperimentCondition, Button> pair in conditionButtons)
        {
            string reason = "Experiment session is missing.";
            bool ready = session != null && session.CanStartCondition(pair.Key, out reason);
            pair.Value.interactable = ready;
            SetButtonLabel(pair.Value, ConditionLabel(pair.Key) + (ready ? "  ·  READY" : "  ·  NOT READY"));
            if (!ready) lines.Add($"{ConditionLabel(pair.Key)}: {reason}");
        }
        status.text = lines.Count == 0 ? "All conditions are ready." : string.Join("\n", lines);
    }

    private void ShowBetweenTrials()
    {
        root.SetActive(true);
        title.text = "SCENARIO COMPLETE";
        status.text = $"Trial {session.TrialIndex + 1} of {session.TrialCount} complete.\nContinue when the participant is ready.";
        SetConditionButtonsVisible(false);
        startButton.gameObject.SetActive(false);
        nextButton.gameObject.SetActive(true);
        repeatButton.gameObject.SetActive(false);
        returnButton.gameObject.SetActive(false);
    }

    private void ShowCompleted()
    {
        root.SetActive(true);
        title.text = "EXPERIMENT COMPLETE";
        status.text = ExperimentProtocol.DisplayName(session.SelectedCondition) + " completed.";
        SetConditionButtonsVisible(false);
        startButton.gameObject.SetActive(false);
        nextButton.gameObject.SetActive(false);
        repeatButton.gameObject.SetActive(true);
        returnButton.gameObject.SetActive(true);
    }

    private void ShowError()
    {
        root.SetActive(true);
        title.text = "EXPERIMENT CANNOT START";
        status.text = session.LastError;
        SetConditionButtonsVisible(false);
        startButton.gameObject.SetActive(false);
        nextButton.gameObject.SetActive(false);
        repeatButton.gameObject.SetActive(false);
        returnButton.gameObject.SetActive(true);
    }

    private void SetConditionButtonsVisible(bool visible)
    {
        foreach (Button button in conditionButtons.Values) button.gameObject.SetActive(visible);
    }

    private string ConditionLabel(ExperimentCondition condition)
    {
        if (!useBlindedLabels) return ExperimentProtocol.DisplayName(condition);
        if (condition == ExperimentCondition.Baseline) return "CONDITION A";
        if (condition == ExperimentCondition.ResearcherHaptic) return "CONDITION B";
        return "CONDITION C";
    }

    private static Button CreateButton(Transform parent, string label, float height,
        UnityEngine.Events.UnityAction callback)
    {
        GameObject buttonObject = CreateUiObject(label, parent, typeof(Image), typeof(Button),
            typeof(LayoutElement));
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.15f, 0.42f, 0.68f, 1f);
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(callback);
        buttonObject.GetComponent<LayoutElement>().preferredHeight = height;
        Text text = CreateText(buttonObject.transform, label, 30, FontStyle.Bold, height);
        Stretch(text.rectTransform);
        text.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    private static Text CreateText(Transform parent, string value, int size, FontStyle style,
        float height)
    {
        GameObject textObject = CreateUiObject("Text", parent, typeof(Text), typeof(LayoutElement));
        Text text = textObject.GetComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        textObject.GetComponent<LayoutElement>().preferredHeight = height;
        return text;
    }

    private static void SetButtonLabel(Button button, string value)
    {
        Text text = button.GetComponentInChildren<Text>();
        if (text != null) text.text = value;
    }

    private static GameObject CreateUiObject(string name, Transform parent, params System.Type[] components)
    {
        GameObject result = new GameObject(name, components);
        result.transform.SetParent(parent, false);
        return result;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        GameObject eventSystem = new GameObject("Experiment Event System", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        eventSystem.AddComponent<InputSystemUIInputModule>();
#else
        eventSystem.AddComponent<StandaloneInputModule>();
#endif
    }
}
