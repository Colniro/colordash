using UnityEngine;

// Persistente Spieler-Einstellungen (PlayerPrefs).
// Genutzt von PlayerMovement (Maus), GameAudio (Lautstärke) und PauseMenu (UI).
public static class GameSettings
{
    private const string KeySensitivity = "cd.mouseSensitivity";
    private const string KeyInvertY = "cd.invertY";
    private const string KeyMasterVolume = "cd.masterVolume";
    private const string KeySfxVolume = "cd.sfxVolume";
    private const string KeyPlayerName = "cd.playerName";

    public const float MinSensitivity = 0.25f;
    public const float MaxSensitivity = 8f;

    // Wird nach jeder Änderung ausgelöst, damit laufende Systeme (Audio, Kamera) nachziehen.
    public static event System.Action Changed;

    private static float sensitivity;
    private static bool invertY;
    private static float masterVolume;
    private static float sfxVolume;
    private static string playerName;

    static GameSettings()
    {
        sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(KeySensitivity, 2f), MinSensitivity, MaxSensitivity);
        invertY = PlayerPrefs.GetInt(KeyInvertY, 0) == 1;
        masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMasterVolume, 0.8f));
        sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeySfxVolume, 0.8f));
        playerName = PlayerPrefs.GetString(KeyPlayerName, "");
    }

    public static float MouseSensitivity
    {
        get => sensitivity;
        set
        {
            sensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
            PlayerPrefs.SetFloat(KeySensitivity, sensitivity);
            Commit();
        }
    }

    public static bool InvertY
    {
        get => invertY;
        set
        {
            invertY = value;
            PlayerPrefs.SetInt(KeyInvertY, value ? 1 : 0);
            Commit();
        }
    }

    public static float MasterVolume
    {
        get => masterVolume;
        set
        {
            masterVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyMasterVolume, masterVolume);
            Commit();
        }
    }

    public static float SfxVolume
    {
        get => sfxVolume;
        set
        {
            sfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeySfxVolume, sfxVolume);
            Commit();
        }
    }

    public static string PlayerName
    {
        get => playerName;
        set
        {
            playerName = string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
            if (playerName.Length > 16) playerName = playerName.Substring(0, 16);
            PlayerPrefs.SetString(KeyPlayerName, playerName);
            Commit();
        }
    }

    private static void Commit()
    {
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
