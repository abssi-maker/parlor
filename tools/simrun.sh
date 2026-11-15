#!/bin/zsh
# Usage: simrun.sh <name> <xrop-script-file> [seconds_after_script]
# REC=<file.mp4> also records the simulator compositor (virtual + synthetic room) for the whole script.
# Play mode against Meta XR Simulator; when the show is ready, feeds the XR Operator script (head and hand poses,
# composited captures, sleeps), then prints PARLOR logs and errors and leaves Play mode.
R=/Users/mac/local/internal/meta/XR/repo; N=$1; S=$2
mkdir -p $R/Captures
export XRSIM_WINDOW=$(swift $R/tools/winid.swift simulator 2>/dev/null | head -1 | cut -d" " -f1)
unity command console clear >/dev/null 2>&1
unity command editor.playmode start --run-in-background true >/dev/null 2>&1
until unity command console get --tail 400 2>/dev/null | grep -q 'PARLOR\] ready'; do sleep 2; done
[ -n "$REC" ] && $HOME/.metavr/bin/metavr xrsim record start -o "$REC" --fps 30 2>&1 | tail -1
python3 $R/tools/xrop.py - < $S
[ -n "$REC" ] && $HOME/.metavr/bin/metavr xrsim record stop 2>&1 | tail -1
sleep ${3:-0}
unity command console get --tail 600 2>/dev/null | python3 -c "
import sys,re,json
t=sys.stdin.read()
m=re.search(r'entries: (\[.*?\])\ncursor', t, re.S)
seen=set()
if m:
    for e in json.loads(m.group(1)):
        if e['level']=='error':
            k=e['message'][:120]
            if k in seen: continue
            seen.add(k); print('ERR', e['message'][:300], '|', e.get('stackTrace','')[:500].replace('\n',' / '))
        elif 'PARLOR' in e['message']: print(e['timestampUtc'][11:19], e['message'].split('\n')[0][:160])
m=re.search(r'counts: (\{.*?\})', t); print(m.group(1) if m else '')"
unity command editor.playmode stop >/dev/null 2>&1
