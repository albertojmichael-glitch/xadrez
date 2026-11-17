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
        public (Point from, Point to)? SecondBestMove { get; set; }
        public char? SecondBestPromotion { get; set; }
        public string SecondBestEvalText { get; set; } = "";
        public double EvalWhitePawns { get; set; }
        public string EvalText { get; set; } = "+0.00";
        public long Nodes { get; set; }
        public string LogLine { get; set; } = "";
        public string PrincipalVariation { get; set; } = "";
        public List<string> TopMovesSummary { get; set; } = new List<string>();
        public List<string> VariationTreeLines { get; set; } = new List<string>();
    }

    public class EngineService : IDisposable
    {
        private Process? stockfishProcess;
        private volatile bool disposed;
        private bool stockfishUnavailable;

        private static Random rng => Random.Shared;

        private readonly SemaphoreSlim stockfishLock = new SemaphoreSlim(1, 1);
        private readonly object stdinLock = new object();

        public bool IsUsingStockfish => Volatile.Read(ref stockfishProcess) != null;

        private readonly record struct SearchMove(Point From, Point To, char? Promotion = null);

        // Tabelas Posicionais de Meio-Jogo
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

        // Rei no Meio-Jogo: incentiva o Roque e esconde o Rei atrás dos peões
        private static readonly int[,] KingMiddlegameTable = {
            {-30,-40,-40,-50,-50,-40,-40,-30 },
            {-30,-40,-40,-50,-50,-40,-40,-30 },
            {-30,-40,-40,-50,-50,-40,-40,-30 },
            {-30,-40,-40,-50,-50,-40,-40,-30 },
            {-20,-30,-30,-40,-40,-30,-30,-20 },
            {-10,-20,-20,-20,-20,-20,-20,-10 },
            { 20, 20,  0,  0,  0,  0, 20, 20 },
            { 20, 35, 25, -5,  0, -5, 35, 20 }
        };

        // Rei no Final de Jogo: centraliza fortemente o Rei para apoiar peões passados e lutar por oposição!
        private static readonly int[,] KingEndgameTable = {
            {-50,-35,-25,-15,-15,-25,-35,-50 },
            {-35,-15,  0, 10, 10,  0,-15,-35 },
            {-25,  0, 25, 35, 35, 25,  0,-25 },
            {-15, 10, 35, 45, 45, 35, 10,-15 },
            {-15, 10, 35, 45, 45, 35, 10,-15 },
            {-25,  0, 25, 35, 35, 25,  0,-25 },
            {-35,-15,  0, 10, 10,  0,-15,-35 },
            {-50,-35,-25,-15,-15,-25,-35,-50 }
        };

        private void InitStockfish(CancellationToken cancellationToken)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "stockfish.exe"),
                Path.Combine(baseDir, "assets", "stockfish.exe")
            };

            string? exePath = candidates.FirstOrDefault(File.Exists);
            if (exePath == null)
            {
                stockfishUnavailable = true;
                return;
            }

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
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                stockfishProcess.StandardInput.WriteLine("uci");
                stockfishProcess.StandardInput.Flush();

                bool uciOk = false;
                string? line;
                while ((line = ReadEngineLine(stockfishProcess.StandardOutput, timeout.Token)) != null)
                {
                    if (line.Trim() == "uciok")
                    {
                        uciOk = true;
                        break;
                    }
                }

                if (!uciOk)
                {
                    stockfishUnavailable = true;
                    ShutdownStockfishProcess();
                    return;
                }

                stockfishProcess.StandardInput.WriteLine("isready");
                stockfishProcess.StandardInput.Flush();

                bool readyOk = false;
                while ((line = ReadEngineLine(stockfishProcess.StandardOutput, timeout.Token)) != null)
                {
                    if (line.Trim() == "readyok")
                    {
                        readyOk = true;
                        break;
                    }
                }

                if (!readyOk)
                {
                    stockfishUnavailable = true;
                    ShutdownStockfishProcess();
                }
            }
            catch
            {
                ShutdownStockfishProcess();
                cancellationToken.ThrowIfCancellationRequested();
                stockfishUnavailable = true;
            }
        }

        private static string? ReadEngineLine(StreamReader reader, CancellationToken token) =>
            reader.ReadLineAsync().WaitAsync(token).GetAwaiter().GetResult();

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
            if (stockfishLock.Wait(0))
            {
                try { ShutdownStockfishProcess(); }
                finally { stockfishLock.Release(); }
            }
            GC.SuppressFinalize(this);
        }

        public static (string badge, string description, Color color) ClassifyMove(
            double evalBeforeWhite,
            double evalAfterWhite,
            bool playedEngineBestMove,
            bool isBookMove = false,
            bool isSacrificeOrTactical = false,
            int? mateInMoves = null,
            bool? isImmediateCheckmate = null)
        {
            if (mateInMoves > 0 && isImmediateCheckmate != true)
                return ("!", $"Mate em {mateInMoves}", Color.FromArgb(20, 150, 80));

            if (isImmediateCheckmate ?? (evalAfterWhite >= 90.0))
                return ("!", "Xeque-Mate!", Color.FromArgb(20, 150, 80));

            if (isBookMove && evalAfterWhite - evalBeforeWhite >= -0.25)
                return ("📖", "Teórico (Livro)", Color.FromArgb(115, 75, 35));

            double delta = evalAfterWhite - evalBeforeWhite;
            double winBefore = EvalToWinProbability(evalBeforeWhite);
            double winAfter = EvalToWinProbability(evalAfterWhite);
            double winDrop = Math.Max(0.0, winBefore - winAfter);

            if (playedEngineBestMove && isSacrificeOrTactical && evalBeforeWhite < 3.0 && delta >= -0.25)
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

        public static double EvalToWinProbability(double evalPawns)
        {
            double clamped = Math.Clamp(evalPawns, -15.0, 15.0);
            return 1.0 / (1.0 + Math.Exp(-0.40 * clamped));
        }

        public static double ComputeMoveAccuracy(double evalBeforeWhite, double evalAfterWhite, bool playedBest, bool isBookMove = false, bool forWhite = true)
        {
            if (!forWhite)
            {
                evalBeforeWhite = -evalBeforeWhite;
                evalAfterWhite = -evalAfterWhite;
            }

            if (playedBest || isBookMove || evalAfterWhite >= evalBeforeWhite)
                return 100.0;

            double winBefore = EvalToWinProbability(evalBeforeWhite) * 100.0;
            double winAfter = EvalToWinProbability(evalAfterWhite) * 100.0;
            double winDiff = Math.Max(0.0, winBefore - winAfter);

            double acc = 103.1668 * Math.Exp(-0.04354 * winDiff) - 3.1669;
            return Math.Clamp(acc, 0.0, 100.0);
        }

        public static bool IsMaterialSacrifice(ChessRules rules, Point from, Point to, char? promotion,
            double evalBeforeWhite, double evalAfterWhite)
        {
            char piece = rules.Board[from.Y, from.X];
            bool white = char.IsUpper(piece);
            int offered = ExchangeValue(piece);
            if (offered <= 1 || offered <= ExchangeValue(rules.Board[to.Y, to.X]) ||
                (white ? 1 : -1) * (evalAfterWhite - evalBeforeWhite) < -0.25 ||
                !rules.GetLegalMovesForSquare(from.X, from.Y, white).Contains(to))
                return false;

            var after = rules.Clone();
            after.ExecuteMove(from, to, promotion);
            return after.GetAllLegalMoves(!white).Any(move => move.to == to);
        }

        private static int ExchangeValue(char piece) => char.ToUpperInvariant(piece) switch
        {
            'P' => 1, 'N' or 'B' => 3, 'R' => 5, 'Q' => 9, _ => 0
        };

        // Detector de Padrões Táticos: Garfo (Fork), Cravada (Pin), Espeto (Skewer) e Ataque Descoberto (Discovered Attack)
        public static List<string> DetectTacticalPatterns(ChessRules rulesBefore, Point from, Point to, char? promo = null, bool english = false)
        {
            var patterns = new List<string>();
            char movedPiece = rulesBefore.Board[from.Y, from.X];
            if (movedPiece == '.') return patterns;

            bool isWhite = char.IsUpper(movedPiece);
            var afterRules = rulesBefore.Clone();
            afterRules.ExecuteMove(from, to, promo);

            char pieceAtDest = afterRules.Board[to.Y, to.X];

            // 1. Garfo / Ataque Duplo (Fork): a peça movida ataca 2 ou mais peças inimigas valiosas (ou Rei + peça)
            var pseudoTargets = afterRules.GetPseudoMoves(afterRules.Board, to.X, to.Y, isWhite, false);
            var valuableAttacked = new List<char>();
            foreach (var sq in pseudoTargets)
            {
                char enemy = afterRules.Board[sq.Y, sq.X];
                if (enemy != '.' && char.IsUpper(enemy) != isWhite)
                {
                    int enemyVal = PieceBaseValue(enemy);
                    if (enemyVal >= 320 || enemyVal > PieceBaseValue(pieceAtDest))
                        valuableAttacked.Add(enemy);
                }
            }
            if (valuableAttacked.Count >= 2)
            {
                patterns.Add(english ? "Fork (Double Attack)" : "Garfo (Ataque Duplo)");
            }

            // 2. Cravada (Pin) e Espeto (Skewer) por peças deslizantes (Bispo, Torre, Dama)
            char lowerDest = char.ToLower(pieceAtDest);
            if (lowerDest is 'b' or 'r' or 'q')
            {
                var dirs = new List<(int dx, int dy)>();
                if (lowerDest is 'r' or 'q') dirs.AddRange(new[] { (1, 0), (-1, 0), (0, 1), (0, -1) });
                if (lowerDest is 'b' or 'q') dirs.AddRange(new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) });

                foreach (var (dx, dy) in dirs)
                {
                    int nx = to.X + dx, ny = to.Y + dy;
                    char? firstEnemy = null;
                    char? secondEnemy = null;

                    while (nx >= 0 && nx < 8 && ny >= 0 && ny < 8)
                    {
                        char sqPiece = afterRules.Board[ny, nx];
                        if (sqPiece != '.')
                        {
                            if (char.IsUpper(sqPiece) == isWhite) break; // Bloqueado por peça amiga
                            if (firstEnemy == null)
                                firstEnemy = sqPiece;
                            else
                            {
                                secondEnemy = sqPiece;
                                break;
                            }
                        }
                        nx += dx;
                        ny += dy;
                    }

                    if (firstEnemy.HasValue && secondEnemy.HasValue)
                    {
                        int val1 = PieceBaseValue(firstEnemy.Value);
                        int val2 = PieceBaseValue(secondEnemy.Value);
                        if (val2 > val1 && val2 >= 500)
                        {
                            string pinLabel = char.ToLower(secondEnemy.Value) == 'k'
                                ? (english ? "Absolute Pin on the King" : "Cravada Absoluta no Rei (Pin)")
                                : (english ? "Relative Pin" : "Cravada Tática (Pin)");
                            if (!patterns.Contains(pinLabel)) patterns.Add(pinLabel);
                        }
                        else if (val1 > val2 && val1 >= 900 && val2 >= 320)
                        {
                            string skewerLabel = english ? "Tactical Skewer" : "Espeto Tático (Skewer)";
                            if (!patterns.Contains(skewerLabel)) patterns.Add(skewerLabel);
                        }
                    }
                }
            }

            // 3. Ataque / Xeque Descoberto (Discovered Attack / Check)
            bool enemyKingInCheckAfter = afterRules.IsKingInCheck(afterRules.Board, !isWhite);
            if (enemyKingInCheckAfter && !pseudoTargets.Any(p => char.ToLower(afterRules.Board[p.Y, p.X]) == 'k'))
            {
                patterns.Add(english ? "Discovered Check!" : "Xeque Descoberto!");
            }

            return patterns;
        }

        // Coach aprimorado: explica o "POR QUÊ" estratégico e tático do lance e da recomendação da IA
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

            // Detecta padrões táticos executados pelo jogador (Garfo, Cravada, Espeto, Descoberto)
            var playerTactics = DetectTacticalPatterns(rulesBefore, from, to, promo, english: false);
            if (playerTactics.Count > 0)
                reasons.Add($"tática: {string.Join(" + ", playerTactics)}");

            // Explica desenvolvimento ou atividade no final
            if (char.ToUpper(movedPiece) is 'N' or 'B' && from.Y == 7 && to.Y < 7)
                reasons.Add("desenvolve peça menor rumo ao centro");
            else if (char.ToUpper(movedPiece) == 'K' && IsEndgamePhase(rulesBefore.Board))
                reasons.Add("ativa o Rei no final de partida");

            double delta = afterAnalysis.EvalWhitePawns - beforeAnalysis.EvalWhitePawns;
            if (!playedBest && delta < -0.50 && beforeAnalysis.BestMove.HasValue)
            {
                var bestFrom = beforeAnalysis.BestMove.Value.from;
                var bestTo = beforeAnalysis.BestMove.Value.to;
                string bestAlg = FormatMoveUciOrAlg(bestFrom, bestTo, beforeAnalysis.Promotion);
                string whyBest = ExplainWhyMoveIsRecommended(rulesBefore, bestFrom, bestTo, beforeAnalysis.Promotion);
                reasons.Add($"perdeu {Math.Abs(delta):0.00} peões — melhor era {bestAlg} porque {whyBest}");
            }

            if (reasons.Count == 0)
                reasons.Add(playedBest ? "manteve a melhor coordenação das peças" : "lance posicional");

            return string.Join(", ", reasons);
        }

        // Explica em linguagem simples POR QUE a IA recomenda determinado lance
        public static string ExplainWhyMoveIsRecommended(ChessRules rules, Point from, Point to, char? promo = null, bool english = false)
        {
            char piece = rules.Board[from.Y, from.X];
            char target = rules.Board[to.Y, to.X];
            char upper = char.ToUpper(piece);

            var tactics = DetectTacticalPatterns(rules, from, to, promo, english);
            if (tactics.Count > 0)
                return english ? $"executes {string.Join(" & ", tactics)}" : $"executa {string.Join(" e ", tactics)}";

            if (upper == 'K' && Math.Abs(to.X - from.X) == 2)
                return english ? "castles the King to safety and activates the Rook" : "coloca o Rei em segurança com o Roque e ativa a Torre";

            if (upper == 'P' && (to.Y == 0 || to.Y == 7))
                return english ? "promotes the pawn to a decisive piece" : "promove o peão ganhando material decisivo";

            if (target != '.')
                return english ? $"captures enemy {PieceNamePt(target)} winning material/initiative" : $"captura {PieceNamePt(target)} adversário ganhando material/iniciativa";

            if ((to.X == 3 || to.X == 4) && (to.Y == 3 || to.Y == 4))
                return english ? "fights directly for central control (d4/e4/d5/e5)" : "domina diretamente as casas centrais do tabuleiro";

            if (upper is 'N' or 'B' && (from.Y == 7 || from.Y == 0))
                return english ? "develops a minor piece to an active square" : "desenvolve uma peça menor para uma casa ativa";

            if (upper == 'R' && (to.X == 3 || to.X == 4))
                return english ? "places the Rook on a central file" : "centraliza a Torre nas colunas 'd' ou 'e'";

            if (upper == 'K' && IsEndgamePhase(rules.Board))
                return english ? "centralizes the King to support pawns in the endgame" : "centraliza o Rei para apoiar os peões no final";

            return english ? "improves piece coordination and prophylaxis" : "melhora a coordenação das peças e restringe o adversário";
        }

        // Relatório Completo da Posição com Top 3 Lances (MultiPV), Árvore de Variantes, Explicação e Lição de Abertura
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

            var sb = new StringBuilder();
            sb.AppendLine($"=== ANÁLISE PROFUNDA DA IA ({phase}) ===");
            sb.AppendLine($"• Avaliação Global: {analysis.EvalText} (Chance estimada das Brancas: {EvalToWinProbability(analysis.EvalWhitePawns) * 100:0.0}%)");
            sb.AppendLine($"• Material: {matText} | Abertura: {StudyLibrary.DetectOpening(rules.SanMoves)}");

            if (analysis.BestMove.HasValue)
            {
                string why = ExplainWhyMoveIsRecommended(rules, analysis.BestMove.Value.from, analysis.BestMove.Value.to, analysis.Promotion);
                string bestStr = FormatMoveUciOrAlg(analysis.BestMove.Value.from, analysis.BestMove.Value.to, analysis.Promotion);
                sb.AppendLine($"• Por que jogar {bestStr}? Porque {why}.");
            }

            if (analysis.TopMovesSummary.Count > 0)
                sb.AppendLine($"• Melhores 3 Lances (MultiPV): {string.Join("  |  ", analysis.TopMovesSummary)}");

            if (analysis.VariationTreeLines.Count > 0)
            {
                sb.AppendLine("• Árvore de Variantes Calculadas:");
                foreach (var branch in analysis.VariationTreeLines)
                    sb.AppendLine($"    ├─ {branch}");
            }
            else if (!string.IsNullOrWhiteSpace(analysis.PrincipalVariation))
            {
                sb.AppendLine($"• Linha Principal (PV): {analysis.PrincipalVariation}");
            }

            sb.AppendLine($"• {StudyLibrary.GetOpeningLesson(rules.SanMoves)}");
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

        // Analisa a posição com dificuldade bem diferenciada:
        // 0 = Iniciante (~800 Elo), 1 = Amador (~1250 Elo), 2 = Clube (~1750 Elo), 3 = Mestre (~2400+ Elo)
        public EngineAnalysisResult Analyze(ChessRules rules, bool forWhite, int difficultyLevel,
            bool isHintOrEvaluationOnly = false, CancellationToken cancellationToken = default)
        {
            int skill = isHintOrEvaluationOnly ? 20 : difficultyLevel switch { 0 => 0, 1 => 5, 2 => 12, _ => 20 };
            int sfDepth = isHintOrEvaluationOnly ? 13 : difficultyLevel switch { 0 => 2, 1 => 5, 2 => 10, _ => 15 };

            cancellationToken.ThrowIfCancellationRequested();
            var stockfish = QueryStockfish(rules, forWhite, skill, sfDepth, difficultyLevel, isHintOrEvaluationOnly, cancellationToken);
            return stockfish ?? QueryInternalEngine(rules, forWhite, difficultyLevel, isHintOrEvaluationOnly, cancellationToken);
        }

        private EngineAnalysisResult? QueryStockfish(ChessRules rules, bool forWhite, int skill, int depth,
            int difficultyLevel, bool isHintOrEvaluationOnly, CancellationToken cancellationToken)
        {
            stockfishLock.Wait(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (stockfishUnavailable) return null;
                if (stockfishProcess == null) InitStockfish(cancellationToken);
                if (stockfishProcess == null) return null;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                string fen = rules.ToFen();
                var sw = stockfishProcess.StandardInput;
                var sr = stockfishProcess.StandardOutput;

                bool limitStrength = !isHintOrEvaluationOnly && difficultyLevel < 3;
                int targetElo = difficultyLevel switch
                {
                    0 => 1320, // Iniciante (combinado com seleção humana)
                    1 => 1450, // Amador
                    2 => 1850, // Clube
                    _ => 2850  // Mestre
                };

                lock (stdinLock)
                {
                    // MultiPV = 3 para obter os 3 melhores lances e a árvore de variantes!
                    sw.WriteLine("setoption name MultiPV value 3");
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
                    sw.WriteLine($"go depth {depth} movetime {(isHintOrEvaluationOnly ? 2000 : 2800)}");
                }

                string lastInfoLine = "";
                long nodes = 0;
                var multiPvData = new Dictionary<int, (string firstMove, double whitePawns, string evalStr, string pvLine)>();
                string? line;

                while ((line = ReadEngineLine(sr, timeout.Token)) != null)
                {
                    if (line.StartsWith("info") && line.Contains(" pv "))
                    {
                        lastInfoLine = line;
                        int mpv = 1;
                        long lineNodes = nodes;
                        double wp = 0.0;
                        string evStr = "+0.00";

                        int pvIdx = line.IndexOf(" pv ", StringComparison.Ordinal);
                        string pv = pvIdx >= 0 ? line[(pvIdx + 4)..].Trim() : "";
                        string firstMv = pv.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

                        var parts = line.Split(' ');
                        for (int i = 0; i < parts.Length - 1; i++)
                        {
                            if (parts[i] == "multipv" && int.TryParse(parts[i + 1], out int parsedMpv))
                                mpv = parsedMpv;
                            if (parts[i] == "nodes")
                                long.TryParse(parts[i + 1], out lineNodes);
                            if (parts[i] == "cp" && int.TryParse(parts[i + 1], out int cp))
                            {
                                wp = (forWhite ? cp : -cp) / 100.0;
                                evStr = (wp >= 0 ? "+" : "") + wp.ToString("0.00", CultureInfo.InvariantCulture);
                            }
                            else if (parts[i] == "mate" && int.TryParse(parts[i + 1], out int mateIn))
                            {
                                int m = forWhite ? mateIn : -mateIn;
                                wp = m > 0 ? 99.0 : -99.0;
                                evStr = $"#{m}";
                            }
                        }

                        nodes = Math.Max(nodes, lineNodes);
                        if (!string.IsNullOrEmpty(firstMv))
                            multiPvData[mpv] = (firstMv, wp, evStr, pv);
                    }
                    else if (line.StartsWith("bestmove"))
                    {
                        string[] parts = line.Split(' ');
                        if (parts.Length >= 2 && parts[1].Length >= 4 && parts[1] != "(none)")
                        {
                            string mv = parts[1];
                            var parsedBest = ParseUciMoveString(mv, forWhite);
                            if (!parsedBest.HasValue) break;

                            var chosenMove = (parsedBest.Value.from, parsedBest.Value.to);
                            char? promo = parsedBest.Value.promo;

                            double whitePawns = multiPvData.TryGetValue(1, out var pv1) ? pv1.whitePawns : 0.0;
                            string evalStr = multiPvData.TryGetValue(1, out pv1) ? pv1.evalStr : "+0.00";
                            string pvLine = multiPvData.TryGetValue(1, out pv1) ? pv1.pvLine : mv;

                            (Point from, Point to)? secondMove = null;
                            char? secondPromo = null;
                            string secondEval = "";
                            if (multiPvData.TryGetValue(2, out var pv2))
                            {
                                var p2 = ParseUciMoveString(pv2.firstMove, forWhite);
                                if (p2.HasValue)
                                {
                                    secondMove = (p2.Value.from, p2.Value.to);
                                    secondPromo = p2.Value.promo;
                                    secondEval = pv2.evalStr;
                                }
                            }

                            var topSummary = new List<string>();
                            var treeLines = new List<string>();
                            foreach (var kv in multiPvData.OrderBy(k => k.Key).Take(3))
                            {
                                var pm = ParseUciMoveString(kv.Value.firstMove, forWhite);
                                string alg = pm.HasValue
                                    ? FormatMoveUciOrAlg(pm.Value.from, pm.Value.to, pm.Value.promo)
                                    : kv.Value.firstMove;
                                topSummary.Add($"{kv.Key}º {alg} ({kv.Value.evalStr})");
                                treeLines.Add($"[Linha {kv.Key} | {kv.Value.evalStr}] {kv.Value.pvLine}");
                            }

                            // Nos níveis Iniciante (0) e Amador (1), usa seleção humana ponderada para diferenciar bem os níveis
                            if (!isHintOrEvaluationOnly && difficultyLevel <= 1)
                            {
                                double humanChance = difficultyLevel == 0 ? 0.75 : 0.40;
                                if (rng.NextDouble() < humanChance)
                                {
                                    var humanFallback = QueryInternalEngine(rules, forWhite, difficultyLevel, false, cancellationToken);
                                    if (humanFallback.BestMove.HasValue)
                                    {
                                        chosenMove = humanFallback.BestMove.Value;
                                        promo = humanFallback.Promotion;
                                    }
                                }
                            }

                            if (topSummary.Count == 0)
                                topSummary.Add($"1º {FormatMoveUciOrAlg(chosenMove.from, chosenMove.to, promo)} ({evalStr})");

                            return new EngineAnalysisResult
                            {
                                BestMove = chosenMove,
                                Promotion = promo,
                                SecondBestMove = secondMove,
                                SecondBestPromotion = secondPromo,
                                SecondBestEvalText = secondEval,
                                EvalWhitePawns = whitePawns,
                                EvalText = evalStr,
                                Nodes = nodes,
                                PrincipalVariation = pvLine,
                                TopMovesSummary = topSummary,
                                VariationTreeLines = treeLines,
                                LogLine = $"[Stockfish MultiPV d={depth}] {mv} ({evalStr}) | {lastInfoLine}"
                            };
                        }
                        break;
                    }
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                ShutdownStockfishProcess();
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (Exception ex) when (!disposed && ex is IOException or InvalidOperationException or OperationCanceledException)
            {
                stockfishUnavailable = true;
                ShutdownStockfishProcess();
            }
            finally
            {
                if (disposed) ShutdownStockfishProcess();
                stockfishLock.Release();
                // Dispose can race with the last check above while this call still owns the lock.
                if (disposed && stockfishLock.Wait(0))
                {
                    try { ShutdownStockfishProcess(); }
                    finally { stockfishLock.Release(); }
                }
            }
            return null;
        }

        private static (Point from, Point to, char? promo)? ParseUciMoveString(string mv, bool forWhite)
        {
            if (string.IsNullOrWhiteSpace(mv) || mv.Length < 4) return null;
            int fCol = mv[0] - 'a';
            int fRow = 8 - (mv[1] - '0');
            int tCol = mv[2] - 'a';
            int tRow = 8 - (mv[3] - '0');
            if (fCol < 0 || fCol > 7 || fRow < 0 || fRow > 7 || tCol < 0 || tCol > 7 || tRow < 0 || tRow > 7)
                return null;

            char? promo = null;
            if (mv.Length >= 5 && "qrbnQRBN".IndexOf(mv[4]) >= 0)
                promo = forWhite ? char.ToUpper(mv[4]) : char.ToLower(mv[4]);

            return (new Point(fCol, fRow), new Point(tCol, tRow), promo);
        }

        private EngineAnalysisResult QueryInternalEngine(ChessRules rules, bool forWhite, int difficultyLevel,
            bool isHintOrEvaluationOnly, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var legalBaseMoves = rules.GetAllLegalMoves(forWhite);
            if (legalBaseMoves.Count == 0)
            {
                bool inCheck = rules.IsKingInCheck(rules.Board, forWhite);
                double mateOrDraw = inCheck ? (forWhite ? -99.0 : 99.0) : 0.0;
                string txt = inCheck ? (forWhite ? "#-0" : "#0") : "0.00";
                return new EngineAnalysisResult { BestMove = null, EvalWhitePawns = mateOrDraw, EvalText = txt };
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

            // Profundidade escalonada por dificuldade (Iniciante=2, Amador=2, Clube=4, Mestre/Análise=4)
            int searchDepth = isHintOrEvaluationOnly ? 4 : difficultyLevel switch
            {
                0 => 2, // Iniciante
                1 => 2, // Amador
                2 => 4, // Clube
                _ => 4  // Mestre
            };

            byte rootCastling = PackCastlingRights(rules.CastleWK, rules.CastleWQ, rules.CastleBK, rules.CastleBQ);
            long searchedNodes = 0;
            var scoredMoves = new List<(SearchMove move, int whiteScore, string pvLine)>();

            foreach (var mv in rootCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                char[,] copy = ApplyMoveOnCopy(rules.Board, mv.From, mv.To, mv.Promotion, out Point? nextEp, rootCastling, out byte nextCastling);

                int repetitionPenalty = 0;
                string nextBoardHash = QuickBoardKey(copy);
                if (rules.PositionHistory.Any(h => h.StartsWith(nextBoardHash)))
                    repetitionPenalty = forWhite ? -65 : 65;

                int val = SearchAlphaBeta(
                    rules,
                    copy,
                    searchDepth - 1,
                    -1000000,
                    1000000,
                    !forWhite,
                    nextEp,
                    nextCastling,
                    ref searchedNodes,
                    out string childPv,
                    cancellationToken);

                if (Math.Abs(val) < 40000)
                    val += repetitionPenalty;

                string rootAlg = FormatMoveUciOrAlg(mv.From, mv.To, mv.Promotion);
                string fullPv = string.IsNullOrEmpty(childPv) ? rootAlg : $"{rootAlg} -> {childPv}";
                scoredMoves.Add((mv, val, fullPv));
            }

            if (forWhite)
                scoredMoves = scoredMoves.OrderByDescending(m => m.whiteScore).ToList();
            else
                scoredMoves = scoredMoves.OrderBy(m => m.whiteScore).ToList();

            var topMove = scoredMoves[0];
            var selectedMove = SelectHumanWeightedMove(scoredMoves, forWhite, difficultyLevel, isHintOrEvaluationOnly);

            (Point from, Point to)? secondMove = null;
            char? secondPromo = null;
            string secondEvalText = "";
            if (scoredMoves.Count > 1)
            {
                secondMove = (scoredMoves[1].move.From, scoredMoves[1].move.To);
                secondPromo = scoredMoves[1].move.Promotion;
                secondEvalText = FormatScoreText(scoredMoves[1].whiteScore);
            }

            double evalPawns = FormatScoreToWhitePawns(topMove.whiteScore);
            string formattedEval = FormatScoreText(topMove.whiteScore);
            string moveAlg = FormatMoveUciOrAlg(selectedMove.From, selectedMove.To, selectedMove.Promotion);

            var topSummary = scoredMoves
                .Take(3)
                .Select((m, idx) => $"{idx + 1}º {FormatMoveUciOrAlg(m.move.From, m.move.To, m.move.Promotion)} ({FormatScoreText(m.whiteScore)})")
                .ToList();

            var variationTree = scoredMoves
                .Take(3)
                .Select((m, idx) => $"[Variante {idx + 1} | {FormatScoreText(m.whiteScore)}] {m.pvLine}")
                .ToList();

            string log = $"[Engine MultiPV d={searchDepth}] Best:{moveAlg} Eval:{formattedEval} Nodes:{searchedNodes} | Top 3: {string.Join(", ", topSummary)}";

            return new EngineAnalysisResult
            {
                BestMove = (selectedMove.From, selectedMove.To),
                Promotion = selectedMove.Promotion,
                SecondBestMove = secondMove,
                SecondBestPromotion = secondPromo,
                SecondBestEvalText = secondEvalText,
                EvalWhitePawns = evalPawns,
                EvalText = formattedEval,
                Nodes = searchedNodes,
                PrincipalVariation = topMove.pvLine,
                TopMovesSummary = topSummary,
                VariationTreeLines = variationTree,
                LogLine = log
            };
        }

        // Seleção de lances humanos diferenciada para os 4 níveis (Iniciante, Amador, Clube, Mestre)
        private static SearchMove SelectHumanWeightedMove(
            List<(SearchMove move, int whiteScore, string pvLine)> scoredMoves,
            bool forWhite,
            int difficultyLevel,
            bool isHintOrEvaluationOnly)
        {
            if (isHintOrEvaluationOnly || difficultyLevel >= 3 || scoredMoves.Count <= 1)
                return scoredMoves[0].move; // Mestre ou Dica: joga sempre o #1 objetivo

            int bestScore = scoredMoves[0].whiteScore;

            // Clube (2): joga quase sempre o #1, mas se o #2 estiver a menos de 0.25 peões, varia humanamente (30%)
            if (difficultyLevel == 2)
            {
                int diff2 = Math.Abs(bestScore - scoredMoves[1].whiteScore);
                if (diff2 <= 25 && rng.NextDouble() < 0.30)
                    return scoredMoves[1].move;
                return scoredMoves[0].move;
            }

            // Amador (1): escolhe entre os top 3 lances razoáveis (que não entreguem mais de 1.5 peões de uma vez)
            if (difficultyLevel == 1)
            {
                var reasonable = scoredMoves
                    .Take(4)
                    .Where(m => Math.Abs(bestScore - m.whiteScore) <= 160)
                    .ToList();
                if (reasonable.Count > 1 && rng.NextDouble() < 0.55)
                    return reasonable[rng.Next(1, reasonable.Count)].move;
                return scoredMoves[0].move;
            }

            // Iniciante (0): escolhe entre o 2º e o 6º lance (evitando levar mate imediato se houver outra opção)
            var nonMateCandidates = scoredMoves
                .Skip(1)
                .Take(5)
                .Where(m => Math.Abs(m.whiteScore) < 40000)
                .ToList();

            if (nonMateCandidates.Count > 0)
                return nonMateCandidates[rng.Next(nonMateCandidates.Count)].move;

            return scoredMoves[Math.Min(1, scoredMoves.Count - 1)].move;
        }

        private static double FormatScoreToWhitePawns(int whiteScore)
        {
            if (whiteScore >= 40000) return 99.0;
            if (whiteScore <= -40000) return -99.0;
            return whiteScore / 100.0;
        }

        private static string FormatScoreText(int whiteScore)
        {
            if (whiteScore >= 40000) return "#+";
            if (whiteScore <= -40000) return "#-";
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
            out string bestPvLine,
            CancellationToken cancellationToken)
        {
            if ((nodes++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            bestPvLine = "";
            if (depth <= 0)
                return QuiescenceCaptures(rules, b, alpha, beta, whiteToMove, epTarget, 3, ref nodes, cancellationToken);

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
                    int eval = SearchAlphaBeta(rules, copy, depth - 1, alpha, beta, false, nextEp, nextCastling, ref nodes, out string childPv, cancellationToken);
                    if (eval > maxEval)
                    {
                        maxEval = eval;
                        string alg = FormatMoveUciOrAlg(mv.From, mv.To, mv.Promotion);
                        bestPvLine = string.IsNullOrEmpty(childPv) ? alg : $"{alg} -> {childPv}";
                    }
                    if (eval > alpha) alpha = eval;
                    if (beta <= alpha) break;
                }

                if (legalMovesFound == 0)
                    return rules.IsKingInCheck(b, true) ? (-50000 - depth * 100) : 0;

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
                    int eval = SearchAlphaBeta(rules, copy, depth - 1, alpha, beta, true, nextEp, nextCastling, ref nodes, out string childPv, cancellationToken);
                    if (eval < minEval)
                    {
                        minEval = eval;
                        string alg = FormatMoveUciOrAlg(mv.From, mv.To, mv.Promotion);
                        bestPvLine = string.IsNullOrEmpty(childPv) ? alg : $"{alg} -> {childPv}";
                    }
                    if (eval < beta) beta = eval;
                    if (beta <= alpha) break;
                }

                if (legalMovesFound == 0)
                    return rules.IsKingInCheck(b, false) ? (50000 + depth * 100) : 0;

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
            CancellationToken cancellationToken)
        {
            if ((nodes++ & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
            int standPat = EvaluateBoardWhiteRelative(b);
            if (qDepth <= 0) return standPat;

            if (whiteToMove)
            {
                if (standPat >= beta) return beta;
                if (alpha < standPat) alpha = standPat;

                var captures = GeneratePseudoLegalOrdered(b, true, epTarget, 0, capturesOnly: true);
                foreach (var cap in captures)
                {
                    char[,] copy = ApplyMoveOnCopy(b, cap.From, cap.To, cap.Promotion, out Point? nextEp, 0, out _);
                    if (rules.IsKingInCheck(copy, true)) continue;

                    int score = QuiescenceCaptures(rules, copy, alpha, beta, false, nextEp, qDepth - 1, ref nodes, cancellationToken);
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

                    int score = QuiescenceCaptures(rules, copy, alpha, beta, true, nextEp, qDepth - 1, ref nodes, cancellationToken);
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

        public static bool IsEndgamePhase(char[,] b)
        {
            int nonPawnMaterial = 0;
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = b[r, c];
                    if (p == '.' || char.ToLower(p) is 'p' or 'k') continue;
                    nonPawnMaterial += PieceBaseValue(p);
                }
            }
            return nonPawnMaterial <= 2600;
        }

        private static bool IsPassedPawn(char[,] b, int row, int col, bool isWhite)
        {
            char enemyPawn = isWhite ? 'p' : 'P';
            int startR = isWhite ? row - 1 : row + 1;
            int endR = isWhite ? 0 : 7;
            int step = isWhite ? -1 : 1;

            for (int r = startR; isWhite ? (r >= endR) : (r <= endR); r += step)
            {
                for (int c = Math.Max(0, col - 1); c <= Math.Min(7, col + 1); c++)
                {
                    if (b[r, c] == enemyPawn)
                        return false;
                }
            }
            return true;
        }

        // Avaliação Completa com Heurística de Finais (KingEndgameTable, Peões Passados e Mop-up de Rei no Final)
        public static int EvaluateBoardWhiteRelative(char[,] b)
        {
            bool isEndgame = IsEndgamePhase(b);
            int score = 0;
            Point? whiteKing = null;
            Point? blackKing = null;

            int[] passedPawnBonusByRank = { 0, 90, 55, 32, 18, 8, 4, 0 };

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = b[r, c];
                    if (p == '.') continue;

                    if (p == 'K') whiteKing = new Point(c, r);
                    else if (p == 'k') blackKing = new Point(c, r);

                    bool isWhite = char.IsUpper(p);
                    int tableRow = isWhite ? r : (7 - r);
                    int baseVal = PieceBaseValue(p);
                    char lower = char.ToLower(p);

                    int posBonus = lower switch
                    {
                        'p' => PawnTable[tableRow, c],
                        'n' => KnightTable[tableRow, c],
                        'b' => BishopTable[tableRow, c],
                        'r' => RookTable[tableRow, c],
                        'q' => (BishopTable[tableRow, c] + RookTable[tableRow, c]) / 2,
                        'k' => isEndgame ? KingEndgameTable[tableRow, c] : KingMiddlegameTable[tableRow, c],
                        _ => 0
                    };

                    // Bônus para Peão Passado (especialmente forte no Final)
                    if (lower == 'p' && IsPassedPawn(b, r, c, isWhite))
                    {
                        int bonus = passedPawnBonusByRank[tableRow];
                        posBonus += isEndgame ? (int)(bonus * 1.4) : bonus;
                    }

                    int total = baseVal + posBonus;
                    score += isWhite ? total : -total;
                }
            }

            // Heurística de Final Ganho (Mop-Up): empurra o Rei perdedor para a borda e aproxima o Rei vencedor
            if (isEndgame && whiteKing.HasValue && blackKing.HasValue && Math.Abs(score) >= 300)
            {
                int kingDist = Math.Abs(whiteKing.Value.X - blackKing.Value.X) + Math.Abs(whiteKing.Value.Y - blackKing.Value.Y);
                if (score > 0)
                {
                    int blackDistFromCenter = Math.Abs(3 - blackKing.Value.X) + Math.Abs(3 - blackKing.Value.Y);
                    score += blackDistFromCenter * 8 + (14 - kingDist) * 4;
                }
                else
                {
                    int whiteDistFromCenter = Math.Abs(3 - whiteKing.Value.X) + Math.Abs(3 - whiteKing.Value.Y);
                    score -= whiteDistFromCenter * 8 + (14 - kingDist) * 4;
                }
            }

            return score;
        }
    }
}
