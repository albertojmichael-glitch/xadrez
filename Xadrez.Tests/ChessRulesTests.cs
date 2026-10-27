using System.Drawing;
using Xunit;
using XadrezApp;

namespace XadrezApp.Tests
{
    public class ChessRulesTests
    {
        [Fact]
        public void InitialBoard_ShouldHave20LegalMovesAndCorrectSetup()
        {
            var rules = new ChessRules();

            Assert.True(rules.WhiteTurn);
            Assert.Equal(1, rules.FullMoveNumber);
            Assert.Equal(0, rules.HalfMoveClock);
            Assert.Equal(ChessRules.StandardStartFen, rules.ToFen());

            var whiteMoves = rules.GetAllLegalMoves(true);
            Assert.Equal(20, whiteMoves.Count); // 16 movimentos de peão + 4 de cavalo

            // Verifica se as variantes visuais (1 e 2) foram inicializadas para os pares de peças
            Assert.Equal(1, rules.PieceVariants[7, 0]); // 1ª Torre branca (a1)
            Assert.Equal(2, rules.PieceVariants[7, 7]); // 2ª Torre branca (h1)
        }

        [Theory]
        [InlineData("a8", 0, 0)]
        [InlineData("h1", 7, 7)]
        [InlineData("e4", 4, 4)]
        public void AlgebraicConversion_ShouldConvertCorrectly(string algebraic, int expectedX, int expectedY)
        {
            Point? pt = ChessRules.AlgebraicToSquare(algebraic);
            Assert.NotNull(pt);
            Assert.Equal(new Point(expectedX, expectedY), pt.Value);
            Assert.Equal(algebraic, ChessRules.SquareToAlgebraic(pt.Value));
        }

        [Theory]
        [InlineData("")]
        [InlineData("-")]
        [InlineData("z9")]
        [InlineData("a0")]
        [InlineData("i4")]
        public void AlgebraicToSquare_InvalidInputs_ShouldReturnNull(string invalidSq)
        {
            Assert.Null(ChessRules.AlgebraicToSquare(invalidSq));
        }

        [Fact]
        public void Castling_KingSideAndQueenSide_ShouldMoveKingAndRookAndPreserveVariant()
        {
            var rules = new ChessRules();
            rules.LoadFen("r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R w KQkq - 0 1");

            byte h1RookVariant = rules.PieceVariants[7, 7];

            // Roque Pequeno (O-O): e1 (4,7) -> g1 (6,7)
            string sanShort = rules.ExecuteMove(new Point(4, 7), new Point(6, 7));
            Assert.Equal("O-O", sanShort);
            Assert.Equal('K', rules.Board[7, 6]);
            Assert.Equal('R', rules.Board[7, 5]);
            Assert.Equal('.', rules.Board[7, 7]);
            Assert.Equal(h1RookVariant, rules.PieceVariants[7, 5]);
            Assert.False(rules.CastleWK);
            Assert.False(rules.CastleWQ);

            // Roque Grande das Pretas (O-O-O): e8 (4,0) -> c8 (2,0)
            string sanLong = rules.ExecuteMove(new Point(4, 0), new Point(2, 0));
            Assert.Equal("O-O-O", sanLong);
            Assert.Equal('k', rules.Board[0, 2]);
            Assert.Equal('r', rules.Board[0, 3]);
            Assert.Equal('.', rules.Board[0, 0]);
            Assert.False(rules.CastleBK);
            Assert.False(rules.CastleBQ);
        }

        [Fact]
        public void Castling_WhenInCheckOrTransitSquareAttacked_ShouldBeIllegal()
        {
            var rules = new ChessRules();

            // 1. Rei branco em xeque pela Torre preta em e8: nenhum roque permitido
            rules.LoadFen("4r1k1/8/8/8/8/8/8/R3K2R w KQ - 0 1");
            var movesInCheck = rules.GetLegalMovesForSquare(4, 7, true);
            Assert.DoesNotContain(new Point(6, 7), movesInCheck);
            Assert.DoesNotContain(new Point(2, 7), movesInCheck);

            // 2. Casa de passagem f1 (5,7) atacada pela Torre preta em f8: impede O-O, mas permite O-O-O
            rules.LoadFen("5rk1/8/8/8/8/8/8/R3K2R w KQ - 0 1");
            var movesTransitAttacked = rules.GetLegalMovesForSquare(4, 7, true);
            Assert.DoesNotContain(new Point(6, 7), movesTransitAttacked);
            Assert.Contains(new Point(2, 7), movesTransitAttacked);
        }

        [Fact]
        public void EnPassant_ShouldCaptureOpponentPawnAndClearSquare()
        {
            var rules = new ChessRules();
            // Peão branco em e5 (4,3), peão preto acabou de avançar para d5 (3,3), alvo en passant em d6 (3,2)
            rules.LoadFen("rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3");

            var legalMoves = rules.GetLegalMovesForSquare(4, 3, true);
            Assert.Contains(new Point(3, 2), legalMoves);

            string san = rules.ExecuteMove(new Point(4, 3), new Point(3, 2));
            Assert.Equal("exd6", san);
            Assert.Equal('P', rules.Board[2, 3]); // Peão branco em d6
            Assert.Equal('.', rules.Board[3, 3]); // Peão preto de d5 removido
            Assert.Equal(0, rules.PieceVariants[3, 3]);
            Assert.Equal(0, rules.HalfMoveClock);
        }

