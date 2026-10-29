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
        // Livro de Aberturas ordenado da linha mais específica (maior número de lances) para a mais geral
        private static readonly List<(string[] moves, string name)> Openings = new()
        {
            // === DEFESA SICILIANA (B20-B99) ===
            (new[] { "e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "a6" }, "B90: Defesa Siciliana (Variante Najdorf)"),
            (new[] { "e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "g6" }, "B70: Defesa Siciliana (Variante do Dragão)"),
            (new[] { "e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "e6" }, "B80: Defesa Siciliana (Variante Scheveningen)"),
            (new[] { "e4", "c5", "Nf3", "d6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "Nc6" }, "B56: Defesa Siciliana (Variante Clássica)"),
            (new[] { "e4", "c5", "Nf3", "Nc6", "d4", "cxd4", "Nxd4", "Nf6", "Nc3", "e5" }, "B33: Defesa Siciliana (Variante Sveshnikov)"),
            (new[] { "e4", "c5", "Nf3", "Nc6", "d4", "cxd4", "Nxd4", "g6" }, "B34: Defesa Siciliana (Dragão Acelerado)"),
            (new[] { "e4", "c5", "Nf3", "e6", "d4", "cxd4", "Nxd4", "a6" }, "B41: Defesa Siciliana (Variante Kan)"),
            (new[] { "e4", "c5", "Nf3", "e6", "d4", "cxd4", "Nxd4", "Nc6" }, "B45: Defesa Siciliana (Variante Taimanov)"),
            (new[] { "e4", "c5", "Nf3", "Nc6", "Bb5" }, "B30: Defesa Siciliana (Ataque Rossolimo)"),
            (new[] { "e4", "c5", "Nf3", "d6", "Bb5" }, "B51: Defesa Siciliana (Ataque Moscou)"),
            (new[] { "e4", "c5", "Nc3", "Nc6", "f4" }, "B23: Defesa Siciliana (Ataque Grand Prix)"),
            (new[] { "e4", "c5", "d4", "cxd4", "c3" }, "B21: Defesa Siciliana (Gambito Smith-Morra)"),
            (new[] { "e4", "c5", "c3" }, "B22: Defesa Siciliana (Variante Alapin)"),
            (new[] { "e4", "c5", "Nc3" }, "B23: Defesa Siciliana Fechada"),
            (new[] { "e4", "c5", "Nf3" }, "B27: Defesa Siciliana"),
            (new[] { "e4", "c5" }, "B20: Defesa Siciliana"),

            // === ABERTURA ESPANHOLA / RUY LOPEZ E ABERTURAS ABERTAS (C20-C99) ===
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Ba4", "Nf6", "O-O", "Be7" }, "C84: Ruy Lopez (Variante Fechada)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Ba4", "Nf6", "O-O", "Nxe4" }, "C80: Ruy Lopez (Variante Aberta)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Bxc6" }, "C68: Ruy Lopez (Variante das Trocas)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "a6", "Ba4" }, "C70: Ruy Lopez (Defesa Morphy)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "Nf6" }, "C65: Ruy Lopez (Defesa Berlim)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "d6" }, "C62: Ruy Lopez (Defesa Steinitz)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bb5", "f5" }, "C63: Ruy Lopez (Gambito Schliemann / Jaenisch)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bb5" }, "C60: Abertura Espanhola (Ruy Lopez)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "b4" }, "C51: Abertura Italiana (Gambito Evans)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "c3", "Nf6", "d3" }, "C50: Abertura Italiana (Giuoco Pianissimo)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5" }, "C50: Abertura Italiana (Giuoco Piano)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Nf6", "Ng5", "d5", "exd5", "Nxd5" }, "C57: Defesa dos Dois Cavalos (Ataque Fried Liver / Fegatello)"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bc4", "Nf6" }, "C55: Defesa dos Dois Cavalos"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Bc4" }, "C50: Abertura Italiana"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Nc3", "Nf6" }, "C47: Abertura dos Quatro Cavalos"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "Nc3" }, "C46: Abertura dos Três Cavalos"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "d4", "exd4", "Bc4" }, "C44: Gambito Escocês"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "d4", "exd4", "Nxd4" }, "C45: Abertura Escocesa Clássica"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "d4" }, "C44: Abertura Escocesa"),
            (new[] { "e4", "e5", "Nf3", "Nc6", "c3" }, "C44: Abertura Ponziani"),
            (new[] { "e4", "e5", "Nf3", "Nf6" }, "C42: Defesa Russa (Petrov)"),
            (new[] { "e4", "e5", "Nf3", "d6" }, "C41: Defesa Philidor"),
            (new[] { "e4", "e5", "Nf3", "f5" }, "C40: Gambito Letão"),
            (new[] { "e4", "e5", "f4", "exf4" }, "C33: Gambito do Rei Aceito"),
            (new[] { "e4", "e5", "f4", "Bc5" }, "C30: Gambito do Rei Recusado"),
            (new[] { "e4", "e5", "f4" }, "C30: Gambito do Rei"),
            (new[] { "e4", "e5", "Nc3" }, "C25: Abertura Vienense"),
            (new[] { "e4", "e5", "Bc4" }, "C23: Abertura do Bispo"),
            (new[] { "e4", "e5", "d4", "exd4", "c3" }, "C21: Gambito Nórdico (Dinamarquês)"),
            (new[] { "e4", "e5", "d4", "exd4", "Qxd4" }, "C22: Abertura do Centro"),
            (new[] { "e4", "e5", "Nf3" }, "C40: Abertura do Cavalo do Rei"),

            // === DEFESA FRANCESA, CARO-KANN E OUTRAS SEMIABERTAS ===
            (new[] { "e4", "e6", "d4", "d5", "Nc3", "Bb4" }, "C15: Defesa Francesa (Variante Winawer)"),
            (new[] { "e4", "e6", "d4", "d5", "Nc3", "Nf6" }, "C11: Defesa Francesa (Variante Clássica)"),
            (new[] { "e4", "e6", "d4", "d5", "Nd2" }, "C03: Defesa Francesa (Variante Tarrasch)"),
            (new[] { "e4", "e6", "d4", "d5", "e5" }, "C02: Defesa Francesa (Variante do Avanço)"),
            (new[] { "e4", "e6", "d4", "d5", "exd5" }, "C01: Defesa Francesa (Variante das Trocas)"),
            (new[] { "e4", "e6", "d4", "d5" }, "C00: Defesa Francesa"),
            (new[] { "e4", "e6" }, "C00: Defesa Francesa"),
            (new[] { "e4", "c6", "d4", "d5", "Nc3", "dxe4", "Nxe4", "Bf5" }, "B18: Defesa Caro-Kann (Variante Clássica)"),
            (new[] { "e4", "c6", "d4", "d5", "e5" }, "B12: Defesa Caro-Kann (Variante do Avanço)"),
            (new[] { "e4", "c6", "d4", "d5", "exd5", "cxd5", "c4" }, "B13: Defesa Caro-Kann (Ataque Panov-Botvinnik)"),
            (new[] { "e4", "c6", "d4", "d5", "exd5" }, "B13: Defesa Caro-Kann (Variante das Trocas)"),
            (new[] { "e4", "c6", "d4", "d5" }, "B12: Defesa Caro-Kann"),
            (new[] { "e4", "c6" }, "B10: Defesa Caro-Kann"),
            (new[] { "e4", "d5", "exd5", "Qxd5", "Nc3", "Qa5" }, "B01: Defesa Escandinava Clássica"),
            (new[] { "e4", "d5" }, "B01: Defesa Escandinava"),
            (new[] { "e4", "d6", "d4", "Nf6", "Nc3", "g6" }, "B07: Defesa Pirc"),
            (new[] { "e4", "g6", "d4", "Bg7" }, "B06: Defesa Moderna (Robatsch)"),
            (new[] { "e4", "Nf6", "e5", "Nd5" }, "B02: Defesa Alekhine"),
            (new[] { "e4", "Nf6" }, "B02: Defesa Alekhine"),
            (new[] { "e4", "Nc6" }, "B00: Defesa Nimzowitsch"),

            // === GAMBITO DA DAMA, SISTEMAS DE D4 E DEFESAS ÍNDIAS (D00-E99) ===
            (new[] { "d4", "d5", "c4", "c6", "Nf3", "Nf6", "Nc3", "e6" }, "D43: Defesa Semi-Eslava"),
            (new[] { "d4", "d5", "c4", "e6", "Nc3", "Nf6" }, "D37: Gambito da Dama Recusado (Ortodoxa)"),
            (new[] { "d4", "d5", "c4", "e6", "Nc3", "c5" }, "D32: Defesa Tarrasch"),
            (new[] { "d4", "d5", "c4", "e6" }, "D30: Gambito da Dama Recusado"),
            (new[] { "d4", "d5", "c4", "dxc4" }, "D20: Gambito da Dama Aceito"),
            (new[] { "d4", "d5", "c4", "c6" }, "D10: Defesa Eslava"),
            (new[] { "d4", "d5", "c4", "e5" }, "D08: Contragambito Albin"),
            (new[] { "d4", "d5", "c4" }, "D06: Gambito da Dama"),
            (new[] { "d4", "d5", "Nf3", "Nf6", "e3" }, "D05: Sistema Colle"),
            (new[] { "d4", "d5", "Bf4" }, "D00: Sistema Londres Clássico"),
            (new[] { "d4", "d5", "e4" }, "D00: Gambito Blackmar-Diemer"),
            (new[] { "d4", "Nf6", "c4", "g6", "Nc3", "Bg7", "e4", "d6" }, "E70: Defesa Índia do Rei Clássica"),
            (new[] { "d4", "Nf6", "c4", "g6", "Nc3", "Bg7" }, "E60: Defesa Índia do Rei"),
            (new[] { "d4", "Nf6", "c4", "g6", "Nc3", "d5" }, "D80: Defesa Grünfeld"),
            (new[] { "d4", "Nf6", "c4", "e6", "Nc3", "Bb4" }, "E20: Defesa Nimzo-Índia"),
            (new[] { "d4", "Nf6", "c4", "e6", "Nf3", "b6" }, "E12: Defesa Índia da Dama"),
            (new[] { "d4", "Nf6", "c4", "e6", "Nf3", "Bb4" }, "E11: Defesa Bogo-Índia"),
            (new[] { "d4", "Nf6", "c4", "e6", "g3" }, "E00: Abertura Catalã"),
            (new[] { "d4", "Nf6", "c4", "c5", "d5", "e6" }, "A60: Defesa Benoni Moderna"),
            (new[] { "d4", "Nf6", "c4", "c5", "d5", "b5" }, "A57: Gambito Benko (Volga)"),
            (new[] { "d4", "Nf6", "c4", "e5" }, "A51: Gambito Budapeste"),
            (new[] { "d4", "Nf6", "c4", "d6" }, "A53: Defesa Índia Antiga"),
            (new[] { "d4", "Nf6", "Nf3", "e6", "Bg5" }, "A46: Ataque Torre"),
            (new[] { "d4", "Nf6", "Bg5" }, "A45: Ataque Trompowsky"),
            (new[] { "d4", "Nf6", "Bf4" }, "A45: Sistema Londres"),
            (new[] { "d4", "f5" }, "A80: Defesa Holandesa"),

            // === ABERTURAS DE FLANCO (A00-A39) ===
            (new[] { "c4", "c5" }, "A30: Abertura Inglesa (Variante Simétrica)"),
            (new[] { "c4", "e5" }, "A20: Abertura Inglesa (Siciliana Invertida)"),
            (new[] { "c4" }, "A10: Abertura Inglesa"),
            (new[] { "Nf3", "d5", "c4" }, "A09: Abertura Réti"),
            (new[] { "Nf3", "d5", "g3" }, "A07: Ataque Índio do Rei"),
            (new[] { "Nf3" }, "A04: Abertura Réti"),
            (new[] { "f4" }, "A02: Abertura Bird"),
            (new[] { "b3" }, "A01: Abertura Nimzo-Larsen"),
            (new[] { "b4" }, "A00: Abertura Sokolsky (Orangotango)"),
            (new[] { "g3" }, "A00: Abertura Benko / Fianchetto do Rei")
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

        // Verifica se a sequência atual de lances ainda faz parte de uma linha teórica do livro de aberturas
        public static bool IsBookMove(IReadOnlyList<string> sanMoves)
        {
            if (sanMoves.Count == 0 || sanMoves.Count > 10) return false;

            foreach (var entry in Openings)
            {
                if (sanMoves.Count <= entry.moves.Length)
                {
                    bool match = true;
                    for (int i = 0; i < sanMoves.Count; i++)
                    {
                        string cleanSan = sanMoves[i].TrimEnd('+', '#', '!', '?');
                        if (cleanSan != entry.moves[i])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return true;
                }
            }
            return false;
        }

        // 30 Quebra-Cabeças Táticos (Puzzles) de Brancas e de Pretas
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
                Fen = "4k3/8/N7/3q4/8/8/8/4K3 w - - 0 1",
                SolutionUci = "a6c7",
                Hint = "Leve o Cavalo para c7 dando xeque no Rei em e8 e atacando a Dama em d5 ao mesmo tempo!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #4: Espeto de Bispo na Grande Diagonal",
                Theme = "Espeto Tático (Rei e Torre)",
                Fen = "7r/1p4k1/pB4p1/8/8/8/PPP2PPP/6K1 w - - 0 1",
                SolutionUci = "b6d4",
                Hint = "Dê xeque com o Bispo em d4 na grande diagonal para espetar o Rei em g7 e ganhar a Torre em h8!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #5: Mate de Legal",
                Theme = "Mate Clássico de Cavalos e Bispo",
                Fen = "r2qkbnr/ppp2ppp/2np4/4N3/2B1P3/2N5/PPPP1PPP/R1BbK2R w KQkq - 0 6",
                SolutionUci = "c4f7",
                Hint = "As Pretas capturaram a Dama achando que o Cavalo estava cravado. Dê o xeque em f7 com o Bispo (seguido de Cd5#)!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #6: Mate Sufocado (Philidor)",
                Theme = "Mate de Cavalo com Rei Trancado",
                Fen = "6rk/6pp/8/6N1/8/8/8/7K w - - 0 1",
                SolutionUci = "g5f7",
                Hint = "A Torre e os peões pretos trancam o próprio Rei. Dê o xeque-mate de Cavalo em f7!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #7: Mate de Anastásia",
                Theme = "Coordenação Cavalo + Torre",
                Fen = "5r2/4N1pk/8/8/8/8/5PP1/5KR1 w - - 0 1",
                SolutionUci = "g1h1",
                Hint = "O Cavalo em e7 controla g8 e g6 enquanto o peão preto bloqueia g7. Leve a Torre para a coluna h dando mate!"
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
                Fen = "2kr3r/p2n1ppp/2p5/8/5B2/8/PPP1BPPP/2KR4 w - - 0 1",
                SolutionUci = "e2a6",
                Hint = "O Bispo em f4 já corta a fuga por c7 e b8. Dê xeque-mate cruzado com o outro Bispo em a6!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #10: Mate das Ombreiras (Epaulette Mate)",
                Theme = "Rei bloqueado pelas próprias Torres",
                Fen = "3rkr2/8/8/3N4/1Q6/8/8/4K3 w - - 0 1",
                SolutionUci = "b4e7",
                Hint = "As duas Torres pretas bloqueiam a fuga lateral do próprio Rei! Leve a Dama a e7 protegida pelo Cavalo."
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
                Fen = "1n2kb1r/p4ppp/4q3/4p1B1/4P3/8/PPP2PPP/2KR3R w k - 0 17",
                SolutionUci = "d1d8",
                Hint = "O Bispo em g5 apoia a casa d8 enquanto as peças pretas bloqueiam a fuga do próprio Rei!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #14: Presente Grego (Ataque em h7)",
                Theme = "Mate com Dama e Cavalo",
                Fen = "r1bq1rk1/ppp2ppp/2np4/4p1NQ/2B1P3/8/PPPP1PPP/RNB1K2R w KQ - 0 1",
                SolutionUci = "h5h7",
                Hint = "A Dama em h5 e o Cavalo em g5 convergem sobre h7. Dê xeque-mate em h7 com a Dama!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #15: Coroação com Xeque-Mate",
                Theme = "Promoção de Peão Decisiva",
                Fen = "6k1/3P1ppp/8/8/8/8/5PPP/6K1 w - - 0 1",
                SolutionUci = "d7d8",
                Hint = "Avance o peão passado de d7 para d8 promovendo para Dama com mate no corredor!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #16: Mate de Lolli (Infiltração em g7)",
                Theme = "Dama e Peão Avançado",
                Fen = "5rk1/5p2/5PpQ/7p/8/8/6PP/6K1 w - - 0 1",
                SolutionUci = "h6g7",
                Hint = "O peão avançado em f6 apoia a casa escura g7. Infiltre a Dama em g7 para dar xeque-mate!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #17: Mate de Greco na Coluna h",
                Theme = "Bispo Cortando a Fuga + Dama",
                Fen = "5r1k/6p1/8/8/2B2Q2/8/6PP/6K1 w - - 0 1",
                SolutionUci = "f4h4",
                Hint = "O Bispo em c4 vigia a casa g8 e o peão preto bloqueia g7. Leve a Dama para h4 dando mate na coluna h!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #18: Ataque Descoberto Ganhando a Dama",
                Theme = "Xeque Descoberto na Coluna d",
                Fen = "4k3/8/8/3q4/8/3B4/8/3R2K1 w - - 0 1",
                SolutionUci = "d3b5",
                Hint = "Dê xeque com o Bispo em b5 para abrir a linha da sua Torre de d1 contra a Dama preta em d5!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #19: Mate com Cravada na Coluna h",
                Theme = "Cavalo + Bispo + Torre Cravando",
                Fen = "7k/6pp/8/4N3/8/1B6/6PP/6KR w - - 0 1",
                SolutionUci = "e5g6",
                Hint = "Salte com o Cavalo para g6! O peão de h7 não pode capturar porque está cravado pela Torre de h1."
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #20: O Beijo da Morte (Dama + Rei)",
                Theme = "Mate de Dama Apoiada pelo Rei",
                Fen = "7k/8/5K2/8/8/8/6Q1/8 w - - 0 1",
                SolutionUci = "g2g7",
                Hint = "Seu Rei em f6 apoia a casa g7 ao lado do Rei adversário. Leve a Dama até g7 para o mate!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #21: Mate da Escadinha (Duas Torres)",
                Theme = "Coordenação de Torres na 7ª e 8ª",
                Fen = "7k/1R6/8/8/8/8/8/R5K1 w - - 0 1",
                SolutionUci = "a1a8",
                Hint = "A Torre em b7 já impede o Rei preto de descer para a 7ª fileira. Complete a escadinha com Ta8#!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #22: Mate do Corredor de Pretas",
                Theme = "Pretas Jogam e Dão Mate",
                Fen = "6k1/5ppp/8/8/3r4/8/5PPP/6K1 b - - 0 1",
                SolutionUci = "d4d1",
                Hint = "Você está de Pretas! O Rei branco não tem respiro atrás dos peões de f2, g2 e h2. Jogue Td1#!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #23: Garfo Real de Pretas",
                Theme = "Pretas Jogam: Ataque Duplo (Rei e Dama)",
                Fen = "4k3/8/8/8/3n4/Q7/8/4K3 b - - 0 1",
                SolutionUci = "d4c2",
                Hint = "Você está de Pretas! Salte com o Cavalo para c2 dando xeque no Rei em e1 e atacando a Dama em a3!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #24: Mate Sufocado de Pretas",
                Theme = "Pretas Jogam: Mate de Cavalo",
                Fen = "6k1/8/8/8/8/7n/6PP/6RK b - - 0 1",
                SolutionUci = "h3f2",
                Hint = "Você está de Pretas! O Rei branco em h1 está trancado pela própria Torre e peões. Jogue Cf2#!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #25: Captura de Dama Cravada",
                Theme = "Cravada Absoluta na Coluna d",
                Fen = "3k4/8/3q4/8/8/6B1/8/3R2K1 w - - 0 1",
                SolutionUci = "g3d6",
                Hint = "A Dama preta em d6 está absolutamente cravada pela sua Torre de d1. Capture-a com o Bispo!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #26: Mate de Damiano Clássico",
                Theme = "Ataque Mortal na Coluna h",
                Fen = "6rk/5p2/5P2/3Q4/8/8/6PP/6K1 w - - 0 1",
                SolutionUci = "d5h5",
                Hint = "A Torre preta em g8 bloqueia a fuga do próprio Rei e seu peão em f6 controla g7. Leve a Dama para h5!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #27: Mate de Anderssen",
                Theme = "Torre Apoiada pelo Peão em g7",
                Fen = "6k1/5pP1/5K2/8/8/8/8/7R w - - 0 1",
                SolutionUci = "h1h8",
                Hint = "O peão em g7 (protegido pelo seu Rei em f6) defende a casa h8. Leve a Torre até h8 para dar mate!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #28: Bateria na Grande Diagonal",
                Theme = "Dama + Bispo em a1-h8",
                Fen = "r4rk1/ppp2p1p/6p1/8/8/2Q5/PB3PPP/1K6 w - - 0 1",
                SolutionUci = "c3g7",
                Hint = "As casas escuras do roque preto estão enfraquecidas. Use a bateria Bispo + Dama levando a Dama a g7!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #29: Ataque Duplo de Dama nas Diagonais",
                Theme = "Xeque + Ataque à Torre Indefesa",
                Fen = "r5k1/7p/5pp1/8/8/8/5PPP/3Q2K1 w - - 0 1",
                SolutionUci = "d1d5",
                Hint = "Centralize a Dama em d5 dando xeque na diagonal d5-g8 e atacando simultaneamente a Torre em a8!"
            },
            new TacticalPuzzle
            {
                Title = "Puzzle #30: Bateria Fatal das Pretas",
                Theme = "Pretas Jogam: Mate em g2",
                Fen = "6k1/1b3ppp/8/8/8/5q2/5P1P/6K1 b - - 0 1",
                SolutionUci = "f3g2",
                Hint = "Você está de Pretas! O Bispo em b7 apoia a Dama na grande diagonal. Jogue Dg2# para dar xeque-mate!"
            }
        };
    }
}
