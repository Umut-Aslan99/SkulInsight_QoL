# Localization (since 0.10.1)

Everything the mod shows follows the game's language. Built 2026-10-03; released in 0.10.1. This file explains how
it works, why it is built this way, how to add or change texts, and what was learned on the way.

## 1. What the game does (facts we rely on)

- **11 languages**, one column each in the game's string table, in this order (index = column):
  `0 ko, 1 en, 2 ja, 3 zh-Hans, 4 zh-Hant, 5 de, 6 es, 7 pt-BR, 8 ru, 9 pl, 10 fr` (`GameResources.Localization.Language`).
- The chosen language is `Data.GameData.Settings.language` (that index). The game's options page just sets it;
  **no event is raised** (`Localization.Change`/`OnChange` exist but the options page doesn't call them). So we poll.
- The string table is a `LocalizationStringResource` ScriptableObject in an Addressables bundle
  (`StreamingAssets/aa/StandaloneWindows64/6ffe3e2272fd9e25d40f6c0a0a6a1e41.bundle`, 7986 rows). Keys are only stored
  as hashes: `h = h*31 + char` over the **uppercased** key (Mono's OrdinalIgnoreCase hash). Known key families:
  `item/<prefab>/name`, `enemy/name/<CharacterKey>`, `synergy/key/<InscriptionKey>/name`, `label/pause/settings/...`
  (`on`, `off`, `return`, `data/reset`), `language/code|native|number|system`. At runtime use
  `Localization.TryGetLocalizedString(key, out text)`. Offline: `tools/unity/loctable.py`.
- **Fonts are static TMP atlases** that hold only the characters the game's own texts use: NotoSans (235 chars:
  Latin incl. German/French/Spanish/Portuguese/Polish letters, Cyrillic, ‘’“”„…«»; *no* "–" "·" "→" "×" "★" "º" "ª"),
  with fallbacks NotoSansCJKkr (1225 Hangul syllables), CJKjp (1915), SC (3602). "–" and "·" render through the CJK
  fallbacks; "→", "×", "º", "ª" render nowhere (boxes). "★" isn't in any game font either (settings page spells
  stars out if the font lacks them; elsewhere it shows whatever TMP falls back to).

## 2. Architecture (`DamageInsight/Lang/`)

