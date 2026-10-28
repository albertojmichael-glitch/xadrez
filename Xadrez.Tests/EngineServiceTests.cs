using System.Drawing;
using Xunit;
using XadrezApp;

namespace XadrezApp.Tests
{
    public class EngineServiceTests
    {
        [Fact]
        public void EvalToWinProbability_ShouldBeSymmetricAroundZeroAndClamped()
        {
            double evenProb = EngineService.EvalToWinProbability(0.0);
            Assert.Equal(0.50, evenProb, precision: 4);

            double whiteAdv = EngineService.EvalToWinProbability(3.0);
            double blackAdv = EngineService.EvalToWinProbability(-3.0);
            Assert.True(whiteAdv > 0.70);
            Assert.True(blackAdv < 0.30);
            Assert.Equal(1.0, whiteAdv + blackAdv, precision: 4);

            // Verifica o clamp em +/- 15 peões
            Assert.Equal(EngineService.EvalToWinProbability(15.0), EngineService.EvalToWinProbability(25.0));
        }

        [Fact]
        public void ComputeMoveAccuracy_ShouldReturn100ForBestOrBookAndPenalizeBlunders()
        {
            Assert.Equal(100.0, EngineService.ComputeMoveAccuracy(0.2, 0.2, playedBest: true));
            Assert.Equal(100.0, EngineService.ComputeMoveAccuracy(0.2, 0.0, playedBest: false, isBookMove: true));
            Assert.Equal(100.0, EngineService.ComputeMoveAccuracy(0.0, 0.5, playedBest: false));

            double blunderAcc = EngineService.ComputeMoveAccuracy(0.5, -4.0, playedBest: false);
            Assert.InRange(blunderAcc, 0.0, 45.0);

            // Também deve calcular corretamente a precisão para as Pretas (forWhite: false)
            Assert.Equal(100.0, EngineService.ComputeMoveAccuracy(-0.3, -0.5, playedBest: false, forWhite: false));
            double blackBlunderAcc = EngineService.ComputeMoveAccuracy(-0.5, 3.5, playedBest: false, forWhite: false);
            Assert.InRange(blackBlunderAcc, 0.0, 45.0);
        }

        [Fact]
        public void ClassifyMove_ShouldAssignCorrectBadgesForAllCategories()
        {
            // Xeque-mate imediato
            var mate = EngineService.ClassifyMove(3.0, 99.0, playedEngineBestMove: true);
            Assert.Equal("!", mate.badge);
            Assert.Equal("Xeque-Mate!", mate.description);

            // Mate forçado em N lances (não imediato)
            var mateIn3 = EngineService.ClassifyMove(2.0, 99.0, playedEngineBestMove: true, mateInMoves: 3, isImmediateCheckmate: false);
            Assert.Equal("!", mateIn3.badge);
            Assert.Equal("Mate em 3", mateIn3.description);

            // Lance Teórico de Livro
            var book = EngineService.ClassifyMove(0.0, 0.0, playedEngineBestMove: false, isBookMove: true);
            Assert.Equal("📖", book.badge);

            // Lance Brilhante (!!)
            var brilliant = EngineService.ClassifyMove(0.5, 1.8, playedEngineBestMove: true, isBookMove: false, isSacrificeOrTactical: true);
            Assert.Equal("!!", brilliant.badge);

            // Posição já ganha (>= +3.0) não deve ser classificada como Brilhante mesmo com sacrifício
            var alreadyWinning = EngineService.ClassifyMove(4.2, 5.5, playedEngineBestMove: true, isBookMove: false, isSacrificeOrTactical: true);
            Assert.Equal("!", alreadyWinning.badge);

            // Melhor Lance (!)
            var best = EngineService.ClassifyMove(0.3, 0.3, playedEngineBestMove: true);
            Assert.Equal("!", best.badge);

            // Lance Bom
            var good = EngineService.ClassifyMove(0.4, 0.2, playedEngineBestMove: false);
            Assert.Equal("", good.badge);
            Assert.Equal("Lance Bom", good.description);

            // Imprecisão (?!)
            var inaccuracy = EngineService.ClassifyMove(0.5, -0.2, playedEngineBestMove: false);
            Assert.Equal("?!", inaccuracy.badge);

            // Erro (?)
            var mistake = EngineService.ClassifyMove(0.5, -1.0, playedEngineBestMove: false);
            Assert.Equal("?", mistake.badge);

            // Capivarada (??)
            var blunder = EngineService.ClassifyMove(0.5, -3.5, playedEngineBestMove: false);
            Assert.Equal("??", blunder.badge);
        }

