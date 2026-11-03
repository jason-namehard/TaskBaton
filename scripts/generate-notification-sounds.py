"""Write the project's soft notification cues as mono PCM WAV files."""

import math
from pathlib import Path
import struct
import wave


RATE = 44100
OUTPUT = Path(__file__).resolve().parents[1] / "src/AgentRelay.Desktop/Assets/Sounds"


def piano(t, frequency):
    if t < 0:
        return 0.0
    attack = 1.0 - math.exp(-t / 0.004)
    return attack * sum(
        amplitude * math.exp(-t * (2.8 + partial * 0.9))
        * math.sin(2 * math.pi * frequency * partial * (1 + 0.00015 * partial**2) * t)
        for partial, amplitude in ((1, 1), (2, 0.32), (3, 0.12), (4, 0.045))
    )


def waterdrop(t):
    attack = 1 - math.exp(-t / 0.0015)
    phase = 2 * math.pi * (720 * t + 72 * (1 - math.exp(-t / 0.032)))
    return attack * math.exp(-t * 17) * (math.sin(phase) + 0.12 * math.sin(phase * 1.53))


def soft_piano(t):
    return piano(t, 523.251) + 0.55 * piano(t - 0.11, 659.255)


def warm_chord(t):
    envelope = (1 - math.exp(-t / 0.055)) * math.exp(-t * 3.2)
    return envelope * sum(
        math.sin(2 * math.pi * frequency * t) + 0.12 * math.sin(4 * math.pi * frequency * t)
        for frequency in (261.626, 329.628, 391.995, 493.883)
    )


def write_cue(name, duration, signal):
    samples = [signal(index / RATE) for index in range(round(duration * RATE))]
    peak = max(abs(sample) for sample in samples)
    gain = 0.22 / peak
    fade = round(0.04 * RATE)
    pcm = bytearray()
    for index, sample in enumerate(samples):
        tail = min(1.0, (len(samples) - 1 - index) / fade)
        pcm.extend(struct.pack("<h", round(sample * gain * tail * 32767)))
    with wave.open(str(OUTPUT / (name + ".wav")), "wb") as output:
        output.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        output.writeframes(pcm)


if __name__ == "__main__":
    write_cue("waterdrop", 0.5, waterdrop)
    write_cue("soft-piano", 1.2, soft_piano)
    write_cue("warm-chord", 1.5, warm_chord)
