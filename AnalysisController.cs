using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XadrezApp
{
    public sealed record MoveEvaluationRecord(
        int MoveNumber,
        string WhiteSan,
        string Badge,
        string Description,
        double EvalBefore,
        double EvalAfter,
        double Accuracy,
        string BestAlternative,
        string CoachNote);

    // Dados do lance do jogador, usados para classificar a precisão depois que o bot responder
    public sealed record PlayerMoveInfo(
        GameSnapshot Before,
        Point From,
        Point To,
        char? Promotion,
        string San,
        bool IsBookMove);

    public sealed record TurnEvaluationSummary(
        EngineAnalysisResult? BestWhiteBefore,
        EngineAnalysisResult EvalAfterAnalysis,
        EngineAnalysisResult BotAnalysis,
        bool PlayedBest,
        (string badge, string description, Color color) Classification,
        string CoachNote,
        string BestAlternativeStr);

    public class AnalysisController : IDisposable
    {
        public EngineService Engine { get; } = new EngineService();
        public List<MoveEvaluationRecord> MoveEvaluations { get; } = new List<MoveEvaluationRecord>();

        public bool IsUsingStockfish => Engine.IsUsingStockfish;

        public void StopSearch() => Engine.StopSearch();

        public void ClearEvaluations() => MoveEvaluations.Clear();

        public void RemoveLastEvaluation()
        {
            if (MoveEvaluations.Count > 0)
                MoveEvaluations.RemoveAt(MoveEvaluations.Count - 1);
        }

        public void RecordCheckmateMove(int moveNum, string whiteSan)
        {
            MoveEvaluations.Add(new MoveEvaluationRecord(
                moveNum,
                whiteSan,
                "!",
                "Xeque-Mate!",
                10.0,
                99.0,
                100.0,
                "-",
                "lance decisivo de xeque-mate"));
        }

        public double GetAverageAccuracy() =>
            MoveEvaluations.Count > 0 ? MoveEvaluations.Average(m => m.Accuracy) : 100.0;

        // Executa todas as etapas de análise do turno (antes do lance das Brancas, avaliação pós-lance em força máxima
        // e escolha do lance do Bot na dificuldade selecionada), conferindo isCancelled() após cada await!
        public async Task<TurnEvaluationSummary?> EvaluateTurnAndChooseBotMoveAsync(
            ChessRules currentRules,
            PlayerMoveInfo? playerMove,
            int moveNumber,
            int diffIdx,
            Func<bool> isCancelled)
        {
            // 1. Avalia qual era o melhor lance das Brancas ANTES da sua jogada (sempre em força máxima)
            EngineAnalysisResult? bestWhiteBefore = null;
            ChessRules? rulesBefore = null;
            if (playerMove != null)
            {
                rulesBefore = new ChessRules();
                rulesBefore.RestoreSnapshot(playerMove.Before);
                bestWhiteBefore = await Task.Run(() => Engine.Analyze(rulesBefore, true, diffIdx, isHintOrEvaluationOnly: true));
                if (isCancelled()) return null;
            }

            // 2. Avalia a posição DEPOIS da sua jogada em FORÇA MÁXIMA (isHintOrEvaluationOnly: true)
            //    para classificar o lance das Brancas sem a distorção do bot enfraquecido (Fácil/Médio).
            var botRules = currentRules.Clone();
            EngineAnalysisResult evalAfterAnalysis;
            EngineAnalysisResult botAnalysis;

            if (playerMove != null && diffIdx < 3)
            {
                evalAfterAnalysis = await Task.Run(() => Engine.Analyze(botRules, false, diffIdx, isHintOrEvaluationOnly: true));
                if (isCancelled()) return null;

                var botPlayCopy = currentRules.Clone();
                botAnalysis = await Task.Run(() => Engine.Analyze(botPlayCopy, false, diffIdx, isHintOrEvaluationOnly: false));
                if (isCancelled()) return null;
            }
            else
            {
                botAnalysis = await Task.Run(() => Engine.Analyze(botRules, false, diffIdx, isHintOrEvaluationOnly: false));
                if (isCancelled()) return null;
                evalAfterAnalysis = botAnalysis;
            }

            bool samePromo = bestWhiteBefore?.Promotion == null ||
                             playerMove?.Promotion == null ||
                             char.ToUpper(bestWhiteBefore.Promotion.Value) == char.ToUpper(playerMove.Promotion.Value);

            bool playedBest = playerMove != null &&
                              bestWhiteBefore?.BestMove != null &&
                              bestWhiteBefore.BestMove.Value.from == playerMove.From &&
                              bestWhiteBefore.BestMove.Value.to == playerMove.To &&
                              samePromo;

            (string badge, string description, Color color) classification = ("", "", Color.Black);
            string coachNote = "";
            string bestAlternativeStr = "-";

            if (playerMove != null && bestWhiteBefore != null && rulesBefore != null)
            {
                bool isTactical = playerMove.San.Contains('x') || playerMove.San.Contains('+');
                classification = EngineService.ClassifyMove(
                    bestWhiteBefore.EvalWhitePawns,
                    evalAfterAnalysis.EvalWhitePawns,
                    playedBest,
                    playerMove.IsBookMove,
                    isTactical);

                double moveAcc = EngineService.ComputeMoveAccuracy(
                    bestWhiteBefore.EvalWhitePawns,
                    evalAfterAnalysis.EvalWhitePawns,
                    playedBest,
                    playerMove.IsBookMove);

                if (bestWhiteBefore.BestMove.HasValue)
                {
                    bestAlternativeStr = EngineService.FormatMoveUciOrAlg(
                        bestWhiteBefore.BestMove.Value.from,
                        bestWhiteBefore.BestMove.Value.to,
                        bestWhiteBefore.Promotion);
                }

                coachNote = EngineService.ExplainMoveCoach(
                    rulesBefore,
                    playerMove.From,
                    playerMove.To,
                    playerMove.Promotion,
                    bestWhiteBefore,
                    evalAfterAnalysis,
                    playedBest,
                    playerMove.IsBookMove);

                MoveEvaluations.Add(new MoveEvaluationRecord(
                    moveNumber,
                    playerMove.San,
                    classification.badge,
                    classification.description,
                    bestWhiteBefore.EvalWhitePawns,
                    evalAfterAnalysis.EvalWhitePawns,
                    moveAcc,
                    bestAlternativeStr,
                    coachNote));
            }

            return new TurnEvaluationSummary(
                bestWhiteBefore,
                evalAfterAnalysis,
                botAnalysis,
                playedBest,
                classification,
                coachNote,
                bestAlternativeStr);
        }

        public Task<EngineAnalysisResult> AnalyzePositionOrHintAsync(ChessRules analysisRules, bool sideToMove)
        {
            return Task.Run(() => Engine.Analyze(analysisRules, sideToMove, 3, isHintOrEvaluationOnly: true));
        }

        public string BuildPositionReport(ChessRules analysisRules, EngineAnalysisResult res)
        {
            return EngineService.BuildPositionReport(analysisRules, res);
        }

        public string BuildGameAccuracySummaryText(List<string> sanMoves)
        {
            if (MoveEvaluations.Count == 0)
                return "Ainda não há lances avaliados nesta partida.";

            double avgAcc = MoveEvaluations.Average(m => m.Accuracy);
            int brilliant = MoveEvaluations.Count(m => m.Badge == "!!");
            int best = MoveEvaluations.Count(m => m.Badge == "!");
            int book = MoveEvaluations.Count(m => m.Badge == "📖");
            int good = MoveEvaluations.Count(m => string.IsNullOrEmpty(m.Badge));
            int inaccuracies = MoveEvaluations.Count(m => m.Badge == "?!");
            int mistakes = MoveEvaluations.Count(m => m.Badge == "?");
            int blunders = MoveEvaluations.Count(m => m.Badge == "??");

            var worst = MoveEvaluations.OrderBy(m => m.EvalAfter - m.EvalBefore).First();
            double worstDrop = worst.EvalAfter - worst.EvalBefore;

            var sb = new StringBuilder();
            sb.AppendLine($"=== RELATÓRIO DE ANÁLISE DA PARTIDA (IA) ===");
            sb.AppendLine($"Precisão Média das Brancas: {avgAcc:0.1}% ({MoveEvaluations.Count} lances analisados)");
            sb.AppendLine($"Abertura jogada: {StudyLibrary.DetectOpening(sanMoves)}");
            sb.AppendLine();
            sb.AppendLine($"• Brilhantes (!!): {brilliant}   |   Melhores Lances (!): {best}   |   Teóricos (📖): {book}");
            sb.AppendLine($"• Lances Bons: {good}        |   Imprecisões (?!): {inaccuracies}");
            sb.AppendLine($"• Erros (?): {mistakes}           |   Capivaradas (??): {blunders}");

            if (worstDrop < -0.45)
            {
                sb.AppendLine();
                sb.AppendLine($"Momento crítico: Lance {worst.MoveNumber}. {worst.WhiteSan} ({worst.Description}) — {worst.CoachNote}.");
                sb.AppendLine($"Recomendação da IA naquele momento: {worst.BestAlternative}.");
            }
            return sb.ToString().TrimEnd();
        }

        public void Dispose()
        {
            try { Engine.Dispose(); } catch { }
        }
    }
}