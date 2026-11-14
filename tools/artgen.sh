#!/bin/zsh
# Usage: artgen.sh <asset> "<description + size + tiling/alpha>"   -> ArtGen/<asset>/<asset>_v1..v4.png
# The prompt goes in on stdin: `-i` is variadic and would otherwise swallow it as an image path.
A="$1"; D="$2"; R=/Users/mac/local/internal/meta/XR/repo
mkdir -p "$R/ArtGen/$A" && cd "$R/ArtGen/$A" || exit 1
P="Using the attached concept art as the style reference (warm velvet reds, gilded gold, candlelight, stylised not photoreal), generate 4 variants of: $D. Do not include any text or letters in the images. Save them in the current directory as ${A}_v1.png ... ${A}_v4.png and print their paths."
print -r -- "$P" | codex exec --skip-git-repo-check --sandbox workspace-write --ephemeral -i "$R/ArtGen/concept.png" - > gen.log 2>&1
echo "$A exit $? files: $(ls | grep -c png)"
