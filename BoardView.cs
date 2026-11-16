using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XadrezApp
{
    public class BoardView : DoubleBufferedPanel
    {
        public const int SquareSize = 62;
        public const int BoardOffset = 24;
        public static int TotalBoardSize => SquareSize * 8 + BoardOffset * 2;

        private readonly ChessRules rules;
        private readonly Dictionary<string, Image> pieceImages = new Dictionary<string, Image>();

        public string CurrentTheme { get; private set; } = "Madeira";

        public Point? SelectedSquare { get; set; } = null;
        public List<Point> LegalMoves { get; set; } = new List<Point>();
        public Point? PremoveSelection { get; set; } = null;
        public (Point from, Point to)? QueuedPremove { get; set; } = null;
        public HashSet<Point> MarkedSquares { get; } = new HashSet<Point>();
        public List<(Point from, Point to)> DrawnArrows { get; } = new List<(Point from, Point to)>();
        public (Point from, Point to)? HintArrow { get; set; } = null;

        private Point? rightDragStart = null;

        // Evento disparado quando o jogador clica com o botão esquerdo em uma casa válida (0..7, 0..7)
        public event Action<Point>? SquareLeftClicked;

        public BoardView(ChessRules rules)
        {
            this.rules = rules;
            Size = new Size(TotalBoardSize, TotalBoardSize);
            BackColor = Color.White;
            BorderStyle = BorderStyle.Fixed3D;

            LoadPieceImages();
        }

        public void SetTheme(string themeName)
        {
            CurrentTheme = themeName;
            Invalidate();
        }

        public void ResetAllOverlays()
        {
            SelectedSquare = null;
            LegalMoves.Clear();
            PremoveSelection = null;
            QueuedPremove = null;
            MarkedSquares.Clear();
            DrawnArrows.Clear();
            HintArrow = null;
            rightDragStart = null;
        }

        public void ClearActiveSelectionAndPremove()
        {
            SelectedSquare = null;
            LegalMoves.Clear();
            PremoveSelection = null;
            QueuedPremove = null;
            HintArrow = null;
        }

        public void ClearSelectionAndHint()
        {
            SelectedSquare = null;
            LegalMoves.Clear();
            HintArrow = null;
        }

        private static string? FindAssetsFolder()
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
                    pieceImages[kv.Key] = Image.FromFile(fullPath);
            }
        }

        // Converte coordenadas do mouse em casa do tabuleiro. Retorna null fora das 64 casas.
        // A checagem explícita da margem é necessária porque a divisão inteira arredonda
        // para zero: um clique em x = 10 daria (10 - 24) / 62 = 0, ou seja, a coluna "a".
        public static Point? ScreenToSquare(int x, int y)
        {
            if (x < BoardOffset || y < BoardOffset) return null;
            int col = (x - BoardOffset) / SquareSize;
            int row = (y - BoardOffset) / SquareSize;
            if (col > 7 || row > 7) return null;
            return new Point(col, row);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            var sq = ScreenToSquare(e.X, e.Y);
            if (!sq.HasValue) return;

            var clicked = sq.Value;

            if (e.Button == MouseButtons.Right)
            {
                rightDragStart = clicked;
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (MarkedSquares.Count > 0 || DrawnArrows.Count > 0)
                {
                    MarkedSquares.Clear();
                    DrawnArrows.Clear();
                }

                SquareLeftClicked?.Invoke(clicked);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Right || !rightDragStart.HasValue) return;

            var sq = ScreenToSquare(e.X, e.Y);
            if (sq.HasValue)
            {
                var endPt = sq.Value;
                if (endPt == rightDragStart.Value)
                {
                    if (!MarkedSquares.Remove(endPt))
                        MarkedSquares.Add(endPt);
                }
                else
                {
                    var arrow = (rightDragStart.Value, endPt);
                    if (!DrawnArrows.Remove(arrow))
                        DrawnArrows.Add(arrow);
                }
            }

            rightDragStart = null;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            Color lightColor = Color.FromArgb(238, 226, 213);
            Color darkColor = Color.FromArgb(166, 117, 90);

            if (CurrentTheme == "Preto e Branco")
            {
                lightColor = Color.FromArgb(225, 225, 225);
                darkColor = Color.FromArgb(135, 135, 135);
            }
            else if (CurrentTheme == "Branco e Verde")
            {
                lightColor = Color.White;
                darkColor = Color.FromArgb(11, 138, 68);
            }

            using var coordFont = new Font("Tahoma", 8.5f, FontStyle.Bold);
            using var coordBrush = new SolidBrush(Color.FromArgb(60, 60, 60));

            for (int i = 0; i < 8; i++)
            {
                string file = ((char)('a' + i)).ToString();
                string rank = (8 - i).ToString();
                g.DrawString(file, coordFont, coordBrush, BoardOffset + i * SquareSize + SquareSize / 2 - 5, BoardOffset + SquareSize * 8 + 4);
                g.DrawString(rank, coordFont, coordBrush, 6, BoardOffset + i * SquareSize + SquareSize / 2 - 7);
            }

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    int x = BoardOffset + c * SquareSize;
                    int y = BoardOffset + r * SquareSize;
                    var pt = new Point(c, r);
                    bool isLight = (r + c) % 2 == 0;

                    Color sqColor = isLight ? lightColor : darkColor;
                    if (SelectedSquare == pt || PremoveSelection == pt)
                        sqColor = Color.FromArgb(246, 246, 105);

                    using (var brush = new SolidBrush(sqColor))
                        g.FillRectangle(brush, x, y, SquareSize, SquareSize);

                    if (MarkedSquares.Contains(pt))
                    {
                        using var markBrush = new SolidBrush(Color.FromArgb(150, 235, 65, 50));
                        g.FillRectangle(markBrush, x, y, SquareSize, SquareSize);
                    }

                    if (QueuedPremove.HasValue && (QueuedPremove.Value.from == pt || QueuedPremove.Value.to == pt))
                    {
                        using var preBrush = new SolidBrush(Color.FromArgb(150, 50, 140, 240));
                        g.FillRectangle(preBrush, x, y, SquareSize, SquareSize);
                    }

                    if (LegalMoves.Contains(pt))
                    {
                        using var orangeBrush = new SolidBrush(Color.FromArgb(175, 255, 145, 0));
                        using var orangePen = new Pen(Color.FromArgb(220, 200, 90, 0), 2);
                        g.FillRectangle(orangeBrush, x + 2, y + 2, SquareSize - 4, SquareSize - 4);
                        g.DrawRectangle(orangePen, x + 2, y + 2, SquareSize - 4, SquareSize - 4);
                    }

                    char piece = rules.Board[r, c];
                    if (piece != '.')
                    {
                        int variant = rules.PieceVariants[r, c];
                        string key2 = $"{piece}2";
                        string key1 = $"{piece}1";
                        string chosenKey = (variant == 2 && pieceImages.ContainsKey(key2)) ? key2 : key1;

                        if (pieceImages.TryGetValue(chosenKey, out Image? img))
                        {
                            int pad = 5;
                            g.DrawImage(img, x + pad, y + pad, SquareSize - pad * 2, SquareSize - pad * 2);
                        }
                        else
                        {
                            DrawFallbackPiece(g, piece, x, y);
                        }
                    }
                }
            }

            foreach (var arrow in DrawnArrows)
                DrawBoardArrow(g, arrow.from, arrow.to, Color.FromArgb(200, 55, 70, 75));

            if (HintArrow.HasValue)
                DrawBoardArrow(g, HintArrow.Value.from, HintArrow.Value.to, Color.FromArgb(215, 30, 160, 50));
        }

        private static void DrawFallbackPiece(Graphics g, char piece, int x, int y)
        {
            string symbol = piece switch
            {
                'K' or 'k' => "♚",
                'Q' or 'q' => "♛",
                'R' or 'r' => "♜",
                'B' or 'b' => "♝",
                'N' or 'n' => "♞",
                _ => "♟"
            };
            using var f = new Font("Segoe UI Symbol", 28, FontStyle.Regular);
            var color = char.IsUpper(piece) ? Color.White : Color.Black;
            var outline = char.IsUpper(piece) ? Color.Black : Color.White;
            using var bOut = new SolidBrush(outline);
            using var bMain = new SolidBrush(color);
            g.DrawString(symbol, f, bOut, x + 5, y + 3);
            g.DrawString(symbol, f, bMain, x + 4, y + 2);
        }

        private static void DrawBoardArrow(Graphics g, Point from, Point to, Color color)
        {
            PointF p1 = new PointF(BoardOffset + from.X * SquareSize + SquareSize / 2f, BoardOffset + from.Y * SquareSize + SquareSize / 2f);
            PointF p2 = new PointF(BoardOffset + to.X * SquareSize + SquareSize / 2f, BoardOffset + to.Y * SquareSize + SquareSize / 2f);

            using var pen = new Pen(color, 9);
            using var arrowCap = new AdjustableArrowCap(4.5f, 4.5f, true);
            pen.StartCap = LineCap.Round;
            pen.CustomEndCap = arrowCap;
            g.DrawLine(pen, p1, p2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var img in pieceImages.Values)
                {
                    try { img.Dispose(); } catch { }
                }
                pieceImages.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
