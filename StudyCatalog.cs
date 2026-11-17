using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace XadrezApp
{
    public sealed record OpeningLesson(string Name, string Moves, string Description);

    public sealed record PuzzleStep(string Uci, Point From, Point To, char? Promotion, string San);

    public static class StudyCatalog
    {
        public static IReadOnlyList<TacticalPuzzle> ExtraPuzzles { get; } = new[]
        {
            new TacticalPuzzle
            {
                Title = "Coordenação de duas torres", Theme = "Mate da escadinha", Level = "Intermediário",
                Phase = "Final", Fen = "8/6k1/R7/7R/8/8/8/6K1 w - - 0 1",
                SolutionUci = "h5b5 g7f7 b5b7", Hint = "Use as torres em fileiras vizinhas para encurralar o rei."
            },
            new TacticalPuzzle
            {
                Title = "A captura en passant", Theme = "En passant", Phase = "Meio-jogo",
                Fen = "4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2",
                SolutionUci = "e5d6", Hint = "O peão preto acabou de avançar duas casas."
            },
            new TacticalPuzzle
            {
                Title = "O roque na Italiana", Theme = "Roque", Phase = "Abertura", Opening = "Italiana",
                Fen = "r1bqk1nr/pppp1ppp/2n5/2b1p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4",
                SolutionUci = "e1g1", Hint = "Coloque seu rei em segurança antes de abrir o centro."
            },
            new TacticalPuzzle
            {
                Title = "Promoção sem afogamento", Theme = "Subpromoção", Level = "Avançado", Phase = "Final",
                Fen = "8/k1P5/2K5/8/8/8/8/8 w - - 0 1",
                SolutionUci = "c7c8r", Hint = "Escolha uma peça que mantenha a vantagem sem tirar todas as casas do rei adversário."
            }
        };

        public static IReadOnlyList<TacticalPuzzle> BuiltInPuzzles => StudyLibrary.Puzzles.Concat(ExtraPuzzles).ToArray();

        public static bool TryReadSolution(TacticalPuzzle puzzle, out IReadOnlyList<PuzzleStep> steps, out string error)
        {
            var rules = new ChessRules();
            var solution = new List<PuzzleStep>();
            steps = solution;
            if (!rules.TryLoadFen(puzzle.Fen, out error)) return false;

            foreach (string token in puzzle.SolutionUci.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                Point? from = token.Length is 4 or 5 ? ChessRules.AlgebraicToSquare(token[..2]) : null;
                Point? to = from.HasValue ? ChessRules.AlgebraicToSquare(token[2..4]) : null;
                bool valid = from.HasValue && to.HasValue &&
                    rules.GetLegalMovesForSquare(from.Value.X, from.Value.Y, rules.WhiteTurn).Contains(to.Value);
                if (valid && from is Point origin && to is Point destination)
                {
                    bool promotes = char.ToUpperInvariant(rules.Board[origin.Y, origin.X]) == 'P' && destination.Y is 0 or 7;
                    if ((!promotes && token.Length == 4) ||
                        (promotes && token.Length == 5 && "qrbn".Contains(token[4])))
                    {
                        char? piece = promotes ? token[4] : null;
                        string san = rules.ExecuteMove(origin, destination, piece);
                        solution.Add(new PuzzleStep(token, origin, destination, piece, san));
                        continue;
                    }
                }
                error = $"Lance inválido na solução: {token} (passo {solution.Count + 1}). Use UCI, por exemplo e2e4 ou e7e8q.";
                return false;
            }

            if (solution.Count > 0) return true;
            error = "Informe pelo menos um lance da solução.";
            return false;
        }

        public static IReadOnlyList<TacticalPuzzle> ReadCustomPuzzles() =>
            GameArchive.ReadJson<TacticalPuzzle>(CustomPuzzleFolder).OrderBy(p => p.Title).ToArray();

        public static void SaveCustomPuzzle(TacticalPuzzle puzzle) =>
            GameArchive.SaveJson(CustomPuzzleFolder, Guid.NewGuid(), puzzle);

        private static string CustomPuzzleFolder => Path.Combine(GameArchive.DataFolder, "Puzzles");
    }
}
