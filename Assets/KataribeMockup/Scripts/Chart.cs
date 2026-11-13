using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public enum NoteType { Tap, Long }

/// <summary>1つのノート。points は (拍, 高さ0〜1)。tap は1点、long は2点以上。</summary>
public class NoteData
{
    public NoteType type;
    public List<Vector2> points = new List<Vector2>();

    public float StartBeat => points[0].x;
    public float EndBeat => points[points.Count - 1].x;
    public float HeightAt(float beat) => Chart.Interp(points, beat);
}

/// <summary>1小節ぶんのノート。</summary>
public class MeasureData
{
    public List<NoteData> notes = new List<NoteData>();
    public bool isRest;
    /// <summary>前の小節から止まらずに続く（お手本とプレイが同時に進む）</summary>
    public bool flow;
}

/// <summary>
/// テキスト譜面の読み込み。
///
///   tap  拍  高さ
///   long 拍 高さ | 拍 高さ | 拍 高さ ...
///   rest            （何もない小節にしたいとき）
///   flow            （この小節は前の小節から止まらずに続く。お手本とプレイが同時に進む）
///   ---             （小節の区切り）
///   # 以降はコメント
///
/// 拍は小節内の位置（3拍子なら 0.0〜3.0）、高さは 0.0（下）〜1.0（上）。
/// </summary>
public static class Chart
{
    public static List<MeasureData> Parse(string text, int beatsPerMeasure)
    {
        var result = new List<MeasureData>();
        var cur = new MeasureData();
        string[] lines = text.Replace("\r", "").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);
            line = line.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("---"))
            {
                result.Add(cur);
                cur = new MeasureData();
                continue;
            }

            string[] tokens = line.Replace("|", " ")
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string kind = tokens[0].ToLowerInvariant();

            if (kind == "rest") { cur.isRest = true; continue; }
            if (kind == "flow") { cur.flow = true; continue; }

            var nums = new List<float>();
            bool ok = true;
            for (int k = 1; k < tokens.Length; k++)
            {
                if (float.TryParse(tokens[k], NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) nums.Add(v);
                else { ok = false; break; }
            }
            if (!ok || nums.Count < 2 || nums.Count % 2 != 0)
            {
                Warn(i, line, "数字の並びが「拍 高さ」の組になっていません");
                continue;
            }

            var note = new NoteData();
            for (int k = 0; k < nums.Count; k += 2)
                note.points.Add(new Vector2(Mathf.Clamp(nums[k], 0f, beatsPerMeasure * 8f), Mathf.Clamp01(nums[k + 1])));

            if (kind == "tap")
            {
                if (note.points.Count != 1) { Warn(i, line, "tap は「拍 高さ」1組だけです"); continue; }
                note.type = NoteType.Tap;
            }
            else if (kind == "long")
            {
                if (note.points.Count < 2) { Warn(i, line, "long は2点以上必要です"); continue; }
                bool increasing = true;
                for (int k = 1; k < note.points.Count; k++)
                    if (note.points[k].x <= note.points[k - 1].x) increasing = false;
                if (!increasing) { Warn(i, line, "long の拍は小さい順に並べてください"); continue; }
                note.type = NoteType.Long;
            }
            else
            {
                Warn(i, line, $"「{tokens[0]}」は知らない種類です（tap / long / rest / ---）");
                continue;
            }

            cur.notes.Add(note);
        }

        // 末尾の --- の後が空なら小節にしない（rest と書けば空の小節にできる）
        if (cur.notes.Count > 0 || cur.isRest || cur.flow || result.Count == 0) result.Add(cur);

        foreach (var m in result) m.notes.Sort((a, b) => a.StartBeat.CompareTo(b.StartBeat));
        return result;
    }

    static void Warn(int lineIndex, string line, string message)
    {
        Debug.LogWarning($"[譜面] {lineIndex + 1}行目「{line}」: {message}");
    }

    /// <summary>青の羽ペンがたどる道筋。全ノートの点を拍順に並べたもの。</summary>
    public static List<Vector2> BuildGuide(MeasureData m, float startHeight)
    {
        var keys = new List<Vector2>();
        foreach (var n in m.notes)
            foreach (var p in n.points) keys.Add(p);
        keys.Sort((a, b) => a.x.CompareTo(b.x));
        if (keys.Count == 0 || keys[0].x > 0.001f) keys.Insert(0, new Vector2(0f, startHeight));
        return keys;
    }

    /// <summary>点と点の間をなめらか（smoothstep）につなぐ。行き過ぎない補間。</summary>
    public static float Interp(List<Vector2> pts, float x)
    {
        if (pts == null || pts.Count == 0) return 0.5f;
        if (x <= pts[0].x) return pts[0].y;
        for (int i = 1; i < pts.Count; i++)
        {
            Vector2 a = pts[i - 1], b = pts[i];
            if (x <= b.x)
            {
                float w = b.x - a.x;
                if (w < 1e-4f) return b.y;
                float u = (x - a.x) / w;
                u = u * u * (3f - 2f * u);
                return Mathf.Lerp(a.y, b.y, u);
            }
        }
        return pts[pts.Count - 1].y;
    }
}