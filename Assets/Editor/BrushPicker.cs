using System.IO;
using UnityEngine;

/// <summary>
/// 칫솔 "쓱쓱" 후보를 여러 개 뽑는다.
///
/// 쓱쓱은 말로 주고받기가 어렵다 — "더 쓱쓱하게" 를 숫자로 옮기는 건
/// 짐작이다. 그래서 짐작하지 말고 **들려주고 고르게** 한다. 고른
/// 번호의 값을 ScenarioAudio.BuildBrush 에 박으면 끝이다.
///
/// 한 번만 들려주면 왕복 박자가 안 들리므로 고리를 세 번 이어 붙인다.
/// 폰에서도 이 고리가 계속 돌아간다.
///
///   Unity.exe -batchmode -quit -buildTarget iOS -projectPath &lt;proj&gt;
///             -executeMethod BrushPicker.Dump
/// </summary>
public static class BrushPicker
{
    private const string OutDir = "C:/UnityProjects/preview/brush";
    private const int Repeat = 3;

    private struct V
    {
        public string name;
        public float hz;
        public int lo, hi;
        public float gain, body, floor;
    }

    public static void Dump()
    {
        Directory.CreateDirectory(OutDir);

        V[] list =
        {
            // 왕복 속도만 바꾼 셋 — 먼저 이 중에서 고르는 게 빠르다.
            new V { name = "1_느린쓱",   hz = 2f, lo = 1800, hi = 7000,
                    gain = 0.62f, body = 0.22f, floor = 0.16f },
            new V { name = "2_지금것",   hz = 3f, lo = 1800, hi = 7000,
                    gain = 0.62f, body = 0.22f, floor = 0.16f },
            new V { name = "3_빠른쓱",   hz = 4f, lo = 1800, hi = 7000,
                    gain = 0.62f, body = 0.22f, floor = 0.16f },

            // 음색만 바꾼 둘 — 속도를 고른 뒤 이쪽을 본다.
            new V { name = "4_둔한쓱",   hz = 3f, lo = 1200, hi = 5000,
                    gain = 0.62f, body = 0.30f, floor = 0.16f },
            new V { name = "5_가는쓱",   hz = 3f, lo = 2600, hi = 9000,
                    gain = 0.62f, body = 0.14f, floor = 0.16f },

            // 왕복을 더 또렷하게 — 지나갈 때와 돌아설 때 차이를 벌린다.
            new V { name = "6_또렷한왕복", hz = 3f, lo = 1800, hi = 7000,
                    gain = 0.62f, body = 0.22f, floor = 0.05f },
        };

        foreach (V v in list)
        {
            AudioClip c = ScenarioAudio.BrushVariant(v.hz, v.lo, v.hi,
                                                     v.gain, v.body, v.floor);
            if (c == null) { Debug.LogError("[BrushPicker] " + v.name + " 실패"); continue; }

            float[] one = new float[c.samples * c.channels];
            c.GetData(one, 0);

            float[] many = new float[one.Length * Repeat];
            for (int r = 0; r < Repeat; r++)
                System.Array.Copy(one, 0, many, r * one.Length, one.Length);

            Write(v.name + ".wav", many, c.channels, c.frequency);
            Debug.Log("[BrushPicker] " + v.name
                      + "  왕복 " + v.hz + "/s  대역 " + v.lo + "~" + v.hi
                      + "  바닥 " + v.floor);
        }

        Debug.Log("[BrushPicker] -> " + OutDir);
    }

    /// <summary>16 비트 모노 WAV. AudioPreview 와 같은 글.</summary>
    private static void Write(string fileName, float[] f, int channels, int rate)
    {
        int bytes = f.Length * 2;
        using (FileStream fs = new FileStream(Path.Combine(OutDir, fileName),
                                              FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write(new char[] { 'R', 'I', 'F', 'F' });
            w.Write(36 + bytes);
            w.Write(new char[] { 'W', 'A', 'V', 'E' });
            w.Write(new char[] { 'f', 'm', 't', ' ' });
            w.Write(16);
            w.Write((short)1);
            w.Write((short)channels);
            w.Write(rate);
            w.Write(rate * channels * 2);
            w.Write((short)(channels * 2));
            w.Write((short)16);
            w.Write(new char[] { 'd', 'a', 't', 'a' });
            w.Write(bytes);
            for (int i = 0; i < f.Length; i++)
                w.Write((short)(Mathf.Clamp(f[i], -1f, 1f) * 32767f));
        }
    }
}
