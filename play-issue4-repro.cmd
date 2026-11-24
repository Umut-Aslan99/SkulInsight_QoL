@echo off
rem GitHub issue #4 by hand (COOP_LOG 71). Needs Steam running and Skul closed. Your real save is not touched
rem (a test copy with its own save). The copy leaves the castle by itself: Dark Mirror 10, chapter 3, the reported
rem seed; it gets the reported build (Grave Digger 4 with King Lear + Hamlet, Werewolf 2 with Hunting, Banshee,
rem the 9 items) and goes straight to the Chimera. Then click the window once and play with your keys.
rem  - You take hits and knockback, but are healed to full below 30 %% health.
rem  - Try what the reporter did: summons out, swap to the Werewolf, use Hunting (its skill), especially around the
rem    Chimera's phase change and near the floor pieces that break.
rem  - When the character is lost (gone, outside the map or no control), a notice says "Repro: the player is LOST",
rem    and a picture plus the last moves are written. Keep playing a bit, then close the window.
rem  - F5 twice = restart the room (the Chimera again, with the build).
cd /d "%~dp0"
dotnet build DamageInsight -c Debug -v q -nologo
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\coop\start-instances.ps1 -Count 1 -First 2 -Repro issue4 -Width 1920 -Height 1080
echo.
echo Close the Skul window when you are done, then press a key here (it puts your window settings back).
pause
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\coop\start-instances.ps1 -Stop
