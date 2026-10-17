using System;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Simula un precio tipo FX: ruido + cambios de humor + picos violentos.
/// </summary>
[Serializable]
public class MarketSimulator
{
    [Header("Precio base")]
    public float startPrice = 14190f;

    [Header("Ruido normal")]
    public float volatility = 0.35f;

    [Header("Cambios de humor")]
    [Range(0f, 0.2f)] public float regimeChangeChance = 0.03f;
    public float maxDrift = 0.25f;
    public Vector2 volatilityMultiplierRange = new Vector2(0.5f, 2.5f);

    [Header("Picos")]
    [Range(0f, 0.1f)] public float spikeChance = 0.012f;
    public float spikeSize = 4f;

    [Header("Modo libre")]
    [Range(0f, 0.05f)] public float meanReversion = 0.002f;

    public float Price { get; private set; }

    float drift, volMult = 1f;
    int spikeTicksLeft;
    float spikePerTick;

    // Escenario actual
    bool scenario;
    float driftBias, volScale = 1f, spikeBias, randomDriftScale = 1f, pull, spikeScale = 1f;
    float? target;

    public void Reset()
    {
        Price = startPrice;
        drift = 0f; volMult = 1f; spikeTicksLeft = 0;
        ClearScenario();
    }

    /// <param name="bias">Tendencia fija por tick (+ sube, - baja).</param>
    /// <param name="volatilityScale">Multiplica el ruido.</param>
    /// <param name="spikeDirection">-1 = picos solo hacia abajo, 1 = solo hacia arriba.</param>
    /// <param name="targetPrice">Precio al que se empuja el mercado (null = ninguno).</param>
    /// <param name="pullStrength">Fuerza del empuje por tick (0.03 - 0.1).</param>
    /// <param name="randomDrift">Cuánto pesan las tendencias aleatorias (0 - 1).</param>
    /// <param name="spikeSizeScale">Multiplica el tamaño de los picos.</param>
    public void SetScenario(float bias, float volatilityScale, float spikeDirection,
                            float? targetPrice = null, float pullStrength = 0f, float randomDrift = 0.3f,
                            float spikeSizeScale = 1f)
    {
        spikeScale = spikeSizeScale;
        scenario = true;
        driftBias = bias;
        volScale = volatilityScale;
        spikeBias = Mathf.Clamp(spikeDirection, -1f, 1f);
        target = targetPrice;
        pull = pullStrength;
        randomDriftScale = randomDrift;
    }

    public void ClearScenario()
    {
        scenario = false;
        driftBias = 0f; volScale = 1f; spikeBias = 0f; target = null; pull = 0f; randomDriftScale = 1f; spikeScale = 1f;
    }

    public float Step()
    {
        if (Random.value < regimeChangeChance)
        {
            drift = Random.Range(-maxDrift, maxDrift);
            volMult = Random.Range(volatilityMultiplierRange.x, volatilityMultiplierRange.y);
        }

        float delta = drift * randomDriftScale + driftBias + Gaussian() * volatility * volMult * volScale;

        if (scenario)
        {
            if (target.HasValue) delta += (target.Value - Price) * pull;
        }
        else
        {
            delta += (startPrice - Price) * meanReversion;
        }

        if (spikeTicksLeft == 0 && Random.value < spikeChance)
        {
            spikeTicksLeft = Random.Range(3, 10);
            float dir = Random.value < 0.5f * (1f + spikeBias) ? 1f : -1f;
            spikePerTick = spikeSize * spikeScale * Random.Range(0.6f, 2.5f) * dir / spikeTicksLeft;
        }
        if (spikeTicksLeft > 0) { delta += spikePerTick; spikeTicksLeft--; }

        Price += delta;
        return Price;
    }

    static float Gaussian()
    {
        float u1 = Mathf.Max(1e-6f, Random.value);
        float u2 = Random.value;
        return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
    }
}
