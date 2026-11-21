import wave, array, math, sys
def load(p):
    w = wave.open(p); return array.array('h', w.readframes(w.getnframes()))
def dist(a, b, rate=48000, period=None):
    period = period or rate // 120
    res = en = 0.0; worst = 0.0
    for f in range(0, min(len(a), len(b)) - period, period):
        io = ii = 0.0
        for i in range(f, f + period):
            io += a[i] * b[i]; ii += a[i] * a[i]
        if ii < (0.01 * 32768) ** 2 * period: continue   # skip near-silence
        k = io / ii; r = e = 0.0
        for i in range(f, f + period):
            d = b[i] - k * a[i]; r += d * d; e += b[i] * b[i]
        res += r; en += e
        if e > 0: worst = max(worst, math.sqrt(r / e))
    return 100 * math.sqrt(res / en), 100 * worst
for n in sys.argv[1:]:
    src = load(f'steam_{n}.wav')
    for tag in ('old', 'new'):
        out = load(f'steam_{n}_{tag}.wav')
        d, w = dist(src, out)
        print(f'{n:6s} {tag}: distortion moyenne {d:.2f} %  pire période {w:.1f} %')
