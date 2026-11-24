using System.Collections.Generic;
using System.Linq;
using DamageInsight.Lang;

namespace DamageInsight.UI;

/// <summary>
/// The changelog in the game (0.16.1; Tythos 2026-11-01: "a lot of Chinese players aren't really familiar with GitHub
/// ... It would be awesome if you could add a simple in-game changelog inside the mod settings"; Umut: "should include
/// ALL changelogs from previous versions ... The Current changelogs ... should be boxed in and highlighted"). Every
/// version's highlights in the player's language, newest first (CHANGELOG.md has the details, in English). The "What's
/// new" window shows it after every update with what is new for the player in a box; settings: General, Changelog.
/// A new version needs its entry at the top (a test compares it with the mod's version).
/// </summary>
public static class Changelog
{
    /// <summary>The versions and their highlights (English keys, translated here), newest first.</summary>
    public static List<(string version, string[] lines)> Versions() => new()
    {
        ("0.17.3", new[]
        {
            Loc.T("Fixed: in co-op, your own inscriptions sometimes never took effect (the game refused to load them a second time), so their bonuses were missing for the whole run."),
            Loc.T("Another co-op mod (like OnlineCoopBeta) or a co-op part that didn't load: a warning at the start and in the Co-op window, and hosting and joining stay off until it is removed. With it, enemies went only for one player."),
            Loc.T("Co-op: enemy attacks that come from shots now go for the right player."),
        }),
        ("0.17.2", new[]
        {
            Loc.T("3-4 players with Steam friends (experimental): the host sets how many in the settings (Co-op, Players). Matchmaking still pairs two players."),
            Loc.T("When a co-op connection breaks, the log now says whether Steam could reach its relay network: this helps with connections where Steam finds no route to the other game."),
        }),
        ("0.17.1", new[]
        {
            Loc.T("Co-op fixes from your runs: the adventurer arena camera locks on the arena again while you fight in it; a teammate no longer walls you in (both players need 0.17.1); enemies without a target go for the nearest player; the Pope's fanatics no longer stay after he dies."),
            Loc.T("Report a bug or idea from the game: a form in the Co-op window, the settings and the Codex sends it to the developer (title and text become a public GitHub issue, logs stay private). After a co-op run you are asked once whether your run logs may be sent."),
            Loc.T("Bosses stop attacking a downed player; the twin sisters' crossing attack and the dark First Hero's wall attack show their warning lines in the right place for the joining player; your teammate's summons' shots no longer get your green outline."),
            Loc.T("Enemy health in the Co-op window: everyone sees the host's setting, the others can ask for more or less and say OK; the slider goes from 1.5x to 4.5x. A crown marks the host."),
            Loc.T("Matchmaking tells you why nobody matches (other mods or another version) and shows who is searching now and the busiest hour. An \"Update available\" window shows when a newer version is out."),
        }),
        ("0.17.0", new[]
        {
            Loc.T("Online co-op (open beta): play a run together with a Steam friend, or find a partner through matchmaking. Off by default: Esc, SkulInsight QoL, Online co-op (open beta), then restart the game. Two players for now."),
            Loc.T("One run in the host's world: enemies, bosses, room clears and doors (by vote) are shared, gold drops for everyone; room rewards, shops and gear are your own. Revive a downed teammate at their ring; enemies have more health in co-op (the host sets 1.5x to 3x)."),
            Loc.T("Your teammate is slightly see-through with an outline in their colour, their effects are drawn fainter, and their screen shakes and damage numbers stay on their screen. Stuck? F6 takes you to your teammate."),
            Loc.T("Enemy projectiles that can hit you get a thin red outline, also when you play alone."),
            Loc.T("This is a beta: the Co-op window's \"Report a bug or idea\" packs your logs (Steam ids hidden) and opens the report page."),
        }),
        ("0.16.2", new[]
        {
            Loc.T("The name for the daily board starts with your Steam name when you haven't set one, so runs no longer go on the board as \"Skul\" by accident. You can change it before sending."),
        }),
        ("0.16.1", new[]
        {
            Loc.T("Fixed: Restart room (F5) brings the run back with all its gear (0.16.0 lost it until the game was started again)."),
            Loc.T("Fixed: a new name for the daily board can be typed (the box kept bringing the old one back)."),
            Loc.T("Other players' runs show in your language: their skills, damage table, enemies and stages, also runs sent before."),
            Loc.T("Daily points: gold and bones left are worth much more (1 point per 5 gold, 25 per bone). From November 2 the day's achievements are choices in the run (a skull and a quintessence of the day to win holding, one of the game's achievements), none that the points count already."),
            Loc.T("The changelog in the game: it opens once after every update with what is new for you in a box, and any time from Esc, SkulInsight QoL, General, Changelog."),
        }),
        ("0.16.0", new[]
        {
            Loc.T("Restart room: press F5 twice to go back to the moment you entered the room, as when you close the game and continue. In a daily run it ends the run."),
            Loc.T("Daily points, new from November 1: the boss fights instead of the game's double-counted kills, speed counts more, flawless rooms and boss fights (no health lost) count, and four achievements of the day give bonus points. The day's board is the run alone; the streak counts on the month board."),
            Loc.T("Yesterday's daily winners give their names to the adventurers you meet."),
            Loc.T("The settings page is new: the features on the left in groups (what changes the game, what only shows, helpers, online, general), each with its switch; hover one to see its settings on the right."),
            Loc.T("Fixed: hovering with the mouse selects items, skulls and dark abilities again; the game no longer stutters when a strong build kills a lot."),
            Loc.T("The crash reports now also note frames per second and what slows the game down (setting Show FPS for a line on screen)."),
        }),
        ("0.15.0", new[]
        {
            Loc.T("Rewards: every achievement of your save slot gives a small lasting bonus that fits it, like more damage to a boss you beat (more without getting hit), attack, speed or gold (Codex, You, Rewards)."),
            Loc.T("Coins: break a legendary item or skull, or beat an adventurer a few times, and its coin summons that gear in a later run. A run ended with Omen items gives Omen coins."),
            Loc.T("Kill quests: every normal enemy has an endless quest that pays dark quartz and gold."),
            Loc.T("Inscription activations: complete an inscription in a run, and in a later run one you have counts one more, like the Dark Mirror's banners."),
            Loc.T("Rewards can be turned off at the top of the settings page; daily runs never use them."),
            Loc.T("Daily runs: every day Normal, the Dark Mirror at a random level and Dark Mirror 10, each with and without the DLC and each with its own board. Everyone gets the same gifts in the castle and the same blessing."),
        }),
        ("0.14.1", new[]
        {
            Loc.T("Thunderstore's rules: SkulInsight no longer updates itself (your mod manager does), and the Mods window lists mods and links their pages."),
        }),
        ("0.14.0", new[]
        {
            Loc.T("Achievements (Codex, You, Achievements): all of Skul's achievements as this save slot has them, with tips, your progress and veils over the story's secrets. Each slot earns its own, also the ones Steam already has."),
            Loc.T("A small notice slides down from the top of the screen when your slot earns an achievement."),
            Loc.T("Clear view: your own effects get fainter over the run and when the screen gets busy, so enemy attacks stay easy to see (settings: Clear view)."),
            Loc.T("Mods (pause menu): your mods and Thunderstore's Skul mods side by side, with search, details and their Thunderstore pages."),
            Loc.T("Healing orbs are easier to see (as a Portable Potion or a green plus), a short text says when some are left, and your runs count the ones you missed."),
            Loc.T("Daily runs: SkulInsight's own achievements for thumbs up received and days in a row. Record bonuses for most damage, gold and kills now need a boss defeated."),
            Loc.T("macOS: the mod now runs on Skul's Mac version, set up by one Terminal script."),
        }),
        ("0.13.1", new[]
        {
            Loc.T("The Thunderstore package is packed correctly again (0.13.0's was broken)."),
        }),
        ("0.13.0", new[]
        {
            Loc.T("Run history (Codex, You, Runs): every run kept like a match history: how it ended, the build, each piece of gear's damage and a timeline, all linked to the Codex. Star a run to keep it whole; End screen shows the game's result panel again."),
            Loc.T("Records (Codex, You, Records): your bests over all runs, per skull, against bosses, and the game's own counters."),
            Loc.T("Daily run (Codex, Daily; online, off until you turn it on): the same run for everyone each day, one try, in its own save slot with everything unlocked. Your own game isn't touched."),
            Loc.T("Daily and monthly boards: open anyone's run like your run history, see how its points came about (with bonuses for the day's records and your streak) and give it a thumbs up."),
        }),
        ("0.12.2", new[]
        {
            Loc.T("Crash reports: a report of every game session that says whose code an error was in (settings: Crash reports folder). The game's skill boxes open beside the mod's box."),
        }),
        ("0.12.1", new[]
        {
            Loc.T("The What's new window closes with a click; the mod's buttons light up under the mouse and play the game's menu sounds."),
        }),
        ("0.12.0", new[]
        {
            Loc.T("The Codex (K) is your reference book: You, Bestiary and Arsenal, boxed pages, every number with its formula. Hold Shift on an Arsenal page to see the numbers with your run's stats."),
            Loc.T("Skill boxes: win a run with a skill (beat a boss of chapter 5 or later while you have it) to reveal it on the skull's page."),
            Loc.T("Descriptions stay short: hold Shift (or the game's details key) to see the mod's numbers."),
            Loc.T("Combat log (L), View: Damage table: your damage per skull, item, inscription and more."),
            Loc.T("Save slots: keep up to 5 games side by side (Esc, SkulInsight QoL, Save slots)."),
        }),
        ("0.11.0", new[]
        {
            Loc.T("Codex: cleaner boss films, a short note on every boss move (what it does and how to deal with it), King Alexander's heart; damage numbers for about 50 more skill parts."),
        }),
        ("0.10.2", new[]
        {
            Loc.T("Cooldown seconds on the skill, swap and quintessence icons, with your cooldown speed included."),
        }),
        ("0.10.1", new[]
        {
            Loc.T("The mod speaks all the game's languages; its settings page is in the game (Esc, SkulInsight QoL); boss moves show when the boss uses them."),
        }),
        ("0.10.0", new[]
        {
            Loc.T("The Codex (K): every enemy, boss, adventurer, skull, item, quintessence and inscription you have met, with your stats, pictures that unlock as you play, boss move lists and fight films."),
        }),
        ("0.9.3", new[]
        {
            Loc.T("Fixed: a boss could stay invulnerable after its intro. Every part of the mod is guarded, so an error in it never stops the game."),
        }),
        ("0.9.2", new[]
        {
            Loc.T("Fixed: an attack could fail when the combat log looked up the item that dealt it."),
        }),
        ("0.9.1", new[]
        {
            Loc.T("Fixed: special characters on the Thunderstore page."),
        }),
        ("0.9.0", new[]
        {
            Loc.T("The first public version: damage numbers with their source, boss health numbers, real damage in every description, a preview of gear before you pick it up, the combat log (L) and cooldown numbers."),
        }),
    };

