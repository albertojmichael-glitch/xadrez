using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XadrezApp
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private const int SquareSize = 75;
        private const int BoardOffset = 35;

        // Tabuleiro: Maiúsculas = Brancas (P, R, N, B, Q, K), Minúsculas = Pretas (p, r, n, b, q, k)
        private char[,] board = new char[8, 8];
        private bool whiteTurn = true;
        private bool isBotThinking = false;
        private bool gameOver = false;

        private Point? selectedSquare = null;
        private List<Point> legalMoves = new List<Point>();

        private ComboBox difficultyBox = null!;
        private Button resetBtn = null!;
        private Label statusLabel = null!;
        private Panel boardPanel = null!;

        private Dictionary<string, Image> pieceImages = new Dictionary<string, Image>();
        private Process? stockfishProcess;

        public MainForm()
        {
            Text = "Xadrez vs Bot (Stockfish)";
            ClientSize = new Size(SquareSize * 8 + BoardOffset * 2 + 20, SquareSize * 8 + BoardOffset * 2 + 100);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(30, 30, 36);

            InitUI();
            LoadPieceImages();
            InitStockfish();
            ResetBoard();
        }

        private void InitUI()
        {
            var topPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 90,
                BackColor = Color.FromArgb(43, 43, 54)
            };

            var diffLabel = new Label
            {
                Text = "Dificuldade:",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, 18)
            };

            difficultyBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 10),
                Location = new Point(110, 14),
                Width = 190
            };
            difficultyBox.Items.AddRange(new object[]
            {
                "Fácil (Iniciante)",
                "Médio (Intermediário)",
                "Difícil (Avançado)",
                "Mestre (Stockfish Máximo)"
            });
            difficultyBox.SelectedIndex = 1;

            resetBtn = new Button
            {
                Text = "Novo Jogo",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(76, 175, 80),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(315, 12),
                Size = new Size(110, 30),
                Cursor = Cursors.Hand
            };
            resetBtn.FlatAppearance.BorderSize = 0;
            resetBtn.Click += (s, e) => ResetBoard();

            statusLabel = new Label
            {
                Text = "Sua vez (Brancas)",
                ForeColor = Color.FromArgb(224, 224, 224),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, 55)
            };

            topPanel.Controls.Add(diffLabel);
            topPanel.Controls.Add(difficultyBox);
            topPanel.Controls.Add(resetBtn);
            topPanel.Controls.Add(statusLabel);

            boardPanel = new DoubleBufferedPanel
            {
                Location = new Point(10, 95),
                Size = new Size(SquareSize * 8 + BoardOffset * 2, SquareSize * 8 + BoardOffset * 2),
                BackColor = Color.White
            };
            boardPanel.Paint += BoardPanel_Paint;
            boardPanel.MouseClick += BoardPanel_MouseClick;

            Controls.Add(topPanel);
            Controls.Add(boardPanel);
        }

        private string? FindAssetsFolder()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "assets"),
                Path.Combine(baseDir, "client", "assets"),
                Path.Combine(baseDir, "..", "..", "..", "client", "assets")
            };
            return candidates.FirstOrDefault(Directory.Exists);
        }

        private void LoadPieceImages()
        {
            string? assetsDir = FindAssetsFolder();
            if (assetsDir == null) return;

            var files = new Dictionary<string, string>
            {
                { "P1", "peao 1 branco.png" },
                { "R1", "torre 1 branca.png" }, { "R2", "torre 2 branca.png" },
                { "N1", "cavalo 1 branco.png" }, { "N2", "cavalo 2 branco.png" },
                { "B1", "bispo 1 branco.png" },  { "B2", "bispo 2 branco.png" },
                { "Q1", "rainha branca.png" },
                { "K1", "rei branco.png" },

                { "p1", "peao1 preto.png" },
                { "r1", "torre 1 preto.png" },  { "r2", "torre 2 preto.png" },
                { "n1", "cavalo 1 preto.png" }, { "n2", "cavalo2 preto.png" },
                { "b1", "bispo1 preto.png" },   { "b2", "bispo2 preto.png" },
                { "q1", "rainha1preto.png" },
                { "k1", "rei preto.png" }
            };

            foreach (var kv in files)
            {
                string fullPath = Path.Combine(assetsDir, kv.Value);
                if (File.Exists(fullPath))
                {
                    pieceImages[kv.Key] = Image.FromFile(fullPath);
                }
            }
        }

        private void InitStockfish()
        {
            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stockfish.exe");
            if (!File.Exists(exePath)) return;

            try
            {
                stockfishProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                stockfishProcess.Start();
                stockfishProcess.StandardInput.WriteLine("uci");
            }
            catch
            {
                stockfishProcess = null;
            }
        }

        private void ResetBoard()
        {
            string[] startRows =
            {
                "rnbqkbnr",
                "pppppppp",
                "........",
                "........",
                "........",
                "........",
                "PPPPPPPP",
                "RNBQKBNR"
            };

            for (int r = 0; r < 8; r++)
                for (int c = 0; c < 8; c++)
                    board[r, c] = startRows[r][c];

            whiteTurn = true;
            isBotThinking = false;
            gameOver = false;
            selectedSquare = null;
            legalMoves.Clear();
            UpdateStatus();
            boardPanel.Invalidate();
        }

        private void BoardPanel_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // Borda e coordenadas inspiradas no seu verde3.webp
            using var borderPen = new Pen(Color.FromArgb(11, 138, 68), 3);
            g.DrawRectangle(borderPen, BoardOffset - 2, BoardOffset - 2, SquareSize * 8 + 4, SquareSize * 8 + 4);

            using var coordFont = new Font("Segoe UI", 10, FontStyle.Bold);
            using var coordBrush = new SolidBrush(Color.FromArgb(40, 40, 40));

            for (int i = 0; i < 8; i++)
            {
                string file = ((char)('A' + i)).ToString();
                string rank = (8 - i).ToString();
                g.DrawString(file, coordFont, coordBrush, BoardOffset + i * SquareSize + SquareSize / 2 - 6, BoardOffset + SquareSize * 8 + 8);
                g.DrawString(rank, coordFont, coordBrush, 10, BoardOffset + i * SquareSize + SquareSize / 2 - 8);
            }

            var pieceCounts = new Dictionary<char, int>();

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    int x = BoardOffset + c * SquareSize;
                    int y = BoardOffset + r * SquareSize;
                    bool isLight = (r + c) % 2 == 0;

                    Color sqColor = isLight ? Color.White : Color.FromArgb(11, 138, 68);
                    if (selectedSquare.HasValue && selectedSquare.Value.X == c && selectedSquare.Value.Y == r)
                        sqColor = Color.FromArgb(246, 246, 105);

                    using (var brush = new SolidBrush(sqColor))
                        g.FillRectangle(brush, x, y, SquareSize, SquareSize);

                    char piece = board[r, c];
                    if (piece != '.')
                    {
                        pieceCounts[piece] = pieceCounts.GetValueOrDefault(piece, 0) + 1;
                        string key2 = $"{piece}2";
                        string key1 = $"{piece}1";
                        string chosenKey = (pieceCounts[piece] >= 2 && pieceImages.ContainsKey(key2)) ? key2 : key1;

                        if (pieceImages.TryGetValue(chosenKey, out Image? img))
                        {
                            int pad = 6;
                            g.DrawImage(img, x + pad, y + pad, SquareSize - pad * 2, SquareSize - pad * 2);
                        }
                    }

                    if (legalMoves.Any(m => m.X == c && m.Y == r))
                    {
                        using var moveBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
                        g.FillEllipse(moveBrush, x + SquareSize / 2 - 10, y + SquareSize / 2 - 10, 20, 20);
                    }
                }
            }
        }

        private async void BoardPanel_MouseClick(object? sender, MouseEventArgs e)
        {
            if (gameOver || isBotThinking || !whiteTurn) return;

            int col = (e.X - BoardOffset) / SquareSize;
            int row = (e.Y - BoardOffset) / SquareSize;
            if (col < 0 || col > 7 || row < 0 || row > 7) return;

            var clicked = new Point(col, row);

            if (selectedSquare.HasValue && legalMoves.Contains(clicked))
            {
                MakeMove(selectedSquare.Value, clicked);
                selectedSquare = null;
                legalMoves.Clear();
                whiteTurn = false;
                boardPanel.Invalidate();
                CheckGameState();

                if (!gameOver)
                {
                    isBotThinking = true;
                    UpdateStatus();
                    await Task.Delay(200);
                    await MakeBotMoveAsync();
                }
                return;
            }

            char p = board[row, col];
            if (p != '.' && char.IsUpper(p))
            {
                selectedSquare = clicked;
                legalMoves = GetLegalMovesForSquare(board, col, row, true);
            }
            else
            {
                selectedSquare = null;
                legalMoves.Clear();
            }

            boardPanel.Invalidate();
        }

        private void MakeMove(Point from, Point to)
        {
            char piece = board[from.Y, from.X];
            board[to.Y, to.X] = piece;
            board[from.Y, from.X] = '.';

            // Promoção automática para Rainha
            if (piece == 'P' && to.Y == 0) board[to.Y, to.X] = 'Q';
            if (piece == 'p' && to.Y == 7) board[to.Y, to.X] = 'q';
        }

        private async Task MakeBotMoveAsync()
        {
            int idx = difficultyBox.SelectedIndex;
            int skill = idx switch { 0 => 1, 1 => 7, 2 => 14, _ => 20 };
            int depth = idx switch { 0 => 2, 1 => 6, 2 => 11, _ => 15 };

            (Point from, Point to)? move = null;

            await Task.Run(() =>
            {
                if (stockfishProcess != null && !stockfishProcess.HasExited)
                {
                    move = GetStockfishMove(ToFen(), skill, depth);
                }
                if (move == null)
                {
                    int fallbackDepth = idx switch { 0 => 1, 1 => 2, 2 => 3, _ => 4 };
                    move = GetFallbackBotMove(fallbackDepth);
                }
            });

            if (move.HasValue)
            {
                MakeMove(move.Value.from, move.Value.to);
            }

            whiteTurn = true;
            isBotThinking = false;
            boardPanel.Invalidate();
            CheckGameState();
        }

        private (Point from, Point to)? GetStockfishMove(string fen, int skill, int depth)
        {
            try
            {
                var sw = stockfishProcess!.StandardInput;
                var sr = stockfishProcess.StandardOutput;

                sw.WriteLine($"setoption name Skill Level value {skill}");
                sw.WriteLine($"position fen {fen}");
                sw.WriteLine($"go depth {depth}");

                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("bestmove"))
                    {
                        string[] parts = line.Split(' ');
                        if (parts.Length >= 2 && parts[1].Length >= 4)
                        {
                            string mv = parts[1];
                            int fCol = mv[0] - 'a';
                            int fRow = 8 - (mv[1] - '0');
                            int tCol = mv[2] - 'a';
                            int tRow = 8 - (mv[3] - '0');
                            return (new Point(fCol, fRow), new Point(tCol, tRow));
                        }
                        break;
                    }
                }
            }
            catch { }
            return null;
        }

        private (Point from, Point to)? GetFallbackBotMove(int depth)
        {
            var allMoves = GetAllLegalMoves(board, false);
            if (allMoves.Count == 0) return null;

            int bestScore = int.MinValue;
            var bestMove = allMoves[0];

            foreach (var mv in allMoves)
            {
                char[,] copy = (char[,])board.Clone();
                copy[mv.to.Y, mv.to.X] = copy[mv.from.Y, mv.from.X];
                copy[mv.from.Y, mv.from.X] = '.';
                if (copy[mv.to.Y, mv.to.X] == 'p' && mv.to.Y == 7) copy[mv.to.Y, mv.to.X] = 'q';

                int score = Minimax(copy, depth - 1, int.MinValue, int.MaxValue, false);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestMove = mv;
                }
            }
            return bestMove;
        }

        private int Minimax(char[,] b, int depth, int alpha, int beta, bool maximizingBlack)
        {
            if (depth == 0) return EvaluateBoard(b);
            var moves = GetAllLegalMoves(b, !maximizingBlack);
            if (moves.Count == 0) return EvaluateBoard(b);

            if (maximizingBlack)
            {
                int maxEval = int.MinValue;
                foreach (var mv in moves)
                {
                    char[,] copy = (char[,])b.Clone();
                    copy[mv.to.Y, mv.to.X] = copy[mv.from.Y, mv.from.X];
                    copy[mv.from.Y, mv.from.X] = '.';
                    int eval = Minimax(copy, depth - 1, alpha, beta, false);
                    maxEval = Math.Max(maxEval, eval);
                    alpha = Math.Max(alpha, eval);
                    if (beta <= alpha) break;
                }
                return maxEval;
            }
            else
            {
                int minEval = int.MaxValue;
                foreach (var mv in moves)
                {
                    char[,] copy = (char[,])b.Clone();
                    copy[mv.to.Y, mv.to.X] = copy[mv.from.Y, mv.from.X];
                    copy[mv.from.Y, mv.from.X] = '.';
                    int eval = Minimax(copy, depth - 1, alpha, beta, true);
                    minEval = Math.Min(minEval, eval);
                    beta = Math.Min(beta, eval);
                    if (beta <= alpha) break;
                }
                return minEval;
            }
        }

        private int EvaluateBoard(char[,] b)
        {
            int score = 0;
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = b[r, c];
                    if (p == '.') continue;
                    int val = char.ToLower(p) switch
                    {
                        'p' => 100,
                        'n' => 320,
                        'b' => 330,
                        'r' => 500,
                        'q' => 900,
                        'k' => 20000,
                        _ => 0
                    };
                    score += char.IsLower(p) ? val : -val;
                }
            }
            return score;
        }

        private string ToFen()
        {
            var rows = new List<string>();
            for (int r = 0; r < 8; r++)
            {
                int empty = 0;
                string rowStr = "";
                for (int c = 0; c < 8; c++)
                {
                    if (board[r, c] == '.') empty++;
                    else
                    {
                        if (empty > 0) { rowStr += empty; empty = 0; }
                        rowStr += board[r, c];
                    }
                }
                if (empty > 0) rowStr += empty;
                rows.Add(rowStr);
            }
            return string.Join("/", rows) + (whiteTurn ? " w - - 0 1" : " b - - 0 1");
        }

        private void CheckGameState()
        {
            var moves = GetAllLegalMoves(board, whiteTurn);
            if (moves.Count == 0)
            {
                gameOver = true;
                if (IsKingInCheck(board, whiteTurn))
                {
                    string winner = whiteTurn ? "Stockfish (Pretas)" : "Você (Brancas)";
                    statusLabel.Text = $"Xeque-mate! Vitória de: {winner}";
                }
                else
                {
                    statusLabel.Text = "Empate por Afogamento!";
                }
                return;
            }
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (gameOver) return;
            if (isBotThinking)
            {
                statusLabel.Text = "Stockfish está pensando...";
            }
            else
            {
                string check = IsKingInCheck(board, true) ? " (XEQUE!)" : "";
                statusLabel.Text = $"Sua vez (Brancas){check}";
            }
        }

        private List<(Point from, Point to)> GetAllLegalMoves(char[,] b, bool forWhite)
        {
            var list = new List<(Point from, Point to)>();
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    char p = b[r, c];
                    if (p != '.' && char.IsUpper(p) == forWhite)
                    {
                        foreach (var to in GetLegalMovesForSquare(b, c, r, forWhite))
                            list.Add((new Point(c, r), to));
                    }
                }
            }
            return list;
        }

        private List<Point> GetLegalMovesForSquare(char[,] b, int col, int row, bool forWhite)
        {
            var pseudo = GetPseudoMoves(b, col, row, forWhite);
            var legal = new List<Point>();

            foreach (var to in pseudo)
            {
                char[,] copy = (char[,])b.Clone();
                copy[to.Y, to.X] = copy[row, col];
                copy[row, col] = '.';
                if (!IsKingInCheck(copy, forWhite))
                    legal.Add(to);
            }
            return legal;
        }

        private bool IsKingInCheck(char[,] b, bool whiteKing)
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
                        var moves = GetPseudoMoves(b, c, r, !whiteKing);
                        if (moves.Contains(kingPos.Value)) return true;
                    }
                }
            }
            return false;
        }

        private List<Point> GetPseudoMoves(char[,] b, int c, int r, bool isWhite)
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
                    if (Inside(c + dc, r + dir) && IsEnemy(c + dc, r + dir))
                        moves.Add(new Point(c + dc, r + dir));
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
    }

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
        }
    }
}