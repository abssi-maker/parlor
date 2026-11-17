#!/usr/bin/env python3
"""Cuts Captures/PARLOR_demo.mp4 from ONE continuous simulator take (tools/recshow.sh + tools/video_game.xrop).

Written for judges who have never heard of the project:
  * the whole simulator view is shown, never cropped, so the player's hands and the table are always in frame;
  * a left panel says what the player is doing right now, a right panel says which capability it shows;
  * the only cuts are trims of dead time inside the same continuous take (no jumping between scenes);
  * opens with a title card that says what the thing is, closes with one that says what was shown.

Segments are anchored to the take's own event log (<take>.wav.events, written by DevAudioRecorder.Event), so a new
take re-cuts itself. Usage: cut_demo.py <take name> [--dry]"""
import json, os, re, subprocess, sys, tempfile

R = "/Users/mac/local/internal/meta/XR/repo"
REC = R + "/Captures/rec"
OUT = R + "/Captures/_demo"
FONTS = R + "/PARLOR/Assets/Parlor/Resources/Fonts"
FB, FT, FI = FONTS + "/BodySource.ttf", FONTS + "/TitleSource.ttf", FONTS + "/ItalicSource.ttf"
W, H = 1920, 1080
VW = 1226 * H // 1284          # the viewport, scaled to full height (1031 px)
VX = (W - VW) // 2             # its left edge (444 px): the panels take the rest
PANEL = VX
BG = "0x170A0E"
GOLD, CREAM, DIM = "0xE8C77A", "0xF5EDD9", "0xB9A89A"


def sh(cmd, quiet=True):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit("FAILED: " + " ".join(cmd[:8]) + "\n" + r.stderr[-2000:])
    return r.stdout


def events(take):
    """[(t, message)] on the mp4 timeline (t = 0 at the first video frame)."""
    vs = float(sh(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=start_time", "-of", "csv=p=0",
                   f"{REC}/{take}_video.mkv"]).strip())
    out = []
    for line in open(f"{REC}/{take}.wav.events"):
        ep, _, msg = line.strip().partition(" ")
        out.append((float(ep) - vs, msg))
    return out


class Timeline:
    def __init__(self, ev):
        self.ev = ev

    def at(self, ref):
        """'gate coin.pick open+0.5' → seconds. Refs: an event substring, optional +/-offset, optional '#n' (n-th match)."""
        if isinstance(ref, (int, float)): return float(ref)
        m = re.match(r"^(.*?)(?:#(\d+))?\s*([+-]\s*[0-9.]+)?$", ref.strip())
        text, nth, off = m.group(1).strip(), int(m.group(2) or 1), float((m.group(3) or "0").replace(" ", ""))
        hits = [t for t, msg in self.ev if text in msg]
        if len(hits) < nth: sys.exit(f"no event '{text}' (#{nth}) in the log")
        return hits[nth - 1] + off


def textfile(s):
    f = tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False, dir=OUT)
    f.write(s); f.close()
    return f.name


def wrap(s, n):
    words, lines, cur = s.split(), [], ""
    for w in words:
        if len(cur) + len(w) + 1 > n and cur: lines.append(cur); cur = w
        else: cur = (cur + " " + w).strip()
    if cur: lines.append(cur)
    return "\n".join(lines)


def label_filters(do, shows, act):
    """A compact label top-left (the app's own caption sits top-centre, the hands at the bottom), and a permanent
    footage tag bottom-right so the simulator's photoreal room is never mistaken for real passthrough."""
    f = []
    def text(t, font, size, color, x, y, box=True, spacing=8):
        b = ":box=1:boxcolor=0x170A0E@0.82:boxborderw=12" if box else ""
        f.append(f"drawtext=fontfile={font}:textfile={textfile(t)}:fontcolor={color}:fontsize={size}:x={x}:y={y}:line_spacing={spacing}{b}")
    y = 30
    if act: text(act, FB, 26, GOLD, 36, y); y += 56
    w = wrap(do, 28)
    text(w, FB, 44, CREAM, 36, y); y += 56 * (w.count("\n") + 1) + 16
    if shows: text(wrap(shows, 34), FB, 32, GOLD, 36, y)
    return f


BAND = 800                      # source rows shown: caption, magician, table and hands all fit
FW = round(1226 * H / BAND)     # 1655 px wide on screen; thin bars either side


def band(y):
    return f"crop=1226:{BAND}:0:{y},scale={FW}:{H}:flags=lanczos,pad={W}:{H}:{(W - FW) // 2}:0:color={BG}"


def frame_filter(seg):
    """The simulator view at 1.35x: a tall band at height y, or a close-up box of the same shape."""
    z = seg.get("zoom")
    if z:
        w = z["w"]; h = round(w * BAND / 1226)
        return f"crop={w}:{h}:{z['x']}:{z['y']},scale={FW}:{H}:flags=lanczos,pad={W}:{H}:{(W - FW) // 2}:0:color={BG}"
    return band(seg.get("y", 300))


RING = R + "/Captures/_demo_assets/ring.png"


