using UnityEngine;

/// <summary>
/// クリック音を合成して、オーディオの時計（dspTime）で正確に鳴らすメトロノーム。
/// 小節の1拍目は高い音。
/// </summary>
public class Metronome : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.6f;
    public float accentFreq = 1760f;
    public float normalFreq = 1046f;

    const double LookAhead = 0.15;

    AudioSource[] sources;
    AudioClip accentClip, normalClip;
    int sourceIndex;

    double startDsp, beatSec;
    int beatsPerMeasure, nextBeat;
    bool running;

    void Awake()
    {
        sources = new AudioSource[4];
        for (int i = 0; i < sources.Length; i++)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.hideFlags = HideFlags.HideInInspector;   // 実行時に作った音をInspectorに表示しない（エディタの警告よけ）
            sources[i] = s;
        }
        accentClip = MakeClick("click_accent", accentFreq);
        normalClip = MakeClick("click", normalFreq);
    }

    AudioClip MakeClick(string clipName, float freq)
    {
        int rate = AudioSettings.outputSampleRate;
        int len = Mathf.CeilToInt(rate * 0.06f);
        var data = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / rate;
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 60f);
        }
        var clip = AudioClip.Create(clipName, len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <param name="firstBeat">負の値ならカウントイン（-3 で1小節ぶん）</param>
    public void Begin(double startDsp, double beatSec, int beatsPerMeasure, int firstBeat)
    {
        Stop();
        this.startDsp = startDsp;
        this.beatSec = beatSec;
        this.beatsPerMeasure = beatsPerMeasure;
        nextBeat = firstBeat;
        running = true;
    }

    public void Stop()
    {
        running = false;
        if (sources == null) return;
        foreach (var s in sources) s.Stop();
    }

    void Update()
    {
        if (!running) return;
        double now = AudioSettings.dspTime;
        while (true)
        {
            double time = startDsp + nextBeat * beatSec;
            if (time > now + LookAhead) break;
            if (time >= now)
            {
                int pos = ((nextBeat % beatsPerMeasure) + beatsPerMeasure) % beatsPerMeasure;
                var s = sources[sourceIndex];
                sourceIndex = (sourceIndex + 1) % sources.Length;
                s.clip = pos == 0 ? accentClip : normalClip;
                s.volume = volume;
                s.PlayScheduled(time);
            }
            nextBeat++;
        }
    }
}