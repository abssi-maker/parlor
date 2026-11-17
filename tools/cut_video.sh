#!/bin/zsh
# Cuts Captures/PARLOR_demo.mp4 from simulator recordings of the build made by tools/recshow.sh: take_game (night 1,
# GameRoom, tools/video_game.xrop), take_return (night 2, same room), room_* (four other rooms, tools/video_room.xrop). Real footage and the build's own audio only; the text
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

montage() {   # montage <overlay text> <src start end crop-y>...: quick cuts joined into one shot, one overlay across all
  local txt=$1; shift; local parts=(); local m=0
  while [ $# -ge 4 ]; do
    m=$((m+1)); local d=$(python3 -c "print(round($3-$2,3))")
    ffmpeg -loglevel error -y -ss $2 -t $d -i $C/$1.mp4 -vf "$NOCURSOR,crop=1226:690:0:$4,scale=1920:1080:flags=lanczos,fps=30" \
      -af "afade=t=in:d=0.06,afade=t=out:st=$(python3 -c "print(max(0,round($d-0.08,3)))"):d=0.08" -c:v libx264 -crf 17 -preset medium -pix_fmt yuv420p \
      -c:a aac -b:a 192k -ar 48000 -ac 2 $O/m_$m.mp4
    echo "file '$O/m_$m.mp4'" >> $O/m_list.txt; shift 4
  done
  n=$((n+1)); local vf="drawbox=y=ih-170:h=96:w=iw:color=0x3E0A17@0.72:t=fill:enable='between(t,0.3,9)'"
  vf="$vf,drawtext=fontfile=${FB}:text='$txt':fontcolor=0xF5EDD9:fontsize=46:x=(w-text_w)/2:y=h-138:enable='between(t,0.3,9)'"
  ffmpeg -loglevel error -y -f concat -safe 0 -i $O/m_list.txt -vf "$vf" -c:v libx264 -crf 17 -preset medium -pix_fmt yuv420p -c:a aac -b:a 192k $O/$(printf %02d $n).mp4
  echo "file '$O/$(printf %02d $n).mp4'" >> $O/list.txt
}

# 1  cold open: the coin rests on the real table (game boxes all round), the table opens under it, it hangs, falls
#    into the lit theatre and he catches it
shot take_game 55.0 63.6 260 ""
# 2  title
card 3.2 take_game 12.0 "PARLOR" "The Borrowed Coin"
# 3  arrival: spotlight finds the table, the trapdoor opens, he rises (skips a one-frame simulator blank at 18.0 s)
shot take_game 4.0 15.5 330 "Finds your real table. No controllers."
# 4  mind reading
shot take_game 22.5 34.0 330 "He reads your gaze. No cursor."
# 5  the borrowed coin
shot take_game 37.0 49.5 330 "Your hands are the props."
# 6  coin in the palm
shot take_game 68.5 79.0 330 "Open your palm. It is there."
# 7  the lesson
shot take_game 82.0 98.0 330 "Then he teaches you the Vanish."
# 8  finale
shot take_game 104.0 126.5 330 "A flat hand over the case. Is this your card?"
# 9  curtain call on the real wall
shot take_game 131.5 142.0 300 "Curtains drop over your real wall."
# 10 the next night: three knocks, the trapdoor reopens on the same spot, he kept your card
shot take_return 4.6 14.2 330 "The next night, he knocks from the same spot."
# 11 any room
montage "Any table, in rooms we never saw." room_Office 5.6 8.8 330 room_FurniturefilledRoom 5.6 8.8 330 room_LivingRoom 5.6 8.8 330 room_Bedroom 5.6 8.8 330
# 12 encore
shot take_game 143.0 156.0 330 "Hands only. Any table. A new show every night."
# 13 end card
card 4.0 take_game 125.0 "PARLOR" "Mixed reality magic for Meta Quest. Hands only."

ffmpeg -loglevel error -y -f concat -safe 0 -i $O/list.txt -c copy $O/joined.mp4
# loudness for YouTube playback: -16 LUFS integrated, true peak under -1.5 dBTP (the build's own mix, only raised)
ffmpeg -loglevel error -y -i $O/joined.mp4 -c:v copy -af "loudnorm=I=-16:TP=-1.5:LRA=14" -c:a aac -b:a 192k -ar 48000 $R/Captures/PARLOR_demo.mp4
ffprobe -v error -show_entries format=duration,size -of default=nw=1 $R/Captures/PARLOR_demo.mp4
