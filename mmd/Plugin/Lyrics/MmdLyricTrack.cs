using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SFM.Mmd;

internal sealed class MmdLyricTrack
{
    static readonly Regex Timestamp = new(@"\G\[([0-9]{1,6}):([0-5][0-9])(?:\.([0-9]{1,3}))?\]");
    readonly (double Seconds, string Text)[] cues;

    MmdLyricTrack((double Seconds, string Text)[] cues) => this.cues = cues;

    internal int Count => cues.Length;

    internal static MmdLyricTrack Read(string directory)
    {
        string path = Path.Combine(directory, "lyrics.lrc");
        if (!File.Exists(path)) return null;

        var cues = new List<(double Seconds, string Text)>();
        var times = new List<double>();
        foreach (string raw in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            string line = raw.Trim();
            int end = 0;
            times.Clear();
            for (var tag = Timestamp.Match(line); tag.Success; tag = tag.NextMatch())
            {
                int minutes = int.Parse(tag.Groups[1].Value, CultureInfo.InvariantCulture);
                int seconds = int.Parse(tag.Groups[2].Value, CultureInfo.InvariantCulture);
                var fraction = tag.Groups[3];
                double subsecond = fraction.Success
                    ? int.Parse(fraction.Value.PadRight(3, '0'), CultureInfo.InvariantCulture) / 1000d : 0;
                times.Add(minutes * 60d + seconds + subsecond);
                end = tag.Index + tag.Length;
            }
            if (times.Count == 0) continue;
            string text = line.Substring(end).Trim();
            foreach (double time in times) cues.Add((time, text));
        }
        return cues.Count == 0 ? null : new MmdLyricTrack(cues.OrderBy(cue => cue.Seconds).ToArray());
    }

    internal string LineAt(double seconds)
    {
        int first = 0, end = cues.Length;
        while (first < end)
        {
            int middle = first + (end - first) / 2;
            if (cues[middle].Seconds <= seconds) first = middle + 1;
            else end = middle;
        }
        return first == 0 ? null : cues[first - 1].Text;
    }
}
