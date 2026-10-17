using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using Random = UnityEngine.Random;

public class CakeTradeGame : MonoBehaviour
{
    [Serializable]
    public class Turn
    {
        [TextArea] public string hint;
        [Tooltip("Tendencia mientras decides (puntos por tick). Positivo = se ve prometedor.")]
        public float idleDrift = 0.06f;
        public float idleVolatility = 1f;
        [Tooltip("-1 = picos solo hacia abajo, 1 = solo hacia arriba.")]
        [Range(-1f, 1f)] public float idleSpikeDirection = 0f;
        [Tooltip("Tamaño de los picos mientras decides (0.5 = bajones pequeños).")]
        public float idleSpikeScale = 1f;
        [Tooltip("Cuánto pesan los cambios de humor aleatorios (0 = estable, 1 = caótico).")]
        [Range(0f, 1f)] public float idleRandomDrift = 0.3f;
        [Tooltip("Resultado de la puja en créditos (mínimo, máximo).")]
        public Vector2 outcome = new Vector2(20f, 40f);
        public float resolveVolatility = 1f;
        [Range(-1f, 1f)] public float resolveSpikeDirection = 0f;
        [Tooltip("Si está activo, este turno cruza el límite y termina en catástrofe.")]
        public bool catastrophic;
    }

    enum Phase { Deciding, Resolving, Ended }

    [Header("Referencias")]
    public CandleChart chart;
    public Button bidButton;
    public Button noButton;
    public Button restartButton;
    [Tooltip("Botones 'Jugar de nuevo' de los paneles finales.")]
    public Button[] panelRestartButtons;
    public Image cakeImage;
    public TMP_Text creditsText;
    public TMP_Text turnText;
    public TMP_Text messageText;

    [Header("Paneles finales")]
    public GameObject winPanel;
    public TMP_Text winText;
    public GameObject debtPanel;
    public TMP_Text debtText;
    public GameObject catastrophePanel;
    public TMP_Text catastropheText;
    public float endPanelDelay = 1.5f;

    [Header("Sprites del pastel")]
    public Sprite cake1Sprite;       // inicial
    public Sprite cake2Sprite;       // pujaste a la alza
    public Sprite cakeMinus1Sprite;  // pujaste a la baja

    [Header("Reglas")]
    public float startCredits = 100f;
    [Tooltip("Créditos ganados/perdidos por cada punto que se mueve el precio.")]
    public float moneyPerPoint = 10f;
    [Tooltip("Segundos que dura cada puja antes de cobrarse.")]
    public float resolveSeconds = 4f;
    [Tooltip("Fuerza con la que el mercado se dirige al resultado de la puja.")]
    public float resolvePull = 0.06f;
    [Tooltip("Pérdida dentro de una puja que activa la catástrofe.")]
    public float catastrophicLoss = 250f;

    public Turn[] turns =
    {
        // TURNO 1: prometedor. Sube con bajones pequeños; la puja siempre gana.
        new Turn {
            hint = "El mercado se ve prometedor... ¿pujas?",
            idleDrift = 0.06f, idleVolatility = 0.6f,
            idleSpikeDirection = 0.6f, idleSpikeScale = 0.5f, idleRandomDrift = 0.15f,
            outcome = new Vector2(25f, 45f), resolveVolatility = 0.7f, resolveSpikeDirection = 1f
        },
        // TURNO 2: arriesgado. Movimientos bruscos en ambos sentidos; se puede ganar, pero es más fácil perder.
        new Turn {
            hint = "El mercado está agitado: puede subir o caer. ¿Te arriesgas o dices que NO?",
            idleDrift = 0.01f, idleVolatility = 1.6f,
            idleSpikeDirection = 0f, idleSpikeScale = 1.3f, idleRandomDrift = 0.8f,
            outcome = new Vector2(-160f, 90f), resolveVolatility = 1.7f, resolveSpikeDirection = 0f
        },
        // TURNO 3: catastrófico. Todo se desploma; si pujas cruzas el límite y no puedes salir.
        new Turn {
            hint = "El mercado se está desplomando... ¿de verdad vas a pujar?",
            idleDrift = -0.06f, idleVolatility = 1.8f,
            idleSpikeDirection = -0.8f, idleSpikeScale = 1.6f, idleRandomDrift = 0.5f,
            outcome = new Vector2(-450f, -350f), resolveVolatility = 2f, resolveSpikeDirection = -1f,
            catastrophic = true
        },
    };

    Phase phase;
    int turn;
    float credits, entry, resolveTimer;

    void Start()
    {
        credits = startCredits;
        bidButton.onClick.AddListener(Bid);
        noButton.onClick.AddListener(SayNo);
        if (restartButton) restartButton.onClick.AddListener(Restart);
        if (panelRestartButtons != null)
            foreach (Button b in panelRestartButtons) if (b) b.onClick.AddListener(Restart);

        HidePanels();
        chart.SetLimit(null);
        SetCake(cake1Sprite);
        EnterDeciding(turns[0].hint);
    }

    void Update()
    {
        if (phase == Phase.Resolving)
        {
            resolveTimer += Time.deltaTime;
            if (turns[turn].catastrophic && OpenPnL() <= -catastrophicLoss) Catastrophe();
            else if (resolveTimer >= resolveSeconds) FinishBid();
        }
        UpdateUI();
    }

