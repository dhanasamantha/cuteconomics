using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using TMPro;
using Random = UnityEngine.Random;

/// <summary>
/// "Aprender a decir que no" — trading de pasteles en 3 turnos.
/// La mascota es la voz que te incentiva a pujar: sus diálogos aparecen en el globo
/// y su sprite cambia según el ánimo de cada mensaje.
/// El pastel tiene 3 niveles: sube de nivel si pujas a la alza, baja si pujas a la baja,
/// y se pudre si llegas a la catástrofe.
/// Cada turno tiene su propio audio clip (clips[0], clips[1], clips[2]).
/// </summary>
public class CakeTradeGame : MonoBehaviour
{
    public enum PetMood { Neutral, Happy, Sad }

    [Serializable]
    public class Turn
    {
        [Tooltip("Lo que dice la mascota en este turno para animarte a pujar.")]
        [TextArea] public string hint;
        [Tooltip("Ánimo de la mascota mientras dice el hint.")]
        public PetMood hintMood = PetMood.Happy;

        [Header("Mercado mientras decides")]
        [Tooltip("Tendencia mientras decides (puntos por tick). Positivo = se ve prometedor.")]
        public float idleDrift = 0.06f;
        public float idleVolatility = 1f;
        [Tooltip("-1 = picos solo hacia abajo, 1 = solo hacia arriba.")]
        [Range(-1f, 1f)] public float idleSpikeDirection = 0f;
        [Tooltip("Tamaño de los picos mientras decides (0.5 = bajones pequeños).")]
        public float idleSpikeScale = 1f;
        [Tooltip("Cuánto pesan los cambios de humor aleatorios (0 = estable, 1 = caótico).")]
        [Range(0f, 1f)] public float idleRandomDrift = 0.3f;

        [Header("Resultado de la puja")]
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
    public Image petImage;
    [Tooltip("Globo de diálogo de la mascota (para la animación).")]
    public RectTransform dialogBubble;
    public TMP_Text creditsText;
    public TMP_Text turnText;
    [Tooltip("Texto dentro del globo: el diálogo de la mascota.")]
    public TMP_Text messageText;

    [Header("Paneles finales")]
    public GameObject winPanel;
    public TMP_Text winText;
    public GameObject debtPanel;
    public TMP_Text debtText;
    public GameObject catastrophePanel;
    public TMP_Text catastropheText;
    public float endPanelDelay = 3f;

    [Header("Sprites del pastel")]
    [Tooltip("Niveles del pastel creciendo: Element 0 = nivel 1 (inicial), 1 = nivel 2, 2 = nivel 3.")]
    public Sprite[] cakeLevelSprites = new Sprite[3];
    [Tooltip("Pastel podrido / destruido: aparece en el final catastrófico.")]
    public Sprite cakeRottenSprite;

    [Header("Sprites de la mascota")]
    public Sprite pet1Sprite;        // neutral
    public Sprite pet2Sprite;        // feliz / emocionada
    [FormerlySerializedAs("petinus1Sprite")]
    public Sprite petMinus1Sprite;   // triste / preocupada

    [Header("Sonido")]
    [Tooltip("Un clip por turno: Element 0 = turno 1, Element 1 = turno 2, Element 2 = turno 3.")]
    public AudioClip[] clips = new AudioClip[3];
    [Tooltip("Si está activo, el clip se repite mientras dure el turno (útil para música).")]
    public bool loopClips;
    [Tooltip("AudioSource que reproduce los clips. Si se deja vacío se crea uno solo.")]
    [FormerlySerializedAs("sorce")]
    public AudioSource audioSource;

