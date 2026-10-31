# Settings page 2.0 and the "insight vs. gameplay" split (brainstorm, 2026-10-30)

Status: proposal for Umut. Nothing built yet. Planned for 0.17 once he picks an option.

## The problem
- The settings page is one long list: about 80 settings in 16 sections. Players scroll a lot to find one switch.
- Players say 0.15's balance changes (rewards) don't interest them. They want what only *shows* information kept
  apart from what *changes the game*.

## Which feature is which (proposal)
| Group | Features |
|---|---|
| **Insight** (shows only) | Damage numbers, damage icons, boss health numbers, descriptions, combat log, mini log, cooldown seconds, Codex, run history and records, achievements, crash reports and performance, save slots, Mods |
| **Helpers** (look, no balance) | Clear view, healing orb look and reminder |
| **Gameplay** (changes the game) | Rewards (balance), Restart room (save-scumming) |
| **Online** | Daily runs (which already ignore every gameplay feature) |

## Layout (Umut's idea: features on the left, their settings on the right)
```
+--------------------------------+---------------------------------------------+
| INSIGHT                        |  Combat log                        [ ON  ]  |
|  [x] Damage numbers            |  Opens with L. Shows every hit: who, what,  |
|  [x] Combat log         <      |  how much, and why (the calculation).       |
|  [x] Codex                     |  -----------------------------------------  |
|  [x] Run history               |  Open / close key ............... L         |
|  ...                           |  Dialogue frame ................. on        |
| HELPERS                        |  Font size ...................... 100 %     |
|  [x] Clear view                |  ...                                        |
| GAMEPLAY (changes the game)    |                                             |
|  [ ] Rewards                   |  Default: on                                |
|  [x] Restart room              |                                             |
| ONLINE                         |                                             |
|  [ ] Daily runs                |                                             |
+--------------------------------+---------------------------------------------+
```
- Left: one row per feature with its on/off switch, grouped under the four headings.
- Right: the selected feature's description, then its settings. Hovering a setting shows its own help below.
- General things (language, What's new, menu sounds) form a row of their own, "General", at the top.
- Keyboard and controller: left/right moves between the lists, up/down within one.
- Reuses the current page's rows (SettingsCatalog stays the source of labels, help texts and translations). Only
  the layout and a feature table (feature → its config sections) are new.

## The split: three options
1. **One package, a choice at the first start (recommended now).** A small window the first time the mod runs: "Only
   show information" / "Information + gameplay". The Gameplay group starts off for new players. Existing players keep
   their settings, and the What's new line says where the switch is. One upload and one review per version; the
   settings page makes the split visible.
2. **Two Thunderstore packages:** "SkulInsight QoL" (insight, helpers, online) and an add-on "SkulInsight Gameplay"
   (rewards, restart room) that needs the first one.
   - Pros: the choice happens in the mod manager, and tournaments can simply say "without the add-on".
   - Cons: two reviews per update (Umut doesn't want to bother the reviewer), shared code split across two DLLs, and
     the Codex's Rewards tab and settings would need hooks for the add-on. That is a large refactor.
3. **Option 1 now, option 2 later** if players keep asking, or if tournaments need a package without gameplay
   features (the daily runs already ignore them, so it isn't urgent).

## Questions for Umut
- Option 1, 2 or 3?
- Should Restart room count as "Gameplay"? It changes nothing that closing the game and continuing doesn't, but it
  makes save-scumming easy.
- Should the first-start window also ask about daily runs (online)?
