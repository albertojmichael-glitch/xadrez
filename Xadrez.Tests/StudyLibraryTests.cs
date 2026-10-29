using System.Collections.Generic;
using System.Drawing;
using Xunit;
using XadrezApp;

namespace XadrezApp.Tests
{
    public class StudyLibraryTests
    {
        [Fact]
        public void All30Puzzles_ShouldHaveValidFenAndLegalSolutionMove()
        {
            Assert.Equal(30, StudyLibrary.Puzzles.Count);

            foreach (var puzzle in StudyLibrary.Puzzles)
            {
                Assert.False(string.IsNullOrWhiteSpace(puzzle.Title));
                Assert.False(string.IsNullOrWhiteSpace(puzzle.Theme));
                Assert.False(string.IsNullOrWhiteSpace(puzzle.Hint));
                Assert.Equal(4, puzzle.SolutionUci.Length);

                var rules = new ChessRules();
                bool loaded = rules.TryLoadFen(puzzle.Fen, out string fenError);
                Assert.True(loaded, $"Falha no FEN do '{puzzle.Title}': {fenError}");

                Point? from = ChessRules.AlgebraicToSquare(puzzle.SolutionUci.Substring(0, 2));
                Point? to = ChessRules.AlgebraicToSquare(puzzle.SolutionUci.Substring(2, 2));

                Assert.NotNull(from);
                Assert.NotNull(to);

                var legalTargets = rules.GetLegalMovesForSquare(from.Value.X, from.Value.Y, rules.WhiteTurn);
                Assert.True(
                    legalTargets.Contains(to.Value),
                    $"A solução '{puzzle.SolutionUci}' do '{puzzle.Title}' não é um lance legal na posição FEN do puzzle.");
            }
        }

        [Fact]
        public void DetectOpening_ShouldIdentifyOpeningsAndIgnoreCheckAnnotations()
        {
            Assert.Equal("Posição Inicial (Aguardando lances)", StudyLibrary.DetectOpening(new List<string>()));

            var ruyLopez = new List<string> { "e4!", "e5", "Nf3", "Nc6", "Bb5+" };
            Assert.Equal("C60: Abertura Espanhola (Ruy Lopez)", StudyLibrary.DetectOpening(ruyLopez));

            var london = new List<string> { "d4", "Nf6", "Bf4" };
            Assert.Equal("A45: Sistema Londres", StudyLibrary.DetectOpening(london));

            var sicilian = new List<string> { "e4", "c5" };
            Assert.Equal("B20: Defesa Siciliana", StudyLibrary.DetectOpening(sicilian));

            var dragon = new List<string> { "e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "g6" };
            Assert.Equal("B70: Defesa Siciliana (Variante do Dragão)", StudyLibrary.DetectOpening(dragon));

            var catalan = new List<string> { "d4", "Nf6", "c4", "e6", "g3" };
            Assert.Equal("E00: Abertura Catalã", StudyLibrary.DetectOpening(catalan));

            var grunfeld = new List<string> { "d4", "Nf6", "c4", "g6", "Nc3", "d5" };
            Assert.Equal("D80: Defesa Grünfeld", StudyLibrary.DetectOpening(grunfeld));

            var custom = new List<string> { "a3", "a6" };
            Assert.Equal("Meio-Jogo / Variante Livre", StudyLibrary.DetectOpening(custom));
        }

        [Fact]
        public void IsBookMove_ShouldReturnTrueForBookLinesAndFalseOutsideBook()
        {
            Assert.False(StudyLibrary.IsBookMove(new List<string>()));
            Assert.True(StudyLibrary.IsBookMove(new List<string> { "e4", "e5", "Nf3" }));
            Assert.True(StudyLibrary.IsBookMove(new List<string> { "d4", "Nf6", "c4", "g6", "Nc3", "d5" }));
            Assert.False(StudyLibrary.IsBookMove(new List<string> { "h4", "h5" }));

            // Mais de 10 meio-lances deve retornar false
            var longLine = new List<string> { "e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "a6", "Be2" };
            Assert.False(StudyLibrary.IsBookMove(longLine));
        }
    }
}
