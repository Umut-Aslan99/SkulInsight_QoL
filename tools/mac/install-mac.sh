#!/bin/sh
# SkulInsight QoL - macOS installer for the native Mac version of Skul (no Wine, no CrossOver needed).
# Sets up BepInEx 5 for Skul.app and installs the newest SkulInsight QoL. Guide: docs/MACOS.md
#
#   sh install-mac.sh               install or update (BepInEx + the unstripped libraries + the mod)
#   sh install-mac.sh --check       only look, change nothing; writes ~/skul-mac-report.txt for bug reports
#   sh install-mac.sh --uninstall   remove BepInEx and the mod again (your saves and the mod's records stay)
#
# Options: --game "<folder that holds Skul.app>"   when Skul is not in a Steam library this script finds
#          --mod-zip <file.zip>                    install this SkulInsight QoL zip instead of downloading it
#          --no-mod                                BepInEx only (for other mods)
#
# Afterwards set Skul's Steam launch option to the line this script prints:  "<game folder>/run_bepinex.sh" %command%

set -u

BEPINEX_URL="https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_macos_universal_5.4.23.5.zip"
BEPINEX_SHA256="01c2ae782eb016dfd6c345a18dbd2dcafffb3d9d318449d6486689f426b4a323"
# Skul's own BepInEx pack from Thunderstore: we only take its unstripped libraries (the same files Windows players use).
PACK_URL="https://thunderstore.io/package/download/BepInEx/BepInExPack_Skul/5.4.2100/"
PACK_SHA256="e9303ab6a00590ef95309927b06def03b798184b9bd41cb9c8f1319119747391"
MOD_RELEASES="https://api.github.com/repos/Umut-Aslan99/SkulInsight_QoL/releases/latest"
MOD_FOLDER="SkulInsight_QoL"
REPORT="$HOME/skul-mac-report.txt"

mode="install"
game=""
mod_zip=""
with_mod=1
while [ $# -gt 0 ]; do
    case "$1" in
        --check) mode="check" ;;
        --uninstall) mode="uninstall" ;;
        --game) game="${2:-}"; shift ;;
        --mod-zip) mod_zip="${2:-}"; shift ;;
        --no-mod) with_mod=0 ;;
        -h|--help) sed -n '2,14p' "$0"; exit 0 ;;
        *) echo "Unknown option: $1 (see: sh install-mac.sh --help)" >&2; exit 2 ;;
    esac
    shift
done

say() { printf '%s\n' "$*"; }
fail() { printf '\nERROR: %s\n' "$*" >&2; exit 1; }
have() { command -v "$1" >/dev/null 2>&1; }

steam_root="$HOME/Library/Application Support/Steam"

# Every Steam library: the default one, then the ones in libraryfolders.vdf (other drives).
steam_libraries() {
    printf '%s\n' "$steam_root"
    vdf="$steam_root/steamapps/libraryfolders.vdf"
    [ -f "$vdf" ] && sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$vdf"
}

find_game() {
    if [ -n "$game" ]; then
        printf '%s\n' "${game%/}"
        return
    fi
    steam_libraries | while IFS= read -r lib; do
        if [ -d "$lib/steamapps/common/Skul/Skul.app" ]; then
            printf '%s\n' "$lib/steamapps/common/Skul"
            break
        fi
    done
}

sha256_of() {
    if have shasum; then shasum -a 256 "$1" | cut -d' ' -f1
    elif have sha256sum; then sha256sum "$1" | cut -d' ' -f1
    else echo "unknown"; fi
}

download() { # url, file, expected sha256 (empty: don't check)
    say "  downloading $(basename "$2") ..."
    curl -fsSL --retry 3 -o "$2" "$1" || fail "could not download $1 (internet connection?)"
    if [ -n "$3" ]; then
        got="$(sha256_of "$2")"
        [ "$got" = "$3" ] || fail "$(basename "$2") is not the expected file (sha256 $got). Please report this."
    fi
}

# One exact text replacement in a file; fails loudly when the text isn't there exactly once.
replace_once() { # file, old, new
    count="$(grep -cF -- "$2" "$1")"
    [ "$count" = "1" ] || fail "run_bepinex.sh: expected one line '$2', found $count. Please report this."
    awk -v old="$2" -v new="$3" '{ if ($0 == old) print new; else print }' "$1" > "$1.new" && mv "$1.new" "$1"
    grep -qxF -- "$3" "$1" || fail "run_bepinex.sh: could not set '$3'."
}

