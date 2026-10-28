using System.Drawing;
using Xunit;
using XadrezApp;

namespace XadrezApp.Tests
{
    public class FenAndPgnTests
    {
        [Theory]
        [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]
        [InlineData("rnbqkbnr/ppp1pppp/8/3pP3/8/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 3")]
        [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R b KQkq - 1 12")]
        public void TryLoadFen_ValidFen_ShouldRoundTripExactFen(string validFen)
        {
            var rules = new ChessRules();
            bool ok = rules.TryLoadFen(validFen, out string error);

            Assert.True(ok);
            Assert.Equal(string.Empty, error);
            Assert.Equal(validFen, rules.ToFen());
        }

        [Theory]
        [InlineData("")]                                                                                  // Vazio
        [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1 extra")]                    // Mais de 6 campos
        [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP w KQkq - 0 1")]                                   // Apenas 7 fileiras
        [InlineData("rnbqkbnr/pppppppp/44/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]                         // Dígitos consecutivos
        [InlineData("rnbqkbnr/pppppppp/9/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1")]                          // Soma da fileira > 8
        [InlineData("rnbqkbnP/pppppppp/8/8/8/8/PPPPPPP1/RNBQKBNR w KQkq - 0 1")]                          // Peão na 8ª fileira
        [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQ1BNR w KQkq - 0 1")]                          // Sem Rei branco
        [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR x KQkq - 0 1")]                          // Turno inválido ('x')
        [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq e4 0 1")]                         // Casa en passant inválida
        [InlineData("4k3/4Q3/8/8/8/8/8/4K3 w - - 0 1")]                                                   // Rei preto em xeque com vez das Brancas
        public void TryLoadFen_InvalidFen_ShouldReturnFalseAndNotModifyExistingBoard(string invalidFen)
        {
            var rules = new ChessRules();
            rules.ExecuteMove(new Point(4, 6), new Point(4, 4)); // Joga 1. e4 antes do teste
            string fenBefore = rules.ToFen();

            bool ok = rules.TryLoadFen(invalidFen, out string error);

            Assert.False(ok);
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.Equal(fenBefore, rules.ToFen()); // Tabuleiro permanece intacto
            Assert.Single(rules.SanMoves);
        }

        [Fact]
        public void TryLoadFen_WithMissingRooks_ShouldSanitizeCastlingRights()
        {
            var rules = new ChessRules();
            // O FEN declara "KQkq", mas não há torres no tabuleiro
            bool ok = rules.TryLoadFen("4k3/8/8/8/8/8/8/4K3 w KQkq - 0 1", out _);

            Assert.True(ok);
            Assert.False(rules.CastleWK);
            Assert.False(rules.CastleWQ);
            Assert.False(rules.CastleBK);
            Assert.False(rules.CastleBQ);
            Assert.Equal("4k3/8/8/8/8/8/8/4K3 w - - 0 1", rules.ToFen());
        }

        [Fact]
        public void ToPgn_FromStandardStart_ShouldGenerateValidPgnWithoutSetUpTag()
        {
            var rules = new ChessRules();
            rules.ExecuteMove(new Point(4, 6), new Point(4, 4)); // 1. e4
            rules.ExecuteMove(new Point(4, 1), new Point(4, 3)); // 1... e5
            rules.ExecuteMove(new Point(6, 7), new Point(5, 5)); // 2. Nf3

            string pgn = rules.ToPgn(blackPlayerName: "Stockfish IA");

            Assert.Contains("[White \"Jogador\"]", pgn);
            Assert.Contains("[Black \"Stockfish IA\"]", pgn);
            Assert.DoesNotContain("[SetUp \"1\"]", pgn);
            Assert.Contains("1. e4 e5 2. Nf3 *", pgn);
        }

        [Fact]
        public void ToPgn_FromCustomFenBlackToMove_ShouldIncludeSetUpFenAndEllipsisMoveNumber()
        {
            var rules = new ChessRules();
            string customFen = "4k3/8/8/8/8/8/8/4K3 b - - 0 5";
            rules.LoadFen(customFen);

            rules.ExecuteMove(new Point(4, 0), new Point(3, 0)); // 5... Kd8

            string pgn = rules.ToPgn();

            Assert.Contains("[SetUp \"1\"]", pgn);
            Assert.Contains($"[FEN \"{customFen}\"]", pgn);
            Assert.Contains("5... Kd8 *", pgn);
        }

        [Fact]
        public void TryLoadPgn_ShouldParseHeadersCommentsAndReplayAllSnapshots()
        {
            var rules = new ChessRules();
            string pgn = @"
[Event ""Partida de Teste""]
[White ""Ana""]
[Black ""Bot""]
[Result ""1-0""]

1. e4 {Abertura do Peão do Rei} e5 2. Nf3 (2. Bc4 Bc5) Nc6 3. Bb5 a6 1-0";

            bool ok = rules.TryLoadPgn(pgn, out var snapshots, out var headers, out string error);

            Assert.True(ok, error);
            Assert.Equal("Ana", headers["White"]);
            Assert.Equal("Bot", headers["Black"]);
            Assert.Equal(7, snapshots.Count); // Posição inicial + 6 meio-lances
            Assert.Equal(6, rules.SanMoves.Count);
            Assert.Equal("1-0", rules.GameResult);
            Assert.Equal(new Point(0, 1), rules.LastMoveFrom);
            Assert.Equal(new Point(0, 2), rules.LastMoveTo);
        }

        [Fact]
        public void FormatUciPvToSan_ShouldConvertUciSequenceToReadableSan()
        {
            var rules = new ChessRules();
            string sanPv = ChessRules.FormatUciPvToSan(rules, "e2e4 e7e5 g1f3 b8c6");

            Assert.Equal("1. e4 e5 2. Nf3 Nc6", sanPv);
        }

        [Fact]
        public void GetCapturedPiecesAndMaterial_ShouldTrackCapturesAndAdvantage()
        {
            var rules = new ChessRules();
            // Sem a Dama preta e um peão preto
            rules.LoadFen("rnb1kbnr/pppp1ppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");

            var (capturedByWhite, capturedByBlack, diffWhite) = rules.GetCapturedPiecesAndMaterial();

            Assert.Contains('q', capturedByWhite);
            Assert.Contains('p', capturedByWhite);
            Assert.Empty(capturedByBlack);
            Assert.Equal(10, diffWhite);
        }
    }
}
