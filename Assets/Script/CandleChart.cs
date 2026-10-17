using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CandleChart : MonoBehaviour
{
    [Header("Área (si se deja vacío usa este RectTransform)")]
    public RectTransform chartArea;

    [Header("Tiempo")]
    [Tooltip("Segundos entre cada movimiento del precio.")]
    public float tickInterval = 0.08f;
    [Tooltip("Cuántos ticks forman una vela.")]
    public int ticksPerCandle = 12;
    public int visibleCandles = 30;

    [Header("Aspecto")]
    public Color upColor = new Color(0.25f, 0.65f, 0.15f);
    public Color downColor = new Color(0.86f, 0.0f, 0.1f);
    public Color gridColor = new Color(0f, 0f, 0f, 0.08f);
    public Color labelColor = new Color(0.35f, 0.35f, 0.35f);
    public Color priceLineColor = new Color(0.2f, 0.4f, 0.9f, 0.8f);
    public Color limitColor = new Color(0.55f, 0f, 0f, 0.9f);
    [Range(0.2f, 0.95f)] public float bodyWidth = 0.6f;
    public float wickWidth = 2f;
    public float minBodyHeight = 2f;
    [Range(0f, 0.5f)] public float verticalPadding = 0.12f;
    [Tooltip("Qué tan suave se reajusta la escala vertical.")]
    public float scaleSmoothing = 6f;
    public int gridLines = 6;
    public float labelFontSize = 22f;
    public int priceDecimals = 1;
    [Tooltip("Si está activo, la escala siempre incluye la línea de límite.")]
    public bool alwaysShowLimit = true;

    [Header("Mercado")]
    public MarketSimulator market = new MarketSimulator();
    [Tooltip("Ticks hacia atrás para decidir si va a la alza o a la baja.")]
    public int trendLookbackTicks = 6;

  
    public bool Running = true;
    public float CurrentPrice => market.Price;
    public float Trend { get; private set; }
    public bool IsRising => Trend > 0f;
    public event Action<float> OnTick;

    public void SetLimit(float? price) { limitPrice = price; }

   
    class Candle { public float open, high, low, close; }
    class CandleView { public RectTransform root, body, wick; public Image bodyImg, wickImg; }

    readonly List<Candle> candles = new List<Candle>();
    readonly List<CandleView> views = new List<CandleView>();
    readonly Queue<float> recent = new Queue<float>();
    readonly List<RectTransform> grid = new List<RectTransform>();
    readonly List<TMP_Text> labels = new List<TMP_Text>();

    RectTransform priceLine, limitLine;
    TMP_Text priceTag, limitTag;
    float? limitPrice;
    float timer, viewMin, viewMax;
    int tickInCandle;
    bool scaleInit;

   
    void Awake()
    {
        if (chartArea == null) chartArea = (RectTransform)transform;

        // Cuadrícula y etiquetas de precio
        for (int i = 0; i < gridLines; i++)
        {
            grid.Add(MakeHLine("Grid" + i, gridColor, 1f));
            labels.Add(MakeLabel("Label" + i, labelColor, false));
        }

        // Velas
        for (int i = 0; i < visibleCandles; i++) views.Add(MakeCandleView(i));

        // Línea del precio actual y línea de límite (encima de las velas)
        priceLine = MakeHLine("PriceLine", priceLineColor, 2f);
        priceTag = MakeLabel("PriceTag", priceLineColor, true);
        limitLine = MakeHLine("LimitLine", limitColor, 3f);
        limitTag = MakeLabel("LimitTag", limitColor, true);
        limitTag.rectTransform.anchorMin = limitTag.rectTransform.anchorMax = Vector2.zero;
        limitTag.rectTransform.pivot = Vector2.zero;
        limitTag.text = "LÍMITE";

        // Historia previa para que no empiece vacía
        market.Reset();
        for (int i = 0; i < visibleCandles - 1; i++)
        {
            NewCandle();
            for (int t = 0; t < ticksPerCandle; t++) StepPrice();
        }
        NewCandle();
    }

    void Update()
    {
        if (Running)
        {
            timer += Time.deltaTime;
            while (timer >= tickInterval && Running)
            {
                timer -= tickInterval;
                StepPrice();
                if (++tickInCandle >= ticksPerCandle) { tickInCandle = 0; NewCandle(); }
                OnTick?.Invoke(market.Price);
            }
        }
        Redraw();
    }

    void StepPrice()
    {
        float p = market.Step();
        Candle c = candles[candles.Count - 1];
        c.close = p;
        c.high = Mathf.Max(c.high, p);
        c.low = Mathf.Min(c.low, p);

        recent.Enqueue(p);
        while (recent.Count > Mathf.Max(2, trendLookbackTicks)) recent.Dequeue();
        Trend = p - recent.Peek();
    }

    void NewCandle()
    {
        float p = market.Price;
        candles.Add(new Candle { open = p, high = p, low = p, close = p });
        while (candles.Count > visibleCandles) candles.RemoveAt(0);
    }

    void Redraw()
    {
        // Escala vertical automática y suave
        float min = float.MaxValue, max = float.MinValue;
        foreach (Candle c in candles) { min = Mathf.Min(min, c.low); max = Mathf.Max(max, c.high); }
        if (limitPrice.HasValue && alwaysShowLimit)
        {
            min = Mathf.Min(min, limitPrice.Value);
            max = Mathf.Max(max, limitPrice.Value);
        }
        float range = Mathf.Max(max - min, market.volatility * 4f);
        float targetMin = min - range * verticalPadding;
        float targetMax = max + range * verticalPadding;

        if (!scaleInit) { viewMin = targetMin; viewMax = targetMax; scaleInit = true; }
        else
        {
            float k = 1f - Mathf.Exp(-scaleSmoothing * Time.deltaTime);
            viewMin = Mathf.Lerp(viewMin, targetMin, k);
            viewMax = Mathf.Lerp(viewMax, targetMax, k);
        }
        // Que nunca se salga nada del área aunque el suavizado vaya atrasado
        viewMin = Mathf.Min(viewMin, min);
        viewMax = Mathf.Max(viewMax, max);

        Rect r = chartArea.rect;
        float h = r.height;
        float slot = r.width / visibleCandles;
        int offset = visibleCandles - candles.Count;

        // Velas 
        for (int j = 0; j < views.Count; j++)
        {
            int i = j - offset;
            CandleView v = views[j];
            bool show = i >= 0 && i < candles.Count;
            v.root.gameObject.SetActive(show);
            if (!show) continue;

            Candle c = candles[i];
            float x = slot * (j + 0.5f);
            float yO = Y(c.open, h), yC = Y(c.close, h), yH = Y(c.high, h), yL = Y(c.low, h);
            Color col = c.close >= c.open ? upColor : downColor;
            v.bodyImg.color = col;
            v.wickImg.color = col;

            v.body.anchoredPosition = new Vector2(x, Mathf.Min(yO, yC));
            v.body.sizeDelta = new Vector2(slot * bodyWidth, Mathf.Max(Mathf.Abs(yC - yO), minBodyHeight));
            v.wick.anchoredPosition = new Vector2(x, yL);
            v.wick.sizeDelta = new Vector2(wickWidth, Mathf.Max(yH - yL, 1f));
        }

        // Cuadrícula y etiquetas
        string fmt = "F" + priceDecimals;
        for (int i = 0; i < grid.Count; i++)
        {
            float t = gridLines > 1 ? i / (float)(gridLines - 1) : 0.5f;
            float y = t * h;
            grid[i].anchoredPosition = new Vector2(0f, y);
            labels[i].rectTransform.anchoredPosition = new Vector2(10f, y);
            labels[i].text = Mathf.Lerp(viewMin, viewMax, t).ToString(fmt);
        }

        // Precio actual
        float py = Y(market.Price, h);
        priceLine.anchoredPosition = new Vector2(0f, py);
        priceTag.rectTransform.anchoredPosition = new Vector2(10f, py);
        priceTag.text = market.Price.ToString(fmt);

        // Límite catastrófico
        bool hasLimit = limitPrice.HasValue;
        limitLine.gameObject.SetActive(hasLimit);
        limitTag.gameObject.SetActive(hasLimit);
        if (hasLimit)
        {
            float ly = Y(limitPrice.Value, h);
            limitLine.anchoredPosition = new Vector2(0f, ly);
            limitTag.rectTransform.anchoredPosition = new Vector2(6f, ly + 4f);
        }
    }

    float Y(float price, float h) => (price - viewMin) / Mathf.Max(viewMax - viewMin, 0.0001f) * h;

    // Construcción de elementos UI 

    Image MakeImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    CandleView MakeCandleView(int index)
    {
        var rootGO = new GameObject("Candle" + index, typeof(RectTransform));
        var root = (RectTransform)rootGO.transform;
        root.SetParent(chartArea, false);
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.sizeDelta = Vector2.zero;
        root.anchoredPosition = Vector2.zero;

        Image wick = MakeImage("Wick", root, Color.white); // primero = queda detrás del cuerpo
        Image body = MakeImage("Body", root, Color.white);
        foreach (var rt in new[] { wick.rectTransform, body.rectTransform })
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
        }
        return new CandleView { root = root, body = body.rectTransform, wick = wick.rectTransform, bodyImg = body, wickImg = wick };
    }

    RectTransform MakeHLine(string name, Color color, float thickness)
    {
        Image img = MakeImage(name, chartArea, color);
        RectTransform rt = img.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(0f, thickness);
        return rt;
    }

    TMP_Text MakeLabel(string name, Color color, bool bold)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(chartArea, false);
        var txt = go.GetComponent<TextMeshProUGUI>();
        txt.fontSize = labelFontSize;
        txt.color = color;
        txt.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        txt.raycastTarget = false;
        txt.enableWordWrapping = false;
        RectTransform rt = txt.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f); // a la derecha del área
        rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(160f, 30f);
        return txt;
    }
}
