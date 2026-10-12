#!/usr/bin/env bash
# SkulInsight QoL - Linux check. READ-ONLY: changes nothing, only collects facts about
# Steam, Skul and r2modman/Gale so we can see why modded Skul does not start.
# Run:   bash skul-linux-check.sh
# Then attach the file ~/skul-linux-report.txt to your bug report (guide: docs/LINUX.md).

APPID=1147560
OUT="$HOME/skul-linux-report.txt"
exec > >(tee "$OUT") 2>&1

h() { printf '\n===== %s =====\n' "$1"; }
mask() { sed -E 's/[0-9]{10,}/<id>/g'; }   # hide Steam account ids

h "System"
. /etc/os-release 2>/dev/null && echo "$PRETTY_NAME"
uname -r
echo "session: ${XDG_SESSION_TYPE:-?}  desktop: ${XDG_CURRENT_DESKTOP:-?}"
command -v nvidia-smi >/dev/null && nvidia-smi --query-gpu=name,driver_version --format=csv,noheader
echo "-- packages:"
dpkg -l 2>/dev/null | awk '/^ii/ && $2 ~ /steam|r2modman|gale/ {print "deb:", $2, $3}'
command -v flatpak >/dev/null && flatpak list --app --columns=application,version 2>/dev/null | grep -iE 'steam|r2modman|gale' | sed 's/^/flatpak: /'
command -v snap >/dev/null && snap list 2>/dev/null | grep -iE 'steam|r2modman' | sed 's/^/snap: /'
find "$HOME" -maxdepth 3 \( -iname '*r2modman*.AppImage' -o -iname '*gale*.AppImage' \) 2>/dev/null | sed 's/^/appimage: /'

h "Steam installs"
declare -A seen
roots=()
for r in "$HOME/.steam/steam" "$HOME/.steam/debian-installation" "$HOME/.local/share/Steam" \
         "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam" "$HOME/snap/steam/common/.local/share/Steam"; do
    [ -d "$r/steamapps" ] || continue
    rp=$(realpath "$r")
    [ -n "${seen[$rp]}" ] && continue
    seen[$rp]=1; roots+=("$rp"); echo "steam root: $r -> $rp"
done
[ ${#roots[@]} -eq 0 ] && echo "NO STEAM INSTALL FOUND"

libs=()
declare -A seenlib
for r in "${roots[@]}"; do
    libs+=("$r")
    while read -r p; do libs+=("$p"); done < <(sed -nE 's/^[[:space:]]*"path"[[:space:]]+"([^"]+)".*/\1/p' "$r/steamapps/libraryfolders.vdf" 2>/dev/null)
done

for r in "${roots[@]}"; do
    h "Steam settings for Skul ($r)"
    echo "-- compatibility tool (config.vdf CompatToolMapping):"
    ct=$(awk -v id="\"$APPID\"" '/"CompatToolMapping"/ {c=1} c && $1==id && NF==1 {f=1} f {print} f && /\}/ {exit}' "$r/config/config.vdf" 2>/dev/null)
    echo "${ct:-<none: Steam default, i.e. the native Linux version>}"
    echo "-- launch options (localconfig.vdf):"
    for lc in "$r"/userdata/*/config/localconfig.vdf; do
        [ -f "$lc" ] || continue
        lo=$(awk -v id="\"$APPID\"" '$1==id && NF==1 {f=1; d=0; next}
            f && /\{/ {d++}  f && /LaunchOptions/ {print}  f && /\}/ {d--; if (d<=0) f=0}' "$lc")
        echo "${lo:-<none>}" | mask
    done
done

for L in "${libs[@]}"; do
    L=$(realpath "$L" 2>/dev/null) || continue
    [ -n "${seenlib[$L]}" ] && continue; seenlib[$L]=1
    h "Library $L"
    findmnt -n -o SOURCE,FSTYPE,OPTIONS -T "$L" 2>/dev/null | sed 's/^/mount: /'
    M="$L/steamapps/appmanifest_$APPID.acf"
    if [ -f "$M" ]; then
        echo "-- Skul manifest:"
        grep -vE 'LastOwner|SizeOnDisk|BytesTo|BytesDownloaded|BytesStaged' "$M" | mask
    else
        echo "no Skul manifest here"
    fi
    C="$L/steamapps/compatdata/$APPID"
    if [ -e "$C" ]; then
        echo "-- compatdata: $(ls -ld "$C")"
        ls -la "$C" 2>/dev/null
        [ -f "$C/version" ] && echo "proton version file: $(cat "$C/version")"
        [ -f "$C/pfx/user.reg" ] && echo "user.reg: PRESENT" || echo "user.reg: MISSING"
        awk '/^\[Software\\+Wine\\+DllOverrides\]/ {f=1} f && /^$/ {exit} f' "$C/pfx/user.reg" 2>/dev/null \
            | grep -iE 'DllOverrides|winhttp|"version"'
    else
        echo "-- compatdata/$APPID: not here"
    fi
    G="$L/steamapps/common/Skul"
    if [ -d "$G" ]; then
        echo "-- game folder $G:"
        ls -la "$G"
        echo "exe files: $(ls "$G" | grep -ic '\.exe$')   linux binary: $(ls "$G" | grep -c '\.x86_64$')   .forceproton: $([ -e "$G/.forceproton" ] && echo yes || echo no)"
        [ -f "$G/doorstop_config.ini" ] && { echo "-- doorstop_config.ini:"; grep -vE '^\s*(#|;|$)' "$G/doorstop_config.ini"; }
        [ -f "$G/BepInEx/LogOutput.log" ] && { echo "-- game BepInEx/LogOutput.log (tail):"; tail -n 30 "$G/BepInEx/LogOutput.log"; }
    fi
done

h "Unity Player.log"
for pl in "$HOME/.config/unity3d/Southpaw Games/Skul/Player.log" \
          "${libs[@]/%//steamapps/compatdata/$APPID/pfx/drive_c/users/steamuser/AppData/LocalLow/Southpaw Games/Skul/Player.log}"; do
    [ -f "$pl" ] && { echo "-- $pl"; tail -n 25 "$pl"; }
done

h "r2modman / Gale"
for d in "$HOME/.config/r2modmanPlus-local" "$HOME/.config/r2modman" \
         "$HOME"/.var/app/*/config/r2modmanPlus-local "$HOME/.local/share/com.kesomannen.gale" \
         "$HOME"/.var/app/*/data/com.kesomannen.gale; do
    [ -d "$d" ] || continue
    echo "-- $d"
    ls "$d"
    [ -f "$d/log.txt" ] && { echo "-- r2modman log.txt (tail):"; tail -n 40 "$d/log.txt"; }
    find "$d" -maxdepth 2 -type d -iname '*skul*' 2>/dev/null | while read -r s; do
        echo "-- Skul folder: $s"; ls "$s"; ls "$s/profiles" 2>/dev/null | sed 's/^/profile: /'
        find "$s" -maxdepth 5 -name LogOutput.log 2>/dev/null | while read -r lg; do
            echo "-- $lg (tail):"; tail -n 30 "$lg"
        done
        find "$s" -maxdepth 4 \( -name winhttp.dll -o -name '*.sh' -o -name doorstop_config.ini \) 2>/dev/null
    done
done

h "Done"
echo "Report saved to $OUT - please attach it to your bug report."