# ---------------------------------------------------------------------------------------------------- check
check() {
    say "SkulInsight QoL - Mac check ($(date '+%Y-%m-%d %H:%M'))"
    say ""
    say "== System"
    have sw_vers && say "macOS $(sw_vers -productVersion) ($(sw_vers -buildVersion))"
    say "CPU: $(uname -m) $(sysctl -n machdep.cpu.brand_string 2>/dev/null)"
    if [ "$(uname -m)" = "arm64" ]; then
        if arch -x86_64 /usr/bin/true 2>/dev/null; then say "Rosetta 2: installed"; else say "Rosetta 2: NOT installed (Skul needs it: softwareupdate --install-rosetta)"; fi
    fi
    say ""
    say "== Steam libraries"
    steam_libraries | while IFS= read -r lib; do
        if [ -d "$lib/steamapps/common/Skul" ]; then say "$lib  (has Skul)"; else say "$lib"; fi
    done
    g="$(find_game)"
    say ""
    say "== Game folder: ${g:-NOT FOUND}"
    [ -n "$g" ] || return 0
    ls -la "$g" 2>/dev/null
    app="$g/Skul.app/Contents"
    [ -f "$g/Skul.exe" ] && say "Skul.exe found: this is the WINDOWS version (CrossOver/Wine) - see the guide."
    if [ -f "$app/MacOS/Skul" ]; then
        say "Skul: $(defaults read "$app/Info" CFBundleShortVersionString 2>/dev/null) $(file -b "$app/MacOS/Skul" 2>/dev/null)"
    fi
    say ""
    say "== BepInEx"
    [ -f "$g/.doorstop_version" ] && say "Doorstop $(cat "$g/.doorstop_version")"
    [ -f "$g/libdoorstop.dylib" ] && say "libdoorstop.dylib: yes" || say "libdoorstop.dylib: MISSING"
    if [ -f "$g/run_bepinex.sh" ]; then
        [ -x "$g/run_bepinex.sh" ] && say "run_bepinex.sh: executable" || say "run_bepinex.sh: NOT executable (chmod +x)"
        grep -E '^(executable_name|dll_search_path_override|doorstop_name)=' "$g/run_bepinex.sh"
    else
        say "run_bepinex.sh: MISSING"
    fi
    [ -d "$g/BepInEx/core" ] && say "BepInEx/core: $(ls "$g/BepInEx/core" | wc -l | tr -d ' ') files"
    [ -d "$g/BepInEx/unstripped_corlib" ] && say "unstripped_corlib: $(ls "$g/BepInEx/unstripped_corlib" | wc -l | tr -d ' ') files"
    [ -f "$g/BepInEx/plugins/$MOD_FOLDER/DamageInsight.dll" ] && say "SkulInsight QoL: installed" || say "SkulInsight QoL: not installed"
    have xattr && xattr -lr "$g/libdoorstop.dylib" "$g/BepInEx" 2>/dev/null | grep -q quarantine && say "QUARANTINE flags found (the installer removes them)"
    say ""
    say "== Steam launch option (Skul)"
    found=0
    for cfg in "$steam_root"/userdata/*/config/localconfig.vdf; do
        [ -f "$cfg" ] || continue
        line="$(grep -A 20 '"1147560"' "$cfg" | grep -m 1 '"LaunchOptions"')"
        [ -n "$line" ] && { say "$line"; found=1; }
    done
    [ "$found" = "1" ] || say "none found (set it: see the guide)"
    say ""
    say "== BepInEx/LogOutput.log"
    log="$g/BepInEx/LogOutput.log"
    if [ -f "$log" ]; then
        say "written $(date -r "$log" '+%Y-%m-%d %H:%M' 2>/dev/null)"
        head -n 12 "$log"
        say "... errors and warnings:"
        grep -E 'Error|Exception|Warning' "$log" | head -n 40
    else
        say "none yet (BepInEx never started)"
    fi
}

# ------------------------------------------------------------------------------------------------ uninstall
uninstall() {
    g="$(find_game)"
    [ -n "$g" ] || fail "Skul not found. Use: sh install-mac.sh --uninstall --game \"<folder with Skul.app>\""
    rm -f "$g/run_bepinex.sh" "$g/libdoorstop.dylib" "$g/.doorstop_version"
    rm -rf "$g/BepInEx/core" "$g/BepInEx/unstripped_corlib" "$g/BepInEx/plugins/$MOD_FOLDER"
    say "Removed BepInEx's loader and SkulInsight QoL from $g"
    say "Kept: BepInEx/config and BepInEx/DamageInsight (the mod's settings and records), your game saves."
    say "Last step: remove Skul's launch option in Steam (Properties > General > Launch options)."
}

# -------------------------------------------------------------------------------------------------- install
install() {
    have curl || fail "curl is missing"
    have unzip || fail "unzip is missing"
    g="$(find_game)"
    if [ -z "$g" ]; then
        fail "Skul.app not found in your Steam libraries. Install Skul in Steam first, or tell me where it is:
  sh install-mac.sh --game \"/path/to/the folder that holds Skul.app\""
    fi
    managed="$g/Skul.app/Contents/Resources/Data/Managed"
    if [ ! -f "$managed/Assembly-CSharp.dll" ]; then
        [ -f "$g/Skul.exe" ] && fail "$g holds the Windows version of Skul. This script is for the Mac version; see the guide."
        fail "$g/Skul.app doesn't look like Skul (no Assembly-CSharp.dll)."
    fi
    if have pgrep && pgrep -x Skul >/dev/null 2>&1; then
        fail "Skul is running. Quit the game first, then run this again."
    fi
    say "Skul found: $g"

    tmp="$(mktemp -d "${TMPDIR:-/tmp}/skulinsight.XXXXXX")" || fail "no temp folder"
    trap 'rm -rf "$tmp"' EXIT

    say "1/4 BepInEx 5.4.23.5 for macOS"
    download "$BEPINEX_URL" "$tmp/bepinex.zip" "$BEPINEX_SHA256"
    unzip -q -o "$tmp/bepinex.zip" -d "$tmp/bepinex" || fail "could not unpack BepInEx"
    mkdir -p "$g/BepInEx/core" "$g/BepInEx/plugins" "$g/BepInEx/config"
    cp -f "$tmp/bepinex/libdoorstop.dylib" "$tmp/bepinex/run_bepinex.sh" "$tmp/bepinex/.doorstop_version" "$g/" || fail "could not copy BepInEx"
    cp -Rf "$tmp/bepinex/BepInEx/core/." "$g/BepInEx/core/" || fail "could not copy BepInEx/core"

    say "2/4 unstripped libraries (from BepInExPack Skul)"
    download "$PACK_URL" "$tmp/pack.zip" "$PACK_SHA256"
    unzip -q -o "$tmp/pack.zip" 'BepInExPack/2020.3.34/*' -d "$tmp/pack" || fail "could not unpack BepInExPack Skul"
    rm -rf "$g/BepInEx/unstripped_corlib"
    mkdir -p "$g/BepInEx/unstripped_corlib"
    cp -f "$tmp/pack/BepInExPack/2020.3.34/"*.dll "$g/BepInEx/unstripped_corlib/" || fail "could not copy the libraries"
    # The pack's copy comes from the Windows version and lacks the Mac's Game Center code: keep the game's own.
    rm -f "$g/BepInEx/unstripped_corlib/UnityEngine.GameCenterModule.dll"

    say "3/4 run_bepinex.sh for Skul"
    run="$g/run_bepinex.sh"
    replace_once "$run" 'executable_name=""' 'executable_name="Skul.app"'
    # Skul's Managed folder is stripped: Mono must find the full libraries first (relative to the game folder).
    replace_once "$run" 'dll_search_path_override=""' 'dll_search_path_override="BepInEx/unstripped_corlib"'
    replace_once "$run" 'export DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE="$dll_search_path_override"' \
        'export DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE="$(abs_path "$dll_search_path_override")"'
    # Apple Silicon: the script starts the game through arch, which drops DYLD_LIBRARY_PATH; a full path for
    # Doorstop works without it (fixed the same way in Doorstop 4.6).
    replace_once "$run" 'doorstop_name="libdoorstop.${lib_extension}"' 'doorstop_name="${doorstop_directory}libdoorstop.${lib_extension}"'
    chmod +x "$run"

    if [ "$with_mod" = "1" ]; then
        say "4/4 SkulInsight QoL"
        if [ -n "$mod_zip" ]; then
            cp -f "$mod_zip" "$tmp/mod.zip" || fail "could not read $mod_zip"
        else
            url="$(curl -fsSL "$MOD_RELEASES" | grep -o '"browser_download_url": *"[^"]*\.zip"' | head -n 1 | sed 's/.*"\(https[^"]*\)"$/\1/')"
            [ -n "$url" ] || fail "could not find the newest SkulInsight QoL release on GitHub"
            download "$url" "$tmp/mod.zip" ""
        fi
        unzip -q -o "$tmp/mod.zip" 'plugins/DamageInsight.dll' -d "$tmp/mod" || fail "the mod zip has no plugins/DamageInsight.dll"
        mkdir -p "$g/BepInEx/plugins/$MOD_FOLDER"
        cp -f "$tmp/mod/plugins/DamageInsight.dll" "$g/BepInEx/plugins/$MOD_FOLDER/" || fail "could not copy the mod"
    else
        say "4/4 SkulInsight QoL skipped (--no-mod)"
    fi

    # Files from the internet can carry macOS's quarantine flag, which stops the loader.
    have xattr && xattr -dr com.apple.quarantine "$g/libdoorstop.dylib" "$g/run_bepinex.sh" "$g/BepInEx" 2>/dev/null

    option="\"$run\" %command%"
    say ""
    say "Done. Last step, once: in Steam right-click Skul > Properties > General > Launch options, paste:"
    say ""
    say "    $option"
    say ""
    if have pbcopy; then printf '%s' "$option" | pbcopy && say "(It is already in your clipboard: just press Cmd+V there.)"; fi
    say "Then start Skul from Steam. Damage numbers on hits and a SkulInsight QoL entry in the pause menu (Esc)"
    say "mean it works. If not: sh install-mac.sh --check and send ~/skul-mac-report.txt"
    say "To play without mods, empty the launch option again."
}

case "$mode" in
    check) check 2>&1 | tee "$REPORT"; say ""; say "Saved as $REPORT" ;;
    uninstall) uninstall ;;
    *) install ;;
esac