**`Loc.cs` (plain C#, tested)**: the English text is the key ("gettext style").
- `Loc.T("text")`: translate. `Loc.F("{0} hits of {1}", a, b)`: translate the format, fill with
  `CultureInfo.InvariantCulture` (all numbers stay "1.5"; a broken translation falls back to English).
- `Loc.P("{0} kill", "{0} kills", n, more...)`: plurals. Key = `one|other`; the translation is a list of forms:
  en/de/es/pt-BR/fr 2 (fr and pt: 0 and 1 are "one"), ru/pl 3 (one, few, many, CLDR rules in `PluralIndex`),
  ko/ja/zh 1. `{0}` is the count.
- `Loc.N("text")`: returns the text unchanged; marks literals in tables (labels, help) for the extractor. They are
  translated where shown with `Loc.T(variable)`.
- `Loc.Name(english)`: names made from game data (boss moves, animations, skull actions, summons, groups). Whole
  name if translated, else by parts: splits " · " and ": ", a trailing number ("Attack 7"), a bracket remark
  ("X (enhanced)"). Unknown names stay English.
- `Loc.Phrase(english, templates)`: English the mod **stored on disk** (move hints in `Codex/Hints/*.json`) is
  translated by matching templates ("below {0} % HP") and filling the captured parts.
- `Loc.Ordinal(n)` = English "3rd"; templates with ordinals get `{0}` = "3rd" and `{1}` = 3 so translations can use
  either ("Jeder {1}. Schwung").
- `Loc.Join` / `Loc.JoinOr` ("a, b and c" with translated ", " " and " " or ").
- `Loc.Use(code)`, `Loc.Current`, `Loc.Version`, `event Loc.Changed`; `Loc.With(code, action)` sets a language for
  one thread (tests run in parallel; never change the global language in a test).
- Tables: embedded `DamageInsight.Lang.<code>.json` = `{"_language", "_note", "strings": {English: text | [forms]}}`.
  Player corrections: `BepInEx/DamageInsight/Lang/<code>.json` (same format, only changed entries) are merged on
  top when the language loads (`Loc.OverrideFolder`, set in Plugin.Awake).
- `Loc.Recorder`: a hook the tests use to collect every key the code looks up.

**`LanguageWatcher.cs` (Unity side)**: `Tick()` from `Plugin.Update`, twice a second: wanted language = config
`General/Language` (`auto` or a code; settings page row) else `GameOrder[GameData.Settings.language]` → `Loc.Use`.
**`FontFallback`**: for ko/ja/zh-Hans/zh-Hant creates once a dynamic TMP font from a Windows font
(`Font.GetPathsToOSFonts()` / `C:\Windows\Fonts`: malgun, YuGoth/meiryo/msgothic, msyh/simhei/simsun, msjh/mingliu;
`new Font(path)` + `TMP_FontAsset.CreateFontAsset(font, 64, 6, SDFAA, 1024, 1024, Dynamic)`) and appends it to
`TMP_Settings.fallbackFontAssets` and every loaded font's fallback list. It's a safety net only: the translations
are written with characters the game fonts have (test), so the game's look stays.

**Where texts are translated** (two kinds):
1. *Made fresh each time* (descriptions, combat log, tooltip, settings page, Codex UI): translated where created.
   Caches that hold translated text clear on `Loc.Changed` (`GearDescriptions` breakdown cache and summons,
   `DamageLog` name cache, `CodexCatalog` entries, `CodexContent`). Windows built once rebuild themselves:
   `CombatLogWindow.Rebuild`, `CodexWindow.Rebuild`, `SettingsPage` (built again in place, same row).
2. *Stored as keys* (boss move labels: progress.json "moves seen", films, refilm lists, `codex_content.json` and the
   hint files are keyed by the English label): **stay English internally**, translated only when drawn
   (`Loc.Name(clip.Label)`, `MoveHints.Localize`). Never translate these at creation.
- Code that used to parse its own English output was changed (e.g. `StatusDescriptions.SuperBleed` instead of
  splitting a text for "Super bleed"; `Step.IsPart` instead of `Label.StartsWith("Part ")`;
  `DescriptionFormatter.Notes(hit, repeats: false)` instead of filtering "repeats").
- Not translated on purpose: BepInEx config descriptions (the cfg file), developer tools, BepInEx log messages.
  The combat log files use the same line text, so they are in the chosen language.

**Codex notes**: `Codex/codex_content.json` (English) + `codex_content.<code>.json` (same keys); a missing field
falls back to English. Only Yggdrasil has notes so far (draft).

## 3. Files

| File | What |
|---|---|
| `DamageInsight/Lang/<code>.json` | 10 translations, 838 texts each (2026-10-03). Embedded. |
| `DamageInsight/Lang/template.json` | Every English text: `"strings"` (from code) and `"names"` (from game data). Not embedded. |
| `DamageInsight/Codex/codex_content.<code>.json` | Codex notes per language. |
| `tools/lang/lang.py` | `status`, `todo [start] [count]`, `merge <batch.json>`, `prune`. |
| `DamageInsight.Tests/LocalizationTests.cs` | Template generator and every check (below). |
| `DamageInsight.Tests/Fixtures/font_chars.json` | Characters of every game font (`tools/unity/fontchars.py`); private (game data). |

csproj: both JSON globs carry **`WithCulture="false"`**. Without it MSBuild treats `codex_content.ko.json` as a
Korean resource and moves it into a satellite assembly `ko/DamageInsight.resources.dll` (the release ships the DLL
only). The test `Language_files_are_inside_the_dll` guards this.

## 4. Workflow: adding or changing a text

1. In code use `Loc.T/F/P` with a **literal** (never `$"..."` inside; a test checks), or `Loc.N` in tables. Keep
   whole sentences in one format string so translators can reorder ("{0} dealt {1} {2} {3} damage to {4}").
   Avoid case-sensitive slots for ru/pl/de: phrase templates so the inserted noun can stay in its base form
   ("При попадании ({0})", "Nach: {0}").
2. `UPDATE_LANG_TEMPLATE=1 dotnet test DamageInsight.Tests -p:DeployToSkul=false --filter "FullyQualifiedName~LocalizationTests.Template"`
   (adds names from the gear scan `C:\Users\Umut\SkulModding\GearScan` and the Codex folder in the Skul install).
3. `python tools/lang/lang.py todo` → write a batch `{"English": {"ko": "...", ..., "ru": [one, few, many]}}` for all
   10 languages → `python tools/lang/lang.py merge batch.json` → `prune` if texts were removed.
4. `dotnet test ...` must be green. Checks: template covers code and game data; every language has every text;
   same placeholders (ordinal texts may use {0} or {1}) and same rich-text tags; only characters of the game fonts
   (all 10 languages + Codex notes); resources inside the DLL; plural rules; Loc fallbacks.
   Note: test output is German on this PC ("Bestanden!/Fehler!"), grep both.

Terms: use the game's own words (from its string table): Skull 스컬 / スカル / 小骨 / 孤骨 / Schädel / calavera / caveira /
череп / czaszka / crâne; Inscription 각인 / 刻印 / Inschrift / inscripción / inscrição / гравировка / inskrypcja;
Quintessence 정수 / 精髄 / 精华 / 精髓 / Essenz / quintaesencia / quintessência / квинтэссенция / kwintesencja;
Dark Mirror 검은 거울 / 闇の鏡 / 魔镜 / 黑暗鏡子 / Dunkler Spiegel / Espejo oscuro / Espelho Sombrio / Тёмное зеркало /
Ciemne lustro / Miroir noir; adventurers are "герои" in Russian; cooldown = クールダウン / 冷却 / Abklingzeit /
recarga / arrefecimento / перезарядка / odnowienie / rechargement. "Tuned" (inscription true form) has no game term:
강화형 / 強化形態 / 强化形态 / Verstärkt / Potenciada / Aprimorada / Усиленная / Wzmocniona / Renforcée.

## 5. State and open points

- Done and released (0.10.1): all texts, 10 languages, Codex notes (Yggdrasil). Umut's first look: works.
- Not checked yet: translations by native speakers (they are Claude's); every screen in every language (Umut did a
  first overall look only); the Windows font fallback in game (log lines "Fonts: ..." show whether it was needed).
- New boss moves met later produce new English names → they show in English until the template is regenerated
  (with the new Codex data) and translated.