    /// <summary>"0.16.1" later than "0.16.0"? (an empty or broken version is older than any).</summary>
    public static bool Newer(string version, string than)
    {
        int[] a = Parts(version), b = Parts(than);
        for (int i = 0; i < 3; i++)
            if (a[i] != b[i])
                return a[i] > b[i];
        return false;
    }

    private static int[] Parts(string version)
    {
        var parts = (version ?? "").Split('.');
        var numbers = new int[3];
        for (int i = 0; i < 3 && i < parts.Length; i++)
            int.TryParse(parts[i], out numbers[i]);
        return numbers;
    }

    /// <summary>
    /// The versions in the box: what is new since <paramref name="lastSeen"/> (the version whose window was shown
    /// last), at least the newest one; the rest below. Plain logic, tested.
    /// </summary>
    public static (List<(string version, string[] lines)> boxed, List<(string version, string[] lines)> earlier) Split(string lastSeen)
    {
        var all = Versions();
        if (string.IsNullOrEmpty(lastSeen))
            lastSeen = all[0].version; // nothing seen before: the newest version alone in the box
        var boxed = all.Where((v, i) => i == 0 || Newer(v.version, lastSeen)).ToList();
        return (boxed, all.Skip(boxed.Count).ToList());
    }

    /// <summary>Versions as text: each version's number, then its lines.</summary>
    public static string Text(IEnumerable<(string version, string[] lines)> versions)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (version, lines) in versions)
        {
            sb.Append($"<size=120%><color=#F2D78B>{version}</color></size>\n");
            foreach (string line in lines)
                sb.Append("»  ").Append(line).Append("\n<size=40%>\n</size>");
            sb.Append("<size=60%>\n</size>");
        }
        return sb.ToString().TrimEnd('\n');
    }
}
