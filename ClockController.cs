using System;
using System.Diagnostics;

namespace XadrezApp
{
    public class ClockController : IDisposable
    {
        private readonly System.Windows.Forms.Timer clockTimer;
        private readonly Stopwatch clockStopwatch = new Stopwatch();
        private readonly Func<bool> isWhiteTurnGetter;
        private readonly Func<bool> isGameOverGetter;
        private readonly Func<bool> isPuzzleActiveGetter;

        public bool ClockRunning { get; private set; } = false;
        public string TimeControlMode { get; private set; } = "Fischer 5+3";
        public double WhiteSecondsLeft { get; private set; } = 300;
        public double BlackSecondsLeft { get; private set; } = 300;
        public double IncrementSeconds { get; private set; } = 3;
        public bool IsBronsteinMode { get; private set; } = false;
        public double TurnElapsedSeconds { get; private set; } = 0;

        public event Action? ClockUpdated;
        public event Action<bool>? TimeExpired; // true = Brancas estouraram o tempo, false = Pretas

        public ClockController(
            Func<bool> isWhiteTurnGetter,
            Func<bool> isGameOverGetter,
            Func<bool> isPuzzleActiveGetter)
        {
            this.isWhiteTurnGetter = isWhiteTurnGetter;
            this.isGameOverGetter = isGameOverGetter;
            this.isPuzzleActiveGetter = isPuzzleActiveGetter;

            // Intervalo curto + Stopwatch de alta precisão: não atrasa se a UI travar
            // e só começa a descontar depois que o 1º lance é efetuado (ClockRunning = true).
            clockTimer = new System.Windows.Forms.Timer { Interval = 200 };
            clockTimer.Tick += OnClockTimerTick;
            clockTimer.Start();
        }

        private void OnClockTimerTick(object? sender, EventArgs e)
        {
            if (isGameOverGetter() || !ClockRunning || TimeControlMode == "Sem Relógio" || isPuzzleActiveGetter())
            {
                clockStopwatch.Reset();
                return;
            }

            if (!clockStopwatch.IsRunning)
            {
                clockStopwatch.Restart();
                return;
            }

            double dt = clockStopwatch.Elapsed.TotalSeconds;
            if (dt <= 0) return;
            clockStopwatch.Restart();

            TurnElapsedSeconds += dt;
            if (isWhiteTurnGetter())
            {
                WhiteSecondsLeft -= dt;
                if (WhiteSecondsLeft <= 0)
                {
                    WhiteSecondsLeft = 0;
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
                    ClockUpdated?.Invoke();
                    TimeExpired?.Invoke(false);
                    return;
                }
            }

            ClockUpdated?.Invoke();
        }

        public void ConfigureClockMode(string mode, bool resetRemainingTime = true)
        {
            TimeControlMode = mode;
            IsBronsteinMode = mode.StartsWith("Bronstein");

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

            // Só reinicia o tempo dos dois jogadores se a partida não estiver em andamento
            // (ou se ainda não havia tempo configurado ao sair de "Sem Relógio").
            if (resetRemainingTime || WhiteSecondsLeft <= 0 || BlackSecondsLeft <= 0)
            {
                WhiteSecondsLeft = initialSeconds;
                BlackSecondsLeft = initialSeconds;
                TurnElapsedSeconds = 0;
                ClockRunning = false;
                clockStopwatch.Reset();
            }
            else if (mode == "Sem Relógio")
            {
                clockStopwatch.Reset();
            }
            else if (ClockRunning && !isGameOverGetter() && !isPuzzleActiveGetter())
            {
                clockStopwatch.Restart();
            }

            ClockUpdated?.Invoke();
        }

        public void ApplyClockIncrement(bool forWhite)
        {
            if (isPuzzleActiveGetter()) return;

            // Desconta a fração de segundo decorrida desde o último tick antes de aplicar o bônus
            if (ClockRunning && clockStopwatch.IsRunning && TimeControlMode != "Sem Relógio")
            {
                double dt = clockStopwatch.Elapsed.TotalSeconds;
                TurnElapsedSeconds += dt;
                if (forWhite) WhiteSecondsLeft = Math.Max(0, WhiteSecondsLeft - dt);
                else BlackSecondsLeft = Math.Max(0, BlackSecondsLeft - dt);
            }

            // O relógio passa a correr assim que o 1º lance é feito
            ClockRunning = true;

            if (TimeControlMode == "Sem Relógio")
            {
                TurnElapsedSeconds = 0;
                clockStopwatch.Reset();
                ClockUpdated?.Invoke();
                return;
            }

            double bonus = IsBronsteinMode ? Math.Min(TurnElapsedSeconds, IncrementSeconds) : IncrementSeconds;
            if (forWhite) WhiteSecondsLeft += bonus;
            else BlackSecondsLeft += bonus;
            TurnElapsedSeconds = 0;
            clockStopwatch.Restart();
            ClockUpdated?.Invoke();
        }

        // Usado quando uma posição FEN é carregada com as Pretas a jogar
        public void StartIfEnabled()
        {
            if (TimeControlMode != "Sem Relógio" && !isPuzzleActiveGetter())
            {
                ClockRunning = true;
                clockStopwatch.Restart();
            }
        }

        public void StopAndResetStopwatch()
        {
            ClockRunning = false;
            clockStopwatch.Reset();
        }

        public (string whiteText, string blackText) GetFormattedLabels()
        {
            if (TimeControlMode == "Sem Relógio" || isPuzzleActiveGetter())
                return ("Brancas: --:--", "Pretas: --:--");

            TimeSpan w = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, WhiteSecondsLeft)));
            TimeSpan b = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, BlackSecondsLeft)));
            return ($"Brancas: {(int)w.TotalMinutes:D2}:{w.Seconds:D2}",
                    $"Pretas: {(int)b.TotalMinutes:D2}:{b.Seconds:D2}");
        }

        public void Dispose()
        {
            try
            {
                clockTimer.Stop();
                clockTimer.Dispose();
            }
            catch { }
        }
    }
}