def segment(src, t0, t1, seg, idx, tl=None):
    d = t1 - t0
    # a segment with no labels at all (the cold open) shows the footage alone
    labels = label_filters(seg.get("do", ""), seg.get("shows", ""), seg.get("act", "")) if (seg.get("do") or seg.get("shows") or seg.get("act")) else []
    vf = ",".join([frame_filter(seg)] + labels + ["fps=30"])
    out = f"{OUT}/s{idx:02d}.mp4"
    fo = max(0.0, d - 0.06)
    cmd = ["ffmpeg", "-y", "-loglevel", "error", "-ss", f"{t0:.3f}", "-t", f"{d:.3f}", "-i", src]
    g = seg.get("gaze")
    if g:
        # a video annotation (the app itself deliberately draws no cursor): where the player is looking
        a, b = tl.at(g["from"]) - t0, tl.at(g["to"]) - t0
        x, y = g["at"]
        lab = textfile(g.get("text", "player's gaze"))
        fc = (f"[0:v]{vf}[v];[1:v]format=rgba[r];[v][r]overlay=x={x - 110}:y={y - 110}:enable='between(t,{a:.2f},{b:.2f})',"
              f"drawtext=fontfile={FB}:textfile={lab}:fontcolor={GOLD}:fontsize=28:x={x + 115}:y={y - 18}:box=1:boxcolor=0x170A0E@0.8:boxborderw=8"
              f":enable='between(t,{a:.2f},{b:.2f})'[o]")
        cmd += ["-loop", "1", "-i", RING, "-filter_complex", fc, "-map", "[o]", "-map", "0:a"]
    else:
        cmd += ["-vf", vf]
    cmd += ["-af", f"afade=t=in:d=0.04,afade=t=out:st={fo:.3f}:d=0.06", "-c:v", "libx264", "-crf", "17", "-preset", "medium",
            "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2", "-t", f"{d:.3f}", out]
    sh(cmd)
    return out


def card(seconds, src, t, lines, idx, live=None):
    """Title card. live = (y band) plays the take's own footage, darkened, under the text; otherwise a blurred still."""
    dt = []
    for s_, font, size, color, y in lines:
        dt.append(f"drawtext=fontfile={font}:textfile={textfile(s_)}:fontcolor={color}:fontsize={size}:x=(w-text_w)/2:y={y}:line_spacing=12"
                  f":shadowcolor=black@0.8:shadowx=2:shadowy=2")
    out = f"{OUT}/s{idx:02d}.mp4"
    fade = f",fade=t=in:d=0.35,fade=t=out:st={seconds - 0.4:.2f}:d=0.4"
    if live is not None:
        vf = band(live) + ",eq=brightness=-0.22:saturation=0.85,fps=30," + ",".join(dt) + fade
        sh(["ffmpeg", "-y", "-loglevel", "error", "-ss", f"{t:.3f}", "-t", f"{seconds}", "-i", src, "-vf", vf,
            "-af", f"afade=t=in:d=0.3,afade=t=out:st={seconds - 0.4:.2f}:d=0.4", "-c:v", "libx264", "-crf", "17", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2", out])
        return out
    still = f"{OUT}/still{idx}.png"
    sh(["ffmpeg", "-y", "-loglevel", "error", "-ss", f"{t:.3f}", "-i", src, "-frames:v", "1", still])
    vf = f"crop=1226:690:0:330,scale={W}:{H},boxblur=20:2,eq=brightness=-0.36:saturation=0.6,fps=30," + ",".join(dt) + fade
    sh(["ffmpeg", "-y", "-loglevel", "error", "-loop", "1", "-t", f"{seconds}", "-i", still, "-f", "lavfi", "-t", f"{seconds}",
        "-i", "anullsrc=r=48000:cl=stereo", "-vf", vf, "-c:v", "libx264", "-crf", "17", "-pix_fmt", "yuv420p",
        "-c:a", "aac", "-b:a", "192k", "-shortest", out])
    return out


def main():
    take = sys.argv[1]
    dry = "--dry" in sys.argv
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT): os.remove(os.path.join(OUT, f))
    tl = Timeline(events(take))
    main_src = f"{REC}/{take}.mp4"
    plan = json.load(open(R + "/tools/demo_plan.json"))
    parts, total = [], 0.0
    idx = 0
    for seg in plan["segments"]:
        idx += 1
        if seg.get("card"):
            lines = [(l[0], {"title": FT, "body": FB, "italic": FI}[l[1]], l[2], {"gold": GOLD, "cream": CREAM, "dim": DIM}[l[3]], l[4])
                     for l in seg["lines"]]
            t = tl.at(seg["still"])
            print(f"{idx:2d} card {seg['card']:.1f}s")
            total += seg["card"]
            if not dry: parts.append(card(seg["card"], main_src, t, lines, idx, seg.get("live")))
            continue
        src = f"{REC}/{seg['take']}.mp4" if seg.get("take") else main_src
        t0, t1 = tl.at(seg["from"]), tl.at(seg["to"])
        if t1 <= t0: sys.exit(f"segment {idx} is empty: {seg['from']} → {seg['to']} ({t0:.2f} → {t1:.2f})")
        total += t1 - t0
        print(f"{idx:2d} {t0:7.2f} → {t1:7.2f} ({t1 - t0:5.2f}s)  {seg['do']}")
        if not dry: parts.append(segment(src, t0, t1, seg, idx, tl))
    print(f"total {total:.1f} s")
    if dry: return
    lst = OUT + "/list.txt"
    open(lst, "w").write("".join(f"file '{p}'\n" for p in parts))
    sh(["ffmpeg", "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", lst, "-c", "copy", OUT + "/joined.mp4"])
    sh(["ffmpeg", "-y", "-loglevel", "error", "-i", OUT + "/joined.mp4", "-c:v", "copy", "-af", "loudnorm=I=-16:TP=-1.5:LRA=14",
        "-c:a", "aac", "-b:a", "192k", "-ar", "48000", R + "/Captures/PARLOR_demo.mp4"])
    print(sh(["ffprobe", "-v", "error", "-show_entries", "format=duration,size", "-of", "default=nw=1", R + "/Captures/PARLOR_demo.mp4"]))


if __name__ == "__main__":
    main()
