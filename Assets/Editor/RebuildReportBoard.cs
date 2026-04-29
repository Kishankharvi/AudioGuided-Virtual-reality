using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Rebuilds the ReportBoard/ReportCanvas with a refined layout:
/// - Title bar with accent
/// - 3 stat cards (Duration, Accuracy, Grip) with icons + large values
/// - Improvement indicator row
/// - Bar chart area for exercise accuracy
/// - Exercise breakdown table
/// - Styled "New Session" button
/// </summary>
public static class RebuildReportBoard
{
    // Colors
    static readonly Color BgColor = new Color(0.04f, 0.05f, 0.08f, 0.95f);
    static readonly Color CardBg = new Color(0.08f, 0.1f, 0.14f, 0.9f);
    static readonly Color AccentGreen = new Color(0.2f, 0.75f, 0.4f, 1f);
    static readonly Color AccentBlue = new Color(0.25f, 0.55f, 0.9f, 1f);
    static readonly Color AccentGold = new Color(0.95f, 0.78f, 0.2f, 1f);
    static readonly Color TextWhite = new Color(1f, 1f, 1f, 1f);
    static readonly Color TextMuted = new Color(0.6f, 0.62f, 0.68f, 1f);
    static readonly Color DividerColor = new Color(0.2f, 0.22f, 0.28f, 0.6f);
    static readonly Color BtnGreen = new Color(0.15f, 0.6f, 0.35f, 1f);
    static readonly Color BtnGreenHover = new Color(0.2f, 0.7f, 0.4f, 1f);
    static readonly Color BtnGreenPress = new Color(0.1f, 0.5f, 0.3f, 1f);
    static readonly Color ImproveBg = new Color(0.15f, 0.5f, 0.8f, 0.25f);

    public static void Execute()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        var reportCanvas = GameObject.Find("ReportBoard/ReportCanvas");
        if (reportCanvas == null)
        {
            Debug.LogError("[RebuildReport] ReportCanvas not found.");
            return;
        }

        var canvasRT = reportCanvas.GetComponent<RectTransform>();

        // Clear all children
        for (int i = canvasRT.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(canvasRT.GetChild(i).gameObject);
        }

        // ===== BACKGROUND =====
        var bg = CreateImage(canvasRT, "ReportBG", BgColor);
        Stretch(bg);

        // ===== TITLE BAR =====
        var titleBar = CreateRect(canvasRT, "TitleBar");
        AnchorTop(titleBar, 0f, 50f, 0f);

        var titleText = CreateTMP(titleBar, "ReportTitle", "SESSION REPORT", 20f, TextWhite);
        Stretch(titleText.GetComponent<RectTransform>());
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.fontStyle = FontStyles.Bold;
        titleText.characterSpacing = 6f;

        // Accent line under title
        var accent = CreateImage(canvasRT, "ReportAccent", AccentGreen);
        AnchorTop(accent.GetComponent<RectTransform>(), -50f, 3f, 30f);

        // ===== STAT CARDS ROW =====
        var statsRow = CreateRect(canvasRT, "StatsRow");
        AnchorTopStretch(statsRow, -62f, 80f, 20f);

        // Duration card
        var durationCard = CreateStatCard(statsRow, "DurationCard", 0f, 0.333f,
            "⏱", "DURATION", "--:--", AccentBlue);
        // Accuracy card
        var accuracyCard = CreateStatCard(statsRow, "AccuracyCard", 0.333f, 0.666f,
            "◎", "ACCURACY", "--%", AccentGreen);
        // Grip card
        var gripCard = CreateStatCard(statsRow, "GripCard", 0.666f, 1f,
            "✊", "AVG GRIP", "--%", AccentGold);

        // ===== IMPROVEMENT ROW =====
        var improveRow = CreateRect(canvasRT, "ImprovementRow");
        AnchorTopStretch(improveRow, -150f, 28f, 25f);

        var improveBG = CreateImage(improveRow, "ImprovementBG", ImproveBg);
        Stretch(improveBG);

        var improveText = CreateTMP(improveRow, "ImprovementText",
            "First session — great start!", 11f, TextWhite);
        Stretch(improveText.GetComponent<RectTransform>());
        improveText.alignment = TextAlignmentOptions.Center;
        improveText.fontStyle = FontStyles.Italic;

