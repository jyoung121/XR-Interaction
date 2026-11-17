"""사과 소리를 장면 박자에 맞춰 3번 이어 붙이고, 진동 타이밍 표를 뽑는다.

한 입 주기(P) 안의 배치:
  0 ~ LEAD        조용함 (접시에서 집어 입으로 가져가는 동안)
  LEAD ~ P        녹음 한 벌 (끝 0.3 초는 서서히 줄임)
LEAD 는 "사과가 입술에 닿는 순간 = 베어 무는 소리 시작" 이 되도록 잡는다.
"""
import sys, json, wave, uuid, numpy as np
from scipy.io import wavfile
from scipy import signal
import warnings; warnings.simplefilter("ignore")

src, events_json, out_wav, out_cs = sys.argv[1:5]

ON_PLATE, APPROACH, AFTER_BITE = 1.4, 1.2, 0.9   # MirrorRig 의 상수와 같아야 한다
COPIES = 3

sr, x = wavfile.read(src)
x = x.astype(np.float64)
if x.ndim > 1: x = x.mean(axis=1)
x /= np.abs(x).max()

d = json.load(open(events_json))
ev = d["events"]
bite_start, bite_end = ev[0]["start"], ev[0]["end"]
last_end = ev[-1]["end"]

LEAD = round(ON_PLATE + APPROACH - bite_start, 3)          # 2.43
HOLD = round(bite_end - bite_start, 3)                      # 0.735
USE  = 13.27                                                # 녹음에서 쓰는 길이
CHEW = round(USE - AFTER_BITE - bite_end, 3)                # 11.465
P    = round(LEAD + USE, 3)                                 # 15.7
assert abs(P - (ON_PLATE + APPROACH + HOLD + CHEW + AFTER_BITE)) < 1e-6
assert bite_end + CHEW > last_end + 0.25, "씹는 구간이 마지막 씹는 소리보다 짧다"

seg = x[: int(USE * sr)].copy()
fi, fo = int(0.03 * sr), int(0.30 * sr)
seg[:fi] *= 0.5 - 0.5 * np.cos(np.linspace(0, np.pi, fi))
seg[-fo:] *= 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, fo))

n_p = int(round(P * sr))
out = np.zeros(n_p * COPIES)
for k in range(COPIES):
    a = k * n_p + int(round(LEAD * sr))
    out[a:a + len(seg)] += seg
out *= 0.89 / np.abs(out).max()
wavfile.write(out_wav, sr, (out * 32767).astype(np.int16))
print("wav: %.3f s, sr %d, mono, period %.3f s x %d" % (len(out) / sr, sr, P, COPIES))

# ── 표 (한 주기 분량. 시각은 주기 시작부터, ms)
L = []
A = L.append
A("// 자동 생성 파일 — 손으로 고치지 말 것. (Tools/apple_sync/build_audio.py 가 만든다)")
A("//")
A("// 소리: Resources/Audio/AppleCrunch.wav")
A("//   원본은 BigSoundBank \"Apple crunched #4\" (Joseph SARDIN, CC0).")
A("//   한 벌(크게 베어 무는 소리 1번 + 잘게 씹는 소리 %d번)을 장면의 한 입 주기에 맞춰" % (len(ev) - 1))
A("//   %d번 이어 붙였다. 주기마다 앞 %.2f 초는 조용하다 — 접시에서 집어 입으로 가져가는 동안이다." % (COPIES, LEAD))
A("//")
A("// 표: 한 주기 분량이다. 시각은 **주기 시작부터** 잰 ms 다 (= MirrorRig 의 한 입 주기 안 시각).")
A("//   이벤트 = 씹는 덩어리 하나 (20 ms RMS 가 -56 dB 를 넘는 구간, 150 ms 안쪽 틈은 이어 붙임)")
A("//   탭     = 덩어리 안의 잔 파열 하나 (800 Hz 고역 통과 뒤 포락선의 봉우리)")
A("//   레벨   = 봉우리 크기를 dB 로 눌러 0~255 로 옮긴 값. 가장 큰 파열이 255.")
A("public static class AppleCrunchData")
A("{")
A("    /// <summary>Resources 안의 소리 파일 경로 (확장자 없이)</summary>")
A("    public const string ClipResource = \"Audio/AppleCrunch\";")
A("")
A("    /// <summary>한 입 주기의 길이 (s). MirrorRig.BiteCycleOf(Apple) 과 같아야 한다.</summary>")
A("    public const float SegmentSeconds = %.3ff;" % P)
A("    /// <summary>소리 파일에 들어 있는 주기 수 = 사과 하나를 먹는 입 수.</summary>")
A("    public const int Segments = %d;" % COPIES)
A("")
A("    /// <summary>사과가 입술에 닿아 있는 시간 (s) = 크게 베어 무는 소리의 길이.</summary>")
A("    public const float HoldSeconds = %.3ff;" % HOLD)
A("    /// <summary>그 뒤 씹는 시간 (s).</summary>")
A("    public const float ChewSeconds = %.3ff;" % CHEW)
A("    /// <summary>주기 안에서 사과가 입술에 닿는 시각 (ms) = OnPlate + ApproachTime.</summary>")
A("    public const int TouchMs = %d;" % round((ON_PLATE + APPROACH) * 1000))
A("")
A("    /// <summary>앞에서부터 몇 개가 '크게 베어 무는' 이벤트인가</summary>")
A("    public const int BiteEvents = 1;")
A("")
ms = lambda t: int(round((t + LEAD) * 1000))
A("    /// <summary>이벤트 시작·끝 (주기 시작부터, ms)</summary>")
A("    public static readonly int[] EventStartMs = { %s };" % ", ".join(str(ms(e["start"])) for e in ev))
A("    public static readonly int[] EventEndMs   = { %s };" % ", ".join(str(ms(e["end"])) for e in ev))
A("")
A("    /// <summary>이벤트별 탭 시각 (그 이벤트 시작부터, ms)</summary>")
A("    public static readonly int[][] TapMs =")
A("    {")
for e in ev:
    A("        new[] { %s }," % ", ".join(str(max(0, round((t - e["start"]) * 1000))) for t, _ in e["taps"]))
A("    };")
A("")
A("    /// <summary>이벤트별 탭 레벨 0~255</summary>")
A("    public static readonly int[][] TapLevel =")
A("    {")
for e in ev:
    A("        new[] { %s }," % ", ".join(str(max(1, round(l * 255))) for _, l in e["taps"]))
A("    };")
A("")
A("    /// <summary>")
A("    /// 씹는 소리(베어 무는 것 다음부터)의 시작·끝. **씹기 시작한 뒤** 흐른 시간(s)으로 잰다 —")
A("    /// MirrorRig.AnimateFood 의 c 와 같은 시계다. 턱이 여기에 맞춰 다물린다.")
A("    /// </summary>")
c0 = bite_end
A("    public static readonly float[] ChewStart = { %s };" % ", ".join("%.3ff" % (e["start"] - c0) for e in ev[1:]))
A("    public static readonly float[] ChewEnd   = { %s };" % ", ".join("%.3ff" % (e["end"] - c0) for e in ev[1:]))
A("}")
open(out_cs, "w", encoding="utf-8", newline="").write("\n".join(L) + "\n")
print("LEAD %.3f HOLD %.3f CHEW %.3f P %.3f total %.1f s" % (LEAD, HOLD, CHEW, P, P * COPIES))
