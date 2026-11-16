#!/usr/bin/env python3
"""Offline mix pass: two-pass EBU R128 loudness normalisation of the shipped clips (nobody can listen on a headset
here, so levels are set by measurement). Voice lines to -16 LUFS; effects sit under the voice. Clips shorter than
0.4 s have no integrated loudness and are only peak-limited. Usage: normalize_audio.py <Resources/Audio dir>"""
import json, os, re, subprocess, sys, tempfile
D = sys.argv[1]
TARGET = {"vo_": -16.0, "voc_": -17.0, "bird": -20.0, "whoosh": -20.0, "shimmer": -19.0, "fanfare": -17.0,
          "coin_chime_low": -15.0, "coin_chime": -17.0, "applause": -19.0, "trap_creak": -20.0, "curtain": -21.0,
          "wand_tap": -18.0, "coin_land": -20.0, "music_loop": None}
def target(name):
    for k in sorted(TARGET, key=len, reverse=True):
        if name.startswith(k): return TARGET[k]
    return None
def measure(f):
    t = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", f, "-af", "loudnorm=I=-16:TP=-1.5:print_format=json", "-f", "null", "-"],
                       capture_output=True, text=True).stderr
    return json.loads(re.findall(r"\{[^{}]*\}", t)[-1])
for name in sorted(os.listdir(D)):
    if not name.endswith(".wav"): continue
    f = os.path.join(D, name); I = target(name); m = measure(f)
    if I is None or m["input_i"] == "-inf":
        # leave the level alone, but never above -1.5 dBTP
        if m["input_tp"] != "-inf" and float(m["input_tp"]) > -1.5:
            g = -1.5 - float(m["input_tp"]); af = f"volume={g:.2f}dB"
        else:
            print(f"{name:28s} kept ({m['input_i']} LUFS, {m['input_tp']} dBTP)"); continue
    else:
        af = (f"loudnorm=I={I}:TP=-1.5:LRA=11:measured_I={m['input_i']}:measured_TP={m['input_tp']}:"
              f"measured_LRA={m['input_lra']}:measured_thresh={m['input_thresh']}:offset={m['target_offset']}:linear=true")
    sr = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "stream=sample_rate,channels", "-of", "csv=p=0", f], capture_output=True, text=True).stdout.strip().split(",")
    tmp = tempfile.mktemp(suffix=".wav")
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", f, "-af", af, "-ar", sr[0], "-ac", sr[1], "-c:a", "pcm_s16le", tmp], check=True)
    os.replace(tmp, f)
    after = measure(f)
    print(f"{name:28s} {m['input_i']:>7} -> {after['input_i']:>7} LUFS   peak {after['input_tp']} dBTP")
