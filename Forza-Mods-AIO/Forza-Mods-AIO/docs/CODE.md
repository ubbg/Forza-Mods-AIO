# Forza-Mods-AIO — Code-Walkthrough

Vollständige Architektur- und API-Dokumentation auf Deutsch. Ziel: neue Mitwirkende sollen in 30 Minuten verstehen, wie der Code zusammenhängt, ohne sich durch jede Datei durchklicken zu müssen. Fachbegriffe (AOB, Shellcode, Detour, RIP-relative, Hook, MVVM, DI, INPC, Singleton) bleiben englisch.

> **Anmerkung zum Format**: Diese Datei dokumentiert Klassen und ihre **öffentlichen** Methoden. Private Hilfsmethoden, Shellcode-Disassemblies und konkrete AOB-Pattern werden bewusst weggelassen — sie sind im Quellcode lesbar und ändern sich mit jedem Spiel-Update. Schwerpunkt liegt auf **dem Zweck** jeder Komponente und ihrem Platz im Datenfluss.

---

## Inhalt

1. [Einleitung & Zweck](#1-einleitung--zweck)
2. [Verzeichnis-Karte](#2-verzeichnis-karte)
3. [Lifecycle: App-Start](#3-lifecycle-app-start)
4. [Lifecycle: Process-Attach](#4-lifecycle-process-attach)
5. [Cheat-Architektur](#5-cheat-architektur)
6. [Cheat-Inventar — Forza Horizon 4](#6-cheat-inventar--forza-horizon-4)
7. [Cheat-Inventar — Forza Horizon 5](#7-cheat-inventar--forza-horizon-5)
8. [MVVM-Schicht — Hauptfenster-ViewModels](#8-mvvm-schicht--hauptfenster-viewmodels)
9. [MVVM-Schicht — Page-ViewModels](#9-mvvm-schicht--page-viewmodels)
10. [MVVM-Schicht — SubPage-ViewModels](#10-mvvm-schicht--subpage-viewmodels)
11. [Code-Behind-Pattern](#11-code-behind-pattern)
12. [Hotkey-System](#12-hotkey-system)
13. [Theming-System](#13-theming-system)
14. [Lokalisierung](#14-lokalisierung)
15. [Debug-Infrastruktur](#15-debug-infrastruktur)
16. [Search-Overlay](#16-search-overlay)
17. [Infrastruktur-Utilities](#17-infrastruktur-utilities)
18. [Custom Controls](#18-custom-controls)
19. [Converters](#19-converters)
20. [Datenfluss-Beispiel](#20-datenfluss-beispiel-velocity-hack)
21. [Erweitern: neue FH-Version](#21-erweitern-neue-fh-version)
22. [Erweitern: neue Sprache](#22-erweitern-neue-sprache)
23. [Erweitern: neuer Cheat](#23-erweitern-neuer-cheat)

---

## 1. Einleitung & Zweck

Forza-Mods-AIO ist ein **All-in-One-Cheat-Tool** für die Forza-Horizon-Reihe (FH4, FH5 und FH6 als FH5-Alias). Es läuft als eigenständiger Windows-Desktop-Prozess neben dem Spiel, attached sich per `OpenProcess` an die Spiel-Instanz, scannt deren Speicher nach AOB-Pattern (Array-of-Bytes-Signaturen), allokiert Code-Caves via `VirtualAllocEx` und installiert x64-Shellcode-Detours, die das Spielverhalten manipulieren (Geschwindigkeit, Schwerkraft, Credits, Autosalon-Datenbank, Fotomodus etc.).

**Technologie-Stack**: .NET 8, WPF (`net8.0-windows`, x64-only), MahApps.Metro für UI, CommunityToolkit.Mvvm für ObservableObject/RelayCommand, `Microsoft.Extensions.Hosting` für DI/IHost, externe `Memory.dll` (Resources/External/) für den AOB-Scan und Prozess-I/O. Plattformen: Microsoft Store, Steam und OnlineFix-Steam-Builds werden automatisch unterschieden.

---

## 2. Verzeichnis-Karte

| Ordner | Rolle |
|---|---|
| `App.xaml(.cs)` | Application-Bootstrap, IHost, Mutex, Exception-Handler, Sprach-/Theme-Bootstrap |
| `Cheats/` | Komplette Cheat-Implementierungen, getrennt nach `ForzaHorizon4/` und `ForzaHorizon5/`, plus gemeinsame Basis (`ICheatsBase`, `IRevertBase`, `CheatsUtilities`) |
| `Controls/` | Custom WPF-Controls (`StatusComboboxItem`, `TranslationComboboxItem`) |
| `Converters/` | WPF `IValueConverter` / `IMultiValueConverter` für Binding-Transformationen |
| `Helpers/` | `Settings.cs` – Persistierung von Theme-Farbe und Sprache in `App.config` |
| `Models/` | Daten-Klassen: `GameVerPlat`, `DebugSession`, `DebugInfoReport`, `SearchResult` |
| `Resources/` | Querschnittsthemen: Memory-Singleton, P/Invoke-Imports, Hotkeys, Theming, Translations (`.xaml`-ResourceDictionaries), Such-Index, gecachte Pages |
| `Resources/External/Memory.dll` | Native Speicher-Bibliothek (AOB-Scan, OpenProcess, ReadMemory/WriteMemory) |
| `Services/` | `ApplicationHostService` (IHostedService), `WindowsProviderService` (Dialog-Helper) |
| `ViewModels/` | MVVM-ViewModels: `Windows/` (Main, Debug), `Pages/` (Tuning, Autoshow, Search, About, AioInfo, Settings), `SubPages/` (SelfVehicle/* und Tuning/*) |
| `Views/` | XAML-Views + Code-Behind, gleiche Hierarchie wie ViewModels |
| `docs/` | Diese Datei |

---

## 3. Lifecycle: App-Start

Einstiegspunkt ist `App.xaml.cs`. Der Start-Pfad ist:

1. **WPF ruft `App.OnStartup(...)`** (`App.xaml.cs:60`).
2. **Single-Instance-Mutex** (`App.xaml.cs:62`, GUID-Konstante in `:21`) — gibt es bereits eine laufende AIO-Instanz, erscheint eine MessageBox und der Prozess beendet sich.
3. **`SetupExceptionHandling()`** (`App.xaml.cs:108`) hängt drei Global-Handler ein: `AppDomain.UnhandledException`, `DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`. Alle drei landen in `ReportException(...)` (`App.xaml.cs:126`), die einen Modal-Dialog mit Versions-/Plattform-Infos und Discord/GitHub-Link zeigt.
4. **`Theming.GetInstance().InitializeTheme()`** (`App.xaml.cs:70`) liest die gespeicherte Theme-Farbe aus `App.config` und wendet sie an (siehe [Kapitel 13](#13-theming-system)).
5. **Sprach-Bootstrap** (`App.xaml.cs:73–98`): `Settings.LoadLanguage()` liefert den `LanguageCode` (Default `"English"`); das passende `Resources/Translations/<LanguageCode>.xaml` wird als `ResourceDictionary` in `Application.Current.Resources.MergedDictionaries` eingefügt. Schlägt das fehl (z. B. Datei fehlt), bleibt es ohne Translation-Dictionary, und alle `{DynamicResource ...}`-Bindings fallen auf ihre Default-Werte zurück.
6. **`App_OnStartup`** (XAML-Event-Handler, `App.xaml.cs:45`) ruft `Host.StartAsync()` auf. Der `IHost` wurde statisch in `App.xaml.cs:24` aufgebaut und registriert per DI:
   - `ApplicationHostService` als `IHostedService`
   - `MainWindowViewModel` + `MainWindow` (als `MetroWindow`)
   - `DebugWindowViewModel` + `DebugWindow`

   Beim `StartAsync` wird `ApplicationHostService.StartAsync(...)` ausgeführt, das in seiner `HandleActivationAsync`-Methode das `MainWindow` aus dem DI-Container holt und anzeigt.
7. **`HotkeysManager.SetupSystemHook()`** (`App.xaml.cs:48`) installiert einen Low-Level Keyboard Hook via `SetWindowsHookEx`. Erst ab diesem Zeitpunkt funktionieren globale Hotkeys (siehe [Kapitel 12](#12-hotkey-system)).
8. **`MainWindowViewModel`-Konstruktor** ruft `InitializeViewModel()` (`MainWindowViewModel.cs:108`), welcher `SetupAttach()` (→ Kapitel 4) sowie asynchron `CheckForUpdates()` triggert (GitHub-API-Polling: `MainWindowViewModel.cs:124`/`160`/`176`).

Beim Beenden (`App_OnExit`, `App.xaml.cs:51`) wird `HotkeysManager.ShutdownSystemHook()`, `DisconnectFromGame()` (ruft `ICheatsBase.Cleanup()` auf alle gecachten Cheat-Instanzen und schließt das Prozess-Handle via `Imports.CloseHandle`) sowie `Host.StopAsync()` aufgerufen. Der Mutex wird in `OnExit` (`App.xaml.cs:136`) freigegeben.

---

## 4. Lifecycle: Process-Attach

Der gesamte Attach-Code lebt in `ViewModels/Windows/MainWindowViewModel.cs`.

**Startsequenz (`SetupAttach`, Zeile 216)**:

1. Liste der bekannten Forza-Prozessnamen wird gesetzt (`MainWindowViewModel.cs:218`): `forzahorizon5.exe`, `forzahorizon4.exe`, `forzahorizon6.exe`. **Wichtig**: Erweiterung einer neuen Forza-Version geschieht hier, nicht durch neuen Cheat-Namespace (siehe [Kapitel 21](#21-erweitern-neue-fh-version)).
2. `LoopProcesses(...)` (`:250`) iteriert die Liste und versucht für jeden Eintrag `Memory.GetInstance().OpenProcess(processName)`. Beim ersten Erfolg wird `GvpMaker(processName)` aufgerufen und `Attached = true` gesetzt.
3. Falls beim Start kein Spiel läuft, registriert `SetupAttach` einen `ManagementEventWatcher` auf die WMI-Query `SELECT * FROM Win32_ProcessStartTrace` (`:224`). Sobald ein passender Prozess startet, wird derselbe `LoopProcesses`-Pfad erneut durchlaufen.

**`GvpMaker(string name)` (`MainWindowViewModel.cs:286`)** befüllt das `GameVerPlat`-Singleton:
- **Platform-Heuristik**: Wenn der Spielpfad `Microsoft.624F8B84B80` oder `Microsoft.SunriseBaseGame` enthält → Microsoft Store; Version wird aus `appxmanifest.xml` gelesen. Sonst: gibt es eine `OnlineFix64.dll` neben der EXE → „OnlineFix - Steam", andernfalls „Steam"; Version dann aus `FileVersionInfo`.
- `GameVerPlat.GetInstance().Name` = lesbarer Spielname, `.Platform`, `.Update`, `.Type` werden gesetzt.
- `AttachedText` wird auf `"<Name>, <Platform>, <Update>"` aktualisiert.

**`GetTypeFromName(string)` (`:323`)** ist der entscheidende Switch:
```
"Forza Horizon 4" → GameType.Fh4
"Forza Horizon 5" → GameType.Fh5
"Forza Horizon 6" → GameType.Fh5  ← Alias!
_                 → GameType.None
```

FH6 wird **bewusst als Alias** auf `Fh5` gemappt, sodass alle bestehenden `case Fh5:`- und `IsFh5`-Checks ohne weiteres greifen.

**`GetSmoothNameFromProcessName(string)` (`:334`)** mappt den niedrigen Prozessnamen (z. B. `forzahorizon5.exe`) auf den lesbaren Namen (z. B. `"Forza Horizon 5"`).

**Process-Exit-Cleanup (`SetupExit`, `:265` → `CleanClasses`, `:278`)**: Sobald der Spielprozess endet, wird `Attached=false` gesetzt, `AttachedText` zurück auf `"Launch FH4, FH5 or FH6"`, die `CurrentView` wieder `AioInfo`, und `Resources.Pages.Clear()` löscht alle Page-Caches außer `AioInfo`. Anschließend ruft `CleanClasses()` `Reset()` auf jeder gecachten Klasse, die `ICheatsBase` implementiert — das nullt interne Detour-Adressen, sodass beim nächsten Attach ein frischer AOB-Scan stattfindet.

**Update-Check (`CheckForUpdates`, `:176`)** parallel zum Attach: `CheckGit` (`:124`) holt das letzte Release-Tag aus der GitHub-API `https://api.github.com/repos/ForzaMods/Forza-Mods-AIO/releases/latest`; `CompareVer` (`:160`) vergleicht mit `Assembly.GetExecutingAssembly().GetName().Version` und zeigt ggf. einen Update-Prompt, der den Browser auf die Releases-Seite öffnet und die App beendet.

---

## 5. Cheat-Architektur

Die Cheat-Logik liegt komplett unter `Cheats/`. Die zentrale Idee: jeder Cheat ist eine Methode auf einer Cheat-Klasse, die im RAM des Spielprozesses einen AOB-Pattern scannt, einen Code-Cave allokiert, einen `JMP`-Detour an die Original-Stelle schreibt und dann über kleine Toggle-Bytes innerhalb des Code-Cave dynamisch ein-/ausschaltbar ist.

### 5.1 Interfaces

**`ICheatsBase` (`Cheats/ICheatsBase.cs`)** — implementiert jede Cheat-Klasse:

- `Cleanup()` — schreibt Original-Bytes zurück, gibt mit `VirtualFreeEx` allokierte Code-Caves frei. Wird beim Application-Exit (`DisconnectFromGame`) aufgerufen.
- `Reset()` — setzt interne UIntPtr-Felder (Detour-Adressen, Scan-Flags) auf 0. Wird bei Process-Exit (`CleanClasses`) aufgerufen, damit beim nächsten Attach erneut gescannt wird.

**`IRevertBase` (`Cheats/IRevertBase.cs`)** — nur für FH5-Cheats relevant. Stellt das Bypass-Pattern bereit:

- `Revert()` — schreibt vor einem Anti-Cheat-Integritätscheck temporär die Original-Bytes zurück.
- `Continue()` — restauriert die Patches nach dem Check.

Der FH5-Anti-Cheat führt periodisch CRC-Checks aus, die fremde Bytes im Spielcode entdecken würden. Die `Bypass`-Klasse (siehe Kapitel 7) startet einen 10-Sekunden-`System.Timers.Timer`, der zyklisch `Revert()` → kurz warten → `Continue()` über alle registrierten `IRevertBase`-Instanzen aufruft.

### 5.2 Geteilte Utilities — `CheatsUtilities.cs`

Basisklasse vieler Cheat-Klassen (`Cheats/CheatsUtilities.cs`). Statische, geschützte Helfer:

- **`SmartAobScan(string search, UIntPtr? start = null, UIntPtr? end = null)`** (`CheatsUtilities.cs:11`) — durchsucht den Adressraum des Spielprozesses nach einem AOB-Pattern. Iteriert mit `VirtualQueryEx` über alle Memory-Regionen des Main-Modules. Regionen >500 MB werden in `ScanRange(...)` (`:71`) per Binärsuche aufgeteilt, um die externe `Mem.AoBScan`-Funktion nicht zu überfordern.
- **`Free(UIntPtr address)`** (`:85`) — wrapper um `VirtualFreeEx(handle, address, 0, MEM_RELEASE)` zum Freigeben einer Code-Cave.
- **`CalculateDetour(nuint address, nuint target, int replaceCount)`** (`:92`) — erzeugt das 5-Byte-`E9`-RIP-relative-JMP plus NOP-Padding zur Sprungstelle.
- **`ShowError(string feature, string sig)`** (`:78`) — zeigt einen MessageBox-Dialog mit Feature-Name, AOB-Pattern und aktueller Spiel-/Tool-Version. Wird aufgerufen, wenn ein Scan fehlschlägt.

### 5.3 Cheat-Instanzen-Cache — `Resources/Cheats.cs`

Statische `Dictionary<Type, object> CachedInstances` plus `GetClass<T>()`-Factory. Jede Cheat-Klasse wird **maximal einmal** instanziiert; spätere Aufrufe von `GetClass<CarCheats>()` liefern dieselbe Instanz. ViewModels und Code-Behind nutzen diese Factory als globalen Service-Locator:

```csharp
private static CarCheats CarCheatsFh5 => GetClass<CarCheats>();
```

Bei Process-Exit iteriert `CleanClasses` (siehe Kapitel 4) über `CachedInstances` und ruft `Reset()` — die Instanzen selbst bleiben im Cache; nur ihr Zustand wird genullt. Bei App-Exit dagegen rufen wir `Cleanup()`, was zusätzlich allokierten Speicher freigibt.

---

## 6. Cheat-Inventar — Forza Horizon 4

Namespace `Cheats/ForzaHorizon4/`. Implementiert die für FH4 funktionierenden Cheats. Im Gegensatz zu FH5 implementiert FH4 *kein* `IRevertBase`, weil der FH4-Anti-Cheat keine periodischen CRC-Checks fährt (nur `CreateRemoteThread`-Checks, die einmalig deaktiviert werden).

| Datei | Implements | Sinn |
|---|---|---|
| `Bypass.cs` | `ICheatsBase` | Patcht `RtlUserThreadStart`/`NtCreateThreadEx` in `ntdll.dll`, um `CreateRemoteThread`-Erkennung zu deaktivieren. Public: `DisableCreateRemoteThreadChecks`, `Cleanup`, `Reset`. |
| `CameraCheats.cs` | `ICheatsBase` | Lokalisiert FOV-Limiter-Adressen für die fünf Kamera-Perspektiven. Felder: `ChaseAddress`, `ChaseFarAddress`, `DriverAddress`, `HoodAddress`, `BumperAddress`, `WereLimitersScanned`. Public: `CheatLimiters`. |
| `CarCheats.cs` | `ICheatsBase` | LocalPlayer-Hook mit 3 Detours: `CheatLocalPlayer` (Velocity/Wheelspeed/Jump/Brake), `CheatAccel` (Beschleunigungs-Multiplikator), `CheatGravity` (Schwerkraft-Multiplikator). |
| `CustomizationCheats.cs` | `ICheatsBase` | Public: `CheatGlowingPaint`, `CheatCleanliness` (Sauberkeit/Schmutz). |
| `EnvironmentCheats.cs` | `ICheatsBase` | Public: `CheatSunRgb` (Sonnenfarb-Vektor), `CheatTime` (Tageszeit-Multiplikator). |
| `MiscCheats.cs` | `ICheatsBase` | 10 Detours: `CheatRaceTimeScale`, `CheatDriftScoreMultiplier`, `CheatSkillScoreMultiplier`, `CheatSpeedZoneMultiplier`, `CheatMissionTimeScale`, `CheatTrailblazerTimeScale`, `CheatPrizeScale`, `CheatSellFactor`, `CheatUnbreakableSkillScore`, `CheatRemoveBuildCap`. |
| `PhotomodeCheats.cs` | `ICheatsBase` | Public: `CheatNoClip`, `CheatNoHeightLimits`, `CheatIncreasedZoom`, `CheatModifiers` (scannt Aperture/Samples/Time/Speed-Adressen). |
| `Sql.cs` | `ICheatsBase` | Public: `SqlExecAobScan` (findet `CDatabase`-Instanz via Vtable-Scan), `Query(string)` (führt SQL via Shellcode aus, das `CDatabase::Exec` via Vtable[9] aufruft und das Ergebnis aus dem allokierten Argument-Puffer liest). |
| `UnlocksCheats.cs` | `ICheatsBase` | Public: `CheatCredits`, `CheatXp`, `CheatSpins`, `CheatSkillPoints`. |

---

## 7. Cheat-Inventar — Forza Horizon 5

Namespace `Cheats/ForzaHorizon5/`. Erweitert FH4 deutlich, alle Klassen (außer `Bypass` selbst und `Sql`) implementieren zusätzlich `IRevertBase`, damit der Bypass-Timer sie periodisch deaktivieren/reaktivieren kann.

### 7.1 Bypass und Verschlüsselung

| Datei | Sinn |
|---|---|
| `Bypass.cs` (FH5) | Findet die `XxhHash`-CRC-Funktion, ersetzt ihren Prolog mit einem `RET`, sodass alle nachfolgenden Integritäts-Checks immer `true` zurückliefern. Startet einen 10-s-`Timer`, der zyklisch `IRevertBase.Revert/Continue` auf allen registrierten Instanzen aufruft. Public: `DisableCrcChecks`. Felder: `CallAddress`, `XxhCheck`, `OrigXxhCheck`, `Ret`. |
| `ValueEncryption.cs` | Patcht die FH5-Wert-Verschlüsselungs-Funktion mit `0xC3` (`RET`) + NOPs, sodass Geldbeträge, XP etc. nicht mehr verschlüsselt geschrieben/gelesen werden und manipulierbar sind. Public: `CheatDisableValueEncryption`. |
| `TuningCheats.cs` | **Nur Konstanten**, keine Cheat-Methoden. 52 statische `const uint`-Offsets (FrontAero/RearAero, Camber, Toe, AntiRoll, Suspension, Ride Height, Spring, Wheelbase, Rim, Gears, TirePressure, Steering). Wird von den Tuning-SubPages konsumiert (siehe Kapitel 11). |

### 7.2 Cheat-Klassen (alle mit `IRevertBase`)

| Datei | Public Cheats |
|---|---|
| `CameraCheats.cs` | `CheatLimiters` (wie FH4), `CheatCamera` (FOV-Lock+Offset-Detour, hängt vom Bypass ab). |
| `CarCheats.cs` | 7 Detours: `CheatLocalPlayer`, `CheatAccel`, `CheatGravity`, `CheatWaypoint`, `CheatFreezeAi`, `CheatNoWaterDrag`, `CheatNoClip`. |
| `CustomizationCheats.cs` | `CheatGlowingPaint`, `CheatHeadlightColour`, `CheatCleanliness`, `CheatForceLod`, `CheatBackfireTime`. |
| `EnvironmentCheats.cs` | `CheatSunRgb`, `CheatTime` (mit Bypass-Check). |
| `MiscCheats.cs` | 17 Detours: `CheatName` (Spieler-Namen-Override), `CheatSellFactor`, `CheatPrizeScale`, `CheatSkillScoreMultiplier`, `CheatDriftScoreMultiplier`, `CheatSkillTreeWideEdit`, `CheatSkillTreePerksCost`, `CheatMissionTimeScale`, `CheatTrailblazerTimeScale`, `CheatRaceTimeScale`, `CheatSpeedZoneMultiplier`, `CheatUnbreakableSkillScore`, `CheatRemoveBuildCap`, `CheatDangerSign1/2/3`, `CheatSpeedTrapMultiplier`, `CheatDroneModeMaxHeightMulti`. |
| `PhotomodeCheats.cs` | `CheatNoClip`, `CheatNoHeightLimits`, `CheatIncreasedZoom`, `CheatModifiers` (mit Bypass-Check). |
| `Sql.cs` | `SqlExecAobScan`, `Query(string)`. Wie FH4, aber mit zusätzlichem JMP-Patch an Offset+41 zur Umgehung einer Conditional-Sperre. |
| `UnlocksCheats.cs` | 9 Detours: `CheatCredits`, `CheatXp`, `CheatSpins`, `CheatSkillPoints`, `CheatSeries`, `CheatSeasonal`, `CheatBxmlEncryption`, `CheatClothing1`, `CheatClothing2`. |

### 7.3 Abhängigkeitskette

Viele FH5-Cheats prüfen am Anfang `Bypass.CallAddress > 3`. Ist sie nicht gesetzt, wird zuerst `Bypass.DisableCrcChecks()` aufgerufen. Dadurch ist sicher, dass die CRC-Checks bereits deaktiviert sind, bevor irgendetwas im Spielcode überschrieben wird. Der Bypass-Timer arbeitet anschließend kontinuierlich im Hintergrund.

---

## 8. MVVM-Schicht — Hauptfenster-ViewModels

### 8.1 `ViewModels/Windows/MainWindowViewModel.cs`

Steuert das Hauptfenster. ObservableObject-Generator (CommunityToolkit.Mvvm) erzeugt aus `[ObservableProperty]`-Feldern Public Properties mit INPC-Notifikation.

**Wichtige `[ObservableProperty]`-Felder**:

| Feld | Typ | Sinn |
|---|---|---|
| `_applicationTitle` | `string` | Fenster-Titel, gesetzt auf `"Forza Mods AIO"` (`:110`) |
| `_attachedText` | `string` | Statuszeile unten, zeigt Spiel/Plattform/Version oder `"Launch FH4, FH5 or FH6"` |
| `_attached` | `bool` | Ob ein Forza-Prozess attached ist; wird von Bindings benutzt, um Cheat-Buttons zu enablen |
| `_currentView` | `object` | Aktuell sichtbare Page (`AioInfo`, `Tuning`, `SelfVehicle`, …) |
| `_searchVisibility`/`_searchOpacity` | `Visibility`/`double` | Search-Overlay-Sichtbarkeit + Animation |
| `_hotkeysVisibility`/`_hotkeysOpacity` | `Visibility`/`double` | Hotkey-Eingabe-Overlay |
| `_windowCornerRadius`/`_topBarCornerRadius`/`_sideBarCornerRadius` | `CornerRadius` | Wechseln zwischen rund (Normal) und 0 (Maximized) |

**`[RelayCommand]`-Methoden**:

- `HandleMaximizeMinimize(object mainWindow)` (`:183`) — schaltet Window-State um und passt CornerRadius an. Wird vom `WindowStateAction_OnClick` im MainWindow-Code-Behind aufgerufen.
- `ToggleSearch()` (`:345`) — blendet Search-Overlay ein/aus via Opazitäts-Animation.
- `CloseSearch()` (`:373`) — Convenience-Variante, schließt nur, wenn sichtbar.

**Wichtige private/Public Methoden** (im Detail in Kapitel 4 beschrieben):

- `SetupAttach()` (`:216`), `LoopProcesses(IEnumerable<string>)` (`:250`), `SetupExit()` (`:265`), `CleanClasses()` (`:278`), `GvpMaker(string)` (`:286`), `GetTypeFromName(string)` (`:323`), `GetSmoothNameFromProcessName(string)` (`:334`)
- `CheckForUpdates()` (`:176`), `CheckGit()` (`:124`), `CompareVer(string?)` (`:160`)
- `GetHotkey(GlobalHotkey)` (`:406`) — interaktive Hotkey-Eingabe: blendet das Overlay ein, wartet bis eine Taste oder `Escape` gedrückt wird, prüft via `HotkeysManager.CheckIfTheSameHotkeyExists`, ob die Kombination schon vergeben ist.

### 8.2 `ViewModels/Windows/DebugWindowViewModel.cs`

Steuert das Debug-Fenster. Minimal:

**Wichtige Properties**:

- `IsFh4` (`:26`) — `GameVerPlat.GetInstance().Type == GameType.Fh4`. Wird vom XAML-Binding genutzt, um FH4-spezifische Buttons sichtbar zu machen.
- `IsFh5` (`:27`) — analog für FH5 (greift auch bei FH6, weil dieses als Fh5 aliasiert ist).
- `CurrentDebugSession`, `AreAnyBreakpointsAvailable`, `WindowTitle` (alle `[ObservableProperty]`).

**`[RelayCommand]`-Methoden**:

- `DisableCrt()` — ruft `Cheats.ForzaHorizon4.Bypass.DisableCreateRemoteThreadChecks()` auf.
- `DisableCrc()` — ruft async `Cheats.ForzaHorizon5.Bypass.DisableCrcChecks()` auf.
- `DisableEncryption()` — ruft async `ValueEncryption.CheatDisableValueEncryption()` auf.

---

## 9. MVVM-Schicht — Page-ViewModels

Pro Page ein ViewModel unter `ViewModels/Pages/`. Alle sind dünn — komplexe Logik lebt im Code-Behind oder den Cheat-Klassen.

| ViewModel | Zweck | Wichtige Properties / Commands |
|---|---|---|
| `TuningViewModel` | Tuning-Page: Steuert den UI-State während des einmaligen Tuning-Scan-Vorgangs. | `AreUiElementsEnabled`, `AreScanPromptUiElementsEnabled`, `AreScanningUiElementsVisible`; `[RelayCommand] Scan()` |
| `AutoshowViewModel` | Autosalon-Page: Wrapper um SQL-Befehle, die `Cheats.ForzaHorizon{4,5}.Sql.Query` aufrufen. | `[RelayCommand] ExecuteSql(object)` — sperrt UI während des SQL-Laufs, dispatcht je nach `GameType` an FH4- oder FH5-Sql-Instanz |
| `SearchViewModel` | Such-Overlay: Volltextfilterung über alle `SearchResult`s. | `SearchResults` (ObservableCollection); private `Search(string)` befüllt die Collection per `SearchResults.EverySearchResult.Where(r => r.Contains(query))` |
| `AboutViewModel` | About-Page: zeigt Versions-String, öffnet URLs. | `Version` (= `Assembly.GetExecutingAssembly().GetName().Version`); `[RelayCommand] LaunchUrl(string)` öffnet die URL per `explorer.exe` |
| `AioInfoViewModel` | Willkommens-Page: Schnellzugriff auf Theme-Wechsel und Debug-Fenster. | `[RelayCommand] LaunchUrl(string)`, `ChangeMonet()`, `ShowDebugWindow()` (nutzt `WindowsProviderService.Show<DebugWindow>()`) |
| `SettingsViewModel` | Einstellungen-Page: Theme-Farbverwaltung. Sprache liegt im Code-Behind (`Settings.xaml.cs`). | `ThemeColor` (`Color`); `[RelayCommand] ChangeTheme()`, `MonetTheme()`, `ResetTheme()` |

---

## 10. MVVM-Schicht — SubPage-ViewModels

`ViewModels/SubPages/SelfVehicle/*.cs` und (falls vorhanden) `ViewModels/SubPages/Tuning/*.cs`. Jede SubPage steuert eine Gruppe verwandter Cheats. Das Muster ist überall gleich:

- **`AreXxxUiElementsEnabled`/`Visible`** — Booleans für UI-State (vor dem Scan / nach erfolgreichem Scan).
- **`<CheatName>Enabled`** — `bool`-Toggle, ob der jeweilige Cheat aktiv ist.
- **`<CheatName>Value`** — der zugehörige Wert (Multiplikator, Limit, Farbe …).

| ViewModel | Cheats |
|---|---|
| `HandlingViewModel` | Acceleration, Gravity, Velocity, Wheelspeed, Jump, Brake, NoWaterDrag, NoClip. Properties: `AccelValue`, `GravityValue`, `IsAccelEnabled`, `IsGravityEnabled` + `IsFh4`, `IsHorizon` zur Spiel-Typ-Diskriminierung im XAML. |
| `CameraViewModel` | FOV-Limiter (Chase/ChaseFar/Driver/Hood/Bumper), FOV-Lock, Kamera-Offset. Properties: `AreScanPromptLimiterUiElementsVisible`, `AreScanningLimiterUiElementsVisible`, `AreLimiterUiElementsVisible`, `AreCameraHookUiElementsEnabled`. |
| `CustomizationViewModel` | Glowing Paint, Headlight Colour, Dirt, Mud, Force LOD, Backfire Time. Pro Cheat Enable/Value-Paar. |
| `EnvironmentViewModel` | Sun RGB, Manual Time, FreezeAI. Properties: `AreSunRgbUiElementsEnabled`, `AreManualTimeUiElementsEnabled`. |
| `MiscViewModel` | 13+ Multiplier/Scale-Cheats (Prize, SkillScore, Drift, MissionTime, SpeedZone, Race, DangerSign, SpeedTrap, DroneHeight …) jeweils mit Enable/Value. |
| `PhotoModeViewModel` | NoClip, NoHeightLimits, IncreasedZoom, Photomode-Modifiers. |
| `UnlocksViewModel` | Credits, XP, Wheelspins, SkillPoints, Accolades, Kudos, Forzathon, Series, Seasonal. Pro Unlock Enable/Value. |

---

## 11. Code-Behind-Pattern

Code-Behind (`*.xaml.cs`) ist in diesem Projekt **nicht minimal**, weil viele UI-Operationen direkt auf Cheat-Methoden zugreifen — das wäre über reine Bindings umständlich. Das wiederkehrende Muster:

```csharp
private static CarCheats CarCheatsFh5 => Resources.Cheats.GetClass<CarCheats>();

private async void VelocitySwitch_OnToggled(object sender, RoutedEventArgs e)
{
    if (CarCheatsFh5.LocalPlayerHookDetourAddress == 0)
        await CarCheatsFh5.CheatLocalPlayer();

    if (CarCheatsFh5.LocalPlayerHookDetourAddress <= 0) return;
    GetInstance().WriteMemory(CarCheatsFh5.LocalPlayerHookDetourAddress + CarCheatsOffsets.VelEnabled, (byte)1);
}
```

Erst wird auf Bedarf der AOB-Scan + Detour-Installation ausgelöst, dann ein einzelnes Toggle-Byte im Code-Cave gesetzt. Mehr passiert nicht.

### 11.1 SelfVehicle-SubPages (`Views/SubPages/SelfVehicle/`)

| Datei | Verdrahtete Cheats |
|---|---|
| `Camera.xaml.cs` | FOV-Limiter-Scan + Min/Max-Slider, FOV-Lock-Toggle, Camera-Offset (X/Y/Z); pro Perspektive (Chase/ChaseFar/Driver/Hood/Bumper) ReadMemory/WriteMemory. Branch auf `GameType.Fh4` vs. `Fh5`. |
| `Customization.xaml.cs` | Glowing Paint + Dirt/Mud (ComboBox-Index → MainSwitch toggelt entweder `CheatGlowingPaint` oder `CheatCleanliness`), Headlight-Color-Picker → `CheatHeadlightColour`, Backfire-Time → `CheatBackfireTime`, Force LOD → `CheatForceLod`. |
| `Environment.xaml.cs` | Sun RGB (`SolidColorBrush` + Intensität-Slider → Vector4) → `CheatSunRgb`; Time-Slider → `CheatTime`; FreezeAI-Toggle → `CheatFreezeAi` (nur FH5). |
| `Handling.xaml.cs` | Vier Hotkey-Bindings (Jump, Brake, StopAllWheels, Velocity, Wheelspeed) + `ModifierToggleSwitch_OnToggled` (Acceleration/Gravity je nach ComboBox), `VelocitySwitch_OnToggled` (Velocity-Boost+Limit), `WheelspeedSwitch_OnToggled` (Mode+Boost+Limit), NoWaterDrag-Toggle, NoClip-Toggle. Siehe `Handling.xaml.cs:185`/`:218`. |
| `Misc.xaml.cs` | Eine zentrale ComboBox wählt zwischen 13+ Cheats; `MainComboBox_OnSelectionChanged` setzt Min/Max/Interval des NumericUpDown, `MainToggleSwitch_OnToggled` ruft die passende Cheat-Methode (Branch FH4/FH5), `MainValueBox_OnValueChanged` schreibt den neuen Wert. |
| `PhotoMode.xaml.cs` | NoClip-Toggle (FH4 und FH5 mit unterschiedlichen Offsets), NoHeightLimits, IncreasedZoom; `ModifiersScanButton_OnClick` triggert `CheatModifiers`, `Selector_OnSelectionChanged` wechselt zwischen Aperture/Samples/etc., `ValueBox_OnValueChanged` schreibt. |
| `Unlocks.xaml.cs` | 9 Unlock-Typen über eine ComboBox; `ToggleSwitch_OnToggled` führt async je Index das passende `CheatXxx` aus; `UnlockBox_OnSelectionChanged` synchronisiert UI. |

### 11.2 Tuning-SubPages (`Views/SubPages/Tuning/`)

Alle Tuning-SubPages folgen einem identischen Muster: ComboBox wählt ein Tuning-Attribut, der NumericUpDown bindet auf eine berechnete Adresse `TuningCheats.Base + TuningCheats.<Offset>` über `FollowMultiLevelPointer`.

| Datei | Verdrahtete Attribute |
|---|---|
| `Aero.xaml.cs` | FrontAero Min/Max, RearAero Min/Max |
| `Alignment.xaml.cs` | Negativer/Positiver Sturz vorne+hinten, Negative/Positive Spur vorne+hinten |
| `Damping.xaml.cs` | Stabilisator Min/Max vorne+hinten, Zugstufe Min/Max vorne+hinten, Druckstufe Min/Max vorne+hinten |
| `Gearing.xaml.cs` | Achsübersetzung, Rückwärtsgang, Gänge 1–10 |
| `Others.xaml.cs` | Radstand, Spurbreite/Spurplatten vorne+hinten, Felgengröße/-radius vorne+hinten |
| `Springs.xaml.cs` | Federn Min/Max vorne+hinten, Höhenlage Min/Max vorne+hinten, Begrenzung vorne+hinten |
| `Steering.xaml.cs` | Lenkwinkel Max + Max 2, Velocity Straight/Turning/Countersteer/DynamicPeek, TimeToMaxSteering |
| `Tires.xaml.cs` | Reifendruck vorne/hinten links/rechts (PSI/Bar) |

### 11.3 Window-Code-Behinds

- `MainWindow.xaml.cs` — Window-Drag (Top-50 px reagieren auf Maus-Drag), Minimize/Maximize/Close-Buttons, Search-Grid-Click → `ToggleSearchCommand`.
- `DebugWindow.xaml.cs` — Hide-on-Close (nicht zerstören, weil DI das Singleton hält), `DebugList_SelectionChanged` updated die `CurrentDebugSession`.
- `OverlayWindow.xaml.cs` — leerer Wrapper, nur `InitializeComponent()`.

### 11.4 Page-Code-Behinds

Meist nur `InitializeComponent` + ViewModel-Instanziierung. Ausnahmen:

- `SelfVehicle.xaml.cs` — `AddSearchResults()` registriert alle 7 SubPage-Expander im globalen Such-Index, sobald die Page geladen wird.
- `Search.xaml.cs` — `GoToAndFocusElement(SearchResult)` navigiert zur Ziel-Page, expandiert den passenden `DropDownExpander` und fokussiert das `FrameworkElement`.
- `Settings.xaml.cs` — Sprach-Auswahl: `LanguageBox_OnSelectionChanged` → `ApplyLanguage(langCode)` swappt das `Translations/<Code>.xaml`-Dictionary in `Application.Current.Resources.MergedDictionaries`; `ResetLanguage_OnClick` setzt zurück auf Englisch; `LoadSavedLanguage` initialisiert die ComboBox-Auswahl beim Page-Load.

---

## 12. Hotkey-System

Zwei Dateien unter `Resources/Keybinds/`:

### 12.1 `GlobalHotkey.cs`

Daten-Klasse für eine einzelne Hotkey-Bindung. Properties:

- `Modifier` (`ModifierKeys`) – Ctrl/Shift/Alt/Win
- `Key` (WPF `Key`) – die Haupttaste
- `Callback` (`Action`) – Methode, die beim Drücken aufgerufen wird
- `CanExecute` (`bool`) – Gating-Bool, z. B. nur wenn ein Cheat aktiv ist
- `IsPressed` (`bool`) – Debounce-State
- `Interval` (`int`, ms) – minimaler Abstand zwischen Wiederholungs-Callbacks bei gehaltener Taste

### 12.2 `HotkeysManager.cs`

Statische Manager-Klasse. Public API:

| Methode | Sinn |
|---|---|
| `SetupSystemHook()` (`:20`) | Installiert globalen Low-Level Keyboard Hook via `SetWindowsHookEx(WH_KEYBOARD_LL, ...)`. Zeigt MessageBox bei Fehler. Wird einmal in `App.xaml.cs:48` aufgerufen. |
| `ShutdownSystemHook()` (`:27`) | Deinstalliert den Hook beim App-Exit. |
| `AddHotkey(GlobalHotkey)` (`:33`) | Registriert eine Hotkey-Bindung. |
| `RemoveHotkey(GlobalHotkey)` (`:38`) | Entfernt eine Bindung. |
| `CheckIfTheSameHotkeyExists(Key, ModifierKeys)` (`:43`) | Duplikat-Check, verwendet von `MainWindowViewModel.GetHotkey` (`:447`). |

Intern (private):

- `Hotkeys` (Liste aller Bindings, statisch in `:11`)
- `HookCallback(...)` (Windows-Callback, ruft `CheckHotkeys()` per Dispatcher)
- `CheckHotkeys()` (`:48`) — iteriert über `Hotkeys`, prüft Modifier+Key+`CanExecute`+`Interval` und ruft `Callback()`.

---

## 13. Theming-System

`Resources/Theme/Theming.cs` — Singleton mit INPC.

### 13.1 Farbpalette

Aus einer einzigen Haupt-Farbe (`MainColour`) werden drei dunklere Varianten abgeleitet (`Theming.cs:65–78`):

- `DarkishColour` = MainColour × 0,9
- `DarkColour` = MainColour × 0,8
- `DarkerColour` = MainColour × 0,7

Alle vier sind `Brush`-Properties mit `SetField`-Setter, der `OnPropertyChanged` triggert → WPF-Bindings updaten automatisch.

### 13.2 Public API

| Methode | Sinn |
|---|---|
| `GetInstance()` (`:16`) | Lazy initialisiertes Singleton |
| `InitializeTheme()` (`:100`) | Liest gespeicherte Farbe via `Settings.LoadThemeColor()` und ruft `ChangeColor(savedColor)`, falls vorhanden |
| `ChangeColor(Color)` (`:53`) | Setzt alle vier Brushes, persistiert via `Settings.SaveThemeColor`, registriert ein dynamisches `ControlzEx.Theming.Theme` mit dem Namen `CustomTheme_<argb>` und ruft `ThemeManager.Current.ChangeTheme(...)` auf |
| `ResetTheme()` (`:109`) | Setzt zurück auf Default `#4C566A` (Nord-Polar-Night-3) |

### 13.3 Persistenz

`Helpers/Settings.cs` schreibt unter den `App.config`-AppSettings-Keys `ThemeColor` (ARGB-String) und `Language` (String). API:

- `SaveThemeColor(Color)` / `LoadThemeColor() → Color?`
- `SaveLanguage(string)` / `LoadLanguage() → string` (Fallback `"English"`)

Implementiert über `ConfigurationManager.OpenExeConfiguration(...)` und `RefreshSection("appSettings")` nach jeder Schreibe.

---

## 14. Lokalisierung

System: ein `ResourceDictionary` pro Sprache unter `Resources/Translations/`, in `Application.Current.Resources.MergedDictionaries` zur Laufzeit hot-swapped.

### 14.1 Datei-Struktur

| Datei | Status |
|---|---|
| `English.xaml` | Default, vollständig |
| `French.xaml` | Aktiviert |
| `ChineseSimplified.xaml` | Aktiviert |
| `German.xaml` | Aktiviert |

Jede Datei enthält ~267 `<system:String x:Key="...">`-Einträge, plus drei „Meta"-Keys: `TranslatedBy` (mit Trailing-Space für Konkatenation mit Translator-Liste), `LanguageName` (für jeden gelisteten Sprach-Code ein Eintrag in der **eigenen** Sprache der Datei — z. B. in `German.xaml`: `English → "Englisch"`, `German → "Deutsch"`).

### 14.2 Bootstrap

`App.xaml.cs:73–98` — beim Start wird `Settings.LoadLanguage()` ausgelesen, das `Translations/<Code>.xaml` als `ResourceDictionary` per `Source = new Uri(...)` geladen, ggf. ein altes Translations-Dictionary entfernt (Substring-Match `"/Resources/Translations/"`) und das neue hinzugefügt. Schlägt das Laden fehl → `try/catch` schluckt die Exception, Translation bleibt am Default.

### 14.3 Runtime-Switch

`Views/Pages/Settings.xaml.cs::ApplyLanguage(string code)` — dieselbe Logik wie der Bootstrap, plus `Settings.SaveLanguage(code)`. Wird von `LanguageBox_OnSelectionChanged` (Sprach-ComboBox in den Settings) aufgerufen.

### 14.4 ComboBox-Einträge

Alle 32 Sprachen sind in `Settings.xaml:100–263` als `<controls:TranslationComboboxItem>` hardcoded mit `Content="{DynamicResource <LanguageName>}"`, `LanguageCode="..."`, `Translators="..."`, `IsEnabled="True|False"`. `IsEnabled=False` bedeutet: die Sprache ist im UI sichtbar, aber nicht auswählbar (Übersetzung noch nicht vorhanden).

---

## 15. Debug-Infrastruktur

Drei Bausteine:

### 15.1 `Models/DebugSession.cs`

Container für eine logische Debug-Gruppe (z. B. „Bypass"). Felder:

- `Name` (string, readonly)
- `DebugInfoReports` (ObservableCollection<DebugInfoReport>, readonly)

### 15.2 `Models/DebugInfoReport.cs`

Einzelner Log-Eintrag: `Info` (Nachricht), `ReportedAt` (ISO-Zeitstempel).

### 15.3 `Resources/DebugSessions.cs`

Singleton-Container, hält `ObservableCollection<DebugSession> EveryDebugSession`. Wird initial mit einer Bypass-Session befüllt, sodass das Debug-Fenster sofort etwas zum Anzeigen hat.

### 15.4 `Views/Windows/DebugWindow.xaml(.cs)`

`Closing`-Event wird auf `Hide()` umgeleitet — das Fenster bleibt im Speicher, damit DI seine Singleton-Instanz wiederverwenden kann. Der Inhalt ist eine TreeView über `EveryDebugSession`.

Aufruf des Debug-Fensters: `AioInfoViewModel.ShowDebugWindow()` → `WindowsProviderService.Show<DebugWindow>()`.

---

## 16. Search-Overlay

### 16.1 `Models/SearchResult.cs`

Readonly-Struct mit:

- `Name`, `Category`, `Feature`, `Page` (alle `string`)
- `PageType` (`Type`) — Ziel-Page für Navigation
- `FrameworkElement`, `DropDownExpander` (UI-Referenzen, werden zur Laufzeit gesetzt)
- `Contains(string query)` — Case-insensitive Substring-Match über alle 4 Strings

### 16.2 `Resources/Search/SearchResults.cs`

Statisch:

```csharp
public static readonly List<SearchResult> EverySearchResult = ...
```

Enthält Initial-Einträge für die Haupt-Pages (AioInfo, Autoshow, SelfVehicle, Tuning, About, Settings). SubPage-Expander werden nachträglich von `SelfVehicle.xaml.cs::AddSearchResults` ergänzt, wenn die Page das erste Mal geladen wird.

### 16.3 Search-Flow

1. User drückt Such-Hotkey oder klickt auf das Such-Icon → `MainWindowViewModel.ToggleSearch()` fadet das Overlay ein.
2. User tippt → `SearchViewModel.Search(query)` filtert `EverySearchResult` und füllt `SearchResults`.
3. Doppelklick / Enter auf einem Treffer → `Search.xaml.cs::GoToAndFocusElement(result)` setzt `MainWindowViewModel.CurrentView = Pages.GetPage(result.PageType)`, expandiert den `DropDownExpander` und fokussiert das `FrameworkElement`.

---

## 17. Infrastruktur-Utilities

Kompakte Tabelle der „Plumbing"-Klassen:

| Datei | Public API | Sinn |
|---|---|---|
| `Resources/Memory.cs` | `GetInstance()` | Singleton-Wrapper um `Memory.Mem` aus `Resources/External/Memory.dll`. Cheat-Klassen rufen `GetInstance().OpenProcess(...)`, `.AoBScan(...)`, `.ReadMemory<T>(...)`, `.WriteMemory<T>(...)`, `.FollowMultiLevelPointer(...)`. |
| `Resources/Cheats.cs` | `CachedInstances` (Dict), `GetClass<T>()` | Lazy-Cache aller Cheat-Klassen-Instanzen. |
| `Resources/Pages.cs` | `GetPage(Type)`, `Clear()` | Lazy-Cache der Page-Views, bewahrt Page-State über Navigation hinweg. `Clear()` löscht alle außer `AioInfo`. |
| `Resources/Imports.cs` | `CloseHandle`, `WaitForSingleObject`, `CreateRemoteThread`, `GetModuleHandle`, `GetProcAddress`, plus interne `VirtualQueryEx`/`VirtualAllocEx`/`VirtualFreeEx`/`GetSystemInfo` | P/Invoke-Wrapper für kernel32.dll. |
| `Resources/DebugSessions.cs` | `GetInstance()`, `EveryDebugSession` | Container für DebugSession-Liste (siehe Kapitel 15). |
| `Models/GameVerPlat.cs` | `GetInstance()`, `Name`, `Platform`, `Update`, `Type`, `enum GameType { None, Fh4, Fh5 }` | Singleton, hält erkannte Spielversion. **Wichtig**: FH6 mappt auf `Fh5`, daher kein eigener Enum-Wert. |
| `Services/ApplicationHostService.cs` | `StartAsync`, `StopAsync` | `IHostedService`: beim `Host.StartAsync()` zeigt es das MainWindow (falls noch nicht da) und ruft `InitTheme()` zur ThemeManager-Initialisierung. |
| `Services/WindowsProviderService.cs` | `Show<T>()` (T : Window) | Holt T aus dem DI-Container, setzt `Owner = MainWindow`, ruft `Show()`. Verhindert Window-Duplikate ohne Owner. |

---

## 18. Custom Controls

Beide unter `Controls/`, je als eigene XAML+CS-Komponente.

### `StatusComboboxItem`

WPF `ComboBoxItem`-Subklasse mit einer zusätzlichen `DependencyProperty<bool> IsOn`. Zweck: ComboBox-Einträge optisch zeigen, ob der zugehörige Cheat gerade aktiv ist (z. B. „Velocity ✓").

### `TranslationComboboxItem`

`ComboBoxItem` mit zwei `DependencyProperty<string>`:

- `LanguageCode` — interner Bezeichner (= XAML-Dateiname ohne Extension, z. B. `"German"`)
- `Translators` — kommaseparierte Liste von Mitwirkenden, wird unter dem Sprachnamen als Tooltip oder direkt im Item angezeigt (mit `{DynamicResource TranslatedBy}` als Präfix).

---

## 19. Converters

Alle unter `Converters/`. Pro Datei eine `IValueConverter`- oder `IMultiValueConverter`-Implementierung:

- **`BoolParamConverter`** — `MultiValueConverter`: liest ein `bool`-Array plus zwei String-Parameter und liefert je nach Bool den ersten oder zweiten String. Beispiel: `{Binding IsOn} → "(Ein)" / "(Aus)"`.
- **`InstanceEqualsConverter`** — `IValueConverter`: vorwärts prüft, ob der Binding-Wert ein bestimmter `Type` ist (`true`/`false`); rückwärts liefert `Pages.GetPage(type)` (für ein RadioButton/Tab-Pattern, das Navigation via Property-Binding macht).
- **`IntParamConverter`** — `MultiValueConverter`: Index (int) + Array → Wert an Position `Index+1`, mit Out-of-Range-Default. Wird bei Statusanzeigen verwendet, die zwischen mehreren Werten je nach Auswahl wechseln.
- **`MultiplyConverter`** — `IValueConverter`: Wert × Parameter (beide als `double`). Praktisch für UI-Sizing über Bindings.
- **`TypeToInstanceConverter`** — `IValueConverter`: Type → Page-Instanz via `Pages.GetPage(type)`. Wird in der Navigation als XAML-Binding-Konverter eingesetzt, damit `ListBox` mit `Type`-Werten arbeiten kann, aber tatsächlich Page-Instanzen rendert.

---

## 20. Datenfluss-Beispiel: Velocity-Hack

Komplett-Durchlauf, wie ein einzelner Cheat von Klick bis Speicher-Write zustandekommt. Spiel ist FH5.

1. **User-Klick**: Toggle-Switch in der Handling-SubPage → WPF feuert `Toggled`-Event → `Views/SubPages/SelfVehicle/Handling.xaml.cs::VelocitySwitch_OnToggled` (`:185`).
2. **Bedingung prüfen**: Method-Body liest `CarCheatsFh5.LocalPlayerHookDetourAddress`. Steht der bei `0`, ist noch nicht gescannt → `await CarCheatsFh5.CheatLocalPlayer()`.
3. **AOB-Scan**: `CheatLocalPlayer()` (`Cheats/ForzaHorizon5/CarCheats.cs:43`):
   - Sicherstellen, dass `Bypass.CallAddress > 3` (sonst `await GetClass<Bypass>().DisableCrcChecks()`).
   - Sicherstellen, dass `_racePtr > 0` (sonst `_racePtr = await CheatRacePtr()`).
   - `SmartAobScan("F3 0F ? ? ? 49 8B ? 49 8B ? 0F 28")` durchsucht den Spielcode-Speicher.
4. **Code-Cave allokieren**: Mit `VirtualAllocEx` Speicher im Spielprozess reservieren, Shellcode (Velocity/Wheelspeed/Jump/Brake/Stop-Logik mit Toggle-Bytes) hineinschreiben.
5. **Detour installieren**: `CalculateDetour(originalAddress, codeCaveAddress, replaceCount)` erzeugt 5-Byte-`E9`-JMP plus NOPs, `Mem.WriteMemory` schreibt sie an die Originalstelle.
6. **Hook ist live**: Spiel ruft jetzt bei jeder LocalPlayer-Update-Logik in den Code-Cave. Der Code-Cave prüft Toggle-Bytes — sind sie 0, fällt er auf das Original-Verhalten zurück; sind sie 1, wendet er die manipulierten Werte an.
7. **Toggle-Byte schreiben**: Zurück im Code-Behind:
   ```csharp
   GetInstance().WriteMemory(
       CarCheatsFh5.LocalPlayerHookDetourAddress + CarCheatsOffsets.VelEnabled,
       (byte)1);
   ```
   `CarCheatsOffsets.VelEnabled` ist ein `const int` aus `CarCheats.cs:9–22`, der den Offset des Velocity-Toggle-Bytes innerhalb des Code-Cave bezeichnet.
8. **Wert schreiben**: Zwei weitere `WriteMemory`-Aufrufe für `VelBoost` (Multiplikator) und `VelLimit` (Hard-Cap).
9. **Anti-Cheat-Bypass läuft im Hintergrund**: Der 10-s-`System.Timers.Timer` aus `Bypass.cs` ruft alle 10 s `Revert()` auf jeder `IRevertBase`-Instanz, was die Patches kurz zurücknimmt, kurz wartet (für den FH5-CRC-Pass), und dann `Continue()` ruft.
10. **Process-Exit**: FH5 wird beendet → `MainWindowViewModel.SetupExit` triggert → `CleanClasses()` ruft `CarCheats.Reset()` → alle Detour-Adressen zurück auf `0`. Beim nächsten Spiel-Start wird der Scan erneut durchlaufen.

---

## 21. Erweitern: neue FH-Version

Aktueller Stand: FH4, FH5, FH6 unterstützt. Eine neue Forza-Version (z. B. FH7) hinzuzufügen erfordert — solange das Memory-Layout zur bestehenden FH5-Codebasis kompatibel bleibt — nur drei Stellen in `MainWindowViewModel.cs`:

1. **Prozessliste** (`:218`):
   ```csharp
   string[] processNames = ["forzahorizon5.exe", "forzahorizon4.exe", "forzahorizon6.exe", "forzahorizon7.exe"];
   ```
2. **Smooth-Name-Mapping** (`:334`):
   ```csharp
   "forzahorizon7.exe" => "Forza Horizon 7",
   ```
3. **GameType-Mapping** (`:323`):
   ```csharp
   "Forza Horizon 7" => GameVerPlat.GameType.Fh5,  // Alias, solange Layout passt
   ```

Optional: `NotAttachedText` (`:32`) anpassen.

**Niemals** einen neuen `GameType.Fh6`/`Fh7`-Enumwert hinzufügen, solange das Verhalten identisch bleiben soll — das würde ~30 `case Fh5:`-Stellen in den ViewModels und Code-Behinds duplizieren.

Falls Memory-Layouts divergieren: dann einen neuen Enum-Wert + parallelen `Cheats/ForzaHorizon7/`-Namespace anlegen und in allen `switch (GameType)` einen neuen Arm ergänzen.

---

## 22. Erweitern: neue Sprache

1. **`Resources/Translations/<LanguageName>.xaml`** erstellen. Als Vorlage `English.xaml` kopieren, alle `<system:String x:Key="...">`-Werte übersetzen. Wichtig:
   - Schlüssel-Reihenfolge beibehalten (vereinfacht Diffs).
   - `TranslatedBy`-Trailing-Space beibehalten.
   - Sprachnamen-Block (Keys `English`, `Afrikaans`, ..., `Vietnamese`) **in der eigenen Zielsprache** übersetzen, damit das Sprachen-Dropdown konsistent erscheint.
2. **`Views/Pages/Settings.xaml`** anpassen. Entweder das bereits vorhandene `<TranslationComboboxItem>` für die Sprache von `IsEnabled="False"` auf `True` umstellen, oder einen neuen `TranslationComboboxItem` ergänzen:
   ```xml
   <controls:TranslationComboboxItem Content="{DynamicResource <LanguageName>}"
                                     Translators="<Namen>"
                                     LanguageCode="<LanguageName>"
                                     IsEnabled="True"/>
   ```
3. **Test**: Build (`dotnet build`), App starten, in den Einstellungen die neue Sprache auswählen — UI muss ohne Neustart umschalten. Persistenz prüfen durch App-Neustart.

Kein Code-Change in `App.xaml.cs`, `Settings.cs` oder `Settings.xaml.cs` nötig — alle drei sind datengetrieben.

---

## 23. Erweitern: neuer Cheat

Beispiel: einen neuen `MiscCheats`-Eintrag für FH5 hinzufügen.

1. **Cheat-Methode in der passenden Klasse anlegen**, hier `Cheats/ForzaHorizon5/MiscCheats.cs`:
   ```csharp
   private UIntPtr _myCheatAddress;
   public UIntPtr MyCheatDetourAddress;

   public async Task CheatMyCheat()
   {
       _myCheatAddress = 0;
       MyCheatDetourAddress = 0;

       if (GetClass<Bypass>().CallAddress <= 3)
           await GetClass<Bypass>().DisableCrcChecks();

       const string sig = "AB CD ? ? EF ...";
       _myCheatAddress = await SmartAobScan(sig);
       if (_myCheatAddress <= 0)
       {
           ShowError("MyCheat", sig);
           return;
       }
       // VirtualAllocEx + Shellcode + Detour-JMP wie in CheatLocalPlayer
       // MyCheatDetourAddress = ...
   }
   ```
2. **`Cleanup()` und `Reset()`** der Klasse erweitern, sodass das neue Feld korrekt freigegeben/genullt wird. Falls `IRevertBase` implementiert ist: `Revert()`/`Continue()` ebenfalls anpassen.
3. **UI-Element** in der zuständigen SubPage-XAML ergänzen (z. B. einen neuen ComboBox-Eintrag in `Misc.xaml`).
4. **Code-Behind-Handler** (`Misc.xaml.cs::MainToggleSwitch_OnToggled`) um einen neuen `case` für den ComboBox-Index erweitern, der bei Bedarf `CheatMyCheat()` ruft und das passende Toggle-Byte schreibt.
5. **Translation-Keys** in allen vier `Resources/Translations/*.xaml` ergänzen (`English`, `French`, `ChineseSimplified`, `German`) — Schlüssel-Name in PascalCase, Wert in der jeweiligen Sprache.
6. **Optional: SearchResult eintragen**, damit der Cheat über das Such-Overlay gefunden wird (`Resources/Search/SearchResults.cs::EverySearchResult`).

Bei FH4 dieselben Schritte, aber in `Cheats/ForzaHorizon4/MiscCheats.cs` und ohne `IRevertBase`.

---

*Stand: Mai 2026. Diese Dokumentation gilt für Build `2.5.0.0` (siehe `Forza-Mods-AIO.csproj::AssemblyVersion`).*