        [Fact]
        public void PawnPromotion_AndSanDisambiguation_ShouldFormatCorrectly()
        {
            var rules = new ChessRules();

            // Subpromoção para Cavalo sem xeque (a7 -> a8=N)
            rules.LoadFen("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");
            string promoKnight = rules.ExecuteMove(new Point(0, 1), new Point(0, 0), 'N');
            Assert.Equal("a8=N", promoKnight);
            Assert.Equal('N', rules.Board[0, 0]);

            // Promoção para Rainha dando xeque na fileira 8 (a7 -> a8=Q+)
            rules.LoadFen("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");
            string promoQueenCheck = rules.ExecuteMove(new Point(0, 1), new Point(0, 0), 'Q');
            Assert.Equal("a8=Q+", promoQueenCheck);
            Assert.Equal('Q', rules.Board[0, 0]);

            // Desambiguação por coluna (duas Torres na fileira 1 indo para d1 -> Rad1)
            rules.LoadFen("4k3/8/8/8/8/8/6K1/R6R w - - 0 1");
            string sanFileDisambig = rules.ExecuteMove(new Point(0, 7), new Point(3, 7));
            Assert.Equal("Rad1", sanFileDisambig);

            // Desambiguação por fileira (duas Torres na coluna 'a' indo para a4 -> R1a4)
            rules.LoadFen("4k3/R7/8/8/8/8/6K1/R7 w - - 0 1");
            string sanRankDisambig = rules.ExecuteMove(new Point(0, 7), new Point(0, 4));
            Assert.Equal("R1a4", sanRankDisambig);
        }

        [Fact]
        public void EndGame_Checkmate_ShouldSetResultAndReason()
        {
            var rules = new ChessRules();
            // Posição do Mate Pastor: Qxf7#
            rules.LoadFen("r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4");

            string san = rules.ExecuteMove(new Point(7, 3), new Point(5, 1));
            Assert.Equal("Qxf7#", san);

            bool ended = rules.CheckEndGame(out string title, out string detail);
            Assert.True(ended);
            Assert.Equal("1-0", rules.GameResult);
            Assert.Equal("Xeque-mate!", title);
            Assert.Contains("Brancas", detail);
        }

        [Fact]
        public void EndGame_Stalemate_ShouldDetectDraw()
        {
            var rules = new ChessRules();
            // Pretas a jogar, sem lances legais e sem estar em xeque
            rules.LoadFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");

            bool ended = rules.CheckEndGame(out string title, out _);
            Assert.True(ended);
            Assert.Equal("1/2-1/2", rules.GameResult);
            Assert.Equal("Empate por Afogamento", title);
        }

        [Fact]
        public void EndGame_FiftyMoveRule_ShouldDetectDrawAt100HalfMoves()
        {
            var rules = new ChessRules();
            rules.LoadFen("4k3/8/8/8/8/8/8/4K2R w - - 99 50");

            // 100º meio-lance sem captura e sem movimento de peão
            rules.ExecuteMove(new Point(7, 7), new Point(7, 6));
            Assert.Equal(100, rules.HalfMoveClock);

            bool ended = rules.CheckEndGame(out string title, out _);
            Assert.True(ended);
            Assert.Equal("1/2-1/2", rules.GameResult);
            Assert.Equal("Empate - Regra dos 50 Movimentos", title);
        }

        [Fact]
        public void EndGame_ThreefoldRepetition_ShouldDetectDraw()
        {
            var rules = new ChessRules();

            // Repete a posição inicial 3 vezes movendo os cavalos de g1/g8 para f3/f6 e voltando
            for (int cycle = 0; cycle < 2; cycle++)
            {
                rules.ExecuteMove(new Point(6, 7), new Point(5, 5)); // Ng1-f3
                rules.ExecuteMove(new Point(6, 0), new Point(5, 2)); // Ng8-f6
                rules.ExecuteMove(new Point(5, 5), new Point(6, 7)); // Nf3-g1
                rules.ExecuteMove(new Point(5, 2), new Point(6, 0)); // Nf6-g8
            }

            bool ended = rules.CheckEndGame(out string title, out _);
            Assert.True(ended);
            Assert.Equal("1/2-1/2", rules.GameResult);
            Assert.Equal("Empate por Repetição (3 Lances)", title);
        }

        [Theory]
        [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 1", true)]      // Rei vs Rei
        [InlineData("4k3/8/8/8/8/8/8/4KB2 w - - 0 1", true)]     // Rei + Bispo vs Rei
        [InlineData("4k3/8/8/8/8/8/8/4KN2 w - - 0 1", true)]     // Rei + Cavalo vs Rei
        [InlineData("4kb2/8/8/8/8/8/8/2B1K3 w - - 0 1", true)]   // Bispos em casas da mesma cor (c1 e f8)
        [InlineData("4k3/8/8/8/8/8/8/4KR2 w - - 0 1", false)]    // Rei + Torre vs Rei (suficiente)
        public void InsufficientMaterial_ShouldDetectAccurately(string fen, bool expectedInsufficient)
        {
            var rules = new ChessRules();
            rules.LoadFen(fen);

            Assert.Equal(expectedInsufficient, rules.IsInsufficientMaterial());
        }

        [Fact]
        public void SnapshotAndClone_ShouldPreserveAndIsolateState()
        {
            var rules = new ChessRules();
            var snap = rules.CreateSnapshot();

            // Faz um lance no tabuleiro principal
            rules.ExecuteMove(new Point(4, 6), new Point(4, 4)); // e2-e4
            Assert.NotEqual(ChessRules.StandardStartFen, rules.ToFen());

            // Testa isolamento do Clone()
            var cloned = rules.Clone();
            cloned.ExecuteMove(new Point(4, 1), new Point(4, 3)); // e7-e5 no clone
            Assert.Single(rules.SanMoves);
            Assert.Equal(2, cloned.SanMoves.Count);

            // Restaura snapshot inicial (Takeback)
            rules.RestoreSnapshot(snap);
            Assert.Equal(ChessRules.StandardStartFen, rules.ToFen());
            Assert.Empty(rules.SanMoves);
        }
    }
}