using System;
using System.Diagnostics;

namespace XadrezApp
{
    public class ClockController : IDisposable
    {
        private readonly System.Windows.Forms.Timer timer;
        private readonly Stopwatch stopwatch = new Stopwatch();

        public string Mode { get; private set; } = "Fischer 5+3";
        public double WhiteSecondsLeft { get; private set; } = 300;
        public double BlackSecondsLeft { get; private set; } = 300;
        public double IncrementSeconds { get; private set; } = 3;
        public bool IsBronsteinMode { get; private set; } = false;
        public bool IsRunning { get; private set; } = false;

        private double turnElapsedSeconds = 0;
        private readonly Func<bool> isWhiteTurnGetter;
        private readonly Func<bool> isPausedOrEndedGetter;

        public event Action? ClockUpdated;
        public event Action<bool>? TimeExpired; // true = tempo das Brancas acabou, false = Pretas

        public ClockController(Func<bool> isWhiteTurnGetter, Func<bool> isPausedOrEndedGetter)
        {
            this.isWhiteTurnGetter = isWhiteTurnGetter;
            this.isPausedOrEndedGetter = isPausedOrEndedGetter;

            timer = new System.Windows.Forms.Timer { Interval = 200 };
            timer.Tick += OnTimerTick;
            timer.Start();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (!IsRunning || Mode == "Sem Relógio" || isPausedOrEndedGetter())
            {
                stopwatch.Reset();
                return;
            }

            if (!stopwatch.IsRunning)
            {
                stopwatch.Restart();
                return;
            }

            double dt = stopwatch.Elapsed.TotalSeconds;
            if (dt <= 0) return;
            stopwatch.Restart();

            turnElapsedSeconds += dt;
            if (isWhiteTurnGetter())
            {
                WhiteSecondsLeft -= dt;
                if (WhiteSecondsLeft <= 0)
                {
                    WhiteSecondsLeft = 0;
                    Stop();
                    ClockUpdated?.Invoke();
                    TimeExpired?.Invoke(true);
                    return;
                }
            }
            else
            {
                BlackSecondsLeft -= dt;
                if (BlackSecondsLeft <= 0)
                {
                    BlackSecondsLeft = 0;
                    Stop();
                    ClockUpdated?.Invoke();
                    TimeExpired?.Invoke(false);
                    return;
                }
            }

            ClockUpdated?.Invoke();
        }

        public void ConfigureMode(string mode, bool resetRemainingTime = true)
        {
            Mode = mode;
            IsBronsteinMode = mode.StartsWith("Bronstein", StringComparison.OrdinalIgnoreCase);

            double initialSeconds = mode switch
            {
                "Fischer 3+2" => 180,
                "Fischer 10+0" => 600,
                _ => 300
            };
            IncrementSeconds = mode switch
            {
                "Fischer 3+2" => 2,
                "Fischer 10+0" => 0,
                _ => 3
            };

            if (resetRemainingTime || WhiteSecondsLeft <= 0 || BlackSecondsLeft <= 0)
            {
                WhiteSecondsLeft = initialSeconds;
                BlackSecondsLeft = initialSeconds;
                turnElapsedSeconds = 0;
                IsRunning = false;
                stopwatch.Reset();
            }
            else if (mode == "Sem Relógio")
            {
                stopwatch.Reset();
            }
            else if (IsRunning && !isPausedOrEndedGetter())
            {
                stopwatch.Restart();
            }

            ClockUpdated?.Invoke();
        }

        public void Start()
        {
            if (Mode == "Sem Relógio") return;
            IsRunning = true;
            stopwatch.Restart();
            ClockUpdated?.Invoke();
        }

        public void Stop()
        {
            IsRunning = false;
            turnElapsedSeconds = 0;
            stopwatch.Reset();
            ClockUpdated?.Invoke();
        }

        public void ApplyIncrement(bool forWhite)
        {
            if (IsRunning && stopwatch.IsRunning && Mode != "Sem Relógio")
            {
                double dt = stopwatch.Elapsed.TotalSeconds;
                turnElapsedSeconds += dt;
                if (forWhite) WhiteSecondsLeft = Math.Max(0, WhiteSecondsLeft - dt);
                else BlackSecondsLeft = Math.Max(0, BlackSecondsLeft - dt);
            }

            IsRunning = true;

            if (Mode == "Sem Relógio")
            {
                turnElapsedSeconds = 0;
                stopwatch.Reset();
                ClockUpdated?.Invoke();
                return;
            }

            double bonus = IsBronsteinMode ? Math.Min(turnElapsedSeconds, IncrementSeconds) : IncrementSeconds;
            if (forWhite) WhiteSecondsLeft += bonus;
            else BlackSecondsLeft += bonus;

            turnElapsedSeconds = 0;
            stopwatch.Restart();
            ClockUpdated?.Invoke();
        }

        public (string whiteText, string blackText) FormatClocks(bool english, bool isPuzzleMode)
        {
            string wLabel = english ? "White" : "Brancas";
            string bLabel = english ? "Black" : "Pretas";

            if (Mode == "Sem Relógio" || isPuzzleMode)
                return ($"⏱ {wLabel}: --:--", $"⏱ {bLabel}: --:--");

            TimeSpan w = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, WhiteSecondsLeft)));
            TimeSpan b = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, BlackSecondsLeft)));
            return ($"⏱ {wLabel}: {(int)w.TotalMinutes:D2}:{w.Seconds:D2}",
                    $"⏱ {bLabel}: {(int)b.TotalMinutes:D2}:{b.Seconds:D2}");
        }

        public void Dispose()
        {
            try
            {
                timer.Stop();
                timer.Dispose();
            }
            catch { }
        }
    }
}