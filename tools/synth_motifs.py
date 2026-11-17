#!/usr/bin/env python3
"""Procedural cues (pure Python, no samples): the Shadow's three-note motif in a menacing and a warm form, and the
falling-coin whistle that follows the coin down through the table. Usage: synth_motifs.py <Resources/Audio dir>"""
import math, struct, sys, wave, random
R = 48000
def write(path, s, peak=0.5):
    m = max(1e-6, max(abs(x) for x in s)); s = [x / m * peak for x in s]
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(R)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, x)) * 32767)) for x in s))
def bowed(f, dur, vib=5.0, depth=0.006, bright=1.0):
    """A cello-ish bowed note: decaying harmonic stack with a slow attack and vibrato."""
    n = int(R * dur); out = []; ph = 0.0
    for i in range(n):
        t = i / R
        env = min(1, t / 0.12) * (1 - max(0, (t - dur + 0.35) / 0.35))
        fr = f * (1 + depth * math.sin(2 * math.pi * vib * t))
        ph += 2 * math.pi * fr / R
        v = sum((1 / k) * (0.82 ** (k / bright)) * math.sin(k * ph) for k in range(1, 9))
        out.append(v * env)
    return out
def seq(notes, gap=0.0):
    s = []
    for f, d in notes: s += bowed(f, d) + [0.0] * int(R * gap)
    return s
def note(name):
    A = {"C":-9,"C#":-8,"D":-7,"Eb":-6,"E":-5,"F":-4,"F#":-3,"G":-2,"Ab":-1,"A":0,"Bb":1,"B":2}
    p, o = name[:-1], int(name[-1]); return 440 * 2 ** ((A[p] + 12 * (o - 4)) / 12)
D = sys.argv[1]
# menace: D3 - Bb2 - F#2 (a falling minor shape with a tritone sting at the bottom)
write(f"{D}/shadow_motif.wav", seq([(note("D3"), 0.55), (note("Bb2"), 0.55), (note("F#2"), 1.2)]), 0.55)
# warm: the same opening, resolving upwards to a major chord tone: D3 - Bb2 - D3 - F#3 softly
w = seq([(note("D3"), 0.5), (note("Bb2"), 0.5), (note("D3"), 0.5), (note("F#3"), 1.3)])
write(f"{D}/shadow_motif_warm.wav", [x * 0.8 for x in w], 0.45)
# coin fall: a slide whistle from 1500 Hz to 520 Hz over 1.9 s, with a glassy sparkle on top
n = int(R * 2.0); s = []; ph = 0.0; random.seed(3)
for i in range(n):
    t = i / R; x = min(1, t / 1.9)
    f = 1500 * (520 / 1500) ** (x ** 1.4) * (1 + 0.01 * math.sin(2 * math.pi * 6 * t))
    ph += 2 * math.pi * f / R
    env = min(1, t / 0.05) * (1 - max(0, (t - 1.75) / 0.25))
    v = 0.7 * math.sin(ph) + 0.15 * math.sin(2 * ph)
    if random.random() < 0.0009: s_t = 1.0
    s.append(v * env)
for k in range(12):   # sparkle: little decaying bell pings along the way
    st = int(R * (0.1 + k * 0.15)); fq = 2600 + 400 * math.sin(k * 1.7)
    for j in range(int(R * 0.25)):
        if st + j < n: s[st + j] += 0.18 * math.exp(-j / (R * 0.05)) * math.sin(2 * math.pi * fq * j / R)
write(f"{D}/coin_fall.wav", s, 0.5)
print("motifs written")
