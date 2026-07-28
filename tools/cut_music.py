"""
Cut the two supplied tracks into the four looping pieces the game needs.

The cuts are chosen from tools/analyse_music.py's measurements, not by ear:

  Track A (85.6s, dark, low brightness 0.02-0.05) has two plain sections —
    a steady quieter one from ~5s, and a fuller louder one from ~40s.
  Track B (134.1s, brighter and more varied) also has two —
    a mid-energy stretch early on, and a fuller darker one after ~95s.

Assignment is by measured energy, quietest screen to loudest moment:

    menu       <- A quiet   (you're standing still; it should not nag)
    castle     <- B mid     (the everyday floors)
    endless    <- B full    (deeper, heavier, hypnotic)
    bloodmoon  <- A full    (the loudest thing in the game)

Three things every game loop needs, all applied here:

  • BAR ALIGNMENT. Each segment's length is a whole number of the track's own
    repeat period (2.25s for A, 2.00s for B, from the envelope autocorrelation),
    so the wrap lands where a beat lands instead of halfway through one.
  • CLICK GUARD. 12ms fades at both ends. A hard cut almost never sits at zero
    amplitude, and the step is audible as a tick on every single loop.
  • MATCHED LOUDNESS. All four are normalised to the same target, so changing
    mode doesn't jump in volume. Without this the mix has to be re-balanced by
    hand every time a track is swapped.

Usage: python tools/cut_music.py <trackA> <trackB>
"""
import os
import subprocess
import sys

OUT = "Assets/Resources/audio"
BAR_A, BAR_B = 2.25, 2.00      # measured repeat period of each track
FADE = 0.012                   # 12 ms — inaudible, but kills the loop tick
LUFS = -16                     # matched target across all four


def bars(bar, n):
    return round(bar * n, 3)


# (source, start, length, output name)
CUTS = [
    ("A", bars(BAR_A, 3),  bars(BAR_A, 14), "music"),           # menu — 31.5s
    ("A", bars(BAR_A, 19), bars(BAR_A, 16), "music_bloodmoon"),  # full — 36.0s
    ("B", bars(BAR_B, 4),  bars(BAR_B, 20), "music_castle"),     # mid  — 40.0s
    ("B", bars(BAR_B, 48), bars(BAR_B, 16), "music_endless"),    # full — 32.0s
]


def main(src_a, src_b):
    srcs = {"A": src_a, "B": src_b}
    os.makedirs(OUT, exist_ok=True)
    for tag, start, length, name in CUTS:
        dst = os.path.join(OUT, name + ".mp3")
        subprocess.run([
            "ffmpeg", "-v", "error", "-y",
            "-ss", str(start), "-t", str(length), "-i", srcs[tag],
            "-af", (f"afade=t=in:st=0:d={FADE},"
                    f"afade=t=out:st={round(length - FADE, 3)}:d={FADE},"
                    f"loudnorm=I={LUFS}:TP=-1.5:LRA=11"),
            "-ar", "44100", "-ac", "2", "-b:a", "192k",
            dst,
        ], check=True)
        size = os.path.getsize(dst) / 1024
        print(f"{name:18} <- track {tag}  {start:6.2f}s +{length:5.2f}s   {size:6.0f} KB")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit("usage: cut_music.py <trackA> <trackB>")
    main(sys.argv[1], sys.argv[2])
