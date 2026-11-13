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
        Color BadgeColor,
        double EvalBefore,
        double EvalAfter,
        double Accuracy,
        string BestAlternative,
        string CoachNote);

    public class AnalysisController : IDisposable
    {
        public EngineService Engine { get; } = new EngineService();
        public List<MoveEvaluationRecord> MoveEvaluations { get; } = new List<MoveEvaluationRecord>();

        public bool IsUsingStockfish => Engine.IsUsingStockfish;

        public void ClearHistory() => MoveEvaluations.Clear();

        public void RemoveLastEvaluation()
        {
            if (MoveEvaluations.Count > 0)
                MoveEvaluations.RemoveAt(MoveEvaluations.Count - 1);
        }

        public void StopSearch() => Engine.StopSearch();

        public void RecordCheckmateMove(int moveNumber, string whiteSan, bool english)
        {
            string desc = english ? "Checkmate!" : "Xeque-Mate!";
            string note = english ? "decisive checkmate move" : "lance decisivo de xeque-mate";
            MoveEvaluations.Add(new MoveEvaluationRecord(
                moveNumber, whiteSan, "!", desc, Color.FromArgb(20, 150, 80), 10.0, 99.0, 100.0, "-", note));
        }

        public async Task<(EngineAnalysisResult bestBefore, EngineAnalysisResult evalAfter, EngineAnalysisResult botPlay, MoveEvaluationRecord record)>
            EvaluatePlayerTurnAndBotReplyAsync(
                GameSnapshot snapshotBeforeWhite,
                ChessRules rulesAfterWhite,
                Point playerFrom,
                Point playerTo,
                char? playerPromo,
                string whiteSan,
                bool isBookMove,
                int moveNumber,
                int difficultyIndex)
        {
            var rulesBefore = new ChessRules();
            rulesBefore.RestoreSnapshot(snapshotBeforeWhite);

            var bestWhiteBefore = await Task.Run(() => Engine.Analyze(rulesBefore, true, difficultyIndex, isHintOrEvaluationOnly: true));

            var botRulesForEval = rulesAfterWhite.Clone();
            EngineAnalysisResult evalAfterAnalysis;
            EngineAnalysisResult botAnalysis;

            if (difficultyIndex < 3)
            {
                evalAfterAnalysis = await Task.Run(() => Engine.Analyze(botRulesForEval, false, difficultyIndex, isHintOrEvaluationOnly: true));
                var botPlayCopy = rulesAfterWhite.Clone();
                botAnalysis = await Task.Run(() => Engine.Analyze(botPlayCopy, false, difficultyIndex, isHintOrEvaluationOnly: false));
            }
            else
            {
                botAnalysis = await Task.Run(() => Engine.Analyze(botRulesForEval, false, difficultyIndex, isHintOrEvaluationOnly: false));
                evalAfterAnalysis = botAnalysis;
            }

            bool samePromo = bestWhiteBefore.Promotion == null ||
                             playerPromo == null ||
                             char.ToUpper(bestWhiteBefore.Promotion.Value) == char.ToUpper(playerPromo.Value);

            bool playedBest = bestWhiteBefore.BestMove.HasValue &&
                              bestWhiteBefore.BestMove.Value.from == playerFrom &&
                              bestWhiteBefore.BestMove.Value.to == playerTo &&
                              samePromo;

            bool isTactical = whiteSan.Contains('x') || whiteSan.Contains('+');
            var classification = EngineService.ClassifyMove(
                bestWhiteBefore.EvalWhitePawns,
                evalAfterAnalysis.EvalWhitePawns,
                playedBest,
                isBookMove,
                isTactical);

            double moveAcc = EngineService.ComputeMoveAccuracy(
                bestWhiteBefore.EvalWhitePawns,
                evalAfterAnalysis.EvalWhitePawns,
                playedBest,
                isBookMove);

            string bestAlternativeStr = bestWhiteBefore.BestMove.HasValue
                ? EngineService.FormatMoveUciOrAlg(bestWhiteBefore.BestMove.Value.from, bestWhiteBefore.BestMove.Value.to, bestWhiteBefore.Promotion)
                : "-";

            string coachNote = EngineService.ExplainMoveCoach(
                rulesBefore,
                playerFrom,
                playerTo,
                playerPromo,
                bestWhiteBefore,
                evalAfterAnalysis,
                playedBest,
                isBookMove);

            var record = new MoveEvaluationRecord(
                moveNumber,
                whiteSan,
                classification.badge,
                classification.description,
                classification.color,
                bestWhiteBefore.EvalWhitePawns,
                evalAfterAnalysis.EvalWhitePawns,
                moveAcc,
                bestAlternativeStr,
                coachNote);

            MoveEvaluations.Add(record);
            return (bestWhiteBefore, evalAfterAnalysis, botAnalysis, record);
        }

        public Task<EngineAnalysisResult> AnalyzePositionAsync(ChessRules rules, bool forWhite, int difficultyIndex, bool isHintOrEvalOnly)
        {
            var clone = rules.Clone();
            return Task.Run(() => Engine.Analyze(clone, forWhite, difficultyIndex, isHintOrEvalOnly));
        }

        public double GetAverageAccuracy() =>
            MoveEvaluations.Count > 0 ? MoveEvaluations.Average(m => m.Accuracy) : 100.0;

        public string BuildGameAccuracySummaryText(IReadOnlyList<string> sanMoves, bool english)
        {
            if (MoveEvaluations.Count == 0)
                return english ? "No moves evaluated in this game yet." : "Ainda não há lances avaliados nesta partida.";

            double avgAcc = GetAverageAccuracy();
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
            if (english)
            {
                sb.AppendLine("=== AI GAME ACCURACY REPORT ===");
                sb.AppendLine($"White Average Accuracy: {avgAcc:0.1}% ({MoveEvaluations.Count} moves analyzed)");
                sb.AppendLine($"Opening played: {StudyLibrary.DetectOpening(sanMoves.ToList())}");
                sb.AppendLine();
                sb.AppendLine($"• Brilliant (!!): {brilliant}   |   Best Moves (!): {best}   |   Book (📖): {book}");
                sb.AppendLine($"• Good Moves: {good}        |   Inaccuracies (?!): {inaccuracies}");
                sb.AppendLine($"• Mistakes (?): {mistakes}           |   Blunders (??): {blunders}");
                if (worstDrop < -0.45)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Critical moment: Move {worst.MoveNumber}. {worst.WhiteSan} ({worst.Description}) — {worst.CoachNote}.");
                    sb.AppendLine($"AI Recommendation: {worst.BestAlternative}.");
                }
            }
            else
            {
                sb.AppendLine("=== RELATÓRIO DE ANÁLISE DA PARTIDA (IA) ===");
                sb.AppendLine($"Precisão Média das Brancas: {avgAcc:0.1}% ({MoveEvaluations.Count} lances analisados)");
                sb.AppendLine($"Abertura jogada: {StudyLibrary.DetectOpening(sanMoves.ToList())}");
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
            }

            return sb.ToString().TrimEnd();
        }

        public void Dispose() => Engine.Dispose();
    }
}