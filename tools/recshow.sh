#!/bin/zsh
# Usage: recshow.sh <out-name> <xrop-script>
# Records the Meta XR Simulator viewport (screen capture, wall-clock timestamps) and the app's own audio mix
# (DevAudioRecorder) for the whole script, then muxes them aligned: Captures/rec/<out-name>.mp4.
# The simulator window must be visible and unobstructed (it is brought to the front).
R=/Users/mac/local/internal/meta/XR/repo; N=$1; S=$2; O=$R/Captures/rec; mkdir -p $O
U=/Users/mac/.unity/bin/unity
export XRSIM_WINDOW=$(swift $R/tools/winid.swift simulator 2>/dev/null | head -1 | cut -d" " -f1)
$U command console clear >/dev/null 2>&1
$U command editor.playmode start --run-in-background true >/dev/null 2>&1
until $U command console get --tail 400 2>/dev/null | grep -q 'PARLOR\] ready'; do sleep 2; done
osascript -e 'tell application "MetaXRSimulator" to activate' >/dev/null 2>&1; sleep 1
swift $R/tools/mouseaway.swift >/dev/null 2>&1
read WID WX WY WW WH <<< "$(swift $R/tools/winbounds.swift simulator | head -1)"
# viewport inside the window, in window pixels (Retina 2x), measured from a window screenshot
VX=$((WX*2+833)); VY=$((WY*2+184)); VW=1226; VH=1284
rm -f $O/$N.wav.events
$U command script eval --timeout 30 --code "return Parlor.DevAudioRecorder.Begin(\"$O/$N.wav\");" 2>&1 | tail -1
ffmpeg -loglevel error -y -f avfoundation -pixel_format uyvy422 -capture_cursor 0 -framerate 30 -use_wallclock_as_timestamps 1 -i "1:none" \
  -vf "crop=$VW:$VH:$VX:$VY" -c:v libx264 -preset veryfast -crf 16 -pix_fmt yuv420p -copyts $O/${N}_video.mkv < /dev/null &
FF=$!
sleep 1
python3 $R/tools/xrop.py - < $S 2>&1 | grep -E "TIMEOUT|failed|Error|Traceback|warm|retrying" 
kill -INT $FF; wait $FF 2>/dev/null
$U command script eval --timeout 60 --code "return Parlor.DevAudioRecorder.End();" 2>&1 | tail -1
$U command editor.playmode stop >/dev/null 2>&1
VS=$(ffprobe -v error -select_streams v:0 -show_entries stream=start_time -of csv=p=0 $O/${N}_video.mkv)
AS=$(cat $O/$N.wav.start)
OFF=$(python3 -c "print(round($AS-$VS,3))")
echo "video starts $VS, audio starts $AS, audio offset $OFF s"
# audio earlier than video (usual: the recorder starts first): trim its head; later: delay it
AF=$(python3 -c "o=$OFF; print(f'atrim=start={-o:.3f},asetpts=PTS-STARTPTS' if o<0 else f'adelay={int(o*1000)}:all=1')")
ffmpeg -loglevel error -y -i $O/${N}_video.mkv -i $O/$N.wav -map 0:v -map 1:a \
  -vf "setpts=PTS-STARTPTS" -fps_mode cfr -r 30 -af "$AF" -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -c:a aac -b:a 192k -ar 48000 -shortest $O/$N.mp4
ffprobe -v error -show_entries format=duration -of csv=p=0 $O/$N.mp4