        // ===== CHART SECTION =====
        var chartSection = CreateRect(canvasRT, "ChartSection");
        AnchorTopStretch(chartSection, -186f, 130f, 20f);

        var chartTitle = CreateTMP(chartSection, "ChartTitle",
            "EXERCISE ACCURACY", 10f, TextMuted);
        AnchorTop(chartTitle.GetComponent<RectTransform>(), 0f, 16f, 5f);
        chartTitle.alignment = TextAlignmentOptions.Center;
        chartTitle.characterSpacing = 3f;

        var chartArea = CreateRect(chartSection, "ChartArea");
        chartArea.anchorMin = new Vector2(0.05f, 0.05f);
        chartArea.anchorMax = new Vector2(0.95f, 0.85f);
        chartArea.offsetMin = Vector2.zero;
        chartArea.offsetMax = Vector2.zero;

        // ===== DIVIDER =====
        var divider = CreateImage(canvasRT, "TableDivider", DividerColor);
        AnchorTopStretch(divider.GetComponent<RectTransform>(), -322f, 1f, 25f);

        // ===== TABLE HEADER =====
        var tableHeader = CreateTMP(canvasRT, "TableHeader",
            "EXERCISE BREAKDOWN", 10f, TextMuted);
        AnchorTop(tableHeader.GetComponent<RectTransform>(), -328f, 16f, 25f);
        tableHeader.characterSpacing = 3f;

        // ===== EXERCISE ROWS =====
        var exerciseRows = CreateRect(canvasRT, "ExerciseRows");
        exerciseRows.anchorMin = new Vector2(0.03f, 0.12f);
        exerciseRows.anchorMax = new Vector2(0.97f, 0.27f);
        exerciseRows.offsetMin = Vector2.zero;
        exerciseRows.offsetMax = Vector2.zero;

        var vlg = exerciseRows.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 3f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        // ===== NEW SESSION BUTTON =====
        var btnRT = CreateRect(canvasRT, "NewSessionBtn");
        btnRT.anchorMin = new Vector2(0.3f, 0f);
        btnRT.anchorMax = new Vector2(0.7f, 0f);
        btnRT.pivot = new Vector2(0.5f, 0f);
        btnRT.anchoredPosition = new Vector2(0f, 12f);
        btnRT.sizeDelta = new Vector2(0f, 36f);

        var btnImg = btnRT.gameObject.AddComponent<Image>();
        btnImg.color = BtnGreen;
        btnImg.raycastTarget = true;

        var btn = btnRT.gameObject.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        var colors = btn.colors;
        colors.normalColor = BtnGreen;
        colors.highlightedColor = BtnGreenHover;
        colors.pressedColor = BtnGreenPress;
        colors.fadeDuration = 0.1f;
        btn.colors = colors;

        var btnLabel = CreateTMP(btnRT, "BtnLabel", "▶  NEW SESSION", 13f, TextWhite);
        Stretch(btnLabel.GetComponent<RectTransform>());
        btnLabel.alignment = TextAlignmentOptions.Center;
        btnLabel.fontStyle = FontStyles.Bold;

