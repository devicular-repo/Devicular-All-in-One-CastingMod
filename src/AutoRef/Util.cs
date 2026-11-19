using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AutoRef
{
    internal sealed class EventLog
    {
        internal struct Entry
        {
            public string Time;
            public string Text;
            public Color Color;
        }

        public readonly List<Entry> Entries = new List<Entry>();

        public void Add(string time, string text, Color color)
        {
            Entries.Add(new Entry { Time = time, Text = text, Color = color });
            if (Entries.Count > 500) Entries.RemoveAt(0);
            Plugin.Log?.LogInfo($"[{time}] {text}");
        }

        public void Clear() => Entries.Clear();
    }
}
