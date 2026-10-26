using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;

namespace XadrezApp
{
    public class EngineAnalysisResult
    {
        public (Point from, Point to)? BestMove { get; set; }
        public double EvalWhitePawns { get; set; }
        public string EvalText { get; set; } = "+0.00";
        public long Nodes { get; set; }
        public string LogLine { get; set; } = "";
    }

    public class EngineService
    {
        private Process? stockfishProcess;
        private readonly Random rng = new Random();
        public bool IsUsingStockfish => stockfishProcess != null && !stockfishProcess.HasExited;

        // Tabelas Posicionais (Piece-Square Tables) para ensinar estratégia, domínio do centro e evitar Rb8-Ra8!
        private static readonly int[,] PawnTable = {
            {  0,  0,  0,  0,  0,  0,  0,  0 },
            { 50, 50, 50, 50, 50, 50, 50, 50 },
            { 10, 10, 20, 30, 30, 20, 10, 10 },
            {  5,  5, 10, 28, 28, 10,  5,  5 },
            {  0,  0,  0, 24, 24,  0,  0,  0 },
            {  5, -5,-10,  0,  0,-10, -5,  5 },
            {  5, 10, 10,-24,-24, 10, 10,  5 },
            {  0,  0,  0,  0,  0,  0,  0,  0 }
        };

        private static readonly int[,] KnightTable = {
            {-50,-40,-30,-30,-30,-30,-40,-50 },
            {-40,-20,  0,  0,  0,  0,-20,-40 },
            {-30,  0, 15, 20, 20, 15,  0,-30 },
            {-30,  5, 20, 25, 25, 20,  5,-30 },
            {-30,  0, 20, 25, 25, 20,  0,-30 },
            {-30,  5, 15, 20, 20, 15,  5,-30 },
            {-40,-20,  0,  5,  5,  0,-20,-40 },
            {-50,-40,-30,-30,-30,-30,-40,-50 }
        };

        private static readonly int[,] BishopTable = {
            {-20,-10,-10,-10,-10,-10,-10,-20 },
            {-10,  0,  0,  0,  0,  0,  0,-10 },
            {-10,  0, 10, 15, 15, 10,  0,-10 },
            {-10, 10, 10, 15, 15, 10, 10,-10 },
            {-10,  0, 15, 15, 15, 15,  0,-10 },
            {-10, 15, 15, 15, 15, 15, 15,-10 },
            {-10, 10,  0,  0,  0,  0, 10,-10 },
            {-20,-10,-10,-10,-10,-10,-10,-20 }
        };

        private static readonly int[,] RookTable = {
            {  0,  0,  0,  5,  5,  0,  0,  0 },
            {  5, 15, 15, 15, 15, 15, 15,  5 },
            { -5,  0,  0,  0,  0,  0,  0, -5 },
            { -5,  0,  0,  0,  0,  0,  0, -5 },
            { -5,  0,  0,  0,  0,  0,  0, -5 },
            { -5,  0,  0,  0,  0,  0,  0, -5 },
            { -5,  0,  0,  0,  0,  0,  0, -5 },
            { -5,  0,  5, 10, 10,  5,  0, -5 } // Penaliza a1/h1 e incentiva d1/e1/c1/f1!
        };

        public EngineService()
        {
            InitStockfish();
        }

