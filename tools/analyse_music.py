"""
Structural analysis of a music file, so its sections and its loop length can be
found by measurement rather than by scrubbing a waveform by ear.

Prints, per track:
  • an RMS/brightness profile over time, so obviously different SECTIONS
    (quiet intro, full arrangement, breakdown) can be told apart
  • the dominant repeat period, from autocorrelation of the loudness envelope —
    this is the bar/phrase length the track actually repeats on, which is what a
    game loop has to be cut to or it will audibly stutter every time it wraps

Usage:  python tools/analyse_music.py <file> [<file> ...]
"""
import subprocess
import sys

import numpy as np

SR = 22050
HOP = 0.25          # seconds per analysis frame


def load_mono(path):
    """Decode anything ffmpeg understands to mono float32 at SR."""
    raw = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", path,
         "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
        capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.float32)


def profile(x):
    n = int(SR * HOP)
    frames = len(x) // n
    x = x[:frames * n].reshape(frames, n)
    rms = np.sqrt((x ** 2).mean(axis=1) + 1e-12)
    # Zero-crossing rate stands in for brightness: cheap, and enough to tell a
    # bass-and-pad passage from one with hats and lead on top.
    zcr = (np.abs(np.diff(np.sign(x), axis=1)) > 0).mean(axis=1)
    return rms, zcr


def repeat_period(rms):
    """Dominant repeat length in seconds, via autocorrelation of the envelope."""
    e = rms - rms.mean()
    ac = np.correlate(e, e, mode="full")[len(e) - 1:]
    ac /= ac[0] + 1e-12
    lo = int(2.0 / HOP)                       # ignore anything under 2s
    hi = min(len(ac) - 1, int(40.0 / HOP))    # …and over 40s
    if hi <= lo:
        return None, 0.0
    k = lo + int(np.argmax(ac[lo:hi]))
    return k * HOP, float(ac[k])


for path in sys.argv[1:]:
    x = load_mono(path)
    dur = len(x) / SR
    rms, zcr = profile(x)
    period, strength = repeat_period(rms)

    print(f"\n=== {path.split(chr(92))[-1]}")
    print(f"duration {dur:.1f}s")
    if period:
        print(f"repeats every ~{period:.2f}s (confidence {strength:.2f})")

    # A coarse map: one line per 5 seconds, loudness as a bar plus brightness.
    step = int(5.0 / HOP)
    peak = rms.max() + 1e-12
    print(" time |  level                     | bright")
    for i in range(0, len(rms), step):
        seg_r = rms[i:i + step].mean() / peak
        seg_z = zcr[i:i + step].mean()
        bar = "#" * int(seg_r * 26)
        print(f"{i * HOP:5.0f} | {bar:<26} | {seg_z:.3f}")
