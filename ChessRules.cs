using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace XadrezApp
{
    public class GameSnapshot
    {
        public char[,] Board { get; set; } = new char[8, 8];
        public bool WhiteTurn { get; set; } = true;
        public bool CastleWK { get; set; } = true;
        public bool CastleWQ { get; set; } = true;
        public bool CastleBK { get; set; } = true;
        public bool CastleBQ { get; set; } = true;
        public Point? EnPassantTarget { get; set; } = null;
        public int HalfMoveClock { get; set; } = 0;
        public int FullMoveNumber { get; set; } = 1;
        public List<string> PositionHistory { get; set; } = new List<string>();
        public List<string>SanMoves { get; set; } = new List<string>();
    }

    public class ChessRules
    {
        public char[,] Board { get; private set; } = new char[8, 8];
        public bool WhiteTurn { get; set; } = true;

        // Direitos de Roque (Pequeno e Grande)
        public bool CastleWK { get; set; } = true;
        public bool CastleWQ { get; set; } = true;
        public bool CastleBK { get; set; } = true;
        public bool CastleBQ { get; set; } = true;

        // Casa alvo de En Passant
        public Point? EnPassantTarget { get; set; } = null;

        // Contadores para Regra dos 50 Lances e Número do Lance
        public int HalfMoveClock { get; set; } = 0;
        public int FullMoveNumber { get; set; } = 1;

        // Histórico de posições para Repetição de 3 Lances e lances para PGN/Aberturas
        public List<string> PositionHistory { get; private set; } = new List<string>();
        public List<string> SanMoves { get; private set; } = new List<string>();

        public ChessRules()
        {
            Reset();
        }

        public void Reset()
        {
            LoadFen("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        }

        public GameSnapshot CreateSnapshot()
        {
            return new GameSnapshot
            {
                Board = (char[,])Board.Clone(),
                WhiteTurn = WhiteTurn,
                CastleWK = CastleWK,
                CastleWQ = CastleWQ,
                CastleBK = CastleBK,
                CastleBQ = CastleBQ,
                EnPassantTarget = EnPassantTarget,
                HalfMoveClock = HalfMoveClock,
                FullMoveNumber = FullMoveNumber,
                PositionHistory = new List<string>(PositionHistory),
                SanMoves = new List<string>(SanMoves)
            };
        }

        public void RestoreSnapshot(GameSnapshot snap)
        {
            Board = (char[,])snap.Board.Clone();
            WhiteTurn = snap.WhiteTurn;
            CastleWK = snap.CastleWK;
            CastleWQ = snap.CastleWQ;
            CastleBK = snap.CastleBK;
            CastleBQ = snap.CastleBQ;
            EnPassantTarget = snap.EnPassantTarget;
            HalfMoveClock = snap.HalfMoveClock;
            FullMoveNumber = snap.FullMoveNumber;
            PositionHistory = new List<string>(snap.PositionHistory);
            SanMoves = new List<string>(snap.SanMoves);
        }

        public static string SquareToAlgebraic(Point p) => $"{(char)('a' + p.X)}{8 - p.Y}";

        public static Point? AlgebraicToSquare(string sq)
        {
            if (string.IsNullOrWhiteSpace(sq) || sq.Length < 2 || sq == "-") return null;
            int col = sq[0] - 'a';
            int row = 8 - (sq[1] - '0');
            if (col < 0 || col > 7 || row < 0 || row > 7) return null;
            return new Point(col, row);
        }

        public string ExecuteMove(Point from, Point to, char? chosenPromotion = null)
        {
            char piece = Board[from.Y, from.X];
            bool isWhite = char.IsUpper(piece);
            char upperPiece = char.ToUpper(piece);
            bool isCapture = Board[to.Y, to.X] != '.';

            // Verifica se é captura En Passant
            bool isEnPassantCapture = (upperPiece == 'P' && EnPassantTarget.HasValue && to == EnPassantTarget.Value && Board[to.Y, to.X] == '.');
            if (isEnPassantCapture)
            {
                isCapture = true;
                int capturedPawnRow = isWhite ? to.Y + 1 : to.Y - 1;
                Board[capturedPawnRow, to.X] = '.';
            }

            // Verifica se é Roque (Pequeno ou Grande)
            string? castlingNotation = null;
            if (upperPiece == 'K' && Math.Abs(to.X - from.X) == 2)
            {
                if (to.X == 6) // Roque Pequeno (O-O)
                {
                    Board[from.Y, 5] = Board[from.Y, 7];
                    Board[from.Y, 7] = '.';
                    castlingNotation = "O-O";
                }
                else if (to.X == 2) // Roque Grande (O-O-O)
                {
                    Board[from.Y, 3] = Board[from.Y, 0];
                    Board[from.Y, 0] = '.';
                    castlingNotation = "O-O-O";
                }
            }

            // Move a peça principal
            Board[to.Y, to.X] = piece;
            Board[from.Y, from.X] = '.';

            // Promoção de Peão
            string promoSuffix = "";
            if (upperPiece == 'P' && (to.Y == 0 || to.Y == 7))
            {
                char promoPiece = chosenPromotion ?? (isWhite ? 'Q' : 'q');
                promoPiece = isWhite ? char.ToUpper(promoPiece) : char.ToLower(promoPiece);
                Board[to.Y, to.X] = promoPiece;
                promoSuffix = $"={char.ToUpper(promoPiece)}";
            }

            // Atualiza casa alvo de En Passant
            if (upperPiece == 'P' && Math.Abs(to.Y - from.Y) == 2)
                EnPassantTarget = new Point(from.X, (from.Y + to.Y) / 2);
            else
                EnPassantTarget = null;

            // Atualiza direitos de Roque se Rei ou Torres se moverem ou forem capturadas
            if (piece == 'K') { CastleWK = false; CastleWQ = false; }
            if (piece == 'k') { CastleBK = false; CastleBQ = false; }
            if (from == new Point(0, 7) || to == new Point(0, 7)) CastleWQ = false;
            if (from == new Point(7, 7) || to == new Point(7, 7)) CastleWK = false;
            if (from == new Point(0, 0) || to == new Point(0, 0)) CastleBQ = false;
            if (from == new Point(7, 0) || to == new Point(7, 0)) CastleBK = false;

            // Regra dos 50 Movimentos
            if (upperPiece == 'P' || isCapture)
                HalfMoveClock = 0;
            else
                HalfMoveClock++;

            if (!WhiteTurn)
                FullMoveNumber++;

            WhiteTurn = !WhiteTurn;

            // Monta notação algébrica (SAN)
            string san;
            if (castlingNotation != null)
            {
                san = castlingNotation;
            }
            else
            {
                string pChar = upperPiece == 'P' ? (isCapture ? ((char)('a' + from.X)).ToString() : "") : upperPiece.ToString();
                string capStr = isCapture ? "x" : "";
                san = $"{pChar}{capStr}{SquareToAlgebraic(to)}{promoSuffix}";
            }

            if (IsKingInCheck(Board, WhiteTurn))
            {
                var nextMoves = GetAllLegalMoves(WhiteTurn);
                san += (nextMoves.Count == 0) ? "#" : "+";
            }

            SanMoves.Add(san);
            PositionHistory.Add(GetPositionKey());
            return san;
        }

        // Chave da posição para detectar Repetição de 3 Lances
        public string GetPositionKey()
        {
            string fen = ToFen();
            var parts = fen.Split(' ');
            return parts.Length >= 4 ? $"{parts[0]} {parts[1]} {parts[2]} {parts[3]}" : fen;
        }

        // Verificação Completa de Fim de Jogo (Mate, Afogamento, 3 Repetições, 50 Lances, Material Insuficiente)
        public bool CheckEndGame(out string reasonTitle, out string reasonDetail)
        {
            reasonTitle = "";
            reasonDetail = "";

            var legal = GetAllLegalMoves(WhiteTurn);
            if (legal.Count == 0)
            {
                if (IsKingInCheck(Board, WhiteTurn))
                {
                    string winner = WhiteTurn ? "Pretas (Stockfish)" : "Brancas (Você)";
                    reasonTitle = "Xeque-mate!";
                    reasonDetail = $"Xeque-mate! Vitória de: {winner}";
                }
                else
                {
                    reasonTitle = "Empate por Afogamento";
                    reasonDetail = "Empate! Rei afogado (Stalemate) — nenhum lance legal disponível.";
                }
                return true;
            }

            // 1. Regra dos 50 movimentos (100 meio-lances)
            if (HalfMoveClock >= 100)
            {
                reasonTitle = "Empate - Regra dos 50 Movimentos";
                reasonDetail = "Empate automático! Foram feitos 50 lances completos sem captura e sem movimento de peão.";
                return true;
            }

            // 2. Empate por Repetição de 3 Lances
            string currentKey = GetPositionKey();
            int repetitions = PositionHistory.Count(k => k == currentKey);
            if (repetitions >= 3)
            {
                reasonTitle = "Empate por Repetição (3 Lances)";
                reasonDetail = "Empate automático! A mesma posição se repetiu 3 vezes na partida.";
                return true;
            }

            // 3. Insuficiência de Material
            if (IsInsufficientMaterial())
            {
                reasonTitle = "Empate por Insuficiência de Material";
                reasonDetail = "Empate automático! Nenhum dos lados possui material suficiente para dar xeque-mate.";
                return true;
            }

            return false;
        }

        public bool IsInsufficientMaterial()
        {
            var pieces = new List<(char piece, int r, int c)>();
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = Board[r, c];
                    if (p != '.' && char.ToUpper(p) != 'K')
                        pieces.Add((p, r, c));
                }
            }

            // Rei vs Rei
            if (pieces.Count == 0) return true;

            // Rei + Bispo vs Rei ou Rei + Cavalo vs Rei
            if (pieces.Count == 1)
            {
                char u = char.ToUpper(pieces[0].piece);
                if (u == 'B' || u == 'N') return true;
            }

            // Rei + Bispo vs Rei + Bispo (ambos os bispos na mesma cor de casa)
            if (pieces.Count == 2)
            {
                if (char.ToUpper(pieces[0].piece) == 'B' && char.ToUpper(pieces[1].piece) == 'B' &&
                    char.IsUpper(pieces[0].piece) != char.IsUpper(pieces[1].piece))
                {
                    bool color0 = (pieces[0].r + pieces[0].c) % 2 == 0;
                    bool color1 = (pieces[1].r + pieces[1].c) % 2 == 0;
                    if (color0 == color1) return true;
                }
            }

            return false;
        }

        public List<(Point from, Point to)> GetAllLegalMoves(bool forWhite)
        {
            var list = new List<(Point from, Point to)>();
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = Board[r, c];
                    if (p != '.' && char.IsUpper(p) == forWhite)
                    {
                        foreach (var to in GetLegalMovesForSquare(c, r, forWhite))
                            list.Add((new Point(c, r), to));
                    }
                }
            }
            return list;
        }

        public List<Point> GetLegalMovesForSquare(int col, int row, bool forWhite)
        {
            var pseudo = GetPseudoMoves(Board, col, row, forWhite, true);
            var legal = new List<Point>();
            char movingPiece = Board[row, col];

            foreach (var to in pseudo)
            {
                // Validação extra de Roque: Rei não pode estar em xeque nem passar por casa atacada
                if (char.ToUpper(movingPiece) == 'K' && Math.Abs(to.X - col) == 2)
                {
                    if (IsKingInCheck(Board, forWhite)) continue;
                    int midCol = (col + to.X) / 2;
                    char[,] midCopy = (char[,])Board.Clone();
                    midCopy[row, midCol] = movingPiece;
                    midCopy[row, col] = '.';
                    if (IsKingInCheck(midCopy, forWhite)) continue;
                }

                char[,] copy = (char[,])Board.Clone();
                // Se for captura En Passant na simulação, remove o peão adversário
                if (char.ToUpper(movingPiece) == 'P' && EnPassantTarget.HasValue && to == EnPassantTarget.Value && copy[to.Y, to.X] == '.')
                {
                    int capRow = forWhite ? to.Y + 1 : to.Y - 1;
                    copy[capRow, to.X] = '.';
                }

                copy[to.Y, to.X] = copy[row, col];
                copy[row, col] = '.';

                if (!IsKingInCheck(copy, forWhite))
                    legal.Add(to);
            }
            return legal;
        }

        public bool IsKingInCheck(char[,] b, bool whiteKing)
        {
            char kingChar = whiteKing ? 'K' : 'k';
            Point? kingPos = null;

            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (b[r, c] == kingChar) kingPos = new Point(c, r);

            if (!kingPos.HasValue) return true;

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = b[r, c];
                    if (p != '.' && char.IsUpper(p) != whiteKing)
                    {
                        var moves = GetPseudoMoves(b, c, r, !whiteKing, false);
                        if (moves.Contains(kingPos.Value)) return true;
                    }
                }
            }
            return false;
        }

        public List<Point> GetPseudoMoves(char[,] b, int c, int r, bool isWhite, bool includeCastlingAndEp)
        {
            var moves = new List<Point>();
            char p = char.ToLower(b[r, c]);

            bool Inside(int x, int y) => x >= 0 && x < 8 && y >= 0 && y < 8;
            bool IsEnemy(int x, int y) => b[y, x] != '.' && char.IsUpper(b[y, x]) != isWhite;

            if (p == 'p')
            {
                int dir = isWhite ? -1 : 1;
                int startRow = isWhite ? 6 : 1;

                if (Inside(c, r + dir) && b[r + dir, c] == '.')
                {
                    moves.Add(new Point(c, r + dir));
                    if (r == startRow && b[r + dir * 2, c] == '.')
                        moves.Add(new Point(c, r + dir * 2));
                }
                foreach (int dc in new[] { -1, 1 })
                {
                    int nx = c + dc, ny = r + dir;
                    if (Inside(nx, ny))
                    {
                        if (IsEnemy(nx, ny))
                            moves.Add(new Point(nx, ny));
                        else if (includeCastlingAndEp && EnPassantTarget.HasValue && EnPassantTarget.Value.X == nx && EnPassantTarget.Value.Y == ny)
                            moves.Add(new Point(nx, ny));
                    }
                }
            }
            else if (p == 'n')
            {
                int[] dx = { -2, -1, 1, 2, 2, 1, -1, -2 };
                int[] dy = { -1, -2, -2, -1, 1, 2, 2, 1 };
                for (int i = 0; i < 8; i++)
                {
                    int nx = c + dx[i], ny = r + dy[i];
                    if (Inside(nx, ny) && (b[ny, nx] == '.' || IsEnemy(nx, ny)))
                        moves.Add(new Point(nx, ny));
                }
            }
            else if (p == 'k')
            {
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = c + dx, ny = r + dy;
                        if (Inside(nx, ny) && (b[ny, nx] == '.' || IsEnemy(nx, ny)))
                            moves.Add(new Point(nx, ny));
                    }

                if (includeCastlingAndEp)
                {
                    int homeRow = isWhite ? 7 : 0;
                    char rookChar = isWhite ? 'R' : 'r';
                    bool canKingSide = isWhite ? CastleWK : CastleBK;
                    bool canQueenSide = isWhite ? CastleWQ : CastleBQ;

                    if (r == homeRow && c == 4)
                    {
                        if (canKingSide && b[homeRow, 5] == '.' && b[homeRow, 6] == '.' && b[homeRow, 7] == rookChar)
                            moves.Add(new Point(6, homeRow));
                        if (canQueenSide && b[homeRow, 3] == '.' && b[homeRow, 2] == '.' && b[homeRow, 1] == '.' && b[homeRow, 0] == rookChar)
                            moves.Add(new Point(2, homeRow));
                    }
                }
            }
            else
            {
                var dirs = new List<(int dx, int dy)>();
                if (p == 'r' || p == 'q') dirs.AddRange(new[] { (1, 0), (-1, 0), (0, 1), (0, -1) });
                if (p == 'b' || p == 'q') dirs.AddRange(new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) });

                foreach (var (dx, dy) in dirs)
                {
                    int nx = c + dx, ny = r + dy;
                    while (Inside(nx, ny))
                    {
                        if (b[ny, nx] == '.') moves.Add(new Point(nx, ny));
                        else
                        {
                            if (IsEnemy(nx, ny)) moves.Add(new Point(nx, ny));
                            break;
                        }
                        nx += dx;
                        ny += dy;
                    }
                }
            }
            return moves;
        }

        // Exporta o estado completo para notação FEN
        public string ToFen()
        {
            var rows = new List<string>();
            for (int r = 0; r < 8; r++)
            {
                int empty = 0;
                string rowStr = "";
                for (int c = 0; c < 8; c++)
                {
                    if (Board[r, c] == '.') empty++;
                    else
                    {
                        if (empty > 0) { rowStr += empty; empty = 0; }
                        rowStr += Board[r, c];
                    }
                }
                if (empty > 0) rowStr += empty;
                rows.Add(rowStr);
            }

            string castling = "";
            if (CastleWK) castling += "K";
            if (CastleWQ) castling += "Q";
            if (CastleBK) castling += "k";
            if (CastleBQ) castling += "q";
            if (string.IsNullOrEmpty(castling)) castling = "-";

            string ep = EnPassantTarget.HasValue ? SquareToAlgebraic(EnPassantTarget.Value) : "-";
            string turn = WhiteTurn ? "w" : "b";

            return $"{string.Join("/", rows)} {turn} {castling} {ep} {HalfMoveClock} {FullMoveNumber}";
        }

        // Carrega qualquer posição via string FEN (usado nos Puzzles e Importação)
        public bool LoadFen(string fen)
        {
            try
            {
                var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 1) return false;

                var rows = parts[0].Split('/');
                if (rows.Length != 8) return false;

                var newBoard = new char[8, 8];
                for (int r = 0; r < 8; r++)
                {
                    int c = 0;
                    foreach (char ch in rows[r])
                    {
                        if (char.IsDigit(ch))
                        {
                            int spaces = ch - '0';
                            for (int s = 0; s < spaces && c < 8; s++)
                                newBoard[r, c++] = '.';
                        }
                        else if (c < 8)
                        {
                            newBoard[r, c++] = ch;
                        }
                    }
                }

                Board = newBoard;
                WhiteTurn = parts.Length < 2 || parts[1] == "w";

                string castling = parts.Length >= 3 ? parts[2] : "KQkq";
                CastleWK = castling.Contains('K');
                CastleWQ = castling.Contains('Q');
                CastleBK = castling.Contains('k');
                CastleBQ = castling.Contains('q');

                EnPassantTarget = parts.Length >= 4 ? AlgebraicToSquare(parts[3]) : null;
                HalfMoveClock = (parts.Length >= 5 && int.TryParse(parts[4], out int hm)) ? hm : 0;
                FullMoveNumber = (parts.Length >= 6 && int.TryParse(parts[5], out int fm)) ? fm : 1;

                PositionHistory.Clear();
                SanMoves.Clear();
                PositionHistory.Add(GetPositionKey());
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Gera arquivo PGN completo da partida para salvar ou copiar
        public string ToPgn(string result = "*")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Event \"Partida Clássica vs Stockfish\"]");
            sb.AppendLine($"[Site \"Xadrez Desktop C#\"]");
            sb.AppendLine($"[Date \"{DateTime.Now:yyyy.MM.dd}\"]");
            sb.AppendLine($"[White \"Jogador\"]");
            sb.AppendLine($"[Black \"Stockfish IA\"]");
            sb.AppendLine($"[Result \"{result}\"]");
            sb.AppendLine();

            for (int i = 0; i < SanMoves.Count; i++)
            {
                if (i % 2 == 0)
                    sb.Append($"{(i / 2) + 1}. {SanMoves[i]} ");
                else
                    sb.Append($"{SanMoves[i]} ");
            }
            sb.Append(result);
            return sb.ToString().Trim();
        }
    }
}