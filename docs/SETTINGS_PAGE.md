# Settings page in the pause menu (since 0.10.1)

Pause (Esc) → **SkulInsight QoL** (right under the game's Settings) opens a page with every config entry of the
mod. Built 2026-10-03, released in 0.10.1. Umut's first look: works (full test still open).

## 1. The game's pause UI (read offline with `tools/unity/dump_pause.py` / `dump2.py`)

- Canvas "UI Canvas" (CanvasScaler 1920x1080, match width) → "Inside Of Letterbox" → **"Pause"** (`UI.Pause.Panel`,
  a `UI.Dialogue`). Children: Black Background, TopLeft HUD, **Menu**, **Controls**, **Settings** (each a Dialogue,
  full-stretch, inactive until opened).
- `Panel.state` (Menu/Controls/Settings) closes one child and opens another. `Panel.Update` calls `Return()` on the
  Pause or Cancel key; `Return()` only acts if Menu/Settings/Controls is the focused dialogue.
- `Panel.Awake` adds `PlaySoundOnSelected` (with `_selectSound`) to every Selectable below it, including inactive
  ones; clones of those widgets keep the sound.
- **Menu**: Frame (Pause_Frame sprite, scale 3), Title, **Grid** (`GridLayoutGroup`, cell 270x51, spacing 20) with
  buttons Continue, New Game, Controls, Settings, Quit. Each button is a TMP text + `Button` (ColorTint) +
  `TextLocalizer` (sets the text in `OnEnable` from a key). Navigation explicit and wrapping (Settings: automatic).
  `Menu.Awake` adds the click listeners at runtime (not serialized, so clones don't get them).
- **Settings** page: Frame `Options_Frame` (510x360 at scale 3 = 1530x1080; it has built-in heading lines at
  y 279 and 696, so we use the **Controls** page frame `Control_Frame` instead: two clean columns). Title at y -124.
  Section headings ("Graphics Label": NotoSans-Black, colour #AE9785, 400x60, pivot bottom-left, sits on the line).
  Rows (`Graphics/Resolution` etc.) are `UI.Pause.Selection` (a Selectable: `SetTexts`, `SetValueWithoutNotify`,
  `onValueChanged`, left/right via `OnMove`, wraps around) with children ColorDiffuser (tints label + value + box on
  selection), Label (TMP 300x50 at x -233), Content (option box sprite, scale 3) / Text, Left/Right arrow hit areas
  (`PointerDownHandler`). Row pitch 53. Button rows (`Data/Reset Data`) have the same shape with a `Button`.
  `ControllerLeftRightNavigation` jumps between columns with bumpers (we remove it from clones).
- Return button at y -982 (bottom plate). The game's `Confirm` dialogue (`uiManager.confirm.Open(text, yes, no)`).
- Heading underline colours (3 rows of 1 px at 3x): (94,63,46), (16,13,11), (43,30,23).

## 2. Architecture

- `Patches/SettingsMenuPatches.cs`:
  - `PauseMenuButtonPatch` (postfix `UI.Pause.Menu.Awake`) → `SettingsPage.AttachTo(menu)`: clones the Settings
    button into the grid after it, removes `TextLocalizer`, text "SkulInsight QoL" (no word wrap, auto-size),
    new `onClick`, shrinks the grid gap so 6 buttons fill the same height, explicit wrapping up/down links.
  - `PauseReturnPatch` (prefix `Panel.Return`) → `SettingsPage.HandleBack()`: Esc/Cancel on our page cancels a key
    capture or goes back to the menu, exactly like the game's own pages. Only acts when our page has been the top
    dialogue since an earlier frame (so a Confirm closing with the same key press doesn't also leave the page).
- `UI/SettingsPage.cs`: a `UI.Dialogue` subclass (overrides must be `public override`: the game DLL is publicized
  at compile time; widening is legal at runtime). Created lazily on the first click as a sibling right after the
  Pause panel (its backdrop stays behind, the Confirm dialogue in front). Built while inactive from clones of the
  game's widgets (so nothing wakes up before the localizers and bumper scripts are stripped): Controls/Frame,
  Settings/Title, headings, Resolution row (choices), Reset Data row (keys, actions), Return (keeps its
  TextLocalizer, so "Return" is in the game's language).
  - Left: `ScrollRect` + `RectMask2D` list; headings with the game's underline; auto-scroll to the selected row only
    for keyboard/controller (mouse moved or wheel used in the last 0.4 s → no auto-scroll, else hover selection
    would chase the mouse). Thin scrollbar. Right: help text and "Default: x" of the selected row.
  - Choice rows: values from the catalog; numbers stop at the ends (the game's Selection wraps; we undo a wrap);
    on/off rows also flip with Enter/A (`SubmitStepper`). Icon rows show the icon sprite (`IconLibrary.Resolve`).
  - Key rows: click → "Press a key" → next keyboard key (with held modifiers) becomes the binding; Esc/B keeps the
    old one; navigation events are off during the capture; clicks right after are ignored. Our L/K hotkeys stay
    quiet while the page is open (`SettingsPage.IsOpen`).
  - Changes are written to the config entry at once (BepInEx saves the file on every set). "Reset everything"
    sets every entry to its default with one save (via the game's Confirm dialogue).
  - A `Link` component on our menu button owns the page; when the language changed (`Loc.Version`) the page is
    destroyed and built again, keeping the selected row (also when the change came from its own Language row).
- `UI/SettingsCatalog.cs` (plain, tested in `SettingsCatalogTests`): turns the `ConfigFile` into rows. Section
  order and headings, labels/help per "Section/Key" (`Specs`, all `Loc.N`), number ranges and units (s, %),
  `NumberSteps` (long ranges get coarser: 1-step to 10, then 5s, 50s from 100, 500s from 1000, so holding an arrow
  is quick), a hand-edited value is kept between the steps, `General/Language` (auto = "Game language" + the
  languages' own names), damage icon choices ("none" = text tag + every inscription icon, names in the game's
  language), Codex balance entries (labels/ranges from `CodexBalance`, grouped "Codex unlocks: enemies/bosses/gear"),
  hidden `Combat Log/WindowRect` → action row "Window position" (reset), final "Reset everything".
  Unknown entries still appear (label from the key; bool/enum/number/key handled generically).
- Live settings: the combat log follows background, opacity, window position and key changes at once
  (`CombatLogWindow.Awake` subscriptions; `WindowFrame.MoveTo`). Everything else already read its config live.
  `CodexBalance.Bind(Config)` runs in all builds now (section "Codex balance", with ranges).

## 3. Tested / open

- Unit: catalog (sections, steps, formats, kept values, icon choices, balance ranges, every number in Plugin.cs
  has a range), patch targets (`Menu.Awake`, `Panel.Return` exist), hooks guarded.
- In game (Umut, 2026-10-03, first look): works. Not yet checked in detail: controller navigation everywhere (Umut
  trusts it), key capture edge cases, 6-button spacing in the menu frame, every language.
- Mouse wheel (fixed and verified by Umut 2026-10-03): it stuck while the pointer was over a row. Cause: the game's
  `PlaySoundOnSelected` (added to every Selectable by `Panel.Awake`, cloned with our rows) is an **EventTrigger**, and
  an EventTrigger implements every pointer interface: it receives the wheel and drops it, so the event never bubbles
  to the ScrollRect. Every row now also has `WheelToList` (IScrollHandler) that passes the wheel to the list.
