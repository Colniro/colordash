using UnityEngine;

// Zentrale Soundausgabe. Da das Projekt (noch) keine Audio-Assets enthält, werden alle
// Effekte beim Start prozedural erzeugt. Wer echte Clips hat, kann diese Komponente
// vorab an ein Szenenobjekt hängen und die Felder befüllen - dann gewinnen die Clips.
[DisallowMultipleComponent]
public class GameAudio : MonoBehaviour
{
    private const int SampleRate = 44100;

    public static GameAudio Instance { get; private set; }

    [Header("Optionale eigene Clips (überschreiben die generierten)")]
    public AudioClip tickClip;
    public AudioClip tickUrgentClip;
    public AudioClip whooshClip;
    public AudioClip fallClip;
    public AudioClip winClip;
    public AudioClip eliminatedClip;
    public AudioClip readyClip;
    public AudioClip unreadyClip;
    public AudioClip roundStartClip;
    public AudioClip uiClip;
    public AudioClip jumpClip;
    public AudioClip landClip;

    private AudioSource source;

    public static GameAudio Ensure()
    {
        if (Instance != null) return Instance;

        GameObject go = new GameObject("GameAudio");
        DontDestroyOnLoad(go);
        return go.AddComponent<GameAudio>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;

        GenerateMissingClips();
        ApplySettings();
        GameSettings.Changed += ApplySettings;
    }

    void OnDestroy()
    {
        GameSettings.Changed -= ApplySettings;
        if (Instance == this) Instance = null;
    }

    private void ApplySettings()
    {
        AudioListener.volume = GameSettings.MasterVolume;
        if (source != null) source.volume = GameSettings.SfxVolume;
    }

    // --- öffentliche Effekte ---

    public void PlayTick(bool urgent) => Play(urgent ? tickUrgentClip : tickClip, urgent ? 0.55f : 0.35f);
    public void PlayWhoosh() => Play(whooshClip, 0.9f);
    public void PlayFall() => Play(fallClip, 0.9f);
    public void PlayWin() => Play(winClip, 1f);
    public void PlayEliminated() => Play(eliminatedClip, 0.85f);
    public void PlayReady(bool ready) => Play(ready ? readyClip : unreadyClip, 0.7f);
    public void PlayRoundStart() => Play(roundStartClip, 0.85f);
    public void PlayUi() => Play(uiClip, 0.5f);
    public void PlayJump() => Play(jumpClip, 0.35f);
    public void PlayLand() => Play(landClip, 0.4f);

    private void Play(AudioClip clip, float volumeScale)
    {
        if (clip == null || source == null) return;
        source.PlayOneShot(clip, volumeScale);
    }

    // --- prozedurale Clip-Erzeugung ---

    private void GenerateMissingClips()
    {
        if (tickClip == null) tickClip = Blip("cd_tick", 1100f, 0.055f, 0.35f);
        if (tickUrgentClip == null) tickUrgentClip = Blip("cd_tick_urgent", 1750f, 0.06f, 0.6f);
        if (whooshClip == null) whooshClip = Whoosh("cd_whoosh", 0.45f);
        if (fallClip == null) fallClip = Sweep("cd_fall", 520f, 90f, 0.7f, 0.25f);
        if (winClip == null) winClip = Arpeggio("cd_win", new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.13f);
        if (eliminatedClip == null) eliminatedClip = Arpeggio("cd_eliminated", new[] { 392f, 311.13f, 233.08f }, 0.16f);
        if (readyClip == null) readyClip = Arpeggio("cd_ready", new[] { 587.33f, 880f }, 0.09f);
        if (unreadyClip == null) unreadyClip = Arpeggio("cd_unready", new[] { 880f, 587.33f }, 0.09f);
        if (roundStartClip == null) roundStartClip = Chord("cd_round_start", new[] { 440f, 554.37f, 659.25f }, 0.55f);
        if (uiClip == null) uiClip = Blip("cd_ui", 760f, 0.05f, 0.3f);
        if (jumpClip == null) jumpClip = Sweep("cd_jump", 330f, 620f, 0.12f, 0.05f);
        if (landClip == null) landClip = Thud("cd_land", 0.16f);
    }

    private static AudioClip Build(string name, float duration, System.Func<float, float> sample)
    {
        int count = Mathf.Max(1, Mathf.RoundToInt(duration * SampleRate));
        float[] data = new float[count];
        for (int i = 0; i < count; i++)
            data[i] = Mathf.Clamp(sample(i / (float)SampleRate), -1f, 1f);

        AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Schneller Anschlag, exponentielles Ausklingen.
    private static float Envelope(float t, float duration, float attack)
    {
        if (t < attack) return t / Mathf.Max(0.0001f, attack);
        float rest = (t - attack) / Mathf.Max(0.0001f, duration - attack);
        return Mathf.Exp(-4.5f * rest);
    }

    private static AudioClip Blip(string name, float frequency, float duration, float amplitude)
    {
        return Build(name, duration, t =>
        {
            float wave = Mathf.Sin(2f * Mathf.PI * frequency * t);
            wave += 0.25f * Mathf.Sin(4f * Mathf.PI * frequency * t);
            return wave * amplitude * Envelope(t, duration, 0.004f);
        });
    }

    private static AudioClip Sweep(string name, float startFreq, float endFreq, float duration, float attack)
    {
        return Build(name, duration, t =>
        {
            float p = t / duration;
            float freq = Mathf.Lerp(startFreq, endFreq, p * p);
            float wave = Mathf.Sin(2f * Mathf.PI * freq * t);
            return wave * 0.55f * Envelope(t, duration, Mathf.Max(0.002f, attack));
        });
    }

    private static AudioClip Whoosh(string name, float duration)
    {
        // Gefiltertes Rauschen (einfacher Tiefpass) mit Glockenhüllkurve - klingt nach Luftzug.
        float last = 0f;
        System.Random rng = new System.Random(1337);
        return Build(name, duration, t =>
        {
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            last = Mathf.Lerp(last, noise, 0.12f);
            float p = t / duration;
            float env = Mathf.Sin(Mathf.PI * p);
            float sweep = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(260f, 70f, p) * t) * 0.35f;
            return (last * 3.2f + sweep) * env * 0.5f;
        });
    }

    private static AudioClip Thud(string name, float duration)
    {
        return Build(name, duration, t =>
        {
            float freq = Mathf.Lerp(160f, 60f, t / duration);
            return Mathf.Sin(2f * Mathf.PI * freq * t) * 0.7f * Envelope(t, duration, 0.003f);
        });
    }

    private static AudioClip Arpeggio(string name, float[] frequencies, float noteDuration)
    {
        float total = noteDuration * frequencies.Length;
        return Build(name, total, t =>
        {
            int index = Mathf.Clamp(Mathf.FloorToInt(t / noteDuration), 0, frequencies.Length - 1);
            float local = t - index * noteDuration;
            float wave = Mathf.Sin(2f * Mathf.PI * frequencies[index] * local);
            wave += 0.3f * Mathf.Sin(4f * Mathf.PI * frequencies[index] * local);
            return wave * 0.45f * Envelope(local, noteDuration, 0.005f);
        });
    }

    private static AudioClip Chord(string name, float[] frequencies, float duration)
    {
        return Build(name, duration, t =>
        {
            float wave = 0f;
            for (int i = 0; i < frequencies.Length; i++)
                wave += Mathf.Sin(2f * Mathf.PI * frequencies[i] * t);

            return wave / frequencies.Length * 0.6f * Envelope(t, duration, 0.02f);
        });
    }
}
