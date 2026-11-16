#!/bin/zsh
# Cuts Captures/PARLOR_demo.mp4 from two simulator recordings of the build (Captures/rec/take_hero.mp4 and take_b.mp4,
# made by tools/recshow.sh in the LivingRoom environment). Real footage and the build's own audio only; the text
# overlays name what each shot demonstrates. Edit list: submission/VIDEO_SCRIPT.md.
set -e
R=/Users/mac/local/internal/meta/XR/repo; C=$R/Captures/rec; O=$R/Captures/_cut; rm -rf $O; mkdir -p $O
FB=$R/PARLOR/Assets/Parlor/Resources/Fonts/BodySource.ttf
FT=$R/PARLOR/Assets/Parlor/Resources/Fonts/TitleSource.ttf
FI=$R/PARLOR/Assets/Parlor/Resources/Fonts/ItalicSource.ttf
# the viewport is 1226 x 1284; every shot is a 16:9 band of it (y offset per shot), the cursor patch blurred out
NOCURSOR="delogo=x=84:y=418:w=36:h=50"

n=0
shot() {   # shot <src> <start> <end> <crop-y> <overlay text or "">
  local src=$1 ss=$2 to=$3 y=$4 txt=$5; n=$((n+1)); local d=$(python3 -c "print(round($to-$ss,3))")
  local vf="$NOCURSOR,crop=1226:690:0:$y,scale=1920:1080:flags=lanczos,fps=30"
  if [ -n "$txt" ]; then
    vf="$vf,drawbox=y=ih-170:h=96:w=iw:color=0x3E0A17@0.72:t=fill:enable='between(t,0.4,4.4)'"
    vf="$vf,drawtext=fontfile=${FB}:text='$txt':fontcolor=0xF5EDD9:fontsize=46:x=(w-text_w)/2:y=h-138:enable='between(t,0.4,4.4)'"
  fi
  local fo=$(python3 -c "print(max(0,round($d-0.12,3)))")
  ffmpeg -loglevel error -y -ss $ss -t $d -i $C/$src.mp4 -vf "$vf" \
    -af "afade=t=in:d=0.08,afade=t=out:st=$fo:d=0.12" -c:v libx264 -crf 17 -preset medium -pix_fmt yuv420p \
    -c:a aac -b:a 192k -ar 48000 -ac 2 $O/$(printf %02d $n).mp4
  echo "file '$O/$(printf %02d $n).mp4'" >> $O/list.txt
}

card() {   # card <seconds> <still-src> <still-time> <title> <subtitle>
  local d=$1 src=$2 t=$3 title=$4 sub=$5; n=$((n+1))
  ffmpeg -loglevel error -y -ss $t -i $C/$src.mp4 -frames:v 1 $O/still_$n.png
  ffmpeg -loglevel error -y -loop 1 -t $d -i $O/still_$n.png -f lavfi -t $d -i anullsrc=r=48000:cl=stereo -vf \
    "$NOCURSOR,crop=1226:690:0:330,scale=1920:1080,boxblur=18:2,eq=brightness=-0.32:saturation=0.7,fps=30,\
drawtext=fontfile=${FT}:text='$title':fontcolor=0xF2D9A0:fontsize=150:x=(w-text_w)/2:y=(h/2)-150,\
drawtext=fontfile=${FI}:text='$sub':fontcolor=0xF5EDD9:fontsize=54:x=(w-text_w)/2:y=(h/2)+40,\
fade=t=in:d=0.4,fade=t=out:st=$(python3 -c "print($d-0.4)"):d=0.4" \
    -c:v libx264 -crf 17 -pix_fmt yuv420p -c:a aac -b:a 192k -shortest $O/$(printf %02d $n).mp4
  echo "file '$O/$(printf %02d $n).mp4'" >> $O/list.txt
}

# 1  cold open: the coin goes through the real table and he catches it far below
shot take_hero 49.0 61.6 430 ""
# 2  title
card 3.6 take_hero 62.5 "PARLOR" "The Borrowed Coin"
# 3  arrival
shot take_b 4.2 17.0 330 "Finds your real table. No controllers."
# 4  mind reading
shot take_b 20.4 32.6 330 "He reads your gaze. No cursor."
# 5  the borrowed coin
shot take_b 35.6 47.6 330 "Your hands are the props."
# 6  coin in the palm
shot take_b 62.6 72.0 330 "Open your palm. It is there."
# 7  the lesson
shot take_b 76.0 92.0 330 "Then he teaches you the Vanish."
# 8  finale
shot take_b 95.0 114.0 330 "A flat hand over the case. Is this your card?"
# 9  curtain call on the real wall
shot take_b 117.6 131.5 420 "Curtains rise on your real wall."
# 10 encore
shot take_b 136.0 149.0 330 "Hands only. Any table. A new show every night."
# 11 end card
card 4.0 take_b 128.0 "PARLOR" "Mixed reality magic for Meta Quest. Hands only."

ffmpeg -loglevel error -y -f concat -safe 0 -i $O/list.txt -c copy $R/Captures/PARLOR_demo.mp4
ffprobe -v error -show_entries format=duration,size -of default=nw=1 $R/Captures/PARLOR_demo.mp4
