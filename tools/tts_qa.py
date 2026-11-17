#!/usr/bin/env python3
"""Regenerate voice lines until each take is clean: the transcript must match the script (no read-out direction, no
extra words) and the take must not be padded with long pauses. Usage: tts_qa.py <audio dir> <clip names...>
Uses tts_lines.say() for generation (same model and voice) and Gemini for the transcript."""
import re, sys, wave, pathlib
sys.path.insert(0, str(pathlib.Path(__file__).parent))
import tts_lines
from google.genai import types
def words(s): return re.findall(r"[a-z']+", s.lower())
def transcribe(path):
    r = tts_lines.client.models.generate_content(model="gemini-2.5-flash", contents=[types.Part.from_bytes(data=path.read_bytes(), mime_type="audio/wav"), "Transcribe exactly what is spoken, verbatim, one line."])
    return r.text.strip()
out = pathlib.Path(sys.argv[1]); want = dict(tts_lines.lines())
for name in sys.argv[2:]:
    text = want[name]; ok = False
    for attempt in range(5):
        p = out / f"{name}.wav"
        p.unlink(missing_ok=True)                      # say() keeps an existing file: force a fresh take
        print(tts_lines.say(name, text, str(out)), flush=True)
        with wave.open(str(p)) as w: dur = w.getnframes() / w.getframerate()
        heard = transcribe(p)
        a, b = words(text), words(heard)
        overlap = len(set(a) & set(b)) / max(1, len(set(a)))
        limit = 1.5 + len(text) * 0.085
        ok = overlap >= 0.85 and len(b) <= len(a) + 2 and dur <= limit
        print(f"  {name} try {attempt+1}: {dur:.1f}s (limit {limit:.1f}) overlap {overlap:.2f} words {len(b)}/{len(a)} -> {'OK' if ok else 'retry'} | {heard[:90]}", flush=True)
        if ok: break
    if not ok: print(f"  {name} STILL BAD after 5 tries", flush=True)
