#!/bin/zsh
# Assembles Captures/PARLOR_demo.mp4 from the shot files named in submission/VIDEO_SCRIPT.md.
# Usage: make_video.sh    (run from anywhere; needs ffmpeg and the v*.mp4 captures)
C=/Users/mac/local/internal/meta/XR/repo/Captures; F=/Users/mac/local/internal/meta/XR/repo/PARLOR/Assets/Parlor/Resources/Fonts/BodySource.ttf
typeset -A TXT
TXT=(v03_arrival "Finds your real table. No controllers." v04_gaze "He reads your gaze. No cursor." v05_palm "Your hands are the props."
     v06_lesson "Then he teaches you the Vanish." v07_finale "Tonight, the star is you." v08_return "He lives in your table. A new show every night."
     v09_rooms_a "Works in rooms we never saw." v10_close "PARLOR. Hands only. Any table.")
mkdir -p $C/_cut; rm -f $C/_cut/*.mp4 $C/_cut/list.txt
for f in v01_coin_drop v02_title v03_arrival v04_gaze v05_palm v06_lesson v07_finale v08_return v09_rooms_a v09_rooms_b v09_rooms_c v09_rooms_d v10_close; do
  [ -f $C/$f.mp4 ] || { echo "missing $f.mp4"; continue; }
  VF="scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2:color=black,fps=30"
  [ -n "${TXT[$f]}" ] && VF="$VF,drawbox=y=ih-150:h=90:w=iw:color=0x4A0E1C@0.7:t=fill:enable='lt(t,3)',drawtext=fontfile=$F:text='${TXT[$f]}':fontcolor=0xF5EDD9:fontsize=44:x=(w-text_w)/2:y=h-125:enable='lt(t,3)'"
  ffmpeg -y -loglevel error -i $C/$f.mp4 -vf "$VF" -c:v libx264 -crf 18 -preset medium -c:a aac -ar 48000 -ac 2 $C/_cut/$f.mp4 && echo "file '$C/_cut/$f.mp4'" >> $C/_cut/list.txt
done
ffmpeg -y -loglevel error -f concat -safe 0 -i $C/_cut/list.txt -c copy $C/PARLOR_demo.mp4 && ffprobe -v error -show_entries format=duration,size -of default=nw=1 $C/PARLOR_demo.mp4
