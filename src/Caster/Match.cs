using System;
using UnityEngine;

namespace DevicularCaster
{
    internal sealed class MatchClock
    {
        public string NameA = "TEAM A";
        public string NameB = "TEAM B";
        public int PointsA, PointsB;
        public double Time = -10;
        public double Lap = double.NaN;
        public double Last = double.NaN;
        public bool Running;
        public float HeadStart = 10f;
        public float Cap = 180f;
        public float Margin = 2f;
        public bool UseReferee = true;

        public void Start() => Running = true;

        public void Toggle() => Running = !Running;

        public void Reset()
        {
            if (Time > 0) Last = Time;
            Running = false;
            Time = -HeadStart;
        }

        public void SetLap() => Lap = Time;

        public void ClearLap() => Lap = double.NaN;

        public void ResetPoints() => PointsA = PointsB = 0;

        public void Tick(float dt)
        {
            if (!Running) return;
            Time += dt;
            if (Time >= Cap)
            {
                Time = Cap;
                Running = false;
            }
        }

        public ScoreView Manual()
        {
            return new ScoreView
            {
                NameA = NameA,
                NameB = NameB,
                PointsA = PointsA,
                PointsB = PointsB,
                Time = Time,
                Lap = Lap,
                Last = Last,
                Running = Running,
                Paused = !Running && Time > -HeadStart && Time < Cap,
                HeadStart = Time < 0,
                Finished = Time >= Cap,
                Margin = Margin
            };
        }
    }
}
