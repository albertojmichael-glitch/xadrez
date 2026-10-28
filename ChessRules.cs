using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XadrezApp
{
    public class GameSnapshot
    {
        public char[,] Board { get; set; } = new char[8, 8];
        public byte[,] PieceVariants { get; set; } = new byte[8, 8];
        public bool WhiteTurn { get; set; } = true;
        public bool CastleWK { get; set; } = true;
        public bool CastleWQ { get; set; } = true;
        public bool CastleBK { get; set; } = true;
        public bool CastleBQ { get; set; } = true;
        public Point? EnPassantTarget { get; set; } = null;
        public Point? LastMoveFrom { get; set; } = null;
        public Point? LastMoveTo { get; set; } = null;
        public int HalfMoveClock { get; set; } = 0;
        public int FullMoveNumber { get; set; } = 1;
        public string InitialFen { get; set; } = ChessRules.StandardStartFen;
        public bool InitialWhiteTurn { get; set; } = true;
        public int InitialMoveNumber { get; set; } = 1;
        public string GameResult { get; set; } = "*";
        public List<string> PositionHistory { get; set; } = new List<string>();
        public List<string> SanMoves { get; set; } = new List<string>();
    }

    public class ChessRules
    {
        public const string StandardStartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        public char[,] Board { get; private set; } = new char[8, 8];
        // Identifica qual variante visual (1 ou 2) cada peça usa, acompanhando a peça quando ela se move
        public byte[,] PieceVariants { get; private set; } = new byte[8, 8];
        public bool WhiteTurn { get; set; } = true;

        // Direitos de Roque (Pequeno e Grande)
        public bool CastleWK { get; set; } = true;
        public bool CastleWQ { get; set; } = true;
        public bool CastleBK { get; set; } = true;
        public bool CastleBQ { get; set; } = true;

        // Casa alvo de En Passant
        public Point? EnPassantTarget { get; set; } = null;

        // Casas de origem e destino do último lance executado (para destaque no tabuleiro)
        public Point? LastMoveFrom { get; set; } = null;
        public Point? LastMoveTo { get; set; } = null;

        // Contadores para Regra dos 50 Lances e Número do Lance
        public int HalfMoveClock { get; set; } = 0;
        public int FullMoveNumber { get; set; } = 1;

        // Metadados de início e resultado da partida para exportação PGN fiel
        public string InitialFen { get; private set; } = StandardStartFen;
        public bool InitialWhiteTurn { get; private set; } = true;
        public int InitialMoveNumber { get; private set; } = 1;
        public string GameResult { get; set; } = "*";

        // Histórico de posições para Repetição de 3 Lances e lances para PGN/Aberturas
        public List<string> PositionHistory { get; private set; } = new List<string>();
        public List<string> SanMoves { get; private set; } = new List<string>();

        public ChessRules()
        {
            Reset();
        }

        public void Reset()
        {
            LoadFen(StandardStartFen);
        }

        public GameSnapshot CreateSnapshot()
        {
            return new GameSnapshot
            {
                Board = (char[,])Board.Clone(),
                PieceVariants = (byte[,])PieceVariants.Clone(),
                WhiteTurn = WhiteTurn,
                CastleWK = CastleWK,
                CastleWQ = CastleWQ,
                CastleBK = CastleBK,
                CastleBQ = CastleBQ,
                EnPassantTarget = EnPassantTarget,
                LastMoveFrom = LastMoveFrom,
                LastMoveTo = LastMoveTo,
                HalfMoveClock = HalfMoveClock,
                FullMoveNumber = FullMoveNumber,
                InitialFen = InitialFen,
                InitialWhiteTurn = InitialWhiteTurn,
                InitialMoveNumber = InitialMoveNumber,
                GameResult = GameResult,
                PositionHistory = new List<string>(PositionHistory),
                SanMoves = new List<string>(SanMoves)
            };
        }

        public void RestoreSnapshot(GameSnapshot snap)
        {
            Board = (char[,])snap.Board.Clone();
            PieceVariants = (byte[,])snap.PieceVariants.Clone();
            WhiteTurn = snap.WhiteTurn;
            CastleWK = snap.CastleWK;
            CastleWQ = snap.CastleWQ;
            CastleBK = snap.CastleBK;
            CastleBQ = snap.CastleBQ;
            EnPassantTarget = snap.EnPassantTarget;
            LastMoveFrom = snap.LastMoveFrom;
            LastMoveTo = snap.LastMoveTo;
            HalfMoveClock = snap.HalfMoveClock;
            FullMoveNumber = snap.FullMoveNumber;
            InitialFen = snap.InitialFen;
            InitialWhiteTurn = snap.InitialWhiteTurn;
            InitialMoveNumber = snap.InitialMoveNumber;
            GameResult = snap.GameResult;
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

        public Point? FindKingSquare(bool whiteKing)
        {
            char kingChar = whiteKing ? 'K' : 'k';
            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    if (Board[r, c] == kingChar)
                        return new Point(c, r);
            return null;
        }

        public Point? GetCheckedKingSquare()
        {
            if (IsKingInCheck(Board, true))
                return FindKingSquare(true);
            if (IsKingInCheck(Board, false))
                return FindKingSquare(false);
            return null;
        }

        public bool IsCaptureDestination(Point? from, Point to)
        {
            if (Board[to.Y, to.X] != '.')
                return true;
            if (from.HasValue)
            {
                char moving = Board[from.Value.Y, from.Value.X];
                if (char.ToUpper(moving) == 'P' && EnPassantTarget.HasValue && EnPassantTarget.Value == to)
                    return true;
            }
            return false;
        }

        // Calcula as peças capturadas por cada lado e a diferença de material em peões (positiva para Brancas)
        public (List<char> capturedByWhite, List<char> capturedByBlack, int materialDiffWhite) GetCapturedPiecesAndMaterial()
        {
            int wQ = 0, wR = 0, wB = 0, wN = 0, wP = 0;
            int bQ = 0, bR = 0, bB = 0, bN = 0, bP = 0;

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    switch (Board[r, c])
                    {
                        case 'Q': wQ++; break;
                        case 'R': wR++; break;
                        case 'B': wB++; break;
                        case 'N': wN++; break;
                        case 'P': wP++; break;
                        case 'q': bQ++; break;
                        case 'r': bR++; break;
                        case 'b': bB++; break;
                        case 'n': bN++; break;
                        case 'p': bP++; break;
                    }
                }
            }

            int wPromos = Math.Max(0, wQ - 1) + Math.Max(0, wR - 2) + Math.Max(0, wB - 2) + Math.Max(0, wN - 2);
            int bPromos = Math.Max(0, bQ - 1) + Math.Max(0, bR - 2) + Math.Max(0, bB - 2) + Math.Max(0, bN - 2);

            // Peças pretas capturadas pelas Brancas
            var capturedByWhite = new List<char>();
            for (int i = 0; i < Math.Max(0, 1 - bQ); i++) capturedByWhite.Add('q');
            for (int i = 0; i < Math.Max(0, 2 - bR); i++) capturedByWhite.Add('r');
            for (int i = 0; i < Math.Max(0, 2 - bB); i++) capturedByWhite.Add('b');
            for (int i = 0; i < Math.Max(0, 2 - bN); i++) capturedByWhite.Add('n');
            for (int i = 0; i < Math.Max(0, 8 - bP - bPromos); i++) capturedByWhite.Add('p');

            // Peças brancas capturadas pelas Pretas
            var capturedByBlack = new List<char>();
            for (int i = 0; i < Math.Max(0, 1 - wQ); i++) capturedByBlack.Add('Q');
            for (int i = 0; i < Math.Max(0, 2 - wR); i++) capturedByBlack.Add('R');
            for (int i = 0; i < Math.Max(0, 2 - wB); i++) capturedByBlack.Add('B');
            for (int i = 0; i < Math.Max(0, 2 - wN); i++) capturedByBlack.Add('N');
            for (int i = 0; i < Math.Max(0, 8 - wP - wPromos); i++) capturedByBlack.Add('P');

            int whitePoints = wQ * 9 + wR * 5 + wB * 3 + wN * 3 + wP;
            int blackPoints = bQ * 9 + bR * 5 + bB * 3 + bN * 3 + bP;

            return (capturedByWhite, capturedByBlack, whitePoints - blackPoints);
        }

        public string ExecuteMove(Point from, Point to, char? chosenPromotion = null)
        {
            char piece = Board[from.Y, from.X];
            bool isWhite = char.IsUpper(piece);
            char upperPiece = char.ToUpper(piece);
            bool isCapture = Board[to.Y, to.X] != '.';

            // Desambiguação SAN (ex.: Nbd2, Rae1, R1e3, Qa1d4) calculada ANTES de alterar o tabuleiro
            string disambiguation = "";
            if (upperPiece != 'P' && upperPiece != 'K')
            {
                var ambiguousPeers = new List<Point>();
                for (int r = 0; r < 8; r++)
                {
                    for (int c = 0; c < 8; c++)
                    {
                        if ((c != from.X || r != from.Y) && Board[r, c] == piece)
                        {
                            if (GetLegalMovesForSquare(c, r, isWhite).Contains(to))
                                ambiguousPeers.Add(new Point(c, r));
                        }
                    }
                }

                if (ambiguousPeers.Count > 0)
                {
                    bool sameFile = ambiguousPeers.Any(p => p.X == from.X);
                    bool sameRank = ambiguousPeers.Any(p => p.Y == from.Y);
                    if (!sameFile)
                        disambiguation = ((char)('a' + from.X)).ToString();
                    else if (!sameRank)
                        disambiguation = (8 - from.Y).ToString();
                    else
                        disambiguation = SquareToAlgebraic(from);
                }
            }

            // Verifica se é captura En Passant
            bool isEnPassantCapture = (upperPiece == 'P' && EnPassantTarget.HasValue && to == EnPassantTarget.Value && Board[to.Y, to.X] == '.');
            if (isEnPassantCapture)
            {
                isCapture = true;
                int capturedPawnRow = isWhite ? to.Y + 1 : to.Y - 1;
                Board[capturedPawnRow, to.X] = '.';
                PieceVariants[capturedPawnRow, to.X] = 0;
            }

            // Verifica se é Roque (Pequeno ou Grande)
            string? castlingNotation = null;
            if (upperPiece == 'K' && Math.Abs(to.X - from.X) == 2)
            {
                if (to.X == 6) // Roque Pequeno (O-O)
                {
                    Board[from.Y, 5] = Board[from.Y, 7];
                    Board[from.Y, 7] = '.';
                    PieceVariants[from.Y, 5] = PieceVariants[from.Y, 7];
                    PieceVariants[from.Y, 7] = 0;
                    castlingNotation = "O-O";
                }
                else if (to.X == 2) // Roque Grande (O-O-O)
                {
                    Board[from.Y, 3] = Board[from.Y, 0];
                    Board[from.Y, 0] = '.';
                    PieceVariants[from.Y, 3] = PieceVariants[from.Y, 0];
                    PieceVariants[from.Y, 0] = 0;
                    castlingNotation = "O-O-O";
                }
            }

            // Move a peça principal (e sua variante de imagem para nunca trocar de aparência)
            Board[to.Y, to.X] = piece;
            Board[from.Y, from.X] = '.';
            PieceVariants[to.Y, to.X] = PieceVariants[from.Y, from.X];
            PieceVariants[from.Y, from.X] = 0;

            LastMoveFrom = from;
            LastMoveTo = to;

            // Promoção de Peão
            string promoSuffix = "";
            if (upperPiece == 'P' && (to.Y == 0 || to.Y == 7))
            {
                char promoPiece = chosenPromotion ?? (isWhite ? 'Q' : 'q');
                promoPiece = isWhite ? char.ToUpper(promoPiece) : char.ToLower(promoPiece);
                Board[to.Y, to.X] = promoPiece;
                PieceVariants[to.Y, to.X] = 1;
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

            // Monta notação algébrica (SAN) completa com desambiguação
            string san;
            if (castlingNotation != null)
            {
                san = castlingNotation;
            }
            else
            {
                string pChar = upperPiece == 'P' ? (isCapture ? ((char)('a' + from.X)).ToString() : "") : upperPiece.ToString();
                string capStr = isCapture ? "x" : "";
                san = $"{pChar}{disambiguation}{capStr}{SquareToAlgebraic(to)}{promoSuffix}";
            }

            if (IsKingInCheck(Board, WhiteTurn))
            {
                var nextMoves = GetAllLegalMoves(WhiteTurn);
                if (nextMoves.Count == 0)
                {
                    san += "#";
                    GameResult = WhiteTurn ? "0-1" : "1-0";
                }
                else
                {
                    san += "+";
                }
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
                    GameResult = WhiteTurn ? "0-1" : "1-0";
                    string winner = WhiteTurn ? "Pretas" : "Brancas";
                    reasonTitle = "Xeque-mate!";
                    reasonDetail = $"Xeque-mate! Vitória de: {winner}";
                }
                else
                {
                    GameResult = "1/2-1/2";
                    reasonTitle = "Empate por Afogamento";
                    reasonDetail = "Empate! Rei afogado (Stalemate) — nenhum lance legal disponível.";
                }
                return true;
            }

            // 1. Regra dos 50 movimentos (100 meio-lances)
            if (HalfMoveClock >= 100)
            {
                GameResult = "1/2-1/2";
                reasonTitle = "Empate - Regra dos 50 Movimentos";
                reasonDetail = "Empate automático! Foram feitos 50 lances completos sem captura e sem movimento de peão.";
                return true;
            }

            // 2. Empate por Repetição de 3 Lances
            string currentKey = GetPositionKey();
            int repetitions = PositionHistory.Count(k => k == currentKey);
            if (repetitions >= 3)
            {
                GameResult = "1/2-1/2";
                reasonTitle = "Empate por Repetição (3 Lances)";
                reasonDetail = "Empate automático! A mesma posição se repetiu 3 vezes na partida.";
                return true;
            }

            // 3. Insuficiência de Material
            if (IsInsufficientMaterial())
            {
                GameResult = "1/2-1/2";
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
        public bool LoadFen(string fen) => TryLoadFen(fen, out _);

        // Valida o FEN por completo ANTES de alterar o estado atual. Em caso de erro,
        // o tabuleiro não é modificado e 'error' descreve o problema.
        public bool TryLoadFen(string fen, out string error)
        {
            error = "";
            try
            {
                if (string.IsNullOrWhiteSpace(fen)) { error = "FEN vazio."; return false; }

                var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 6) { error = "FEN com campos demais (máximo 6)."; return false; }

                var rows = parts[0].Split('/');
                if (rows.Length != 8) { error = $"O tabuleiro precisa ter 8 fileiras separadas por '/', mas tem {rows.Length}."; return false; }

                const string validPieces = "pnbrqkPNBRQK";
                var newBoard = new char[8, 8];
                int whiteKings = 0, blackKings = 0;

                for (int r = 0; r < 8; r++)
                {
                    int c = 0;
                    bool lastWasDigit = false;
                    foreach (char ch in rows[r])
                    {
                        if (ch >= '1' && ch <= '8')
                        {
                            if (lastWasDigit) { error = $"Fileira {8 - r}: dígitos consecutivos não são permitidos."; return false; }
                            int spaces = ch - '0';
                            if (c + spaces > 8) { error = $"Fileira {8 - r}: soma maior que 8 casas."; return false; }
                            for (int s = 0; s < spaces; s++)
                                newBoard[r, c++] = '.';
                            lastWasDigit = true;
                        }
                        else if (validPieces.IndexOf(ch) >= 0)
                        {
                            if (c >= 8) { error = $"Fileira {8 - r}: soma maior que 8 casas."; return false; }
                            if ((ch == 'P' || ch == 'p') && (r == 0 || r == 7))
                            {
                                error = $"Fileira {8 - r}: peões não podem estar na 1ª nem na 8ª fileira.";
                                return false;
                            }
                            if (ch == 'K') whiteKings++;
                            if (ch == 'k') blackKings++;
                            newBoard[r, c++] = ch;
                            lastWasDigit = false;
                        }
                        else
                        {
                            error = $"Fileira {8 - r}: caractere inválido '{ch}'.";
                            return false;
                        }
                    }
                    if (c != 8) { error = $"Fileira {8 - r}: soma das casas é {c}, deveria ser 8."; return false; }
                }

                if (whiteKings != 1 || blackKings != 1)
                {
                    error = $"É necessário exatamente um Rei de cada cor (brancos: {whiteKings}, pretos: {blackKings}).";
                    return false;
                }

                bool whiteTurn = true;
                if (parts.Length >= 2)
                {
                    if (parts[1] == "w") whiteTurn = true;
                    else if (parts[1] == "b") whiteTurn = false;
                    else { error = $"Lado a jogar inválido '{parts[1]}' (use 'w' ou 'b')."; return false; }
                }

                string castling = parts.Length >= 3 ? parts[2] : "KQkq";
                if (castling != "-" && (castling.Any(ch => "KQkq".IndexOf(ch) < 0) || castling.Distinct().Count() != castling.Length))
                {
                    error = $"Direitos de roque inválidos '{castling}'.";
                    return false;
                }

                Point? epTarget = null;
                if (parts.Length >= 4 && parts[3] != "-")
                {
                    epTarget = AlgebraicToSquare(parts[3]);
                    int expectedRow = whiteTurn ? 2 : 5; // 6ª fileira se as brancas jogam, 3ª se as pretas jogam
                    if (parts[3].Length != 2 || !epTarget.HasValue || epTarget.Value.Y != expectedRow)
                    {
                        error = $"Casa de en passant inválida '{parts[3]}'.";
                        return false;
                    }
                }

                int halfMove = 0, fullMove = 1;
                if (parts.Length >= 5 && (!int.TryParse(parts[4], out halfMove) || halfMove < 0))
                {
                    error = $"Contador de meio-lances inválido '{parts[4]}'.";
                    return false;
                }
                if (parts.Length >= 6 && (!int.TryParse(parts[5], out fullMove) || fullMove < 1))
                {
                    error = $"Número do lance inválido '{parts[5]}'.";
                    return false;
                }

                // O lado que NÃO está com a vez não pode estar em xeque (posição ilegal)
                if (IsKingInCheck(newBoard, !whiteTurn))
                {
                    error = whiteTurn
                        ? "Posição ilegal: o Rei preto está em xeque, mas é a vez das brancas."
                        : "Posição ilegal: o Rei branco está em xeque, mas é a vez das pretas.";
                    return false;
                }

                // Tudo validado: aplica o novo estado e inicializa o mapa fixo de variantes de imagem por peça
                var newVariants = new byte[8, 8];
                var pieceSeenCount = new Dictionary<char, int>();
                for (int r = 0; r < 8; r++)
                {
                    for (int c = 0; c < 8; c++)
                    {
                        char ch = newBoard[r, c];
                        if (ch == '.') continue;
                        int seen = pieceSeenCount.GetValueOrDefault(ch, 0) + 1;
                        pieceSeenCount[ch] = seen;
                        newVariants[r, c] = (byte)(seen >= 2 ? 2 : 1);
                    }
                }

                Board = newBoard;
                PieceVariants = newVariants;
                WhiteTurn = whiteTurn;

                // Só mantém o direito de roque se Rei e Torre estiverem nas casas de origem
                CastleWK = castling.Contains('K') && newBoard[7, 4] == 'K' && newBoard[7, 7] == 'R';
                CastleWQ = castling.Contains('Q') && newBoard[7, 4] == 'K' && newBoard[7, 0] == 'R';
                CastleBK = castling.Contains('k') && newBoard[0, 4] == 'k' && newBoard[0, 7] == 'r';
                CastleBQ = castling.Contains('q') && newBoard[0, 4] == 'k' && newBoard[0, 0] == 'r';

                EnPassantTarget = epTarget;
                LastMoveFrom = null;
                LastMoveTo = null;
                HalfMoveClock = halfMove;
                FullMoveNumber = fullMove;

                InitialFen = ToFen();
                InitialWhiteTurn = whiteTurn;
                InitialMoveNumber = fullMove;
                GameResult = "*";

                PositionHistory.Clear();
                SanMoves.Clear();
                PositionHistory.Add(GetPositionKey());
                return true;
            }
            catch (Exception ex)
            {
                error = $"FEN inválido: {ex.Message}";
                return false;
            }
        }

        // Cópia independente do estado (usada pelas tarefas em segundo plano da engine)
        public ChessRules Clone()
        {
            var copy = new ChessRules();
            copy.RestoreSnapshot(CreateSnapshot());
            return copy;
        }

        // Gera arquivo PGN completo da partida para salvar ou copiar (com SetUp/FEN, resultado real e engine ativa)
        public string ToPgn(string? result = null, string? blackPlayerName = null, string? eventName = null, string? whitePlayerName = null)
        {
            string finalResult = string.IsNullOrWhiteSpace(result) ? GameResult : result!;
            string whiteName = string.IsNullOrWhiteSpace(whitePlayerName) ? "Jogador" : whitePlayerName!;
            string opponent = string.IsNullOrWhiteSpace(blackPlayerName) ? "Engine Tática Interna" : blackPlayerName!;
            string ev = string.IsNullOrWhiteSpace(eventName) ? $"Partida Clássica vs {opponent}" : eventName!;

            var sb = new StringBuilder();
            sb.AppendLine($"[Event \"{ev}\"]");
            sb.AppendLine($"[Site \"Xadrez Desktop C#\"]");
            sb.AppendLine($"[Date \"{DateTime.Now:yyyy.MM.dd}\"]");
            sb.AppendLine($"[White \"{whiteName}\"]");
            sb.AppendLine($"[Black \"{opponent}\"]");
            sb.AppendLine($"[Result \"{finalResult}\"]");
            if (InitialFen != StandardStartFen)
            {
                sb.AppendLine("[SetUp \"1\"]");
                sb.AppendLine($"[FEN \"{InitialFen}\"]");
            }
            sb.AppendLine();

            int moveNo = InitialMoveNumber;
            bool whiteToPlay = InitialWhiteTurn;

            for (int i = 0; i < SanMoves.Count; i++)
            {
                if (whiteToPlay)
                {
                    sb.Append($"{moveNo}. {SanMoves[i]} ");
                }
                else
                {
                    if (i == 0)
                        sb.Append($"{moveNo}... {SanMoves[i]} ");
                    else
                        sb.Append($"{SanMoves[i]} ");
                    moveNo++;
                }
                whiteToPlay = !whiteToPlay;
            }
            sb.Append(finalResult);
            return sb.ToString().Trim();
        }

        // Resolve um lance em notação SAN (ex.: "Nf3", "exd5", "O-O", "e8=Q+", "Cbd7") ou UCI ("e2e4") na posição atual
        public bool TryParseSanMove(string rawMoveToken, out Point from, out Point to, out char? promo)
        {
            from = default;
            to = default;
            promo = null;

            if (string.IsNullOrWhiteSpace(rawMoveToken))
                return false;

            string token = rawMoveToken.Trim().TrimEnd('+', '#', '!', '?');
            if (string.IsNullOrEmpty(token))
                return false;

            // Normaliza roques escritos com zeros (0-0 / 0-0-0)
            if (token is "0-0" or "O-O" or "o-o") token = "O-O";
            else if (token is "0-0-0" or "O-O-O" or "o-o-o") token = "O-O-O";

            // Suporta letras de peças em português (C=N, B=B, T=R, D=Q, R=K) se o primeiro caractere for maiúsculo
            if (token.Length >= 3 && token != "O-O" && token != "O-O-O")
            {
                char first = token[0];
                char mapped = first switch
                {
                    'C' => 'N',
                    'T' => 'R',
                    'D' => 'Q',
                    _ => first
                };
                if (mapped != first)
                    token = mapped + token[1..];
            }

            var legalMoves = GetAllLegalMoves(WhiteTurn);

            // 1. Verifica formato UCI direto (ex.: e2e4, e7e8q, e2-e4)
            string cleanUci = token.Replace("-", "");
            if (cleanUci.Length is 4 or 5 &&
                cleanUci[0] is >= 'a' and <= 'h' &&
                cleanUci[1] is >= '1' and <= '8' &&
                cleanUci[2] is >= 'a' and <= 'h' &&
                cleanUci[3] is >= '1' and <= '8')
            {
                var uFrom = AlgebraicToSquare(cleanUci[..2]);
                var uTo = AlgebraicToSquare(cleanUci.Substring(2, 2));
                if (uFrom.HasValue && uTo.HasValue && legalMoves.Contains((uFrom.Value, uTo.Value)))
                {
                    from = uFrom.Value;
                    to = uTo.Value;
                    if (cleanUci.Length == 5 && "qrbnQRBN".IndexOf(cleanUci[4]) >= 0)
                        promo = WhiteTurn ? char.ToUpper(cleanUci[4]) : char.ToLower(cleanUci[4]);
                    return true;
                }
            }

            // 2. Compara com o SAN exato gerado por cada lance legal na posição atual
            char[] promoOptions = { 'Q', 'R', 'B', 'N' };
            foreach (var mv in legalMoves)
            {
                char movingPiece = Board[mv.from.Y, mv.from.X];
                bool isPromo = char.ToUpper(movingPiece) == 'P' && (mv.to.Y == 0 || mv.to.Y == 7);

                if (isPromo)
                {
                    foreach (char pOpt in promoOptions)
                    {
                        var probe = Clone();
                        string generatedSan = probe.ExecuteMove(mv.from, mv.to, pOpt).TrimEnd('+', '#');
                        string generatedNoEq = generatedSan.Replace("=", "");
                        if (string.Equals(generatedSan, token, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(generatedNoEq, token, StringComparison.OrdinalIgnoreCase))
                        {
                            from = mv.from;
                            to = mv.to;
                            promo = WhiteTurn ? char.ToUpper(pOpt) : char.ToLower(pOpt);
                            return true;
                        }
                    }
                }
                else
                {
                    var probe = Clone();
                    string generatedSan = probe.ExecuteMove(mv.from, mv.to).TrimEnd('+', '#');
                    string generatedNoCapture = generatedSan.Replace("x", "");
                    string tokenNoCapture = token.Replace("x", "");
                    if (string.Equals(generatedSan, token, StringComparison.Ordinal) ||
                        string.Equals(generatedNoCapture, tokenNoCapture, StringComparison.Ordinal))
                    {
                        from = mv.from;
                        to = mv.to;
                        promo = null;
                        return true;
                    }
                }
            }

            return false;
        }

        // Importa um texto PGN completo, validando cada lance e devolvendo os snapshots de todos os meio-lances
        public bool TryLoadPgn(
            string pgnText,
            out List<GameSnapshot> plySnapshots,
            out Dictionary<string, string> headers,
            out string error)
        {
            plySnapshots = new List<GameSnapshot>();
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = "";

            if (string.IsNullOrWhiteSpace(pgnText))
            {
                error = "O texto PGN está vazio.";
                return false;
            }

            try
            {
                var lines = pgnText.Replace("\r\n", "\n").Split('\n');
                var moveTextBuilder = new StringBuilder();
                var tagRegex = new Regex(@"^\s*\[\s*([A-Za-z0-9_]+)\s+""([^""]*)""\s*\]\s*$");

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith('%'))
                        continue;

                    var match = tagRegex.Match(line);
                    if (match.Success)
                    {
                        headers[match.Groups[1].Value] = match.Groups[2].Value;
                    }
                    else
                    {
                        int semiIdx = line.IndexOf(';');
                        if (semiIdx >= 0)
                            line = line[..semiIdx];
                        moveTextBuilder.Append(line).Append(' ');
                    }
                }

                var probe = new ChessRules();
                if (headers.TryGetValue("FEN", out string? customFen) && !string.IsNullOrWhiteSpace(customFen))
                {
                    if (!probe.TryLoadFen(customFen, out string fenErr))
                    {
                        error = $"FEN inicial do PGN é inválido: {fenErr}";
                        return false;
                    }
                }

                // Remove comentários {...} e variantes (...) recursivamente
                string moveText = StripPgnCommentsAndVariations(moveTextBuilder.ToString());
                plySnapshots.Add(probe.CreateSnapshot());

                var tokens = moveText.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string rawTok in tokens)
                {
                    string tok = rawTok.Trim();
                    if (string.IsNullOrEmpty(tok)) continue;

                    // Ignora resultados finais
                    if (tok is "1-0" or "0-1" or "1/2-1/2" or "*")
                    {
                        probe.GameResult = tok;
                        break;
                    }

                    // Ignora NAGs ($1, $2...)
                    if (tok.StartsWith('$')) continue;

                    // Remove prefixo de número de lance (ex.: "1.e4" -> "e4", "12...Nf6" -> "Nf6", "1." -> "")
                    int lastDot = tok.LastIndexOf('.');
                    if (lastDot >= 0)
                    {
                        tok = tok[(lastDot + 1)..].Trim();
                        if (string.IsNullOrEmpty(tok)) continue;
                    }

                    // Se for apenas número puro, ignora
                    if (int.TryParse(tok, out _)) continue;

                    if (!probe.TryParseSanMove(tok, out Point from, out Point to, out char? promo))
                    {
                        error = $"Lance inválido ou irreconhecível no PGN: \"{tok}\" (lance {probe.FullMoveNumber}).";
                        return false;
                    }

                    probe.ExecuteMove(from, to, promo);
                    plySnapshots.Add(probe.CreateSnapshot());
                }

                if (headers.TryGetValue("Result", out string? hdrResult) &&
                    hdrResult is "1-0" or "0-1" or "1/2-1/2" &&
                    probe.GameResult == "*")
                {
                    probe.GameResult = hdrResult;
                    if (plySnapshots.Count > 0)
                        plySnapshots[^1].GameResult = hdrResult;
                }

                RestoreSnapshot(probe.CreateSnapshot());
                return true;
            }
            catch (Exception ex)
            {
                error = $"Erro ao interpretar PGN: {ex.Message}";
                return false;
            }
        }

        private static string StripPgnCommentsAndVariations(string text)
        {
            var sb = new StringBuilder(text.Length);
            int braceDepth = 0;
            int parenDepth = 0;

            foreach (char ch in text)
            {
                if (braceDepth > 0)
                {
                    if (ch == '}') braceDepth--;
                    continue;
                }
                if (ch == '{')
                {
                    braceDepth++;
                    continue;
                }
                if (ch == '(')
                {
                    parenDepth++;
                    continue;
                }
                if (ch == ')')
                {
                    if (parenDepth > 0) parenDepth--;
                    continue;
                }
                if (parenDepth == 0)
                    sb.Append(ch);
            }
            return sb.ToString();
        }

        // Converte uma linha principal (PV) em UCI ("e2e4 e7e5 g1f3") para notação SAN legível ("1. e4 e5 2. Nf3")
        public static string FormatUciPvToSan(ChessRules startPosition, string uciPv, int maxPlies = 8)
        {
            if (string.IsNullOrWhiteSpace(uciPv)) return "";

            var probe = startPosition.Clone();
            var tokens = uciPv.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            int plies = 0;
            bool firstToken = true;

            foreach (string raw in tokens)
            {
                if (plies >= maxPlies) break;
                if (!probe.TryParseSanMove(raw, out Point from, out Point to, out char? promo))
                {
                    if (firstToken) return uciPv;
                    break;
                }

                bool whiteMoving = probe.WhiteTurn;
                int moveNum = probe.FullMoveNumber;
                string san = probe.ExecuteMove(from, to, promo);

                if (whiteMoving)
                    sb.Append($"{moveNum}. {san} ");
                else if (firstToken)
                    sb.Append($"{moveNum}... {san} ");
                else
                    sb.Append($"{san} ");

                firstToken = false;
                plies++;
            }

            return sb.ToString().Trim();
        }
    }
}
