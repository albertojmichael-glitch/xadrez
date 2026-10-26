using System.Collections.Generic;

namespace XadrezApp
{
    public class TacticalPuzzle
    {
        public string Title { get; set; } = "";
        public string Theme { get; set; } = "";
        public string Fen { get; set; } = "";
        public string SolutionUci { get; set; } = "";
        public string Hint { get; set; } = "";
    }

    public static class StudyLibrary
    {
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
            (new[] { "d4", "Nf6", "Bf4" }, "A45: Sistema Londres"),
            (new[] { "d4", "d5", "Bf4" }, "D00: Sistema Londres Clássico"),
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
                        string cleanSan = sanMoves[i].TrimEnd('+', '#', '!', '?');
                        if (cleanSan != entry.moves[i])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return entry.name;
                }
            }

            if (sanMoves[0].StartsWith("e4")) return "B00: Abertura do Peão do Rei (1. e4)";
            if (sanMoves[0].StartsWith("d4")) return "A40: Abertura do Peão da Dama (1. d4)";
            return "Meio-Jogo / Variante Livre";
        }

        // 15 Quebra-Cabeças Táticos (Puzzles)
        public static readonly List<TacticalPuzzle> Puzzles = new()
        {
            new TacticalPuzzle
            {
                Title = "Puzzle #1: Mate Pastor (Mate em 1)",
                Theme = "Ataque em f7",
                Fen = "r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4",
                SolutionUci = "h5f7",
                Hint = "O ponto f7 está defendido apenas pelo Rei preto. Use a Dama apoiada pelo Bispo!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #2: Mate do Corredor (Back Rank)",
                Theme = "Mate na 8ª Fileira",
                Fen = "6k1/5ppp/8/8/8/8/5PPP/3R2K1 w - - 0 1",
                SolutionUci = "d1d8",
                Hint = "O Rei adversário está preso atrás dos próprios peões. Leve a Torre até a 8ª fileira!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #3: Garfo Real de Cavalo",
                Theme = "Ataque Duplo (Rei e Dama)",
                Fen = "4k3/8/8/3q4/8/4N3/8/4K3 w - - 0 1",
                SolutionUci = "e3f5",
                Hint = "Encontre a casa onde o Cavalo dá xeque no Rei em e8 e ataca a Dama em d5 ao mesmo tempo! (Ou c7/f5)"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #4: Espeto de Bispo na Grande Diagonal",
                Theme = "Espeto Tático (Rei e Torre)",
                Fen = "7r/1p4k1/p5p1/8/8/2B5/PPP2PPP/6K1 w - - 0 1",
                SolutionUci = "c3h8",
                Hint = "Capture a Torre dando xeque na grande diagonal!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #5: Mate de Legal",
                Theme = "Mate Clássico de Cavalos e Bispo",
                Fen = "r2qkbnr/ppp2ppp/2np4/4N3/2B1P1b1/2N5/PPPP1PPP/R1BbK2R w KQkq - 0 6",
                SolutionUci = "c4f7",
                Hint = "O Rei preto não tem casas seguras. Dê o xeque mortal em f7 com o Bispo!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #6: Mate Sufocado (Philidor)",
                Theme = "Mate de Cavalo com Rei Trancado",
                Fen = "6rk/6pp/8/5N2/8/8/8/7K w - - 0 1",
                SolutionUci = "f5f7",
                Hint = "A Torre e os peões pretos trancam o próprio Rei. Dê o xeque de Cavalo em f7!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #7: Mate de Anastásia",
                Theme = "Coordenação Cavalo + Torre",
                Fen = "5r1k/4N1pp/8/8/8/8/8/6RK w - - 0 1",
                SolutionUci = "e7g6",
                Hint = "Ou dê o xeque descoberto ou leve a Torre para a coluna h! (Experimente Cg6+ ou Tg8)"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #8: Mate Árabe (Torre e Cavalo)",
                Theme = "Mate no Canto do Tabuleiro",
                Fen = "7k/2R5/5N2/8/8/8/8/6K1 w - - 0 1",
                SolutionUci = "c7h7",
                Hint = "O Cavalo em f6 controla g8 e defende a casa h7. Leve a Torre até h7!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #9: Mate de Boden (Dois Bispos)",
                Theme = "Bispos Cruzados",
                Fen = "2kr3r/ppp2ppp/8/8/8/5B2/P1P2PPP/2KR2B1 w - - 0 1",
                SolutionUci = "f3g4",
                Hint = "Dê o xeque diagonal com o Bispo de casas claras em g4!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #10: Mate dasombreiras (Epaulette Mate)",
                Theme = "Rei bloqueado pelas próprias Torres",
                Fen = "3rkr2/8/4Q3/8/8/8/8/4K3 w - - 0 1",
                SolutionUci = "e6e7",
                Hint = "As duas Torres pretas bloqueiam a fuga lateral do próprio Rei! Aproxime a Dama."
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #11: Cravada Absoluta na Coluna",
                Theme = "Explorando Peça Cravada",
                Fen = "4k3/4r3/8/2B5/8/8/8/4R1K1 w - - 0 1",
                SolutionUci = "c5e7",
                Hint = "A Torre preta em e7 está cravada pela sua Torre de e1. Capture-a com o Bispo!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #12: Garfo de Peão Central",
                Theme = "Ataque Duplo de Peão",
                Fen = "r1bqkbnr/ppp2ppp/2n5/3p4/4P3/2N2N2/PPPP1PPP/R1BQKB1R w KQkq - 0 4",
                SolutionUci = "e4d5",
                Hint = "Capture o peão central em d5 atacando o Cavalo de c6!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #13: Mate da Ópera de Morphy",
                Theme = "Mate Clássico com Torre e Bispo",
                Fen = "4kb1r/p2n1ppp/4q3/4p1B1/4P3/8/PPP2PPP/3RK2R w Kk - 0 1",
                SolutionUci = "d1d8",
                Hint = "O Bispo em g5 apoia a casa d8 e o Cavalo em d7 bloqueia o próprio Rei!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #14: Presente Grego (Ataque em h7)",
                Theme = "Mate com Dama e Cavalo",
                Fen = "r1bq1rk1/ppp2p1p/2np4/6N1/2B1P3/8/PPPP1PPP/RNB1K2R w KQ - 0 1",
                SolutionUci = "c4f7",
                Hint = "Ataque o Rei exposto com o Bispo em f7!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #15: Coroação com Xeque-Mate",
                Theme = "Promoção de Peão Decisiva",
                Fen = "6k1/3P1ppp/8/8/8/8/5PPP/6K1 w - - 0 1",
                SolutionUci = "d7d8",
                Hint = "Avance o peão passado de d7 para d8 promovendo para Dama com mate no corredor!"
            }
        };
    }
}