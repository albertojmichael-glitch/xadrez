using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace XadrezApp
{
    public class EngineAnalysisResult
    {
        public (Point from, Point to)? BestMove { get; set; }
        public char? Promotion { get; set; }
        public double EvalWhitePawns { get; set; }
        public string EvalText { get; set; } = "+0.00";
        public int? MateInMoves { get; set; }
        public long Nodes { get; set; }
        public string LogLine { get; set; } = "";
        public string PrincipalVariation { get; set; } = "";
        public List<string> TopMovesSummary { get; set; } = new List<string>();
        public bool WasCancelled { get; set; }
    }

    public class EngineService : IDisposable
    {
        private Process? stockfishProcess;
        private bool disposed = false;

        // Random.Shared é thread-safe (o Random comum não é, e a engine roda em Task.Run)
        private static Random rng => Random.Shared;

        // Garante que só UMA consulta conversa com o processo do Stockfish por vez
        private readonly SemaphoreSlim stockfishLock = new SemaphoreSlim(1, 1);

        // Protege as escritas no stdin e o CancellationTokenSource ativo
        private readonly object stdinLock = new object();
        private CancellationTokenSource? activeSearchCts;

        public bool IsUsingStockfish => stockfishProcess != null && !stockfishProcess.HasExited;

        private readonly record struct SearchMove(Point From, Point To, char? Promotion = null);

        // Tabelas Posicionais (Piece-Square Tables) para ensinar estratégia, domínio do centro e segurança do Rei
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
            { -5,  0,  5, 10, 10,  5,  0, -5 }
        };

        private static readonly int[,] KingTable = {
            {-30,-40,-40,-50,-50,-40,-40,-30 },
            {-30,-40,-40,-50,-50,-40,-40,-30 },
            {-30,-40,-40,-50,-50,-40,-40,-30 },
            {-30,-40,-40,-50,-50,-40,-40,-30 },
            {-20,-30,-30,-40,-40,-30,-30,-20 },
            {-10,-20,-20,-20,-20,-20,-20,-10 },
            { 20, 20,  0,  0,  0,  0, 20, 20 },
            { 20, 35, 25, -5,  0, -5, 35, 20 }
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
                stockfishProcess.StandardInput.Flush();

                bool uciOk = false;
                string? line;
                while ((line = stockfishProcess.StandardOutput.ReadLine()) != null)
                {
                    if (line.Trim() == "uciok")
                    {
                        uciOk = true;
                        break;
                    }
                }

                if (!uciOk)
                {
                    ShutdownStockfishProcess();
                    return;
                }

                stockfishProcess.StandardInput.WriteLine("isready");
                stockfishProcess.StandardInput.Flush();

                bool readyOk = false;
                while ((line = stockfishProcess.StandardOutput.ReadLine()) != null)
                {
                    if (line.Trim() == "readyok")
                    {
                        readyOk = true;
                        break;
                    }
                }

                if (!readyOk)
                {
                    ShutdownStockfishProcess();
                }
            }
            catch
            {
                ShutdownStockfishProcess();
            }
        }

        private void ShutdownStockfishProcess()
        {
            var proc = stockfishProcess;
            stockfishProcess = null;
            if (proc == null) return;

            try
            {
                if (!proc.HasExited)
                {
                    lock (stdinLock)
                    {
                        proc.StandardInput.WriteLine("stop");
                        proc.StandardInput.WriteLine("quit");
                        proc.StandardInput.Flush();
                    }
                    if (!proc.WaitForExit(500))
                        proc.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            }
            finally
            {
                try { proc.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            StopSearch();
            ShutdownStockfishProcess();
            GC.SuppressFinalize(this);
        }

        public void StopSearch()
        {
            try
            {
                lock (stdinLock)
                {
                    activeSearchCts?.Cancel();
                    stockfishProcess?.StandardInput.WriteLine("stop");
                    stockfishProcess?.StandardInput.Flush();
                }
            }
            catch { }
        }

        // Verifica se um lance entregou material de propósito (sacrifício real, não simples captura ou posição já ganha >= +3)
        public static bool IsMaterialSacrifice(
            ChessRules rulesBefore,
            Point from,
            Point to,
            char? promo,
            double evalBeforeWhite,
            double evalAfterWhite)
        {
            bool forWhite = rulesBefore.WhiteTurn;
            double evalBeforeMover = forWhite ? evalBeforeWhite : -evalBeforeWhite;
            double evalAfterMover = forWhite ? evalAfterWhite : -evalAfterWhite;

            // Posição não pode já estar ganha (+3.0 ou mais) e não pode ficar perdida após o lance
            if (evalBeforeMover >= 3.0 || evalAfterMover < -0.25)
                return false;

            // Promoção não é sacrifício de material
            if (promo.HasValue)
                return false;

            char movedPiece = rulesBefore.Board[from.Y, from.X];
            if (movedPiece == '.' || char.ToUpper(movedPiece) == 'K')
                return false;

            char capturedPiece = rulesBefore.Board[to.Y, to.X];
            int movedVal = PieceBaseValue(movedPiece);
            int capturedVal = PieceBaseValue(capturedPiece);

            // Se já capturou peça de valor igual ou maior, é troca ou ganho material, não sacrifício
            if (capturedVal >= movedVal)
                return false;

            var after = rulesBefore.Clone();
            after.ExecuteMove(from, to, promo);

            // Verifica se a peça movida foi colocada numa casa atacada pelo oponente
            int cheapestEnemyAttacker = GetCheapestAttackerValue(after, to, !forWhite);
            if (cheapestEnemyAttacker > 0)
            {
                bool isDefended = GetCheapestAttackerValue(after, to, forWhite) > 0;
                // Se entregou a peça sem defesa (ganhando menos do que entregou) ou para um atacante mais barato
                if (!isDefended && (movedVal - capturedVal) >= 200)
                    return true;
                if (cheapestEnemyAttacker + capturedVal <= movedVal - 150)
                    return true;
            }

            return false;
        }

        private static int GetCheapestAttackerValue(ChessRules state, Point target, bool byWhite)
        {
            int cheapest = int.MaxValue;
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = state.Board[r, c];
                    if (p == '.' || char.IsUpper(p) != byWhite) continue;
                    var attacks = state.GetPseudoMoves(state.Board, c, r, byWhite, false);
                    if (attacks.Contains(target))
                    {
                        int val = PieceBaseValue(p);
                        if (val < cheapest) cheapest = val;
                    }
                }
            }
            return cheapest == int.MaxValue ? 0 : cheapest;
        }

        // Classificação precisa comparando o lance do jogador com o melhor lance da Engine (suporta Brancas ou Pretas)
        public static (string badge, string description, Color color) ClassifyMove(
            double evalBeforeWhite,
            double evalAfterWhite,
            bool playedEngineBestMove,
            bool isBookMove = false,
            bool isSacrificeOrTactical = false,
            bool forWhite = true,
            int? mateInMoves = null,
            bool isImmediateCheckmate = true)
        {
            double evalBeforeMover = forWhite ? evalBeforeWhite : -evalBeforeWhite;
            double evalAfterMover = forWhite ? evalAfterWhite : -evalAfterWhite;

            if (evalAfterMover >= 90.0)
            {
                if (!isImmediateCheckmate && mateInMoves.HasValue && Math.Abs(mateInMoves.Value) > 0)
                    return ("!", $"Mate em {Math.Abs(mateInMoves.Value)}", Color.FromArgb(20, 150, 80));
                return ("!", "Xeque-Mate!", Color.FromArgb(20, 150, 80));
            }

            if (isBookMove && evalAfterMover - evalBeforeMover >= -0.25)
                return ("📖", "Teórico (Livro)", Color.FromArgb(115, 75, 35));

            // Usa a mudança em probabilidade de vitória (Win%) + perda em peões sob a ótica de quem jogou
            double delta = evalAfterMover - evalBeforeMover;
            double winBefore = EvalToWinProbability(evalBeforeMover);
            double winAfter = EvalToWinProbability(evalAfterMover);
            double winDrop = Math.Max(0.0, winBefore - winAfter);

            // Brilhante exige sacrifício real em posição ainda não decisiva (< +3.0) sendo o melhor lance
            if (playedEngineBestMove && isSacrificeOrTactical && evalBeforeMover < 3.0 && evalAfterMover >= -0.25)
                return ("!!", "Brilhante!", Color.FromArgb(15, 150, 165));
            if (playedEngineBestMove || (delta >= -0.15 && winDrop <= 0.025))
                return ("!", "Melhor Lance", Color.FromArgb(25, 140, 35));
            if (delta >= -0.45 && winDrop <= 0.065)
                return ("", "Lance Bom", Color.FromArgb(55, 115, 55));
            if (delta >= -1.00 && winDrop <= 0.14)
                return ("?!", "Imprecisão", Color.FromArgb(195, 130, 0));
            if (delta >= -2.10 && winDrop <= 0.26)
                return ("?", "Erro", Color.FromArgb(215, 85, 20));

            return ("??", "Capivarada (Blunder)", Color.FromArgb(195, 20, 20));
        }

        // Converte avaliação em peões para probabilidade estimada de vitória (0.0 a 1.0)
        public static double EvalToWinProbability(double evalPawns)
        {
            double clamped = Math.Clamp(evalPawns, -15.0, 15.0);
            return 1.0 / (1.0 + Math.Exp(-0.40 * clamped));
        }

        // Calcula a precisão (0% a 100%) de um único lance (das Brancas ou das Pretas)
        public static double ComputeMoveAccuracy(
            double evalBeforeWhite,
            double evalAfterWhite,
            bool playedBest,
            bool isBookMove = false,
            bool forWhite = true)
        {
            double evalBeforeMover = forWhite ? evalBeforeWhite : -evalBeforeWhite;
            double evalAfterMover = forWhite ? evalAfterWhite : -evalAfterWhite;

            if (playedBest || isBookMove || evalAfterMover >= evalBeforeMover)
                return 100.0;

            double winBefore = EvalToWinProbability(evalBeforeMover) * 100.0;
            double winAfter = EvalToWinProbability(evalAfterMover) * 100.0;
            double winDiff = Math.Max(0.0, winBefore - winAfter);

            // Fórmula inspirada no Lichess/Chess.com: 103.1668 * exp(-0.04354 * winDiff) - 3.1669
            double acc = 103.1668 * Math.Exp(-0.04354 * winDiff) - 3.1669;
            return Math.Clamp(acc, 0.0, 100.0);
        }

        // Gera uma explicação em português sobre o motivo tático/posicional da avaliação do lance
        public static string ExplainMoveCoach(
            ChessRules rulesBefore,
            Point from,
            Point to,
            char? promo,
            EngineAnalysisResult beforeAnalysis,
            EngineAnalysisResult afterAnalysis,
            bool playedBest,
            bool isBookMove)
        {
            bool forWhite = rulesBefore.WhiteTurn;
            char movedPiece = rulesBefore.Board[from.Y, from.X];
            char capturedPiece = rulesBefore.Board[to.Y, to.X];
            bool isEnPassant = char.ToUpper(movedPiece) == 'P' && from.X != to.X && capturedPiece == '.';
            bool isCastling = char.ToUpper(movedPiece) == 'K' && Math.Abs(to.X - from.X) == 2;

            var reasons = new List<string>();
            if (isBookMove)
                reasons.Add("lance teórico de abertura");
            if (isCastling)
                reasons.Add(to.X == 6 ? "roque pequeno (Rei em segurança)" : "roque grande (Rei protegido e Torre centralizada)");
            if (promo.HasValue && char.ToUpper(movedPiece) == 'P' && (to.Y == 0 || to.Y == 7))
                reasons.Add($"promoção para {PieceNamePt(promo.Value)}");
            if (capturedPiece != '.' || isEnPassant)
                reasons.Add(isEnPassant ? "captura en passant" : $"capturou {PieceNamePt(capturedPiece)}");
            else if ((to.X == 3 || to.X == 4) && (to.Y == 3 || to.Y == 4))
                reasons.Add("controle ativo do centro");

            double beforeMover = forWhite ? beforeAnalysis.EvalWhitePawns : -beforeAnalysis.EvalWhitePawns;
            double afterMover = forWhite ? afterAnalysis.EvalWhitePawns : -afterAnalysis.EvalWhitePawns;
            double delta = afterMover - beforeMover;

            if (!playedBest && delta < -0.90 && beforeAnalysis.BestMove.HasValue)
            {
                string bestAlg = FormatMoveUciOrAlg(beforeAnalysis.BestMove.Value.from, beforeAnalysis.BestMove.Value.to, beforeAnalysis.Promotion);
                reasons.Add($"perdeu {Math.Abs(delta):0.00} peões em relação a {bestAlg}");
            }

            if (reasons.Count == 0)
                reasons.Add(playedBest ? "manteve a melhor coordenação das peças" : "lance posicional");

            return string.Join(", ", reasons);
        }

        // Gera um relatório completo da posição atual para o botão "Analisar Posição (IA)"
        public static string BuildPositionReport(ChessRules rules, EngineAnalysisResult analysis)
        {
            int whiteMat = 0, blackMat = 0, minorMajorCount = 0;
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = rules.Board[r, c];
                    if (p == '.' || char.ToUpper(p) == 'K') continue;
                    int val = PieceBaseValue(p);
                    if (char.IsUpper(p)) whiteMat += val;
                    else blackMat += val;
                    if (char.ToUpper(p) != 'P') minorMajorCount++;
                }
            }

            string phase = rules.FullMoveNumber <= 10 && minorMajorCount >= 10
                ? "Abertura"
                : (minorMajorCount <= 4 ? "Final" : "Meio-Jogo");

            double matDiff = (whiteMat - blackMat) / 100.0;
            string matText = Math.Abs(matDiff) < 0.05
                ? "Equilíbrio material"
                : (matDiff > 0 ? $"Brancas +{matDiff:0.0} pts de material" : $"Pretas +{Math.Abs(matDiff):0.0} pts de material");

            string pvSan = ChessRules.FormatUciPvToSan(rules, analysis.PrincipalVariation);

            var sb = new StringBuilder();
            sb.AppendLine($"=== ANÁLISE PROFUNDA DA IA ({phase}) ===");
            sb.AppendLine($"• Avaliação Global: {analysis.EvalText} (Chance estimada das Brancas: {EvalToWinProbability(analysis.EvalWhitePawns) * 100:0.0}%)");
            sb.AppendLine($"• Material: {matText} | Abertura: {StudyLibrary.DetectOpening(rules.SanMoves)}");
            if (analysis.TopMovesSummary.Count > 0)
                sb.AppendLine($"• Melhores Candidatos: {string.Join("  |  ", analysis.TopMovesSummary)}");
            if (!string.IsNullOrWhiteSpace(pvSan))
                sb.AppendLine($"• Linha Principal (PV): {pvSan}");
            return sb.ToString().TrimEnd();
        }

        private static string PieceNamePt(char p) => char.ToLower(p) switch
        {
            'p' => "peão",
            'n' => "Cavalo",
            'b' => "Bispo",
            'r' => "Torre",
            'q' => "Dama",
            'k' => "Rei",
            _ => "peça"
        };

        public static string FormatMoveUciOrAlg(Point from, Point to, char? promo = null)
        {
            string suffix = promo.HasValue ? $"={char.ToUpper(promo.Value)}" : "";
            return $"{ChessRules.SquareToAlgebraic(from)}-{ChessRules.SquareToAlgebraic(to)}{suffix}";
        }

        // Método para pedir a melhor jogada (para Ajuda de Lance, Avaliação Pós-Lance ou para o Bot jogar)
        public EngineAnalysisResult Analyze(
            ChessRules rules,
            bool forWhite,
            int difficultyLevel,
            bool isHintOrEvaluationOnly = false,
            CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (stdinLock)
            {
                activeSearchCts = linkedCts;
            }
            var token = linkedCts.Token;

            try
            {
                // No modo Fácil (0) para o bot jogar, usa diretamente o motor interno com filtro por centipeões
                // (evita rodar o Stockfish à toa e descartar 70% das buscas!)
                bool useStockfish = IsUsingStockfish && (isHintOrEvaluationOnly || difficultyLevel > 0);
                if (useStockfish)
                {
                    int skill = isHintOrEvaluationOnly ? 20 : difficultyLevel switch { 0 => 0, 1 => 6, 2 => 13, _ => 20 };
                    int sfDepth = isHintOrEvaluationOnly ? 12 : difficultyLevel switch { 0 => 1, 1 => 5, 2 => 10, _ => 15 };
                    var sfRes = QueryStockfish(rules, forWhite, skill, sfDepth, difficultyLevel, isHintOrEvaluationOnly, token);
                    if (sfRes != null) return sfRes;
                }

                // Motor Interno Posicional + Tático (respeita roque, en passant, promoção, afogamento e cancelamento)
                return QueryInternalEngine(rules, forWhite, difficultyLevel, isHintOrEvaluationOnly, token);
            }
            finally
            {
                lock (stdinLock)
                {
                    if (ReferenceEquals(activeSearchCts, linkedCts))
                        activeSearchCts = null;
                }
            }
        }

        private EngineAnalysisResult? QueryStockfish(
            ChessRules rules,
            bool forWhite,
            int skill,
            int depth,
            int difficultyLevel,
            bool isHintOrEvaluationOnly,
            CancellationToken token)
        {
            stockfishLock.Wait();
            try
            {
                if (token.IsCancellationRequested)
                    return new EngineAnalysisResult { WasCancelled = true };

                string fen = rules.ToFen();
                var sw = stockfishProcess!.StandardInput;
                var sr = stockfishProcess.StandardOutput;

                bool limitStrength = !isHintOrEvaluationOnly && difficultyLevel < 3;
                int targetElo = difficultyLevel switch
                {
                    0 => 1320,
                    1 => 1500,
                    2 => 1900,
                    _ => 2850
                };

                lock (stdinLock)
                {
                    if (limitStrength)
                    {
                        sw.WriteLine("setoption name UCI_LimitStrength value true");
                        sw.WriteLine($"setoption name UCI_Elo value {targetElo}");
                    }
                    else
                    {
                        sw.WriteLine("setoption name UCI_LimitStrength value false");
                    }
                    sw.WriteLine($"setoption name Skill Level value {skill}");
                    sw.WriteLine($"position fen {fen}");
                    sw.WriteLine($"go depth {depth}");
                    sw.Flush();
                }

                string lastInfoLine = "";
                string pvLine = "";
                long nodes = 0;
                double whitePawns = 0.0;
                int? mateInMoves = null;
                string evalStr = "+0.00";
                string? line;

                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("info") && line.Contains(" pv "))
                    {
                        lastInfoLine = line;
                        int pvIdx = line.IndexOf(" pv ", StringComparison.Ordinal);
                        if (pvIdx >= 0)
                            pvLine = line[(pvIdx + 4)..].Trim();

                        var parts = line.Split(' ');
                        for (int i = 0; i < parts.Length - 1; i++)
                        {
                            if (parts[i] == "nodes") long.TryParse(parts[i + 1], out nodes);
                            if (parts[i] == "cp" && int.TryParse(parts[i + 1], out int cp))
                            {
                                mateInMoves = null;
                                whitePawns = (forWhite ? cp : -cp) / 100.0;
                                evalStr = (whitePawns >= 0 ? "+" : "") + whitePawns.ToString("0.00", CultureInfo.InvariantCulture);
                            }
                            else if (parts[i] == "mate" && int.TryParse(parts[i + 1], out int mateIn))
                            {
                                int m = forWhite ? mateIn : -mateIn;
                                mateInMoves = m;
                                whitePawns = m > 0 ? 99.0 : -99.0;
                                evalStr = m > 0 ? $"#+{m}" : $"#{m}";
                            }
                        }
                    }
                    else if (line.StartsWith("bestmove"))
                    {
                        if (token.IsCancellationRequested)
                            return new EngineAnalysisResult { WasCancelled = true };

                        string[] parts = line.Split(' ');
                        if (parts.Length >= 2 && parts[1].Length >= 4 && parts[1] != "(none)")
                        {
                            string mv = parts[1];
                            int fCol = mv[0] - 'a';
                            int fRow = 8 - (mv[1] - '0');
                            int tCol = mv[2] - 'a';
                            int tRow = 8 - (mv[3] - '0');
                            var chosenMove = (new Point(fCol, fRow), new Point(tCol, tRow));

                            char? promo = null;
                            if (mv.Length >= 5 && "qrbnQRBN".IndexOf(mv[4]) >= 0)
                                promo = forWhite ? char.ToUpper(mv[4]) : char.ToLower(mv[4]);

                            string chosenUci = FormatMoveUciOrAlg(chosenMove.Item1, chosenMove.Item2, promo);
                            return new EngineAnalysisResult
                            {
                                BestMove = chosenMove,
                                Promotion = promo,
                                EvalWhitePawns = whitePawns,
                                EvalText = evalStr,
                                MateInMoves = mateInMoves,
                                Nodes = nodes,
                                PrincipalVariation = pvLine,
                                TopMovesSummary = new List<string> { $"1º {chosenUci} ({evalStr})" },
                                LogLine = $"[Stockfish d={depth}] {mv} ({evalStr}) | {lastInfoLine}"
                            };
                        }
                        break;
                    }
                }
            }
            catch { }
            finally
            {
                stockfishLock.Release();
            }
            return null;
        }

        private EngineAnalysisResult QueryInternalEngine(
            ChessRules rules,
            bool forWhite,
            int difficultyLevel,
            bool isHintOrEvaluationOnly,
            CancellationToken token)
        {
            var legalBaseMoves = rules.GetAllLegalMoves(forWhite);
            if (legalBaseMoves.Count == 0)
            {
                bool inCheck = rules.IsKingInCheck(rules.Board, forWhite);
                double mateOrDraw = inCheck ? (forWhite ? -99.0 : 99.0) : 0.0;
                string txt = inCheck ? (forWhite ? "#-0" : "#+0") : "0.00";
                return new EngineAnalysisResult
                {
                    BestMove = null,
                    EvalWhitePawns = mateOrDraw,
                    EvalText = txt,
                    MateInMoves = inCheck ? 0 : null
                };
            }

            var rootCandidates = new List<SearchMove>();
            foreach (var mv in legalBaseMoves)
            {
                char p = rules.Board[mv.from.Y, mv.from.X];
                if (char.ToUpper(p) == 'P' && (mv.to.Y == 0 || mv.to.Y == 7))
                {
                    rootCandidates.Add(new SearchMove(mv.from, mv.to, forWhite ? 'Q' : 'q'));
                    rootCandidates.Add(new SearchMove(mv.from, mv.to, forWhite ? 'N' : 'n'));
                    rootCandidates.Add(new SearchMove(mv.from, mv.to, forWhite ? 'R' : 'r'));
                    rootCandidates.Add(new SearchMove(mv.from, mv.to, forWhite ? 'B' : 'b'));
                }
                else
                {
                    rootCandidates.Add(new SearchMove(mv.from, mv.to, null));
                }
            }

            rootCandidates = rootCandidates.OrderBy(_ => rng.Next()).ToList();

            int searchDepth = isHintOrEvaluationOnly ? 4 : difficultyLevel switch
            {
                0 => 2,
                1 => 2,
                2 => 4,
                _ => 4
            };

            byte rootCastling = PackCastlingRights(rules.CastleWK, rules.CastleWQ, rules.CastleBK, rules.CastleBQ);
            long searchedNodes = 0;
            var scoredMoves = new List<(SearchMove move, int whiteScore)>();

            foreach (var mv in rootCandidates)
            {
                if (token.IsCancellationRequested && scoredMoves.Count > 0)
                    break;

                char[,] copy = ApplyMoveOnCopy(rules.Board, mv.From, mv.To, mv.Promotion, out Point? nextEp, rootCastling, out byte nextCastling);

                int repetitionPenalty = 0;
                string nextBoardHash = QuickBoardKey(copy);
                if (rules.PositionHistory.Any(h => h.StartsWith(nextBoardHash)))
                    repetitionPenalty = forWhite ? -65 : 65;

                int val = SearchAlphaBeta(rules, copy, searchDepth - 1, -1000000, 1000000, !forWhite, nextEp, nextCastling, ref searchedNodes, token);

                if (Math.Abs(val) < 40000)
                    val += repetitionPenalty;

                scoredMoves.Add((mv, val));
            }

            if (forWhite)
                scoredMoves = scoredMoves.OrderByDescending(m => m.whiteScore).ToList();
            else
                scoredMoves = scoredMoves.OrderBy(m => m.whiteScore).ToList();

            var topMove = scoredMoves[0];
            var selectedMove = topMove.move;

            // Seleção filtrada por perda em centipeões (sem entregar mate imediato!) nos modos Fácil e Médio
            if (!isHintOrEvaluationOnly && difficultyLevel == 0 && scoredMoves.Count > 1)
            {
                int bestMoverScore = forWhite ? topMove.whiteScore : -topMove.whiteScore;
                // Filtra lances que não levem mate forçado (moverScore > -40000) e percam no máximo 240 cp
                var safeHumanMoves = scoredMoves
                    .Skip(1)
                    .Where(m =>
                    {
                        int s = forWhite ? m.whiteScore : -m.whiteScore;
                        return s > -40000 && (bestMoverScore - s) <= 240;
                    })
                    .Take(4)
                    .ToList();

                if (safeHumanMoves.Count > 0 && rng.NextDouble() < 0.75)
                {
                    selectedMove = safeHumanMoves[rng.Next(safeHumanMoves.Count)].move;
                }
            }
            else if (!isHintOrEvaluationOnly && difficultyLevel == 1 && scoredMoves.Count > 1 && rng.NextDouble() < 0.25)
            {
                int bestMoverScore = forWhite ? topMove.whiteScore : -topMove.whiteScore;
                int secondMoverScore = forWhite ? scoredMoves[1].whiteScore : -scoredMoves[1].whiteScore;
                if (secondMoverScore > -40000 && (bestMoverScore - secondMoverScore) <= 120)
                    selectedMove = scoredMoves[1].move;
            }

            double evalPawns = FormatScoreToWhitePawns(topMove.whiteScore);
            int? mateInMoves = ComputeMateInMoves(topMove.whiteScore, searchDepth);
            string formattedEval = FormatScoreText(topMove.whiteScore, searchDepth);
            string moveAlg = FormatMoveUciOrAlg(selectedMove.From, selectedMove.To, selectedMove.Promotion);

            var topSummary = scoredMoves
                .Take(3)
                .Select((m, idx) => $"{idx + 1}º {FormatMoveUciOrAlg(m.move.From, m.move.To, m.move.Promotion)} ({FormatScoreText(m.whiteScore, searchDepth)})")
                .ToList();

            string log = $"[Engine d={searchDepth}] Best:{moveAlg} Eval:{formattedEval} Nodes:{searchedNodes} | Top: {string.Join(", ", topSummary)}";

            return new EngineAnalysisResult
            {
                BestMove = (selectedMove.From, selectedMove.To),
                Promotion = selectedMove.Promotion,
                EvalWhitePawns = evalPawns,
                EvalText = formattedEval,
                MateInMoves = mateInMoves,
                Nodes = searchedNodes,
                PrincipalVariation = FormatMoveUciOrAlg(topMove.move.From, topMove.move.To, topMove.move.Promotion),
                TopMovesSummary = topSummary,
                LogLine = log,
                WasCancelled = token.IsCancellationRequested
            };
        }

        private static double FormatScoreToWhitePawns(int whiteScore)
        {
            if (whiteScore >= 40000) return 99.0;
            if (whiteScore <= -40000) return -99.0;
            return whiteScore / 100.0;
        }

        private static int? ComputeMateInMoves(int whiteScore, int rootSearchDepth)
        {
            if (Math.Abs(whiteScore) < 40000) return null;
            int remDepth = Math.Max(0, (Math.Abs(whiteScore) - 50000) / 100);
            int plies = Math.Max(1, rootSearchDepth - remDepth);
            int moves = Math.Max(1, (plies + 1) / 2);
            return whiteScore > 0 ? moves : -moves;
        }

        private static string FormatScoreText(int whiteScore, int rootSearchDepth = 4)
        {
            int? mateIn = ComputeMateInMoves(whiteScore, rootSearchDepth);
            if (mateIn.HasValue)
                return mateIn.Value > 0 ? $"#+{mateIn.Value}" : $"#{mateIn.Value}";
            double pawns = whiteScore / 100.0;
            return (pawns >= 0 ? "+" : "") + pawns.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private int SearchAlphaBeta(
            ChessRules rules,
            char[,] b,
            int depth,
            int alpha,
            int beta,
            bool whiteToMove,
            Point? epTarget,
            byte castling,
            ref long nodes,
            CancellationToken token)
        {
            nodes++;
            if ((nodes & 255) == 0 && token.IsCancellationRequested)
                return EvaluateBoardWhiteRelative(b);

            if (depth <= 0)
                return QuiescenceCaptures(rules, b, alpha, beta, whiteToMove, epTarget, 2, ref nodes, token);

            var moves = GeneratePseudoLegalOrdered(b, whiteToMove, epTarget, castling, capturesOnly: false);
            int legalMovesFound = 0;

            if (whiteToMove)
            {
                int maxEval = -999999;
                foreach (var mv in moves)
                {
                    if (!IsCastlingStepLegal(rules, b, mv, true)) continue;

                    char[,] copy = ApplyMoveOnCopy(b, mv.From, mv.To, mv.Promotion, out Point? nextEp, castling, out byte nextCastling);
                    if (rules.IsKingInCheck(copy, true)) continue;

                    legalMovesFound++;
                    int eval = SearchAlphaBeta(rules, copy, depth - 1, alpha, beta, false, nextEp, nextCastling, ref nodes, token);
                    if (eval > maxEval) maxEval = eval;
                    if (eval > alpha) alpha = eval;
                    if (beta <= alpha || token.IsCancellationRequested) break;
                }

                if (legalMovesFound == 0)
                {
                    return rules.IsKingInCheck(b, true) ? (-50000 - depth * 100) : 0;
                }
                return maxEval;
            }
            else
            {
                int minEval = 999999;
                foreach (var mv in moves)
                {
                    if (!IsCastlingStepLegal(rules, b, mv, false)) continue;

                    char[,] copy = ApplyMoveOnCopy(b, mv.From, mv.To, mv.Promotion, out Point? nextEp, castling, out byte nextCastling);
                    if (rules.IsKingInCheck(copy, false)) continue;

                    legalMovesFound++;
                    int eval = SearchAlphaBeta(rules, copy, depth - 1, alpha, beta, true, nextEp, nextCastling, ref nodes, token);
                    if (eval < minEval) minEval = eval;
                    if (eval < beta) beta = eval;
                    if (beta <= alpha || token.IsCancellationRequested) break;
                }

                if (legalMovesFound == 0)
                {
                    return rules.IsKingInCheck(b, false) ? (50000 + depth * 100) : 0;
                }
                return minEval;
            }
        }

        private int QuiescenceCaptures(
            ChessRules rules,
            char[,] b,
            int alpha,
            int beta,
            bool whiteToMove,
            Point? epTarget,
            int qDepth,
            ref long nodes,
            CancellationToken token)
        {
            nodes++;
            int standPat = EvaluateBoardWhiteRelative(b);
            if (qDepth <= 0 || token.IsCancellationRequested) return standPat;

            if (whiteToMove)
            {
                if (standPat >= beta) return beta;
                if (alpha < standPat) alpha = standPat;

                var captures = GeneratePseudoLegalOrdered(b, true, epTarget, 0, capturesOnly: true);
                foreach (var cap in captures)
                {
                    char[,] copy = ApplyMoveOnCopy(b, cap.From, cap.To, cap.Promotion, out Point? nextEp, 0, out _);
                    if (rules.IsKingInCheck(copy, true)) continue;

                    int score = QuiescenceCaptures(rules, copy, alpha, beta, false, nextEp, qDepth - 1, ref nodes, token);
                    if (score >= beta) return beta;
                    if (score > alpha) alpha = score;
                }
                return alpha;
            }
            else
            {
                if (standPat <= alpha) return alpha;
                if (beta > standPat) beta = standPat;

                var captures = GeneratePseudoLegalOrdered(b, false, epTarget, 0, capturesOnly: true);
                foreach (var cap in captures)
                {
                    char[,] copy = ApplyMoveOnCopy(b, cap.From, cap.To, cap.Promotion, out Point? nextEp, 0, out _);
                    if (rules.IsKingInCheck(copy, false)) continue;

                    int score = QuiescenceCaptures(rules, copy, alpha, beta, true, nextEp, qDepth - 1, ref nodes, token);
                    if (score <= alpha) return alpha;
                    if (score < beta) beta = score;
                }
                return beta;
            }
        }

        private static bool IsCastlingStepLegal(ChessRules rules, char[,] b, SearchMove mv, bool forWhite)
        {
            char piece = b[mv.From.Y, mv.From.X];
            if (char.ToUpper(piece) != 'K' || Math.Abs(mv.To.X - mv.From.X) != 2)
                return true;

            if (rules.IsKingInCheck(b, forWhite)) return false;
            int midCol = (mv.From.X + mv.To.X) / 2;
            char[,] midCopy = (char[,])b.Clone();
            midCopy[mv.From.Y, midCol] = piece;
            midCopy[mv.From.Y, mv.From.X] = '.';
            return !rules.IsKingInCheck(midCopy, forWhite);
        }

        private List<SearchMove> GeneratePseudoLegalOrdered(
            char[,] b,
            bool forWhite,
            Point? epTarget,
            byte castling,
            bool capturesOnly)
        {
            var list = new List<(SearchMove mv, int priority)>();
            bool Inside(int x, int y) => x >= 0 && x < 8 && y >= 0 && y < 8;
            bool IsEnemy(int x, int y) => b[y, x] != '.' && char.IsUpper(b[y, x]) != forWhite;

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = b[r, c];
                    if (p == '.' || char.IsUpper(p) != forWhite) continue;

                    char lower = char.ToLower(p);
                    var from = new Point(c, r);

                    if (lower == 'p')
                    {
                        int dir = forWhite ? -1 : 1;
                        int startRow = forWhite ? 6 : 1;
                        int promoRow = forWhite ? 0 : 7;

                        // Avanço de peão
                        if (Inside(c, r + dir) && b[r + dir, c] == '.')
                        {
                            bool isPromo = (r + dir) == promoRow;
                            if (!capturesOnly || isPromo)
                            {
                                if (isPromo)
                                {
                                    list.Add((new SearchMove(from, new Point(c, r + dir), forWhite ? 'Q' : 'q'), 850));
                                    list.Add((new SearchMove(from, new Point(c, r + dir), forWhite ? 'N' : 'n'), 350));
                                }
                                else
                                {
                                    list.Add((new SearchMove(from, new Point(c, r + dir)), 5));
                                    if (r == startRow && b[r + dir * 2, c] == '.')
                                        list.Add((new SearchMove(from, new Point(c, r + dir * 2)), 12));
                                }
                            }
                        }

                        // Capturas normais e En Passant
                        foreach (int dc in new[] { -1, 1 })
                        {
                            int nx = c + dc, ny = r + dir;
                            if (!Inside(nx, ny)) continue;

                            if (IsEnemy(nx, ny))
                            {
                                char target = b[ny, nx];
                                int basePrio = PieceBaseValue(target) * 10 - 100;
                                if (ny == promoRow)
                                {
                                    list.Add((new SearchMove(from, new Point(nx, ny), forWhite ? 'Q' : 'q'), basePrio + 850));
                                    list.Add((new SearchMove(from, new Point(nx, ny), forWhite ? 'N' : 'n'), basePrio + 350));
                                }
                                else
                                {
                                    list.Add((new SearchMove(from, new Point(nx, ny)), basePrio));
                                }
                            }
                            else if (epTarget.HasValue && epTarget.Value.X == nx && epTarget.Value.Y == ny && b[ny, nx] == '.')
                            {
                                list.Add((new SearchMove(from, new Point(nx, ny)), 950));
                            }
                        }
                    }
                    else if (lower == 'n')
                    {
                        int[] dx = { -2, -1, 1, 2, 2, 1, -1, -2 };
                        int[] dy = { -1, -2, -2, -1, 1, 2, 2, 1 };
                        for (int i = 0; i < 8; i++)
                        {
                            int nx = c + dx[i], ny = r + dy[i];
                            if (!Inside(nx, ny)) continue;
                            char target = b[ny, nx];
                            if (target == '.')
                            {
                                if (!capturesOnly) list.Add((new SearchMove(from, new Point(nx, ny)), 10));
                            }
                            else if (IsEnemy(nx, ny))
                            {
                                list.Add((new SearchMove(from, new Point(nx, ny)), PieceBaseValue(target) * 10 - 320));
                            }
                        }
                    }
                    else if (lower == 'k')
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = c + dx, ny = r + dy;
                                if (!Inside(nx, ny)) continue;
                                char target = b[ny, nx];
                                if (target == '.')
                                {
                                    if (!capturesOnly) list.Add((new SearchMove(from, new Point(nx, ny)), 0));
                                }
                                else if (IsEnemy(nx, ny))
                                {
                                    list.Add((new SearchMove(from, new Point(nx, ny)), PieceBaseValue(target) * 10 - 100));
                                }
                            }
                        }

                        if (!capturesOnly && castling != 0)
                        {
                            int homeRow = forWhite ? 7 : 0;
                            char rookChar = forWhite ? 'R' : 'r';
                            bool canKS = forWhite ? (castling & 1) != 0 : (castling & 4) != 0;
                            bool canQS = forWhite ? (castling & 2) != 0 : (castling & 8) != 0;

                            if (r == homeRow && c == 4)
                            {
                                if (canKS && b[homeRow, 5] == '.' && b[homeRow, 6] == '.' && b[homeRow, 7] == rookChar)
                                    list.Add((new SearchMove(from, new Point(6, homeRow)), 60));
                                if (canQS && b[homeRow, 3] == '.' && b[homeRow, 2] == '.' && b[homeRow, 1] == '.' && b[homeRow, 0] == rookChar)
                                    list.Add((new SearchMove(from, new Point(2, homeRow)), 55));
                            }
                        }
                    }
                    else
                    {
                        var dirs = new List<(int dx, int dy)>();
                        if (lower == 'r' || lower == 'q') dirs.AddRange(new[] { (1, 0), (-1, 0), (0, 1), (0, -1) });
                        if (lower == 'b' || lower == 'q') dirs.AddRange(new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) });

                        int pieceVal = PieceBaseValue(p);
                        foreach (var (dx, dy) in dirs)
                        {
                            int nx = c + dx, ny = r + dy;
                            while (Inside(nx, ny))
                            {
                                char target = b[ny, nx];
                                if (target == '.')
                                {
                                    if (!capturesOnly) list.Add((new SearchMove(from, new Point(nx, ny)), 5));
                                }
                                else
                                {
                                    if (IsEnemy(nx, ny))
                                        list.Add((new SearchMove(from, new Point(nx, ny)), PieceBaseValue(target) * 10 - pieceVal));
                                    break;
                                }
                                nx += dx;
                                ny += dy;
                            }
                        }
                    }
                }
            }
            return list.OrderByDescending(x => x.priority).Select(x => x.mv).ToList();
        }

        private static byte PackCastlingRights(bool wk, bool wq, bool bk, bool bq)
        {
            byte mask = 0;
            if (wk) mask |= 1;
            if (wq) mask |= 2;
            if (bk) mask |= 4;
            if (bq) mask |= 8;
            return mask;
        }

        private static char[,] ApplyMoveOnCopy(
            char[,] src,
            Point from,
            Point to,
            char? promo,
            out Point? nextEp,
            byte castling,
            out byte nextCastling)
        {
            char[,] copy = (char[,])src.Clone();
            char piece = copy[from.Y, from.X];
            bool isWhite = char.IsUpper(piece);
            char upper = char.ToUpper(piece);

            if (upper == 'P' && from.X != to.X && copy[to.Y, to.X] == '.')
            {
                int capturedPawnRow = isWhite ? to.Y + 1 : to.Y - 1;
                copy[capturedPawnRow, to.X] = '.';
            }

            if (upper == 'K' && Math.Abs(to.X - from.X) == 2)
            {
                if (to.X == 6)
                {
                    copy[from.Y, 5] = copy[from.Y, 7];
                    copy[from.Y, 7] = '.';
                }
                else if (to.X == 2)
                {
                    copy[from.Y, 3] = copy[from.Y, 0];
                    copy[from.Y, 0] = '.';
                }
            }

            copy[to.Y, to.X] = piece;
            copy[from.Y, from.X] = '.';

            if (upper == 'P' && (to.Y == 0 || to.Y == 7))
            {
                char chosen = promo ?? 'Q';
                copy[to.Y, to.X] = isWhite ? char.ToUpper(chosen) : char.ToLower(chosen);
            }

            nextEp = (upper == 'P' && Math.Abs(to.Y - from.Y) == 2)
                ? new Point(from.X, (from.Y + to.Y) / 2)
                : null;

            nextCastling = castling;
            if (piece == 'K') nextCastling = (byte)(nextCastling & ~3);
            if (piece == 'k') nextCastling = (byte)(nextCastling & ~12);
            if ((from.X == 7 && from.Y == 7) || (to.X == 7 && to.Y == 7)) nextCastling = (byte)(nextCastling & ~1);
            if ((from.X == 0 && from.Y == 7) || (to.X == 0 && to.Y == 7)) nextCastling = (byte)(nextCastling & ~2);
            if ((from.X == 7 && from.Y == 0) || (to.X == 7 && to.Y == 0)) nextCastling = (byte)(nextCastling & ~4);
            if ((from.X == 0 && from.Y == 0) || (to.X == 0 && to.Y == 0)) nextCastling = (byte)(nextCastling & ~8);

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

        public static int PieceBaseValue(char p) => char.ToLower(p) switch
        {
            'p' => 100,
            'n' => 320,
            'b' => 330,
            'r' => 500,
            'q' => 900,
            'k' => 20000,
            _ => 0
        };

        // Avaliação Posicional Completa (Material + Tabelas Posicionais de Centro/Desenvolvimento/Segurança do Rei)
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
                        'k' => KingTable[tableRow, c],
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