    float OpenPnL() => (chart.CurrentPrice - entry) * moneyPerPoint;

    public void Bid()
    {
        if (phase != Phase.Deciding || turn >= turns.Length) return;

        // El pastel cambia según la dirección de la gráfica al momento de pujar
        bool rising = chart.IsRising;
        SetCake(rising ? cake2Sprite : cakeMinus1Sprite);

        Turn t = turns[turn];
        entry = chart.CurrentPrice;
        float outcome = Random.Range(t.outcome.x, t.outcome.y);
        chart.market.SetScenario(0f, t.resolveVolatility, t.resolveSpikeDirection,
                                 entry + outcome / moneyPerPoint, resolvePull, 0.2f);
        if (t.catastrophic) chart.SetLimit(entry - catastrophicLoss / moneyPerPoint);

        phase = Phase.Resolving;
        resolveTimer = 0f;
        SetButtons(false);
        Say(rising ? "Pujaste a la alza... la puja está en curso." : "Pujaste a la baja... la puja está en curso.");
    }

    public void SayNo()
    {
        if (phase != Phase.Deciding) return;
        EndByBalance();
    }

    public void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

    void EnterDeciding(string message)
    {
        phase = Phase.Deciding;
        Turn t = turns[turn];
        // La gráfica nunca se detiene
        chart.market.SetScenario(t.idleDrift, t.idleVolatility, t.idleSpikeDirection,
                                 null, 0f, t.idleRandomDrift, t.idleSpikeScale);
        SetButtons(true);
        Say(message);
    }

    void FinishBid()
    {
        Turn t = turns[turn];
        float pnl = OpenPnL();
        // Garantiza el signo del resultado si el turno lo define (turno 1 siempre gana)
        if (t.outcome.x > 0f) pnl = Mathf.Max(pnl, t.outcome.x * 0.5f);
        if (t.outcome.y < 0f) pnl = Mathf.Min(pnl, t.outcome.y * 0.5f);
        pnl = Mathf.Round(pnl);

        credits += pnl;
        chart.SetLimit(null);
        turn++;

        string result = pnl >= 0 ? $"¡Ganaste {pnl:F0} créditos!" : $"Perdiste {-pnl:F0} créditos.";
        if (turn >= turns.Length) { Say(result); EndByBalance(); return; }
        EnterDeciding(result + " " + turns[turn].hint);
    }

    void EndByBalance()
    {
        phase = Phase.Ended;
        SetButtons(false);
        float diff = credits - startCredits;

        if (credits >= startCredits)
        {
            string msg = turn == 0
                ? $"No arriesgaste nada y conservaste tus {credits:F0} créditos."
                : $"Dijiste que NO a tiempo.\nTerminaste con {credits:F0} créditos (+{diff:F0}).";
            ShowEnd(winPanel, winText, msg + "\n\nSaber parar también es ganar.");
        }
        else
        {
            string msg = credits < 0
                ? $"Terminaste debiendo {-credits:F0} créditos."
                : $"Perdiste {-diff:F0} créditos. Te quedan {credits:F0}.";
            ShowEnd(debtPanel, debtText, msg + "\n\nCuando ya ganaste, decir que no te protege.");
        }
    }

    void Catastrophe()
    {
        phase = Phase.Ended;
        credits += OpenPnL();
        SetButtons(false);
        Say("¡Cruzaste el límite! Ya no puedes salir...");
        ShowEnd(catastrophePanel, catastropheText,
            $"Ignoraste las señales y no pudiste salir.\nDeuda final: {Mathf.Abs(credits):F0} créditos.\n\n" +
            "Decir que no a tiempo habría evitado esto.");
    }

    void ShowEnd(GameObject panel, TMP_Text text, string msg)
    {
        if (text) text.text = msg;
        StartCoroutine(ShowPanelAfterDelay(panel));
    }

    IEnumerator ShowPanelAfterDelay(GameObject panel)
    {
        yield return new WaitForSeconds(endPanelDelay); // deja ver cómo se mueve la gráfica
        chart.Running = false;
        if (restartButton) restartButton.gameObject.SetActive(false);
        if (panel) panel.SetActive(true);
    }

    // UI

    void UpdateUI()
    {
        if (creditsText)
        {
            if (phase == Phase.Resolving)
            {
                float pnl = OpenPnL();
                creditsText.text = $"Créditos: {credits:F0}  <color={(pnl >= 0 ? "#3a9a1f" : "#d4001a")}>({(pnl >= 0 ? "+" : "")}{pnl:F0})</color>";
            }
            else creditsText.text = $"Créditos: {credits:F0}";
        }
        if (turnText) turnText.text = $"Turno {Mathf.Min(turn + 1, turns.Length)}/{turns.Length}";
    }

    void SetButtons(bool on)
    {
        bidButton.interactable = on;
        noButton.interactable = on;
    }

    void HidePanels()
    {
        if (winPanel) winPanel.SetActive(false);
        if (debtPanel) debtPanel.SetActive(false);
        if (catastrophePanel) catastrophePanel.SetActive(false);
    }

    void SetCake(Sprite s) { if (cakeImage && s) cakeImage.sprite = s; }
    void Say(string msg) { if (messageText) messageText.text = msg; }
}