        [Fact]
        public void IsMaterialSacrifice_ShouldDistinguishTrueSacrificeFromNormalCapture()
        {
            var rules = new ChessRules();

            // 1. Captura normal de peça de mesmo valor (ex.: exd5 na Escandinava) NÃO é sacrifício
            rules.LoadFen("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2");
            bool normalCapture = EngineService.IsMaterialSacrifice(rules, new Point(4, 4), new Point(3, 3), null, 0.3, 0.5);
            Assert.False(normalCapture);

            // 2. Sacrifício do Presente Grego (Bxh7+ entregando Bispo por peão na frente do Rei) É sacrifício real
            rules.LoadFen("r1bq1rk1/ppp2ppp/2np1n2/4p3/2B1P3/2NP1N2/PPP2PPP/R1BQK2R w KQ - 0 6");
            bool greekGift = EngineService.IsMaterialSacrifice(rules, new Point(2, 4), new Point(7, 1), null, 0.4, 1.5);
            Assert.True(greekGift);
        }

        [Fact]
        public void EvaluateBoardWhiteRelative_ShouldReturnZeroOnStartAndPositiveWithExtraWhiteMaterial()
        {
            var rules = new ChessRules();
            Assert.Equal(0, EngineService.EvaluateBoardWhiteRelative(rules.Board));

            // Posição sem a Dama preta em d8
            rules.LoadFen("rnb1kbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
            Assert.True(EngineService.EvaluateBoardWhiteRelative(rules.Board) >= 850);
        }

        [Fact]
        public void ExplainMoveCoach_AndBuildPositionReport_ShouldGeneratePortugueseDescriptions()
        {
            var rules = new ChessRules();
            var beforeRes = new EngineAnalysisResult { BestMove = (new Point(4, 6), new Point(4, 4)), EvalWhitePawns = 0.2 };
            var afterRes = new EngineAnalysisResult { EvalWhitePawns = 0.25, EvalText = "+0.25" };

            // Lance e2-e4 (controle do centro)
            string noteCenter = EngineService.ExplainMoveCoach(
                rules, new Point(4, 6), new Point(4, 4), null, beforeRes, afterRes, playedBest: true, isBookMove: true);
            Assert.Contains("abertura", noteCenter);
            Assert.Contains("centro", noteCenter);

            // Relatório de posição
            string report = EngineService.BuildPositionReport(rules, afterRes);
            Assert.Contains("=== ANÁLISE PROFUNDA DA IA", report);
            Assert.Contains("Avaliação Global: +0.25", report);
        }

        [Fact]
        public void Analyze_OnMateInOnePosition_ShouldFindWinningCheckmateMove()
        {
            using var engine = new EngineService();
            var rules = new ChessRules();

            // Mate do Corredor em 1 lance: Td1-d8# -> (3,7) para (3,0)
            rules.LoadFen("6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1");

            var analysis = engine.Analyze(rules, forWhite: true, difficultyLevel: 3, isHintOrEvaluationOnly: true);

            Assert.NotNull(analysis.BestMove);
            Assert.Equal(new Point(3, 7), analysis.BestMove.Value.from);
            Assert.Equal(new Point(3, 0), analysis.BestMove.Value.to);
            Assert.True(analysis.Nodes > 0);
        }
    }
}