        private void InitStockfish()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "stockfish.exe"),
                Path.Combine(baseDir, "assets", "stockfish.exe")
            };

            string? exePath = candidates.FirstOrDefault(File.Exists);
            if (exePath == null) return;

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
                stockfishProcess.StandardInput.WriteLine("isready");
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

        // Classificação precisa comparando o lance do jogador com o melhor lance da Engine
        public static (string badge, string description, Color color) ClassifyMove(
            double evalBeforeWhite,
            double evalAfterWhite,
            bool playedEngineBestMove)
        {
            double delta = evalAfterWhite - evalBeforeWhite;

            if (playedEngineBestMove && delta >= 0.40)
                return ("!!", "Brilhante!", Color.FromArgb(20, 160, 170));
            if (playedEngineBestMove || delta >= -0.15)
                return ("!", "Melhor Lance", Color.FromArgb(30, 145, 30));
            if (delta >= -0.45)
                return ("", "Lance Bom", Color.FromArgb(60, 110, 60));
            if (delta >= -1.00)
                return ("?!", "Imprecisão", Color.FromArgb(200, 140, 0));
            if (delta >= -2.10)
                return ("?", "Erro", Color.FromArgb(215, 90, 20));

            return ("??", "Capivarada (Blunder)", Color.FromArgb(200, 20, 20));
        }

        // Método para pedir a melhor jogada (para Ajuda de Lance ou para o Bot jogar)
        public EngineAnalysisResult Analyze(ChessRules rules, bool forWhite, int difficultyLevel, bool isHintOrEvaluationOnly = false)
        {
            // Se for Ajuda de Lance ou Avaliação pura, usa força máxima!
            int skill = isHintOrEvaluationOnly ? 20 : difficultyLevel switch { 0 => 0, 1 => 6, 2 => 13, _ => 20 };
            int sfDepth = isHintOrEvaluationOnly ? 12 : difficultyLevel switch { 0 => 1, 1 => 5, 2 => 10, _ => 15 };

            if (IsUsingStockfish)
            {
                var sfRes = QueryStockfish(rules, forWhite, skill, sfDepth, !isHintOrEvaluationOnly && difficultyLevel == 0);
                if (sfRes != null) return sfRes;
            }

            // Motor Interno Posicional + Tático (nunca repete Rb8-Ra8 e joga estrategicamente)
            return QueryInternalEngine(rules, forWhite, difficultyLevel, isHintOrEvaluationOnly);
        }

        private EngineAnalysisResult? QueryStockfish(ChessRules rules, bool forWhite, int skill, int depth, bool isEasyBotMode)
        {
            try
            {
                string fen = rules.ToFen();
                var sw = stockfishProcess!.StandardInput;
                var sr = stockfishProcess.StandardOutput;

                if (isEasyBotMode)
                {
                    sw.WriteLine("setoption name UCI_LimitStrength value true");
                    sw.WriteLine("setoption name UCI_Elo value 800");
                }
                else
                {
                    sw.WriteLine("setoption name UCI_LimitStrength value false");
                }
                sw.WriteLine($"setoption name Skill Level value {skill}");
                sw.WriteLine($"position fen {fen}");
                sw.WriteLine($"go depth {depth}");

                string lastInfoLine = "";
                long nodes = 0;
                double whitePawns = 0.0;
                string evalStr = "+0.00";
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
                                evalStr = (whitePawns >= 0 ? "+" : "") + whitePawns.ToString("0.00", CultureInfo.InvariantCulture);
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
                            var chosenMove = (new Point(fCol, fRow), new Point(tCol, tRow));

                            // No modo Fácil, evita que mesmo o Stockfish jogue lances perfeitos demais
                            if (isEasyBotMode && rng.NextDouble() < 0.45)
                            {
                                var legal = rules.GetAllLegalMoves(forWhite);
                                if (legal.Count > 1)
                                    chosenMove = legal[rng.Next(legal.Count)];
                            }

                            return new EngineAnalysisResult
                            {
                                BestMove = chosenMove,
                                EvalWhitePawns = whitePawns,
                                EvalText = evalStr,
                                Nodes = nodes,
                                LogLine = $"[Stockfish d={depth}] {mv} ({evalStr}) | {lastInfoLine}"
                            };
                        }
                        break;
                    }
                }
            }
            catch { }
            return null;
        }

        private EngineAnalysisResult QueryInternalEngine(ChessRules rules, bool forWhite, int difficultyLevel, bool isHintOrEvaluationOnly)
        {
            var legalMoves = rules.GetAllLegalMoves(forWhite);
            if (legalMoves.Count == 0)
                return new EngineAnalysisResult { BestMove = null, EvalWhitePawns = 0, EvalText = "0.00" };

            // Embaralha os lances para nunca viciar no canto superior esquerdo (fim do Rb8-Ra8!)
            legalMoves = legalMoves.OrderBy(_ => rng.Next()).ToList();

            // Profundidade sempre PAR na busca principal + busca de capturas para nunca entregar peça por efeito horizonte!
            int searchDepth = isHintOrEvaluationOnly ? 4 : difficultyLevel switch
            {
                0 => 2, // Fácil
                1 => 2, // Médio
                2 => 4, // Difícil
                _ => 4  // Mestre
            };

            long searchedNodes = 0;
            var scoredMoves = new List<((Point from, Point to) move, int whiteScore)>();

            int alpha = -1000000;
            int beta = 1000000;

            foreach (var mv in legalMoves)
            {
                char[,] copy = ApplyMoveOnCopy(rules.Board, mv.from, mv.to);

                // Penalidade forte se o lance repetir uma posição que já apareceu na partida
                int repetitionPenalty = 0;
                string nextBoardHash = QuickBoardKey(copy);
                if (rules.PositionHistory.Any(h => h.StartsWith(nextBoardHash)))
                    repetitionPenalty = forWhite ? -65 : 65;

                int val = SearchAlphaBeta(rules, copy, searchDepth - 1, alpha, beta, !forWhite, ref searchedNodes) + repetitionPenalty;
                scoredMoves.Add((mv, val));

                if (forWhite) alpha = Math.Max(alpha, val);
                else beta = Math.Min(beta, val);
            }

            // Ordena do melhor para o pior sob a ótica de quem está jogando
            if (forWhite)
                scoredMoves = scoredMoves.OrderByDescending(m => m.whiteScore).ToList();
            else
                scoredMoves = scoredMoves.OrderBy(m => m.whiteScore).ToList();

            var topMove = scoredMoves[0];
            var selectedMove = topMove.move;

            // Se for o Bot no Modo Fácil (0), escolhe entre os lances intermediários para ser realmente fácil de vencer!
            if (!isHintOrEvaluationOnly && difficultyLevel == 0 && scoredMoves.Count > 2)
            {
                int maxIdx = Math.Min(scoredMoves.Count - 1, 5);
                int pickedIdx = rng.Next(1, maxIdx + 1);
                selectedMove = scoredMoves[pickedIdx].move;
            }
            else if (!isHintOrEvaluationOnly && difficultyLevel == 1 && scoredMoves.Count > 1 && rng.NextDouble() < 0.25)
            {
                selectedMove = scoredMoves[1].move;
            }

            double evalPawns = topMove.whiteScore / 100.0;
            string formattedEval = (evalPawns >= 0 ? "+" : "") + evalPawns.ToString("0.00", CultureInfo.InvariantCulture);
            string moveAlg = $"{ChessRules.SquareToAlgebraic(selectedMove.from)}-{ChessRules.SquareToAlgebraic(selectedMove.to)}";
            string log = $"[Engine d={searchDepth}] Best:{moveAlg} Eval:{formattedEval} Nodes:{searchedNodes}";

            return new EngineAnalysisResult
            {
                BestMove = selectedMove,
                EvalWhitePawns = evalPawns,
                EvalText = formattedEval,
                Nodes = searchedNodes,
                LogLine = log
            };
        }

        private int SearchAlphaBeta(ChessRules rules, char[,] b, int depth, int alpha, int beta, bool whiteToMove, ref long nodes)
        {
            nodes++;
            if (depth <= 0)
                return QuiescenceCaptures(rules, b, alpha, beta, whiteToMove, 2, ref nodes);

            var moves = GeneratePseudoLegalOrdered(rules, b, whiteToMove, false);
            if (moves.Count == 0) return EvaluateBoardWhiteRelative(b);

            if (whiteToMove)
            {
                int maxEval = -999999;
                foreach (var mv in moves)
                {
                    // Se capturar o Rei inimigo, é mate/vitória imediata
                    if (b[mv.to.Y, mv.to.X] == 'k') return 50000 + depth * 100;

                    char[,] copy = ApplyMoveOnCopy(b, mv.from, mv.to);
                    int eval = SearchAlphaBeta(rules, copy, depth - 1, alpha, beta, false, ref nodes);
                    if (eval > maxEval) maxEval = eval;
                    if (eval > alpha) alpha = eval;
                    if (beta <= alpha) break;
                }
                return maxEval;
            }
            else
            {
                int minEval = 999999;
                foreach (var mv in moves)
                {
                    if (b[mv.to.Y, mv.to.X] == 'K') return -50000 - depth * 100;

                    char[,] copy = ApplyMoveOnCopy(b, mv.from, mv.to);
                    int eval = SearchAlphaBeta(rules, copy, depth - 1, alpha, beta, true, ref nodes);
                    if (eval < minEval) minEval = eval;
                    if (eval < beta) beta = eval;
                    if (beta <= alpha) break;
                }
                return minEval;
            }
        }

        // Busca de Quiescência: continua calculando recapturas para a Ajuda de Lance NUNCA entregar uma peça!
        private int QuiescenceCaptures(ChessRules rules, char[,] b, int alpha, int beta, bool whiteToMove, int qDepth, ref long nodes)
        {
            nodes++;
            int standPat = EvaluateBoardWhiteRelative(b);
            if (qDepth <= 0) return standPat;

            if (whiteToMove)
            {
                if (standPat >= beta) return beta;
                if (alpha < standPat) alpha = standPat;

                var captures = GeneratePseudoLegalOrdered(rules, b, true, true);
                foreach (var cap in captures)
                {
                    if (b[cap.to.Y, cap.to.X] == 'k') return 50000;
                    char[,] copy = ApplyMoveOnCopy(b, cap.from, cap.to);
                    int score = QuiescenceCaptures(rules, copy, alpha, beta, false, qDepth - 1, ref nodes);
                    if (score >= beta) return beta;
                    if (score > alpha) alpha = score;
                }
                return alpha;
            }
            else
            {
                if (standPat <= alpha) return alpha;
                if (beta > standPat) beta = standPat;

                var captures = GeneratePseudoLegalOrdered(rules, b, false, true);
                foreach (var cap in captures)
                {
                    if (b[cap.to.Y, cap.to.X] == 'K') return -50000;
                    char[,] copy = ApplyMoveOnCopy(b, cap.from, cap.to);
                    int score = QuiescenceCaptures(rules, copy, alpha, beta, true, qDepth - 1, ref nodes);
                    if (score <= alpha) return alpha;
                    if (score < beta) beta = score;
                }
                return beta;
            }
        }

        private List<(Point from, Point to)> GeneratePseudoLegalOrdered(ChessRules rules, char[,] b, bool forWhite, bool capturesOnly)
        {
            var list = new List<((Point from, Point to) mv, int priority)>();
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = b[r, c];
                    if (p == '.' || char.IsUpper(p) != forWhite) continue;

                    foreach (var to in rules.GetPseudoMoves(b, c, r, forWhite, false))
                    {
                        char target = b[to.Y, to.X];
                        if (capturesOnly && target == '.') continue;

                        int prio = target != '.' ? PieceBaseValue(target) * 10 - PieceBaseValue(p) : 0;
                        list.Add(((new Point(c, r), to), prio));
                    }
                }
            }
            return list.OrderByDescending(x => x.priority).Select(x => x.mv).ToList();
        }

        private static char[,] ApplyMoveOnCopy(char[,] src, Point from, Point to)
        {
            char[,] copy = (char[,])src.Clone();
            char piece = copy[from.Y, from.X];
            copy[to.Y, to.X] = piece;
            copy[from.Y, from.X] = '.';
            if (piece == 'P' && to.Y == 0) copy[to.Y, to.X] = 'Q';
            if (piece == 'p' && to.Y == 7) copy[to.Y, to.X] = 'q';
            return copy;
        }

        private static string QuickBoardKey(char[,] b)
        {
            var rows = new string[8];
            for (int r = 0; r < 8; r++)
            {
                int empty = 0;
                string s = "";
                for (int c = 0; c < 8; c++)
                {
                    if (b[r, c] == '.') empty++;
                    else
                    {
                        if (empty > 0) { s += empty; empty = 0; }
                        s += b[r, c];
                    }
                }
                if (empty > 0) s += empty;
                rows[r] = s;
            }
            return string.Join("/", rows);
        }

        private static int PieceBaseValue(char p) => char.ToLower(p) switch
        {
            'p' => 100,
            'n' => 320,
            'b' => 330,
            'r' => 500,
            'q' => 900,
            'k' => 20000,
            _ => 0
        };

        // Avaliação Posicional Completa (Material + Tabelas Posicionais de Centro/Desenvolvimento)
        public static int EvaluateBoardWhiteRelative(char[,] b)
        {
            int score = 0;
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = b[r, c];
                    if (p == '.') continue;

                    bool isWhite = char.IsUpper(p);
                    int tableRow = isWhite ? r : (7 - r);
                    int baseVal = PieceBaseValue(p);

                    int posBonus = char.ToLower(p) switch
                    {
                        'p' => PawnTable[tableRow, c],
                        'n' => KnightTable[tableRow, c],
                        'b' => BishopTable[tableRow, c],
                        'r' => RookTable[tableRow, c],
                        'q' => (BishopTable[tableRow, c] + RookTable[tableRow, c]) / 2,
                        _ => 0
                    };

                    int total = baseVal + posBonus;
                    score += isWhite ? total : -total;
                }
            }
            return score;
        }
    }
}