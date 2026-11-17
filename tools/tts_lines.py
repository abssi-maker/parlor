#!/usr/bin/env python3
"""Offline voice for the magician: one WAV per caption line, generated once with Gemini TTS and shipped as
audio files. Nothing here runs in the app (no runtime TTS or network calls).

Lines are read straight from NightProgram.cs, so captions and voice can't drift apart.
Output names: vo_<id>.wav for the base script, vo_n<N>_<id>.wav for a night's override ('.' becomes '_').

Usage: GEMINI_API_KEY=... <venv>/bin/python tts_lines.py <out_dir> [line ids...]
Needs google-genai >= 2.x (e.g. /Users/mac/local/devpost/present-video/.venv/bin/python)."""
import os, re, struct, sys, time, pathlib
from google import genai
from google.genai import types

MODEL = "gemini-3.8-flash-tts"
VOICE = os.environ.get("PARLOR_VOICE", "Algieba")
STYLE = ("Voice a tiny Victorian stage magician performing a close-up magic show: warm, twinkling, theatrical, "
         "a touch conspiratorial, crisp English diction, unhurried. Say only the transcript, exactly as written.")
SRC = pathlib.Path(__file__).resolve().parent.parent / "PARLOR/Assets/Parlor/Scripts/Core/NightProgram.cs"
# Lines spoken by the thing that possesses him: same voice, played as a pantomime villain.
STYLES = {
    "vo_mishap_monster": ("Voice a pantomime villain possessing a tiny stage magician: slow, deep, growling, gleefully menacing, "
                          "comic rather than truly frightening, leaning hard on the capitalised words, at a brisk natural speaking pace with no long pauses. Say only the transcript, exactly as written."),
    "vo_mishap_dragged": ("Voice a tiny Victorian stage magician being dragged away by a monster: rising comic panic, fast, "
                          "breathless, ending in a squeak. Say only the transcript, exactly as written."),
    "vo_mishap_hide": ("Voice a tiny Victorian stage magician hiding behind a box from a monster, peeking out: a nervous, "
                       "dignified stage whisper, comic. Say only the transcript, exactly as written."),
    "vo_mishap_pity": ("Voice a tiny Victorian stage magician watching his own runaway shadow disappear: tender, wistful, "
                       "quietly fond, slower. Say only the transcript, exactly as written."),
    "vo_n2_mishap_pity": ("Voice a tiny Victorian stage magician, fondly exasperated with his sulking shadow: warm, a little "
                          "amused. Say only the transcript, exactly as written."),
    "vo_n3_mishap_pity": ("Voice a tiny Victorian stage magician admitting he would miss his troublesome shadow: warm, "
                          "touching, a smile in the voice, at a natural conversational pace with no long pauses. Say only the transcript, exactly as written."),
    "vo_curtain_shadow": ("Voice a tiny Victorian stage magician coaxing his shy shadow out to share the applause: warm, "
                          "generous, gentle, then proud. Say only the transcript, exactly as written."),
    "vo_mishap_help": ("Voice a tiny Victorian stage magician whispering urgently from inside a monster that has possessed him: "
                       "a hoarse stage whisper, quick, comic. Say only the transcript, exactly as written."),
}
client = genai.Client()


def lines():
    """[(clip_name, text)] from the C# script table and the per-night overrides."""
    src = SRC.read_text()
    pat = re.compile(r'new Line\("([^"]+)",\s*"((?:[^"\\]|\\.)*)"')
    base_at = src.index("Line[] Base")
    out, base = [], {}
    for m in pat.finditer(src, base_at, src.index("class NightCatalog")):
        base[m.group(1)] = m.group(2)
        out.append(("vo_" + m.group(1).replace(".", "_"), m.group(2)))
    for night in re.finditer(r"n(\d)\.Overrides = new List<Line>\s*\{(.*?)\};", src, re.S):
        for m in pat.finditer(night.group(2)):
            if base.get(m.group(1)) != m.group(2):
                out.append((f"vo_n{night.group(1)}_" + m.group(1).replace(".", "_"), m.group(2)))
    return out


def wav(pcm, rate):
    return struct.pack("<4sI4s4sIHHIIHH4sI", b"RIFF", 36 + len(pcm), b"WAVE", b"fmt ", 16, 1, 1, rate, rate * 2, 2, 16,
                       b"data", len(pcm)) + pcm


def say(name, text, out):
    path = pathlib.Path(out) / (name + ".wav")
    if path.exists(): return f"{path.name} kept"
    voice = (types.VoiceConfig(voice=VOICE) if VOICE.startswith("voice_") else
             types.VoiceConfig(prebuilt_voice_config=types.PrebuiltVoiceConfig(voice_name=VOICE)))
    cfg = types.GenerateContentConfig(temperature=1, response_modalities=["audio"],
                                      speech_config=types.SpeechConfig(voice_config=voice))
    style = STYLES.get(name, STYLE)
    contents = [types.Content(role="user", parts=[types.Part.from_text(text=style + "\n\n## Transcript:\n" + text)])]
    err = ""
    for _ in range(8):
        try:
            pcm = bytearray(); rate = 24000
            for ch in client.models.generate_content_stream(model=MODEL, contents=contents, config=cfg):
                if ch.parts and ch.parts[0].inline_data and ch.parts[0].inline_data.data:
                    d = ch.parts[0].inline_data; pcm.extend(d.data)
                    for p in d.mime_type.split(";"):
                        if p.strip().startswith("rate="): rate = int(p.split("=")[1])
            if len(pcm) < rate // 2: raise RuntimeError("empty audio")
            path.write_bytes(wav(bytes(pcm), rate))
            return f"{path.name} {len(pcm) / (2 * rate):.2f}s"
        except Exception as e:  # the model allows about 10 requests a minute; wait as long as the API says
            err = str(e)[:160]
            if "voice" in err.lower() and "not" in err.lower(): break
            wait = re.search(r"retry in ([0-9.]+)s", str(e))
            time.sleep(float(wait.group(1)) + 1 if wait else 10)
    return f"{name} FAILED {err}"


if __name__ == "__main__":
    out = sys.argv[1]; only = set(sys.argv[2:])
    os.makedirs(out, exist_ok=True)
    for name, text in lines():
        if only and name not in only: continue
        print(say(name, text, out), flush=True)
