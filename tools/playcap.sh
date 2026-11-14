#!/bin/zsh
# Usage: playcap.sh <prefix> [max_seconds] [interval]
# Enters Play mode, saves a Game view frame every <interval> s into Captures/<prefix>_NNN.png until the show
# reaches Encore (or max_seconds), then prints the PARLOR log lines and any errors, and leaves Play mode.
R=/Users/mac/local/internal/meta/XR/repo; P=$1; MAX=${2:-150}; INT=${3:-3}
mkdir -p $R/Captures; rm -f $R/Captures/${P}_*.png
unity command console clear >/dev/null 2>&1
unity command editor.playmode start --run-in-background true >/dev/null 2>&1
i=0; t0=$(date +%s)
while [ $(( $(date +%s) - t0 )) -lt $MAX ]; do
  i=$((i+1))
  unity command capture game_view --width 1280 --height 720 --output-path $R/Captures/${P}_$(printf %03d $i).png >/dev/null 2>&1
  if unity command console get --tail 200 2>/dev/null | grep -q 'beat Encore'; then sleep 6; i=$((i+1)); unity command capture game_view --width 1280 --height 720 --output-path $R/Captures/${P}_$(printf %03d $i).png >/dev/null 2>&1; break; fi
  sleep $INT
done
unity command console get --tail 400 2>/dev/null | python3 -c "
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
echo "frames: $i"
