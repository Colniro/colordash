# ColorDash

Ein "Der Boden ist Lava"-Reaktionsspiel im Fall-Guys-Stil: eine Farbe wird angesagt, alle
Spieler rennen auf ein Feld aus bunten Plattformen, und nach ein paar Sekunden verschwinden
alle Plattformen außer der angesagten Farbe unter einem weg. Wer fällt, scheidet aus. Wer als
Letztes übrig bleibt, gewinnt. Gebaut in Unity mit Netcode for GameObjects (Client-Host,
optional über Unity Relay ohne Portfreigabe).

## Inhalt

- [Spielprinzip](#spielprinzip)
- [Steuerung](#steuerung)
- [Schnellstart](#schnellstart)
- [Voraussetzungen](#voraussetzungen)
- [Unity-Projekt öffnen](#unity-projekt-öffnen)
- [Multiplayer testen](#multiplayer-testen)
- [Projektstruktur](#projektstruktur)
- [Wichtige Skripte](#wichtige-skripte)
- [Spiel einstellen / tunen](#spiel-einstellen--tunen)
- [Bekannte Einschränkungen](#bekannte-einschränkungen)
- [Verwendete Assets & Lizenzen](#verwendete-assets--lizenzen)

## Spielprinzip

1. Alle Spieler stehen auf der Lobby-Plattform und drücken **E**, um sich bereit zu melden.
2. Sobald genug Spieler bereit sind, teleportiert der Server alle aufs Spielfeld.
3. Eine Farbe wird angesagt (ab Runde 10 werden es nach und nach mehrere Farben hintereinander,
   bis zu 4). Ein Countdown-Balken zeigt die verbleibende Reaktionszeit.
4. Kurz bevor die Zeit abläuft, wackeln die "falschen" Plattformen als Vorwarnung — dann
   verschwinden sie, und nur die angesagte Farbe bleibt stehen.
5. Wer nicht rechtzeitig auf der richtigen Farbe steht, fällt durch und wird **Zuschauer**
   (Kamera löst sich vom Körper, man kann zwischen den verbliebenen Spielern durchschalten).
6. Jede Runde wird das Zeitfenster kürzer. Der letzte Überlebende gewinnt die Partie; danach
   geht es zurück in die Lobby für die nächste Runde.

Im **Einzelspieler**-Modus spielt man alleine gegen die steigende Schwierigkeit — Ziel ist,
möglichst viele Runden zu überleben.

### Runden-Modifikatoren

Ab Runde 10 ist in **jeder** Runde genau einer von drei Modifikatoren aktiv, der oben rechts
unter der Rundenanzeige eingeblendet wird:

| Modifikator | Wirkung |
|---|---|
| **Rutschiger Boden** | Beschleunigung stark reduziert — man schlittert weit über sein Ziel hinaus |
| **Niedrige Schwerkraft** | Schwerkraft runter, Sprungkraft rauf — lange, schwebende Sprünge |
| **Umgekehrte Kamera** | Beide Mausachsen sind invertiert |

### Schubsen & Emotes

Mit `F` schubst man Mitspieler in Blickrichtung weg (kurze Abklingzeit, wirkt nur in einem
Kegel nach vorn) — ideal, um jemanden im letzten Moment von der richtigen Farbe zu befördern.
Über die Tasten `1`–`4` gibt es kurze Emotes, die als Sprechblase über dem eigenen Kopf
erscheinen. Beides läuft über den Server, damit niemand fremde Spieler direkt manipulieren kann.

### Persönlicher Rekord

Die höchste je erreichte Runde wird pro Gerät gespeichert (`PlayerPrefs`) und dauerhaft im HUD
angezeigt. Wird der Rekord übertroffen, erscheint das am Rundenende zusätzlich im Ergebnis-Banner.

## Steuerung

| Taste | Aktion |
|---|---|
| `W` `A` `S` `D` | Bewegen |
| Maus | Umschauen |
| `Leertaste` | Springen |
| `Shift` (halten) | Rennen |
| `R` (halten) | Ducken |
| `E` | Bereit / Nicht bereit (auf der Lobby-Plattform) |
| `F` | Mitspieler schubsen |
| `1` `2` `3` `4` | Emote über dem eigenen Kopf zeigen |
| `Esc` | Pausenmenü (Einstellungen, Partie verlassen, Beenden) |
| Zuschauer-Modus: `A` / `D` oder Maustasten | Zwischen lebenden Spielern wechseln |

Maus-Empfindlichkeit, Y-Achsen-Invertierung, Lautstärke und der eigene Anzeigename lassen
sich im Pausenmenü einstellen und werden pro Gerät gespeichert (`PlayerPrefs`).

## Schnellstart

```bash
git clone https://github.com/Colniro/colordash.git
```

Danach das Unity-Projekt im Unterordner `colordash/` mit Unity **6000.0.78f1** (Unity 6) öffnen
und die Szene `Assets/Scenes/SampleScene.unity` starten.

## Voraussetzungen

- **Unity 6000.0.78f1** (siehe `colordash/ProjectSettings/ProjectVersion.txt`) — am einfachsten
  über den [Unity Hub](https://unity.com/download), der die passende Version automatisch anbietet.
- Für den Mehrspieler-Modus über Relay: ein kostenloses
  [Unity Cloud](https://cloud.unity.com/) Projekt mit aktivierten **Relay**- und
  **Authentication**-Diensten (siehe [Multiplayer testen](#multiplayer-testen)).
- Für Singleplayer und lokale Tests ist keine Cloud-Anbindung nötig.

## Unity-Projekt öffnen

1. Unity Hub → **Open** → den Ordner `colordash/colordash` auswählen (das ist der Ordner mit
   `Assets/`, `Packages/`, `ProjectSettings/`).
2. Beim ersten Öffnen zieht Unity alle Pakete aus `Packages/manifest.json` nach — das dauert
   je nach Verbindung ein paar Minuten.
3. Szene `Assets/Scenes/SampleScene.unity` öffnen und **Play** drücken.
4. Im Hauptmenü **Einzelspieler** wählen, um sofort ohne Internetverbindung zu testen.

## Multiplayer testen

Es gibt zwei Wege, Verbindungen aufzubauen:

**A) Über Unity Relay (empfohlen, funktioniert ohne Portfreigabe)**

1. Im [Unity Cloud Dashboard](https://cloud.unity.com/) ein Projekt anlegen bzw. das
   bestehende verknüpfen, dann in Unity über **Edit → Project Settings → Services** mit dem
   Projekt verbinden.
2. Sicherstellen, dass **Relay** und **Authentication** im Dashboard für das Projekt aktiviert
   sind.
3. Im Spiel-Hauptmenü: ein Spieler klickt **Lobby erstellen** und bekommt einen Beitrittscode
   angezeigt, alle anderen geben diesen Code bei **Beitreten** ein.
4. Funktioniert über das Internet, ohne dass jemand Ports weiterleiten muss.

**B) Lokal testen (zwei Editor-Instanzen)**

Für schnelle Tests ohne Cloud-Setup: das Projekt zweimal öffnen (z. B. via
[ParrelSync](https://github.com/VeriorPies/ParrelSync) oder zwei Kopien des Projektordners)
und in einer Instanz **Einzelspieler** (= lokaler Host) starten. Ohne Relay-Konfiguration ist
regulärer Mehrspieler-Beitritt über den Code-Dialog nicht möglich, da Relay für die
Verbindungsvermittlung gebraucht wird.

Die Lobby braucht standardmäßig mindestens 2 bereite Spieler, bevor eine Runde startet
(`minPlayersToStart` am `GameFlowManager`), unterstützt aber bis zu 8 Spieler
(`maxPlayers`) — beides im Inspector einstellbar.

## Projektstruktur

```
colordash/                       Repo-Root
└── colordash/                   Unity-Projekt
    ├── Assets/
    │   ├── Scenes/SampleScene.unity   Hauptszene (Lobby + Spielfeld)
    │   ├── Scripts/                   Eigener Spielcode (siehe unten)
    │   ├── Prefabs/NetworkPlayer.prefab
    │   ├── Settings/                  URP-Rendering-Profile
    │   └── ...                        Third-Party-Grafik-Assets (siehe Lizenzen)
    ├── Packages/manifest.json         Paketabhängigkeiten
    └── ProjectSettings/               Projekt-/Build-Einstellungen
```

## Wichtige Skripte

Alle in `colordash/Assets/Scripts/`:

| Datei | Zuständigkeit |
|---|---|
| [`GameFlowManager.cs`](colordash/Assets/Scripts/Multiplayer/GameFlowManager.cs) | Hauptmenü (Singleplayer / Lobby erstellen / Beitreten via Relay), Lobby-/Rundenablauf, Spieler-Slots, Sieger-Ermittlung, Verbindungs-/Disconnect-Handling |
| [`ColorDashManager.cs`](colordash/Assets/Scripts/ColorDashManager.cs) | Serverautoritative Rundenlogik: Farbansagen, Countdown, Schwierigkeitskurve, Ansage- und Sequenz-UI |
| [`PlayerMovement.cs`](colordash/Assets/Scripts/PlayerMovement.cs) | Ego-Perspektive-Steuerung (Laufen/Rennen/Springen/Ducken), Schubsen, Emotes, Rundenmodifikatoren, Absturzerkennung, Netzwerk-Teleport |
| [`PlayerNametag.cs`](colordash/Assets/Scripts/PlayerNametag.cs) | Schwebendes Namensschild über jedem Spieler, zeigt auch Emotes an |
| [`ReadyButtonZone.cs`](colordash/Assets/Scripts/Multiplayer/ReadyButtonZone.cs) | Trigger-Zone auf der Lobby-Plattform für den Bereit-Status |
| [`TileAnimator.cs`](colordash/Assets/Scripts/TileAnimator.cs) | Wackel-Vorwarnung und Wegkipp-Animation der Plattformen |
| [`SpectatorCamera.cs`](colordash/Assets/Scripts/SpectatorCamera.cs) | Freie Zuschauerkamera für ausgeschiedene Spieler |
| [`PauseMenu.cs`](colordash/Assets/Scripts/PauseMenu.cs) | Pausenmenü, Einstellungen, Partie verlassen |
| [`GameAudio.cs`](colordash/Assets/Scripts/GameAudio.cs) | Prozedural erzeugte Soundeffekte (kein Audio-Asset-Import nötig) |
| [`GameSettings.cs`](colordash/Assets/Scripts/GameSettings.cs) | Persistente Spieler-Einstellungen (Sensitivität, Lautstärke, Name) |
| [`UIFactory.cs`](colordash/Assets/Scripts/UIFactory.cs) | Gemeinsame Bau-Helfer für die TextMeshPro-Laufzeit-UI |
| [`ClientNetworkTransform.cs`](colordash/Assets/Scripts/Multiplayer/ClientNetworkTransform.cs) | Client-autoritative Positions-Synchronisation für die eigene Spielerbewegung |

Die komplette Laufzeit-UI (Menüs, HUD, Countdown, Bannertexte) wird zur Laufzeit prozedural
über `UIFactory` gebaut — es gibt keine UI-Prefabs, die in der Szene verdrahtet werden müssen.

## Spiel einstellen / tunen

Am `ColorDashManager`-GameObject in der Szene (Inspector):

| Feld | Standard | Bedeutung |
|---|---|---|
| `startReactionTime` | 4 s | Reaktionszeit in Runde 1 |
| `minReactionTime` | 1 s | Untergrenze, unter die die Reaktionszeit nicht fällt |
| `reactionTimeStep` | 0.2 s | Um wie viel die Reaktionszeit pro Runde sinkt |
| `hiddenDuration` | 4 s | Wie lange die "falschen" Plattformen verschwunden bleiben |
| `multiAnnounceStartRound` | 10 | Ab welcher Runde mehrere Farben nacheinander angesagt werden |
| `maxAnnounceCount` | 4 | Maximale Anzahl Farben pro Ansage-Sequenz |
| `modifierStartRound` | 10 | Ab welcher Runde immer ein Rundenmodifikator aktiv ist |
| `wobbleLeadTime` | 1.2 s | Vorwarnzeit, bevor Plattformen wackeln/verschwinden |
| `urgentTickTime` | 1.5 s | Ab wann das Countdown-Ticken hektischer wird |

Am `GameFlowManager`-GameObject:

| Feld | Standard | Bedeutung |
|---|---|---|
| `maxPlayers` | 8 | Obergrenze pro Lobby |
| `minPlayersToStart` | 2 | Mindestanzahl bereiter Spieler im Mehrspieler |

## Bekannte Einschränkungen

- Die Szene hat nur 2 feste Spawn-Punkte für Lobby und Spielfeld; zusätzliche Spieler werden
  im Kreis um den ersten Punkt verteilt (`extraSpawnRadius` am `GameFlowManager`).
- Ohne konfiguriertes Unity-Cloud-Projekt (Relay/Authentication) ist nur der
  Einzelspieler-Modus nutzbar.
- Es gibt aktuell nur ein Arenen-Layout.
- Der persönliche Rekord liegt in den lokalen `PlayerPrefs` — es gibt keine geräteübergreifende
  oder gemeinsame Bestenliste.

## Verwendete Assets & Lizenzen

Dieses Projekt nutzt mehrere Third-Party-Asset-Pakete unter `Assets/` (Skyboxen, Umgebungen,
Fahrzeuge, Effekte). Für die jeweiligen Lizenzbedingungen gelten die Angaben der Anbieter in
den entsprechenden Unterordnern (`BOXOPHOBIC/`, `AllSkyFree/`, `Polytope Studio/`,
`TooMooseGames/`, `mighty_handful/`, `EmaceArt_LavaPlant/`, `VCR01/`, `TextMesh Pro/`). Der
eigene Spielcode in `Assets/Scripts/` steht ohne gesonderte Lizenzangabe unter den Bedingungen
dieses Repositories.
