@echo off
rem Revive test by hand (Umut 2026-11-14). Needs Steam running and Skul closed.
rem  - LEFT window  = the host, YOURS: click it once, then play with your keys. Your player takes no damage.
rem  - RIGHT window = the client, a bot. In every fight it is killed after 8 seconds and lies down.
rem  - Walk to its head and HOLD your interact key (default F) for 3 seconds: it stands up again with 30 % health.
rem  - Leave it lying and clear the room instead: it comes back by itself with 10 % health.
rem  - Doors: press one (your interact key) when the room is cleared and you took its reward: that is your vote.
rem    The bot votes too, a few seconds after the room is cleared. Same door = you go at once; another door =
rem    a coin flip; no press = the bot's door when the countdown runs out.
rem  - Nothing closes by itself. Close one of the two windows to stop; the other one closes and the report is written.
cd /d "%~dp0"
dotnet build DamageInsight -c Debug -v q -nologo
python tools\coop\coop-test.py n1 --full --down 8 --no-host-bot --own-cam --no-retry
echo.
echo The run is over. The report is in C:\Users\Umut\SkulModding\CoopRuns (newest folder).
pause
