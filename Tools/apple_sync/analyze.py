import sys, json, numpy as np
from scipy.io import wavfile
from scipy import signal
import warnings; warnings.simplefilter("ignore")
sr, x = wavfile.read(sys.argv[1])
x = x.astype(np.float64)
if x.ndim > 1: x = x.mean(axis=1)
x /= np.abs(x).max()
dur = len(x)/sr

def env(x, hop_s, win_s):
    hop = int(sr*hop_s); win = int(sr*win_s)
    sq = np.concatenate([[0], np.cumsum(x*x)])
    idx = np.arange(0, len(x)-win, hop)
    return np.sqrt((sq[idx+win]-sq[idx])/win), idx/sr + win_s/2

# ── 1) 씹는 덩어리(이벤트) 나누기: 20 ms RMS
e20, t20 = env(x, 0.005, 0.020)
db20 = 20*np.log10(e20+1e-9)
TH = float(sys.argv[2]) if len(sys.argv) > 2 else -56.0
on = db20 > TH
events = []
i = 0
while i < len(on):
    if on[i]:
        j = i
        while j < len(on) and on[j]: j += 1
        events.append([t20[i]-0.010, t20[j-1]+0.010]); i = j
    else: i += 1
merged = []
for s, e in events:
    if merged and s - merged[-1][1] < 0.150: merged[-1][1] = e
    else: merged.append([s, e])
merged = [m for m in merged if m[1]-m[0] >= 0.060]

# ── 2) 이벤트 안의 잔 파열(탭): 고역 통과 뒤 2 ms 포락선의 봉우리
b, a = signal.butter(2, 800/(sr/2), "high")
xh = signal.filtfilt(b, a, x)
e2, t2 = env(xh, 0.001, 0.003)
db2 = 20*np.log10(e2+1e-9)
FLOOR, TOP = -62.0, float(db2.max())
out = []
for k, (s, e) in enumerate(merged):
    m = (t2 >= s) & (t2 <= e)
    seg = db2[m]; ts = t2[m]
    pk, _ = signal.find_peaks(seg, distance=6, prominence=3.0, height=FLOOR+4)
    taps = [(float(ts[p]), float(np.clip((seg[p]-FLOOR)/(TOP-FLOOR), 0, 1))) for p in pk]
    pdb = db20[(t20>=s)&(t20<=e)].max()
    out.append(dict(start=s, end=e, peak_db=float(pdb), taps=taps))
    print("ev%2d  %6.3f-%6.3f  len %4.0f ms  peak %6.1f dB  taps %3d  maxlevel %.2f  meanlevel %.2f" % (
        k, s, e, (e-s)*1000, pdb, len(taps), max([l for _,l in taps] or [0]), np.mean([l for _,l in taps] or [0])))
print("top dB (2ms, HP):", TOP, " events:", len(out), " total taps:", sum(len(o["taps"]) for o in out))
json.dump(dict(dur=dur, events=out), open(sys.argv[3], "w"))

import matplotlib; matplotlib.use("Agg")
import matplotlib.pyplot as plt
fig, ax = plt.subplots(3, 1, figsize=(16, 9))
tt = np.arange(len(x))/sr
ax[0].plot(tt, x, lw=0.3); ax[0].set_title("waveform + events")
for o in out: ax[0].axvspan(o["start"], o["end"], color="orange", alpha=0.3)
ax[1].plot(t20, db20, lw=0.6); ax[1].axhline(TH, color="r", lw=0.5); ax[1].set_title("20ms RMS dB")
for o in out:
    for t, l in o["taps"]: ax[2].vlines(t, 0, l, lw=0.6)
ax[2].set_title("taps (level 0..1)")
for a_ in ax: a_.set_xlim(0, dur)
plt.tight_layout(); plt.savefig(sys.argv[4], dpi=70)