    [Header("Animación")]
    public float popDuration = 0.25f;
    public float popScale = 0.12f;
    [Tooltip("Rebote del pastel cuando cambia de nivel.")]
    public float cakePopScale = 0.08f;

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
            hint = "¡Mira cómo sube! Es tu gran oportunidad... ¡puja ya!",
            hintMood = PetMood.Happy,
            idleDrift = 0.06f, idleVolatility = 0.6f,
            idleSpikeDirection = 0.6f, idleSpikeScale = 0.5f, idleRandomDrift = 0.15f,
            outcome = new Vector2(25f, 45f), resolveVolatility = 0.7f, resolveSpikeDirection = 1f
        },
        // TURNO 2: arriesgado. Movimientos bruscos; se puede ganar, pero es más fácil perder.
        new Turn {
            hint = "Está un poco movido... ¡pero podrías ganar el doble! ¿Otra más?",
            hintMood = PetMood.Neutral,
            idleDrift = 0.01f, idleVolatility = 1.6f,
            idleSpikeDirection = 0f, idleSpikeScale = 1.3f, idleRandomDrift = 0.8f,
            outcome = new Vector2(-160f, 90f), resolveVolatility = 1.7f, resolveSpikeDirection = 0f
        },
        // TURNO 3: catastrófico. Todo se desploma; si pujas cruzas el límite y no puedes salir.
        new Turn {
            hint = "Se está cayendo... ¡pero seguro rebota! Una más y lo recuperas todo.",
            hintMood = PetMood.Happy,
            idleDrift = -0.06f, idleVolatility = 1.8f,
            idleSpikeDirection = -0.8f, idleSpikeScale = 1.6f, idleRandomDrift = 0.5f,
            outcome = new Vector2(-450f, -350f), resolveVolatility = 2f, resolveSpikeDirection = -1f,
            catastrophic = true
        },
    };

    Phase phase;
    int turn;
    int cakeLevel;   // 0 = nivel 1, 1 = nivel 2, 2 = nivel 3
    float credits, entry, resolveTimer, popTimer, cakePopTimer;

    void Start()
    {
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        credits = startCredits;
        bidButton.onClick.AddListener(Bid);
        noButton.onClick.AddListener(SayNo);
        if (restartButton) restartButton.onClick.AddListener(Restart);
        if (panelRestartButtons != null)
            foreach (Button b in panelRestartButtons) if (b) b.onClick.AddListener(Restart);

        HidePanels();
        chart.SetLimit(null);
        cakeLevel = 0;
        ShowCakeLevel();
        EnterDeciding(turns[0].hint, turns[0].hintMood);
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
        AnimatePop();
    }

    float OpenPnL() => (chart.CurrentPrice - entry) * moneyPerPoint;

    // Acciones

    public void Bid()
    {
        if (phase != Phase.Deciding || turn >= turns.Length) return;

        // Pujar a la alza sube un nivel el pastel; pujar a la baja lo regresa al nivel anterior
        bool rising = chart.IsRising;
        ChangeCakeLevel(rising ? 1 : -1);

        Turn t = turns[turn];
        entry = chart.CurrentPrice;
        float outcome = Random.Range(t.outcome.x, t.outcome.y);
        chart.market.SetScenario(0f, t.resolveVolatility, t.resolveSpikeDirection,
                                 entry + outcome / moneyPerPoint, resolvePull, 0.2f);
        if (t.catastrophic) chart.SetLimit(entry - catastrophicLoss / moneyPerPoint);

        phase = Phase.Resolving;
        resolveTimer = 0f;
        SetButtons(false);
        if (rising) Say("¡Eso! ¡Va para arriba, el pastel está creciendo!", PetMood.Happy);
        else Say("Mmm... va bajando, pero seguro rebota.", PetMood.Sad);
    }

    public void SayNo()
    {
        if (phase != Phase.Deciding) return;
        EndByBalance();
    }

    public void Restart() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

    // Flujo

    void EnterDeciding(string message, PetMood mood)
    {
        phase = Phase.Deciding;
        Turn t = turns[turn];

        chart.market.SetScenario(t.idleDrift, t.idleVolatility, t.idleSpikeDirection,
                                 null, 0f, t.idleRandomDrift, t.idleSpikeScale);
        PlayTurnClip(turn);
        SetButtons(true);
        Say(message, mood);
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

        bool won = pnl >= 0;
        string result = won ? $"¡Ganaste {pnl:F0} créditos!" : $"Uy, perdiste {-pnl:F0} créditos...";
        if (turn >= turns.Length)
        {
            Say(result, won ? PetMood.Happy : PetMood.Sad);
            EndByBalance();
            return;
        }
        Turn next = turns[turn];
        EnterDeciding(result + " " + next.hint, won ? next.hintMood : PetMood.Sad);
    }

    void EndByBalance()
    {
        phase = Phase.Ended;
        SetButtons(false);
        float diff = credits - startCredits;

        if (credits >= startCredits)
        {
            Say("Bueno... ¡supongo que hiciste bien en parar!", PetMood.Neutral);
            string msg = turn == 0
                ? $"No arriesgaste nada y conservaste tus {credits:F0} créditos."
                : $"Dijiste que NO a tiempo.\nTerminaste con {credits:F0} créditos (+{diff:F0}).";
            ShowEnd(winPanel, winText, msg + "\n\nSaber parar también es ganar.");
        }
        else
        {
            Say("Ay... debimos parar antes.", PetMood.Sad);
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
        RotCake(); // en el último turno todo se fue a la baja: el pastel se pudre
        Say("¡Cruzamos el límite! El pastel se pudrió...", PetMood.Sad);
        ShowEnd(catastrophePanel, catastropheText,
            $"Le hiciste caso a la tentación y no pudiste salir.\nDeuda final: {Mathf.Abs(credits):F0} créditos.\n\n" +
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

    // Pastel

    /// <summary>+1 sube un nivel, -1 regresa al nivel anterior. Se queda entre nivel 1 y nivel 3.</summary>
    void ChangeCakeLevel(int delta)
    {
        int max = (cakeLevelSprites != null && cakeLevelSprites.Length > 0) ? cakeLevelSprites.Length - 1 : 0;
        cakeLevel = Mathf.Clamp(cakeLevel + delta, 0, max);
        ShowCakeLevel();
    }

    void ShowCakeLevel()
    {
        if (cakeLevelSprites == null || cakeLevelSprites.Length == 0) return;
        SetCake(cakeLevelSprites[Mathf.Clamp(cakeLevel, 0, cakeLevelSprites.Length - 1)]);
    }

    void RotCake() => SetCake(cakeRottenSprite);

    void SetCake(Sprite s)
    {
        if (cakeImage && s) cakeImage.sprite = s;
        cakePopTimer = popDuration;
    }

    // Audio

    void PlayTurnClip(int index)
    {
        if (audioSource == null || clips == null || index < 0 || index >= clips.Length) return;
        AudioClip c = clips[index];
        if (c == null) return;
        audioSource.Stop();
        audioSource.clip = c;
        audioSource.loop = loopClips;
        audioSource.Play();
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

    void SetPet(PetMood mood)
    {
        if (!petImage) return;
        Sprite s = mood == PetMood.Happy ? pet2Sprite :
                   mood == PetMood.Sad ? petMinus1Sprite : pet1Sprite;
        if (s) petImage.sprite = s;
    }

    /// <summary>La mascota "dice" el mensaje: cambia su sprite y rebota.</summary>
    void Say(string msg, PetMood mood)
    {
        if (messageText) messageText.text = msg;
        SetPet(mood);
        popTimer = popDuration;
    }

    void AnimatePop()
    {
        if (popTimer > 0f)
        {
            popTimer = Mathf.Max(0f, popTimer - Time.deltaTime);
            float s = 1f + popScale * Mathf.Sin(Mathf.PI * (1f - popTimer / popDuration));
            Vector3 scale = new Vector3(s, s, 1f);
            if (petImage) petImage.rectTransform.localScale = scale;
            if (dialogBubble) dialogBubble.localScale = scale;
        }
        if (cakePopTimer > 0f)
        {
            cakePopTimer = Mathf.Max(0f, cakePopTimer - Time.deltaTime);
            float s = 1f + cakePopScale * Mathf.Sin(Mathf.PI * (1f - cakePopTimer / popDuration));
            if (cakeImage) cakeImage.rectTransform.localScale = new Vector3(s, s, 1f);
        }
    }
}