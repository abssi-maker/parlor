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


def panel_filters(do, shows, act):
    """drawtext filters for the two side panels."""
    f = []
    def text(s, font, size, color, x, y, spacing=10):
        f.append(f"drawtext=fontfile={font}:textfile={textfile(s)}:fontcolor={color}:fontsize={size}:x={x}:y={y}:line_spacing={spacing}")
    # left: brand, then what the player is doing
    text("PARLOR", FT, 54, GOLD, 48, 56)
    text("The Borrowed Coin", FI, 26, DIM, 50, 122)
    if act: text(act, FB, 24, GOLD, 48, 250)
    text("YOU", FB, 22, DIM, 48, 330)
    text(wrap(do, 17), FB, 36, CREAM, 48, 368, 14)
    text(wrap("Pale hands = the player's tracked hands (Meta XR Simulator). On a headset these are your own hands.", 30), FB, 18, DIM, 48, H - 130, 8)
    # right: which capability this shows
    x = VX + VW + 44
    text("WHAT IT SHOWS", FB, 22, DIM, x, 330)
    text(wrap(shows, 19), FB, 32, CREAM, x, 368, 14)
    text(wrap("Real footage of the build. One continuous take; only pauses trimmed.", 26), FB, 18, DIM, x, H - 104, 8)
    return f


def segment(src, t0, t1, do, shows, act, idx):
    d = t1 - t0
    vf = (f"crop=1226:1284:0:0,scale={VW}:{H}:flags=lanczos,pad={W}:{H}:{VX}:0:color={BG},"
          f"drawbox=x={VX - 2}:y=0:w=2:h={H}:color={GOLD}@0.35:t=fill,drawbox=x={VX + VW}:y=0:w=2:h={H}:color={GOLD}@0.35:t=fill,"
          + ",".join(panel_filters(do, shows, act)) + ",fps=30")
    out = f"{OUT}/s{idx:02d}.mp4"
    fo = max(0.0, d - 0.08)
    sh(["ffmpeg", "-y", "-loglevel", "error", "-ss", f"{t0:.3f}", "-t", f"{d:.3f}", "-i", src, "-vf", vf,
        "-af", f"afade=t=in:d=0.05,afade=t=out:st={fo:.3f}:d=0.08", "-c:v", "libx264", "-crf", "17", "-preset", "medium",
        "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2", out])
    return out


def card(seconds, still_src, still_t, lines, idx):
    """A title card over a darkened, blurred frame of the take. lines: [(text, font, size, color, y)]."""
    still = f"{OUT}/still{idx}.png"
    sh(["ffmpeg", "-y", "-loglevel", "error", "-ss", f"{still_t:.3f}", "-i", still_src, "-frames:v", "1", still])
    dt = []
    for s, font, size, color, y in lines:
        dt.append(f"drawtext=fontfile={font}:textfile={textfile(s)}:fontcolor={color}:fontsize={size}:x=(w-text_w)/2:y={y}:line_spacing=12")
    vf = (f"scale={W}:-1,crop={W}:{H},boxblur=20:2,eq=brightness=-0.36:saturation=0.6,fps=30," + ",".join(dt)
          + f",fade=t=in:d=0.4,fade=t=out:st={seconds - 0.4:.2f}:d=0.4")
    out = f"{OUT}/s{idx:02d}.mp4"
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
    src = f"{REC}/{take}.mp4"
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
            if not dry: parts.append(card(seg["card"], src, t, lines, idx))
            continue
        t0, t1 = tl.at(seg["from"]), tl.at(seg["to"])
        if t1 <= t0: sys.exit(f"segment {idx} is empty: {seg['from']} → {seg['to']} ({t0:.2f} → {t1:.2f})")
        total += t1 - t0
        print(f"{idx:2d} {t0:7.2f} → {t1:7.2f} ({t1 - t0:5.2f}s)  {seg['do']}")
        if not dry: parts.append(segment(src, t0, t1, seg["do"], seg["shows"], seg.get("act", ""), idx))
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
