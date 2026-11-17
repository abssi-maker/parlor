#!/usr/bin/env python3
"""Loudness envelopes of the magician's recorded lines, for lip-sync.

The voice clips ship "Compressed In Memory", so the app cannot read their samples at runtime. This writes one line per
vo_*.wav clip to PARLOR/Assets/Parlor/Resources/Audio/vo_envelopes.txt:

    <clip name> <frames per second> <digits>

one digit 0-9 per frame (mouth closed .. wide open). AudioDirector.VoiceLevel reads it. Re-run after tts_lines.py.
Usage: python3 voice_envelopes.py"""
import array, math, pathlib, wave

AUDIO = pathlib.Path(__file__).resolve().parent.parent / "PARLOR/Assets/Parlor/Resources/Audio"
FPS = 30


def envelope(path):
    with wave.open(str(path), "rb") as w:
        ch, width, rate, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        if width != 2:
            raise SystemExit(f"{path.name}: expected 16-bit PCM")
        a = array.array("h", w.readframes(n))
    mono = a[::ch]
    hop = max(1, rate // FPS)
    rms = []
    for i in range(0, len(mono), hop):
        seg = mono[i:i + hop]
        rms.append(math.sqrt(sum(x * x for x in seg) / max(1, len(seg))) if seg else 0.0)
    loud = sorted(rms)[int(len(rms) * 0.95)] if rms else 1.0
    loud = max(loud, 1.0)
    gate = loud * 0.08                                           # breaths and room tone keep the mouth shut
    digits = []
    for r in rms:
        v = 0.0 if r < gate else min(1.0, (r - gate) / (loud - gate))
        digits.append(str(min(9, int(round(math.sqrt(v) * 9)))))
    return "".join(digits)


def main():
    out = []
    for p in sorted(AUDIO.glob("vo_*.wav")):
        out.append(f"{p.stem} {FPS} {envelope(p)}")
    (AUDIO / "vo_envelopes.txt").write_text("\n".join(out) + "\n")
    print(f"{len(out)} envelopes → {AUDIO / 'vo_envelopes.txt'}")


if __name__ == "__main__":
    main()
