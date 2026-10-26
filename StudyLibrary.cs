using System.Collections.Generic;
using System.Linq;

namespace XadrezApp
{
    public class TacticalPuzzle
    {
        public string Title { get; set; } = "";
        public string Theme { get; set; } = "";
        public string Fen { get; set; } = "";
        public string SolutionUci { get; set; } = ""; // Ex: "f3f7"
        public string Hint { get; set; } = "";
    }

    public static class StudyLibrary
    {
        // Biblioteca de Aberturas (Opening Book) baseada na sequência de lances SAN
        private static readonly List<(string[] moves, string name)> Openings = new()
        {
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bb5" }, "C60: Abertura Espanhola (Ruy Lopez)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5" }, "C50: Abertura Italiana (Giuoco Piano)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Nf6" }, "C55: Defesa dos Dois Cavalos"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bc4" }, "C50: Abertura Italiana"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "d4" }, "C44: Abertura Escocesa"),
            (new[] { "e4", "e5", "f4" }, "C30: Gambito do Rei"),
            (new[] { "e4", "e5", "Nf3" }, "C40: Abertura do Cavalo do Rei"),
            (new[] { "e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "a6" }, "B90: Defesa Siciliana (Variante Najdorf)"),
            (new[] { "e4", "c5", "Nf3" }, "B27: Defesa Siciliana"),
            (new[] { "e4", "c5" }, "B20: Defesa Siciliana"),
            (new[] { "e4", "e6", "d4", "d5" }, "C00: Defesa Francesa"),
            (new[] { "e4", "e6" }, "C00: Defesa Francesa"),
            (new[] { "e4", "c6", "d4", "d5" }, "B12: Defesa Caro-Kann"),
            (new[] { "e4", "c6" }, "B10: Defesa Caro-Kann"),
            (new[] { "e4", "d5" }, "B01: Defesa Escandinava"),
            (new[] { "d4", "d5", "c4", "e6" }, "D30: Gambito da Dama Recusado"),
            (new[] { "d4", "d5", "c4", "dxc4" }, "D20: Gambito da Dama Aceito"),
            (new[] { "d4", "d5", "c4", "c6" }, "D10: Defesa Eslava"),
            (new[] { "d4", "d5", "c4" }, "D06: Gambito da Dama"),
            (new[] { "d4", "Nf6", "c4", "g6", "Nc3", "Bg7" }, "E60: Defesa Índia do Rei"),
            (new[] { "d4", "Nf6", "c4", "e6", "Nc3", "Bb4" }, "E20: Defesa Nimzo-Índia"),
            (new[] { "c4" }, "A10: Abertura Inglesa"),
            (new[] { "Nf3" }, "A04: Abertura Réti")
        };

        public static string DetectOpening(List<string> sanMoves)
        {
            if (sanMoves.Count == 0) return "Posição Inicial (Aguardando lances)";

            foreach (var entry in Openings)
            {
                if (sanMoves.Count >= entry.moves.Length)
                {
                    bool match = true;
                    for (int i = 0; i < entry.moves.Length; i++)
                    {
                        string cleanSan = sanMoves[i].TrimEnd('+', '#');
                        if (cleanSan != entry.moves[i])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return entry.name;
                }
            }

            if (sanMoves[0] == "e4") return "B00: Abertura do Peão do Rei (1. e4)";
            if (sanMoves[0] == "d4") return "A40: Abertura do Peão da Dama (1. d4)";
            return "Meio-Jogo / Variante Livre";
        }

        // Modo de Quebra-Cabeças (Puzzles Táticos)
        public static readonly List<TacticalPuzzle> Puzzles = new()
        {
            new TacticalPuzzle
            {
                Title = "Puzzle #1: Mate Pastor (Mate em 1)",
                Theme = "Ataque em f7 / Xeque-mate",
                Fen = "r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4",
                SolutionUci = "h5f7",
                Hint = "O ponto f7 está defendido apenas pelo Rei preto. Use a Dama apoiada pelo Bispo!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #2: Garfo Real de Cavalo",
                Theme = "Garfo Duplo (Rei e Dama)",
                Fen = "r1bqk2r/pppp1ppp/2n5/8/3nP3/5N2/PPP2PPP/RNBQKB1R w KQkq - 0 6",
                SolutionUci = "f3d4",
                Hint = "Capture a peça centralizada que está desprotegida com o seu Cavalo!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #3: Mate do Corredor (Back Rank)",
                Theme = "Mate na 8ª Fileira",
                Fen = "6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1",
                SolutionUci = "d1d8",
                Hint = "O Rei adversário está preso atrás dos próprios peões. Leve a Torre até a última fileira!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #4: Espeto de Bispo na Diagonal",
                Theme = "Espeto Tático (Rei e Torre)",
                Fen = "7r/1p4k1/p5p1/8/8/2B5/PPP2PPP/6K1 w - - 0 1",
                SolutionUci = "c3h8",
                Hint = "Aproveite a grande diagonal aberta para capturar a Torre dando xeque!"
            }
        };
    }
}