        // ===== WIRE SessionSummaryUI =====
        var reportBoard = GameObject.Find("ReportBoard");
        if (reportBoard != null)
        {
            var summaryUI = reportBoard.GetComponent<AGVRSystem.UI.SessionSummaryUI>();
            if (summaryUI != null)
            {
                var so = new SerializedObject(summaryUI);

                // Stat labels
                SetRef(so, "_durationText", FindTMP(canvasRT, "DurationCard/Label"));
                SetRef(so, "_accuracyText", FindTMP(canvasRT, "AccuracyCard/Label"));
                SetRef(so, "_gripText", FindTMP(canvasRT, "GripCard/Label"));

                // Stat values
                SetRef(so, "_durationValue", FindTMP(canvasRT, "DurationCard/Value"));
                SetRef(so, "_accuracyValue", FindTMP(canvasRT, "AccuracyCard/Value"));
                SetRef(so, "_gripValue", FindTMP(canvasRT, "GripCard/Value"));

                // Stat icons
                SetRef(so, "_durationIcon", FindTMP(canvasRT, "DurationCard/Icon"));
                SetRef(so, "_accuracyIcon", FindTMP(canvasRT, "AccuracyCard/Icon"));
                SetRef(so, "_gripIcon", FindTMP(canvasRT, "GripCard/Icon"));

                // Chart
                SetRef(so, "_chartArea", chartArea);
                SetRef(so, "_chartTitle", chartTitle);

                // Improvement
                SetRef(so, "_improvementText", improveText);
                SetRef(so, "_improvementBG", improveBG);

                // Table
                SetRef(so, "_exerciseTableParent", exerciseRows);
                SetRef(so, "_tableHeader", tableHeader);

                // Button
                SetRef(so, "_newSessionButton", btn);

                // Prefab
                var prefabProp = so.FindProperty("_exerciseRowPrefab");
                if (prefabProp != null)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ExerciseRow.prefab");
                    if (prefab != null) prefabProp.objectReferenceValue = prefab;
                }

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(summaryUI);
            }
        }

        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);
        Debug.Log("[RebuildReport] ReportBoard rebuilt and saved.");
    }

    // ===== STAT CARD BUILDER =====
    static RectTransform CreateStatCard(RectTransform parent, string name,
        float anchorMinX, float anchorMaxX, string icon, string label, string value, Color accentColor)
    {
        var card = CreateRect(parent, name);
        card.anchorMin = new Vector2(anchorMinX, 0f);
        card.anchorMax = new Vector2(anchorMaxX, 1f);
        card.offsetMin = new Vector2(4f, 4f);
        card.offsetMax = new Vector2(-4f, -4f);

        var cardBg = CreateImage(card, "CardBG", CardBg);
        Stretch(cardBg);

        // Accent strip at top
        var strip = CreateImage(card, "AccentStrip", accentColor);
        var stripRT = strip.GetComponent<RectTransform>();
        stripRT.anchorMin = new Vector2(0.1f, 1f);
        stripRT.anchorMax = new Vector2(0.9f, 1f);
        stripRT.pivot = new Vector2(0.5f, 1f);
        stripRT.anchoredPosition = Vector2.zero;
        stripRT.sizeDelta = new Vector2(0f, 2f);

        // Icon
        var iconTMP = CreateTMP(card, "Icon", icon, 18f, accentColor);
        var iconRT = iconTMP.GetComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0.5f, 0.7f);
        iconRT.anchorMax = new Vector2(0.5f, 0.7f);
        iconRT.sizeDelta = new Vector2(30f, 22f);
        iconRT.anchoredPosition = Vector2.zero;
        iconTMP.alignment = TextAlignmentOptions.Center;

        // Value (large)
        var valTMP = CreateTMP(card, "Value", value, 16f, TextWhite);
        var valRT = valTMP.GetComponent<RectTransform>();
        valRT.anchorMin = new Vector2(0f, 0.25f);
        valRT.anchorMax = new Vector2(1f, 0.6f);
        valRT.offsetMin = Vector2.zero;
        valRT.offsetMax = Vector2.zero;
        valTMP.alignment = TextAlignmentOptions.Center;
        valTMP.fontStyle = FontStyles.Bold;

        // Label
        var lblTMP = CreateTMP(card, "Label", label, 8f, TextMuted);
        var lblRT = lblTMP.GetComponent<RectTransform>();
        lblRT.anchorMin = new Vector2(0f, 0.05f);
        lblRT.anchorMax = new Vector2(1f, 0.25f);
        lblRT.offsetMin = Vector2.zero;
        lblRT.offsetMax = Vector2.zero;
        lblTMP.alignment = TextAlignmentOptions.Center;
        lblTMP.characterSpacing = 2f;

        return card;
    }

    // ===== HELPERS =====
    static RectTransform CreateRect(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static Image CreateImage(RectTransform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    static TMP_Text CreateTMP(RectTransform parent, string name, string text,
        float fontSize, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.raycastTarget = false;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        return tmp;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void Stretch(Image img)
    {
        Stretch(img.GetComponent<RectTransform>());
    }

    static void AnchorTop(RectTransform rt, float yOffset, float height, float xPad)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, yOffset);
        rt.sizeDelta = new Vector2(-xPad * 2f, height);
    }

    static void AnchorTopStretch(RectTransform rt, float yOffset, float height, float xPad)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, yOffset);
        rt.sizeDelta = new Vector2(-xPad * 2f, height);
    }

    static TMP_Text FindTMP(RectTransform root, string path)
    {
        var t = root.Find(path);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    static void SetRef(SerializedObject so, string prop, Object value)
    {
        var p = so.FindProperty(prop);
        if (p != null && value != null) p.objectReferenceValue = value;
    }
}
