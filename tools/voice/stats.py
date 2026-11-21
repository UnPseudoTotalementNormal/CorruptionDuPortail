import wave, array, math, sys
def load(p):
    w = wave.open(p); n = w.getnframes(); ch = w.getnchannels(); r = w.getframerate()
    a = array.array('h', w.readframes(n))
    if ch > 1: a = a[::ch]
    return a, r
def stats(name, a, r):
    x = [v / 32768 for v in a]
    peak = max(abs(v) for v in x); clipped = sum(1 for v in x if abs(v) >= 0.98)
    blk = r // 100; rmss = []
    for i in range(0, len(x) - blk, blk):
        s = sum(v * v for v in x[i:i+blk]) / blk; rmss.append(math.sqrt(s))
    speech = [v for v in rmss if v > 0.005]
    sp = math.sqrt(sum(v*v for v in speech) / len(speech)) if speech else 0
    # flat-top runs: >=3 consecutive samples equal and |v|>0.5 (hard clip signature)
    flat = sum(1 for i in range(2, len(a)) if a[i] == a[i-1] == a[i-2] and abs(a[i]) > 16000)
    db = lambda v: 20 * math.log10(v) if v > 0 else -120
    print(f"{name}: {len(x)/r:.1f}s rate={r} peak={db(peak):.1f}dB speech={db(sp):.1f}dB crest={db(peak)-db(sp):.1f}dB clipped={100*clipped/len(x):.3f}% flatTops={flat} speechBlocks={len(speech)}/{len(rmss)}")
for p in sys.argv[1:]:
    a, r = load(p); stats(p.split('/')[-1], a, r)
