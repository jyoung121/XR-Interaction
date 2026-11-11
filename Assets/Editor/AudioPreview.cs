using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 합성한 소리를 WAV 로 뽑는다. 빌드 전에 **들어 보려고** 있는 것이다.
///
/// ToolPreview 가 거울을 PNG 로 찍어 두는 것과 같은 이유다. 아이폰에
/// 올려 봐야 아는 것을 매번 올려 볼 수는 없고, 소리는 특히 그렇다 —
/// 파형을 아무리 들여다봐도 "드릴로 들리는가" 는 들어 봐야 안다.
///
/// 배치 모드에서:
///   Unity.exe -batchmode -quit -projectPath . -executeMethod AudioPreview.Dump
/// </summary>
public static class AudioPreview
{
    private const string OutDir = @"C:\UnityProjects\preview\audio";

    [MenuItem("Tools/소리 미리듣기 뽑기")]
    public static void Dump()
    {
        Directory.CreateDirectory(OutDir);

        Write("drill.wav", ScenarioAudio.Drill);
        Write("brush.wav", ScenarioAudio.Brush);

        // 음식마다 한 입. 진동에 쓰는 바로 그 값으로 그린다.
        foreach (MirrorRig.FoodKind k in
                 System.Enum.GetValues(typeof(MirrorRig.FoodKind)))
        {
            int peak = Mathf.Max(1, Mathf.RoundToInt(255f * MirrorRig.PeakScaleOf(k)));
            AudioClip c = ScenarioAudio.CrunchClip(
                MirrorRig.FragmentsOf(k), peak, 1, MirrorRig.BrightnessOf(k));
            Write("crunch_" + k.ToString().ToLowerInvariant() + ".wav", c);
            Chew(k);
        }

        Debug.Log("[AudioPreview] 완료 → " + OutDir);
    }

    /// <summary>
    /// 씹는 소리 한 번. **게임에서 들릴 그대로** 그린다.
    ///
    /// 바탕 소리를 그냥 뽑으면 고른 쉭 소리일 뿐이라 들어 봐도 모른다 —
    /// 오물거리는 박자는 바탕에 없고 턱이 만든다. 그래서 거울이 턱을
    /// 움직이는 식(MirrorRig.ChewJawOf)과 소리 세기를 뽑는 식
    /// (ScenarioAudio.ChewLevel)을 그대로 빌려 와, 60 fps 로 한 프레임씩
    /// 걸어가며 그 세기를 먹인다. 폰에서 돌 때와 같은 값이 나온다.
    /// </summary>
    private static void Chew(MirrorRig.FoodKind k)
    {
        AudioClip bed = ScenarioAudio.ChewBedOf(MirrorRig.BrightnessOf(k));
        if (bed == null) return;

        float[] loop = new float[bed.samples * bed.channels];
        bed.GetData(loop, 0);

        const float Dt = 1f / 60f;              // 게임이 도는 속도
        int step = Mathf.Max(1, Mathf.RoundToInt(bed.frequency * Dt));
        int frames = Mathf.CeilToInt(MirrorRig.ChewSecondsOf(k) / Dt);

        float[] outp = new float[frames * step];
        float level = 0f, prev = 0f;
        for (int fi = 0; fi < frames; fi++)
        {
            float open = MirrorRig.ChewJawOf(k, fi * Dt);
            level = ScenarioAudio.ChewLevel(level, open, prev, Dt);
            prev = open;

            for (int i = 0; i < step; i++)
            {
                int at = fi * step + i;
                outp[at] = loop[at % loop.Length] * level;
            }
        }

        Write("chew_" + k.ToString().ToLowerInvariant() + ".wav",
              outp, 1, bed.frequency);
    }

    private static void Write(string fileName, AudioClip clip)
    {
        if (clip == null) return;

        float[] f = new float[clip.samples * clip.channels];
        clip.GetData(f, 0);
        Write(fileName, f, clip.channels, clip.frequency);
    }

    /// <summary>
    /// 16 비트 모노 WAV. 헤더 44 바이트에 샘플을 붙이면 끝이다 —
    /// 이것 하나 때문에 라이브러리를 들이지 않는다.
    /// </summary>
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
            w.Write(16);                                  // fmt 조각 크기
            w.Write((short)1);                            // PCM
            w.Write((short)channels);
            w.Write(rate);
            w.Write(rate * channels * 2);                 // 초당 바이트
            w.Write((short)(channels * 2));               // 한 프레임 바이트
            w.Write((short)16);                           // 비트
            w.Write(new char[] { 'd', 'a', 't', 'a' });
            w.Write(bytes);

            for (int i = 0; i < f.Length; i++)
                w.Write((short)(Mathf.Clamp(f[i], -1f, 1f) * 32767f));
        }

        Debug.Log("[AudioPreview] " + fileName + " — " + f.Length + "샘플 / "
                  + (f.Length / (float)(rate * channels)).ToString("0.00") + "초");
    }
}
