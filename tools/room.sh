#!/bin/zsh
# Usage: room.sh <room name>. Switches the Meta XR Simulator's synthetic room safely: Play mode off, set the room,
# restart the simulator (it starts its environment server with the chosen room), wait until it answers.
M=~/.metavr/bin/metavr; U=/Users/mac/.unity/bin/unity
$U command editor.playmode stop >/dev/null 2>&1; sleep 2
$M xrsim env set --id "$1" 2>&1 | tail -1
$M xrsim app quit >/dev/null 2>&1; sleep 4
$M xrsim app launch 2>&1 | tail -1
for i in $(seq 1 30); do $M xrsim env list 2>/dev/null | grep -q "true" && break; sleep 2; done
sleep 6
$M xrsim env list 2>&1 | awk '$NF=="true"{print "current room:", $0}'
