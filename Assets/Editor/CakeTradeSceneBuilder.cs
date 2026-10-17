#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Menú: Tools > Pasteles FX > Crear escena de trading
/// Crea Canvas, gráfica, pastel, textos, botones PUJAR / NO / REINICIAR
/// y los 3 paneles finales, todo ya conectado.
/// Este archivo debe ir dentro de una carpeta llamada "Editor".
/// </summary>
public static class CakeTradeSceneBuilder
{
    static readonly Color Green = new Color(0.25f, 0.65f, 0.15f);
    static readonly Color Red = new Color(0.86f, 0f, 0.1f);
    static readonly Color Gray = new Color(0.45f, 0.45f, 0.5f);

    [MenuItem("Tools/Pasteles FX/Crear escena de trading")]
    static void Build()
    {
        var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        Undo.RegisterCreatedObjectUndo(canvasGO, "Crear escena trading");

        if (Object.FindObjectOfType<EventSystem>() == null)
        {
#if ENABLE_INPUT_SYSTEM
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
            Undo.RegisterCreatedObjectUndo(es, "EventSystem");
        }

        Transform root = canvasGO.transform;

        // ---- HUD ----
        AddImage(Rect("Background", root, Vector2.zero, Vector2.one), Color.white);
        Text("Title", root, new Vector2(0.03f, 0.9f), new Vector2(0.5f, 0.98f), "GBP/USD · Pasteles", 40, TextAlignmentOptions.MidlineLeft, true);
        TMP_Text turnTxt = Text("Turn", root, new Vector2(0.5f, 0.9f), new Vector2(0.64f, 0.98f), "Turno 1/3", 34, TextAlignmentOptions.MidlineRight, true);

        RectTransform chartArea = Rect("Chart", root, new Vector2(0.03f, 0.13f), new Vector2(0.64f, 0.88f));
        var chart = chartArea.gameObject.AddComponent<CandleChart>();
        chart.chartArea = chartArea;

        Image cake = AddImage(Rect("Cake", root, new Vector2(0.75f, 0.42f), new Vector2(0.97f, 0.88f)), Color.white);
        cake.preserveAspect = true;

        TMP_Text credits = Text("Credits", root, new Vector2(0.73f, 0.32f), new Vector2(0.99f, 0.4f), "Créditos: 100", 36, TextAlignmentOptions.Center, true);

        Button bid = Btn("BidButton", root, new Vector2(0.73f, 0.2f), new Vector2(0.855f, 0.3f), "PUJAR", Green, 38);
        Button no = Btn("NoButton", root, new Vector2(0.865f, 0.2f), new Vector2(0.99f, 0.3f), "NO", Red, 38);
        Button restart = Btn("RestartButton", root, new Vector2(0.79f, 0.12f), new Vector2(0.93f, 0.18f), "REINICIAR", Gray, 26);

        TMP_Text msg = Text("Message", root, new Vector2(0.03f, 0.02f), new Vector2(0.97f, 0.1f), "", 30, TextAlignmentOptions.MidlineLeft, false);

        // ---- Paneles finales ----
        var win = Panel(root, "WinPanel", "¡GANASTE!", new Color(0.2f, 0.6f, 0.2f));
        var debt = Panel(root, "DebtPanel", "TERMINASTE EN DEUDA", new Color(0.85f, 0.45f, 0.05f));
        var cat = Panel(root, "CatastrophePanel", "FINAL CATASTRÓFICO", new Color(0.5f, 0f, 0.05f));

        // ---- Manager ----
        var managerGO = new GameObject("GameManager");
        Undo.RegisterCreatedObjectUndo(managerGO, "GameManager");
        var game = managerGO.AddComponent<CakeTradeGame>();
        game.chart = chart;
        game.bidButton = bid;
        game.noButton = no;
        game.restartButton = restart;
        game.panelRestartButtons = new[] { win.button, debt.button, cat.button };
        game.cakeImage = cake;
        game.creditsText = credits;
        game.turnText = turnTxt;
        game.messageText = msg;
        game.winPanel = win.panel; game.winText = win.body;
        game.debtPanel = debt.panel; game.debtText = debt.body;
        game.catastrophePanel = cat.panel; game.catastropheText = cat.body;

        win.panel.SetActive(false);
        debt.panel.SetActive(false);
        cat.panel.SetActive(false);

        Selection.activeGameObject = managerGO;
        Debug.Log("Escena creada. Asigna Pastel 1, Pastel 2 y Pastel -1 en GameManager.");
    }

    struct PanelRefs { public GameObject panel; public TMP_Text body; public Button button; }

    static PanelRefs Panel(Transform root, string name, string title, Color color)
    {
        RectTransform overlay = Rect(name, root, Vector2.zero, Vector2.one);
        AddImage(overlay, new Color(0f, 0f, 0f, 0.6f)); // bloquea clics detrás

        RectTransform card = Rect("Card", overlay, new Vector2(0.25f, 0.2f), new Vector2(0.75f, 0.8f));
        AddImage(card, color);

        Text("Title", card, new Vector2(0.05f, 0.75f), new Vector2(0.95f, 0.95f), title, 60, TextAlignmentOptions.Center, true).color = Color.white;
        TMP_Text body = Text("Body", card, new Vector2(0.07f, 0.28f), new Vector2(0.93f, 0.75f), "", 34, TextAlignmentOptions.Center, false);
        body.color = Color.white;
        Button btn = Btn("PlayAgain", card, new Vector2(0.3f, 0.06f), new Vector2(0.7f, 0.22f), "JUGAR DE NUEVO", Color.white, 34);
        btn.GetComponentInChildren<TMP_Text>().color = color;

        return new PanelRefs { panel = overlay.gameObject, body = body, button = btn };
    }

    static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return rt;
    }

    static Image AddImage(RectTransform rt, Color c)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        return img;
    }

    static TMP_Text Text(string name, Transform parent, Vector2 aMin, Vector2 aMax, string txt, float size, TextAlignmentOptions align, bool bold)
    {
        RectTransform rt = Rect(name, parent, aMin, aMax);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = txt; t.fontSize = size; t.alignment = align;
        t.color = new Color(0.15f, 0.15f, 0.15f);
        t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        t.raycastTarget = false;
        return t;
    }

    static Button Btn(string name, Transform parent, Vector2 aMin, Vector2 aMax, string label, Color color, float fontSize)
    {
        RectTransform rt = Rect(name, parent, aMin, aMax);
        AddImage(rt, color);
        var b = rt.gameObject.AddComponent<Button>();
        TMP_Text t = Text("Label", rt, Vector2.zero, Vector2.one, label, fontSize, TextAlignmentOptions.Center, true);
        t.color = Color.white;
        return b;
    }
}
#endif
