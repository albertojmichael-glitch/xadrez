using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace XadrezApp
{
    public class EngineAnalysisResult
    {
        public (Point from, Point to)? BestMove { get; set; }
        public double EvalWhitePawns { get; set; }
        public string EvalText { get; set; } = "0.00";
        public long Nodes { get; set; }
        public string LogLine { get; set; } = "";
    }

    public class EngineService
    {
        private Process? stockfishProcess;

        public EngineService()
        {
            InitStockfish();
        }

        private void InitStockfish()
        {
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stockfish.exe");
            if (!File.Exists(exePath)) return;

            try
            {
                stockfishProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                stockfishProcess.Start();
                stockfishProcess.StandardInput.WriteLine("uci");
            }
            catch
            {
                stockfishProcess = null;
            }
        }

        public void StopSearch()
        {
            try { stockfishProcess?.StandardInput.WriteLine("stop"); } catch { }
        }

        // Classifica o lance do jogador comparando a avaliação antes e depois do lance
        public static (string badge, string description, Color color) ClassifyMove(double evalBeforeWhite, double evalAfterWhite, bool wasBestMove)
        {
            double diff = evalAfterWhite - evalBeforeWhite;

            if (wasBestMove && diff >= 0.80)
                return ("!!", "Brilhante", Color.FromArgb(20, 160, 170));
            if (wasBestMove || diff >= -0.15)
                return ("!", "Bom Lance", Color.FromArgb(40, 150, 40));
            if (diff >= -0.55)
                return ("", "Normal", Color.Black);
            if (diff >= -1.10)
                return ("?!", "Imprecisão", Color.FromArgb(200, 140, 0));
            if (diff >= -2.20)
                return ("?", "Erro", Color.FromArgb(215, 90, 20));

            return ("??", "Erro Grave (Capivarada)", Color.FromArgb(200, 20, 20));
        }

        public EngineAnalysisResult Analyze(ChessRules rules, bool forWhite, int skill, int depth)
        {
            string fen = rules.ToFen();

            if (stockfishProcess != null && !stockfishProcess.HasExited)
            {
                try
                {
                    var sw = stockfishProcess.StandardInput;
                    var sr = stockfishProcess.StandardOutput;

                    sw.WriteLine($"setoption name Skill Level value {skill}");
                    sw.WriteLine($"position fen {fen}");
                    sw.WriteLine($"go depth {depth}");

                    string lastInfoLine = "";
                    long nodes = 0;
                    double whitePawns = 0.0;
                    string evalStr = "0.00";
                    string? line;

                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.StartsWith("info") && line.Contains(" pv "))
                        {
                            lastInfoLine = line;
                            var parts = line.Split(' ');
                            for (int i = 0; i < parts.Length - 1; i++)
                            {
                                if (parts[i] == "nodes") long.TryParse(parts[i + 1], out nodes);
                                if (parts[i] == "cp" && int.TryParse(parts[i + 1], out int cp))
                                {
                                    whitePawns = (forWhite ? cp : -cp) / 100.0;
                                    evalStr = (whitePawns >= 0 ? "+" : "") + whitePawns.ToString("0.00");
                                }
                                else if (parts[i] == "mate" && int.TryParse(parts[i + 1], out int mateIn))
                                {
                                    int m = forWhite ? mateIn : -mateIn;
                                    whitePawns = m > 0 ? 99.0 : -99.0;
                                    evalStr = $"#{m}";
                                }
                            }
                        }
                        else if (line.StartsWith("bestmove"))
                        {
                            string[] parts = line.Split(' ');
                            if (parts.Length >= 2 && parts[1].Length >= 4 && parts[1] != "(none)")
                            {
                                string mv = parts[1];
                                int fCol = mv[0] - 'a';
                                int fRow = 8 - (mv[1] - '0');
                                int tCol = mv[2] - 'a';
                                int tRow = 8 - (mv[3] - '0');
                                return new EngineAnalysisResult
                                {
                                    BestMove = (new Point(fCol, fRow), new Point(tCol, tRow)),
                                    EvalWhitePawns = whitePawns,
                                    EvalText = evalStr,
                                    Nodes = nodes,
                                    LogLine = $"{depth} {mv} ({evalStr}) | {lastInfoLine}"
                                };
                            }
                            break;
                        }
                    }
                }
                catch { }
            }

            // IA Interna de Fallback (Minimax Alpha-Beta)
            long searchedNodes = 0;
            int searchDepth = Math.Min(depth, 3);
            var moves = rules.GetAllLegalMoves(forWhite);
            if (moves.Count == 0)
                return new EngineAnalysisResult { BestMove = null, EvalWhitePawns = 0, EvalText = "0.00" };

            var bestMove = moves[0];
            int bestVal = forWhite ? int.MinValue : int.MaxValue;

            foreach (var mv in moves)
            {
                char[,] copy = (char[,])rules.Board.Clone();
                copy[mv.to.Y, mv.to.X] = copy[mv.from.Y, mv.from.X];
                copy[mv.from.Y, mv.from.X] = '.';

                int val = MinimaxWhiteRelative(rules, copy, searchDepth - 1, int.MinValue, int.MaxValue, !forWhite, ref searchedNodes);
                if (forWhite && val > bestVal) { bestVal = val; bestMove = mv; }
                else if (!forWhite && val < bestVal) { bestVal = val; bestMove = mv; }
            }

            double evalPawns = bestVal / 100.0;
            string formattedEval = (evalPawns >= 0 ? "+" : "") + evalPawns.ToString("0.00");
            string log = $"{searchDepth} {ChessRules.SquareToAlgebraic(bestMove.from)}-{ChessRules.SquareToAlgebraic(bestMove.to)} [{formattedEval}] nodes:{searchedNodes}";

            return new EngineAnalysisResult
            {
                BestMove = bestMove,
                EvalWhitePawns = evalPawns,
                EvalText = formattedEval,
                Nodes = searchedNodes,
                LogLine = log
            };
        }

        private int MinimaxWhiteRelative(ChessRules rules, char[,] b, int depth, int alpha, int beta, bool whiteToMove, ref long nodes)
        {
            nodes++;
            if (depth <= 0) return EvaluateBoardWhiteRelative(b);

            int best = whiteToMove ? int.MinValue : int.MaxValue;
            bool anyMove = false;

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = b[r, c];
                    if (p != '.' && char.IsUpper(p) == whiteToMove)
                    {
                        foreach (var to in rules.GetPseudoMoves(b, c, r, whiteToMove, false))
                        {
                            anyMove = true;
                            char[,] copy = (char[,])b.Clone();
                            copy[to.Y, to.X] = copy[r, c];
                            copy[r, c] = '.';

                            int eval = MinimaxWhiteRelative(rules, copy, depth - 1, alpha, beta, !whiteToMove, ref nodes);
                            if (whiteToMove)
                            {
                                best = Math.Max(best, eval);
                                alpha = Math.Max(alpha, eval);
                            }
                            else
                            {
                                best = Math.Min(best, eval);
                                beta = Math.Min(beta, eval);
                            }
                            if (beta <= alpha) return best;
                        }
                    }
                }
            }

            return anyMove ? best : EvaluateBoardWhiteRelative(b);
        }

        public static int EvaluateBoardWhiteRelative(char[,] b)
        {
            int score = 0;
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = b[r, c];
                    if (p == '.') continue;
                    int val = char.ToLower(p) switch
                    {
                        'p' => 100,
                        'n' => 320,
                        'b' => 330,
                        'r' => 500,
                        'q' => 900,
                        'k' => 20000,
                        _ => 0
                    };
                    score += char.IsUpper(p) ? val : -val;
                }
            }
            return score;
        }
    }
}