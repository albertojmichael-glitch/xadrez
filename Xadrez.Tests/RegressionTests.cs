using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using Xunit;

namespace XadrezApp.Tests
{
    public class RegressionTests
    {
        [Theory]
        [InlineData(ChessRules.StandardStartFen, 1, 20)]
        [InlineData(ChessRules.StandardStartFen, 2, 400)]
        [InlineData(ChessRules.StandardStartFen, 3, 8902)]
        [InlineData("7k/P7/8/8/8/8/8/7K w - - 0 1", 1, 7)]
        [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 3, 2812)]
        [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 2, 2039)]
        public void Perft_CountsLegalNodes(string fen, int depth, long expected)
        {
            var rules = new ChessRules();
            Assert.True(rules.TryLoadFen(fen, out var error), error);
            Assert.Equal(expected, Perft(rules, depth));
            Assert.Equal(fen, rules.ToFen());
        }

        private static long Perft(ChessRules rules, int depth)
        {
            if (depth == 0) return 1;

            long count = 0;
            foreach (var (from, to) in rules.GetAllLegalMoves(rules.WhiteTurn))
            {
                bool promotion = char.ToUpperInvariant(rules.Board[from.Y, from.X]) == 'P' && to.Y is 0 or 7;
                foreach (char? choice in promotion ? new char?[] { 'Q', 'R', 'B', 'N' } : new char?[] { null })
                {
                    var next = rules.Clone();
                    next.ExecuteMove(from, to, choice);
                    count += Perft(next, depth - 1);
                }
            }
            return count;
        }

        [Theory]
        [InlineData("4r1k1/8/8/8/8/8/4R3/4K3 w - - 0 1", "e2", "d2")]
        [InlineData("4r1k1/8/8/3pP3/8/8/8/4K3 w - d6 0 2", "e5", "d6")]
        [InlineData("4kr2/8/8/8/8/8/8/R3K2R w KQ - 0 1", "e1", "g1")]
        public void KingSafety_BlocksPinnedMovesAndCastlingThroughCheck(string fen, string origin, string target)
        {
            var rules = new ChessRules();
            Assert.True(rules.TryLoadFen(fen, out var error), error);
            Point from = ChessRules.AlgebraicToSquare(origin)!.Value;
            Point to = ChessRules.AlgebraicToSquare(target)!.Value;
            Assert.DoesNotContain(to, rules.GetLegalMovesForSquare(from.X, from.Y, rules.WhiteTurn));
        }

        [Theory]
        [InlineData("7k/6Q1/6K1/8/8/8/8/8 b - - 0 1", true, "1-0")]
        [InlineData("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1", false, "1/2-1/2")]
        [InlineData("7k/7Q/6K1/8/8/8/8/8 b - - 0 1", true, "1-0")]
        public void Endgame_DistinguishesMateAndStalemate(string fen, bool check, string result)
        {
            var rules = new ChessRules();
            Assert.True(rules.TryLoadFen(fen, out var error), error);
            Assert.Equal(check, rules.IsKingInCheck(rules.Board, rules.WhiteTurn));
            Assert.True(rules.CheckEndGame(out _, out _));
            Assert.Equal(result, rules.GameResult);
        }

        [Fact]
        public void Archive_RoundTripsMetadataAndAnnotatedBlackToMovePgn()
        {
            const string fen = "4k3/8/8/8/8/8/8/4K2R b - - 0 14";
            var rules = new ChessRules();
            Assert.True(rules.TryLoadFen(fen, out var error), error);
            Play(rules, "e8e7");
            Play(rules, "h1h7");
            rules.GameResult = "1/2-1/2";
            var date = new DateTimeOffset(2026, 11, 17, 10, 11, 12, TimeSpan.FromHours(-3));
            var evaluation = new MoveEvaluationRecord(15, rules.SanMoves[1], "!", "Melhor lance",
                4, 5, 96, "h1h7", "<script>{teste}; com ponto e vírgula</script>");
            var game = GameArchive.Capture(Guid.NewGuid(), rules, date, "João \"A\"", @"IA\Local", "Clube",
                new[] { evaluation });

            string folder = Path.Combine(AppContext.BaseDirectory, "TestResults", Guid.NewGuid().ToString("N"));
            var archive = new GameArchive(folder);
            try
            {
                archive.Save(game);
                archive.Save(game);
                var restored = Assert.Single(archive.ReadAll());
                Assert.Equal(date, restored.StartedAt);
                Assert.Equal(date.Offset, restored.StartedAt.Offset);
                Assert.Equal(evaluation, Assert.Single(restored.Analysis));
                Assert.Single(GameArchive.Filter(new[] { restored }, "joao", "Posição personalizada"));
                Assert.Empty(GameArchive.Filter(new[] { restored }, "joao", "Italiana"));
                Assert.Contains("&lt;script&gt;", restored.ToHtml());
                Assert.DoesNotContain("<script>", restored.ToHtml());
                Assert.Contains("14... Ke7 15. Rh7+", restored.Pgn);
                Assert.Contains("[%eval 5.00]", restored.Pgn);
                var loaded = new ChessRules();
                Assert.True(loaded.TryLoadPgn(restored.Pgn, out var positions, out var tags, out error), error);
                Assert.Equal(rules.ToFen(), loaded.ToFen());
                Assert.Equal("1/2-1/2", positions[^1].GameResult);
                Assert.Equal("João \"A\"", tags["White"]);
                Assert.Equal(@"IA\Local", tags["Black"]);
                Assert.Equal("10:11:12", tags["Time"]);
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void Pgn_HandlesEndOfLineCommentsAndAdjacentBraceComments()
        {
            const string pgn = "1. e4{peão central}; esta linha tem comentário\n e5 2. Nf3 *";
            var rules = new ChessRules();
            Assert.True(rules.TryLoadPgn(pgn, out _, out _, out var error), error);
            Assert.Equal(new[] { "e4", "e5", "Nf3" }, rules.SanMoves);
        }

        [Fact]
        public void StudyCatalog_ValidatesEveryBuiltInLineAndRejectsIllegalCustomSolution()
        {
            Assert.True(StudyCatalog.BuiltInPuzzles.Count > StudyLibrary.Puzzles.Count);
            Assert.Contains(StudyLibrary.GetOpeningBook(), x => x.Name.Contains("Italiana") && x.Moves.Contains("Bc4"));
            foreach (var puzzle in StudyCatalog.BuiltInPuzzles)
            {
                Assert.True(StudyCatalog.TryReadSolution(puzzle, out var steps, out var error), $"{puzzle.Title}: {error}");
                Assert.NotEmpty(steps);
            }
            Assert.False(StudyCatalog.TryReadSolution(new TacticalPuzzle
                { Fen = ChessRules.StandardStartFen, SolutionUci = "e2e4 e7e5 e1e8" }, out _, out var invalid));
            Assert.Contains("passo 3", invalid);
        }

        [Fact]
        public void Engine_DiscardsCanceledWorkAndCanAnalyzeAgain()
        {
            using var engine = new EngineService();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var rules = new ChessRules();
            Assert.Throws<OperationCanceledException>(() => engine.Analyze(rules, true, 3, cancellationToken: cancellation.Token));
            Assert.NotNull(engine.Analyze(rules, true, 0).BestMove);
        }

        private static void Play(ChessRules rules, string token)
        {
            Assert.True(rules.TryParseSanMove(token, out var from, out var to, out var promotion));
            rules.ExecuteMove(from, to, promotion);
        }
    }
}
