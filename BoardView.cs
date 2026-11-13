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
        private readonly ChessRules rules;
        private readonly Dictionary<string, Image> pieceImages = new Dictionary<string, Image>();

        // Zoom e Geometria Dinâmica Responsiva
        public float ZoomFactor { get; private set; } = 1.0f;
        private int squareSize = 62;
        private int boardOriginX = 24;
        private int boardOriginY = 24;

        // Estado Visual
        public string CurrentTheme { get; private set; } = "Madeira";
        public bool HighContrastPieces { get; set; } = false;
        public bool IsDarkModeUI { get; set; } = false;
        public bool IsEnglish { get; set; } = false;

        public Point? SelectedSquare { get; set; } = null;
        public List<Point> LegalMoves { get; set; } = new List<Point>();
        public Point? PremoveSelection { get; set; } = null;
        public (Point from, Point to)? QueuedPremove { get; set; } = null;
        public (Point from, Point to)? LastMove { get; set; } = null;
        public (Point from, Point to)? HintArrow { get; set; } = null;

        public HashSet<Point> MarkedSquares { get; } = new HashSet<Point>();
        public List<(Point from, Point to)> DrawnArrows { get; } = new List<(Point from, Point to)>();
        private Point? rightDragStart = null;

        // Navegação por Teclado e Acessibilidade (Foco Visível + Screen Reader)
        public Point KeyboardCursor { get; private set; } = new Point(4, 6); // Começa em e2
        public bool ShowKeyboardFocus { get; private set; } = false;

        // Feedback de Lance Inválido (Flash Vermelho)
        private Point? invalidSquareFlash = null;
        private readonly System.Windows.Forms.Timer flashTimer;

        // Eventos para o MainForm / GameController
        public event Action<Point>? SquareLeftClicked;
        public event Action<string>? AccessibilityAnnouncement;

        public BoardView(ChessRules rules)
        {
            this.rules = rules;
            TabStop = true; // Permite receber foco via tecla Tab
            AccessibleRole = AccessibleRole.Client;
            AccessibleName = "Tabuleiro de Xadrez 8x8";
            AccessibleDescription = "Use as setas para navegar pelas casas e Enter ou Espaço para selecionar e mover peças.";

            flashTimer = new System.Windows.Forms.Timer { Interval = 380 };
            flashTimer.Tick += (s, e) =>
            {
                invalidSquareFlash = null;
                flashTimer.Stop();
                Invalidate();
            };

            LoadPieceImages();
            UpdateAccessibilityMetadata();
        }

        public void SetZoom(float newZoom)
        {
            ZoomFactor = Math.Clamp(newZoom, 0.70f, 1.45f);
            RecalculateGeometry();
            Invalidate();
        }

        public void AdjustZoom(float delta) => SetZoom(ZoomFactor + delta);

        public void SetTheme(string themeName)
        {
            CurrentTheme = themeName;
            Invalidate();
        }

        public void TriggerInvalidMoveFlash(Point square)
        {
            invalidSquareFlash = square;
            flashTimer.Stop();
            flashTimer.Start();
            Invalidate();
        }

        public void ClearAnnotations()
        {
            MarkedSquares.Clear();
            DrawnArrows.Clear();
            Invalidate();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            RecalculateGeometry();
            Invalidate();
        }

        private void RecalculateGeometry()
        {
            int margin = 26;
            int available = Math.Min(ClientSize.Width, ClientSize.Height) - margin * 2;
            int baseSq = Math.Max(36, available / 8);
            squareSize = Math.Clamp((int)(baseSq * ZoomFactor), 34, 96);

            int totalBoard = squareSize * 8;
            boardOriginX = Math.Max(margin, (ClientSize.Width - totalBoard) / 2);
            boardOriginY = Math.Max(margin, (ClientSize.Height - totalBoard) / 2);
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
                    pieceImages[kv.Key] = Image.FromFile(fullPath);
            }
        }

        public Point? ScreenToSquare(int x, int y)
        {
            if (x < boardOriginX || y < boardOriginY) return null;
            int col = (x - boardOriginX) / squareSize;
            int row = (y - boardOriginY) / squareSize;
            if (col < 0 || col > 7 || row < 0 || row > 7) return null;
            return new Point(col, row);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData switch
            {
                Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Enter or Keys.Space or Keys.Escape => true,
                _ => base.IsInputKey(keyData)
            };
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            ShowKeyboardFocus = true;
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            ShowKeyboardFocus = true;

            if (e.KeyCode == Keys.Left)
            {
                KeyboardCursor = new Point(Math.Max(0, KeyboardCursor.X - 1), KeyboardCursor.Y);
                AnnounceFocusedSquare();
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right)
            {
                KeyboardCursor = new Point(Math.Min(7, KeyboardCursor.X + 1), KeyboardCursor.Y);
                AnnounceFocusedSquare();
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up)
            {
                KeyboardCursor = new Point(KeyboardCursor.X, Math.Max(0, KeyboardCursor.Y - 1));
                AnnounceFocusedSquare();
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                KeyboardCursor = new Point(KeyboardCursor.X, Math.Min(7, KeyboardCursor.Y + 1));
                AnnounceFocusedSquare();
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                SquareLeftClicked?.Invoke(KeyboardCursor);
                AnnounceFocusedSquare();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                SelectedSquare = null;
                LegalMoves.Clear();
                PremoveSelection = null;
                QueuedPremove = null;
                ClearAnnotations();
                Invalidate();
                e.Handled = true;
            }
            else if (e.Control && (e.KeyCode == Keys.Oemplus || e.KeyCode == Keys.Add))
            {
                AdjustZoom(0.10f);
                e.Handled = true;
            }
            else if (e.Control && (e.KeyCode == Keys.OemMinus || e.KeyCode == Keys.Subtract))
            {
                AdjustZoom(-0.10f);
                e.Handled = true;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (ModifierKeys.HasFlag(Keys.Control))
            {
                AdjustZoom(e.Delta > 0 ? 0.08f : -0.08f);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            var sq = ScreenToSquare(e.X, e.Y);
            if (!sq.HasValue) return;

            KeyboardCursor = sq.Value;

            if (e.Button == MouseButtons.Right)
            {
                rightDragStart = sq.Value;
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (MarkedSquares.Count > 0 || DrawnArrows.Count > 0)
                {
                    MarkedSquares.Clear();
                    DrawnArrows.Clear();
                }
                SquareLeftClicked?.Invoke(sq.Value);
                UpdateAccessibilityMetadata();
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

        public void AnnounceFocusedSquare()
        {
            UpdateAccessibilityMetadata();
            AccessibilityAnnouncement?.Invoke(AccessibleDescription ?? "");
        }

        public void UpdateAccessibilityMetadata()
        {
            string alg = ChessRules.SquareToAlgebraic(KeyboardCursor);
            char p = rules.Board[KeyboardCursor.Y, KeyboardCursor.X];
            string pieceDesc = DescribePieceForScreenReader(p, IsEnglish);
            bool isLegalTarget = LegalMoves.Contains(KeyboardCursor);

            string legalTag = isLegalTarget
                ? (IsEnglish ? " — Legal target square" : " — Casa de destino válida")
                : "";

            AccessibleName = IsEnglish
                ? $"Chessboard square {alg}: {pieceDesc}{legalTag}"
                : $"Casa {alg}: {pieceDesc}{legalTag}";
            AccessibleDescription = AccessibleName;
        }

        private static string DescribePieceForScreenReader(char p, bool english)
        {
            if (p == '.') return english ? "Empty square" : "Casa vazia";
            bool white = char.IsUpper(p);
            string color = english ? (white ? "White" : "Black") : (white ? "Branco(a)" : "Preto(a)");
            string name = char.ToLower(p) switch
            {
                'p' => english ? "Pawn" : "Peão",
                'n' => english ? "Knight" : "Cavalo",
                'b' => english ? "Bishop" : "Bispo",
                'r' => english ? "Rook" : "Torre",
                'q' => english ? "Queen" : "Rainha",
                'k' => english ? "King" : "Rei",
                _ => english ? "Piece" : "Peça"
            };
            return $"{name} {color}";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            RecalculateGeometry();

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            Color lightColor = Color.FromArgb(238, 226, 213);
            Color darkColor = Color.FromArgb(166, 117, 90);

            if (CurrentTheme == "Preto e Branco")
            {
                lightColor = Color.FromArgb(230, 230, 230);
                darkColor = Color.FromArgb(120, 120, 120);
            }
            else if (CurrentTheme == "Branco e Verde")
            {
                lightColor = Color.White;
                darkColor = Color.FromArgb(11, 138, 68);
            }
            else if (CurrentTheme == "Alto Contraste")
            {
                lightColor = Color.FromArgb(255, 255, 235);
                darkColor = Color.FromArgb(45, 65, 95);
            }

            // Coordenadas laterais (1-8 e a-h)
            using var coordFont = new Font("Tahoma", Math.Max(8f, squareSize * 0.14f), FontStyle.Bold);
            using var coordBrush = new SolidBrush(IsDarkModeUI ? Color.FromArgb(230, 230, 230) : Color.FromArgb(50, 50, 50));

            for (int i = 0; i < 8; i++)
            {
                string file = ((char)('a' + i)).ToString();
                string rank = (8 - i).ToString();
                g.DrawString(file, coordFont, coordBrush, boardOriginX + i * squareSize + squareSize / 2 - 5, boardOriginY + squareSize * 8 + 4);
                g.DrawString(rank, coordFont, coordBrush, boardOriginX - 18, boardOriginY + i * squareSize + squareSize / 2 - 7);
            }

            bool whiteKingInCheck = rules.IsKingInCheck(rules.Board, true);
            bool blackKingInCheck = rules.IsKingInCheck(rules.Board, false);
            bool noLegalMoves = rules.GetAllLegalMoves(rules.WhiteTurn).Count == 0;

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    int x = boardOriginX + c * squareSize;
                    int y = boardOriginY + r * squareSize;
                    var pt = new Point(c, r);
                    bool isLight = (r + c) % 2 == 0;

                    Color sqColor = isLight ? lightColor : darkColor;

                    // Destaque suave do último lance jogado
                    if (LastMove.HasValue && (LastMove.Value.from == pt || LastMove.Value.to == pt))
                        sqColor = Color.FromArgb(215, 235, 135);

                    // Casa selecionada
                    if (SelectedSquare == pt || PremoveSelection == pt)
                        sqColor = Color.FromArgb(246, 246, 105);

                    using (var brush = new SolidBrush(sqColor))
                        g.FillRectangle(brush, x, y, squareSize, squareSize);

                    // Feedback de Lance Inválido (Flash Vermelho)
                    if (invalidSquareFlash == pt)
                    {
                        using var invalidBrush = new SolidBrush(Color.FromArgb(190, 230, 30, 30));
                        g.FillRectangle(invalidBrush, x, y, squareSize, squareSize);
                    }

                    // Feedback Visual Claro de Xeque e Xeque-Mate no Rei
                    char piece = rules.Board[r, c];
                    if ((piece == 'K' && whiteKingInCheck) || (piece == 'k' && blackKingInCheck))
                    {
                        Color alertColor = noLegalMoves
                            ? Color.FromArgb(220, 180, 0, 0) // Xeque-Mate
                            : Color.FromArgb(185, 245, 50, 40); // Xeque
                        using var checkBrush = new SolidBrush(alertColor);
                        using var checkPen = new Pen(Color.Yellow, 3);
                        g.FillRectangle(checkBrush, x, y, squareSize, squareSize);
                        g.DrawRectangle(checkPen, x + 1, y + 1, squareSize - 2, squareSize - 2);
                    }

                    // Quadrado demarcado com Botão Direito
                    if (MarkedSquares.Contains(pt))
                    {
                        using var markBrush = new SolidBrush(Color.FromArgb(150, 235, 65, 50));
                        g.FillRectangle(markBrush, x, y, squareSize, squareSize);
                    }

                    // Premove agendado (azul)
                    if (QueuedPremove.HasValue && (QueuedPremove.Value.from == pt || QueuedPremove.Value.to == pt))
                    {
                        using var preBrush = new SolidBrush(Color.FromArgb(150, 50, 140, 240));
                        g.FillRectangle(preBrush, x, y, squareSize, squareSize);
                    }

                    // Quadrados Laranjas indicando lances legais
                    if (LegalMoves.Contains(pt))
                    {
                        using var orangeBrush = new SolidBrush(Color.FromArgb(175, 255, 145, 0));
                        using var orangePen = new Pen(Color.FromArgb(220, 200, 90, 0), 2);
                        g.FillRectangle(orangeBrush, x + 2, y + 2, squareSize - 4, squareSize - 4);
                        g.DrawRectangle(orangePen, x + 2, y + 2, squareSize - 4, squareSize - 4);
                    }

                    // Desenho das Peças (Modo Normal ou Alto Contraste)
                    if (piece != '.')
                    {
                        if (HighContrastPieces)
                        {
                            DrawHighContrastPiece(g, piece, x, y);
                        }
                        else
                        {
                            int variant = rules.PieceVariants[r, c];
                            string key2 = $"{piece}2";
                            string key1 = $"{piece}1";
                            string chosenKey = (variant == 2 && pieceImages.ContainsKey(key2)) ? key2 : key1;

                            if (pieceImages.TryGetValue(chosenKey, out Image? img))
                            {
                                int pad = Math.Max(3, squareSize / 12);
                                g.DrawImage(img, x + pad, y + pad, squareSize - pad * 2, squareSize - pad * 2);
                            }
                            else
                            {
                                DrawHighContrastPiece(g, piece, x, y);
                            }
                        }
                    }

                    // Moldura de Foco Visível para Navegação por Teclado (Acessibilidade)
                    if (ShowKeyboardFocus && Focused && KeyboardCursor == pt)
                    {
                        using var focusPen = new Pen(Color.FromArgb(0, 210, 255), 3.5f);
                        g.DrawRectangle(focusPen, x + 2, y + 2, squareSize - 4, squareSize - 4);
                    }
                }
            }

            foreach (var arrow in DrawnArrows)
                DrawBoardArrow(g, arrow.from, arrow.to, Color.FromArgb(200, 55, 70, 75));

            if (HintArrow.HasValue)
                DrawBoardArrow(g, HintArrow.Value.from, HintArrow.Value.to, Color.FromArgb(215, 30, 160, 50));
        }

        private void DrawHighContrastPiece(Graphics g, char piece, int x, int y)
        {
            bool isWhite = char.IsUpper(piece);
            int pad = Math.Max(5, squareSize / 10);
            var badgeRect = new Rectangle(x + pad, y + pad, squareSize - pad * 2, squareSize - pad * 2);

            Color fill = isWhite ? Color.FromArgb(252, 252, 252) : Color.FromArgb(22, 22, 26);
            Color border = isWhite ? Color.FromArgb(20, 20, 20) : Color.FromArgb(245, 215, 60);

            using (var bgBrush = new SolidBrush(fill))
            using (var borderPen = new Pen(border, 2.5f))
            {
                g.FillEllipse(bgBrush, badgeRect);
                g.DrawEllipse(borderPen, badgeRect);
            }

            string symbol = char.ToUpper(piece) switch
            {
                'K' => "♚",
                'Q' => "♛",
                'R' => "♜",
                'B' => "♝",
                'N' => "♞",
                _ => "♟"
            };

            using var f = new Font("Segoe UI Symbol", Math.Max(14f, squareSize * 0.40f), FontStyle.Bold);
            using var fgBrush = new SolidBrush(isWhite ? Color.Black : Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(symbol, f, fgBrush, new RectangleF(x, y + 2, squareSize, squareSize), sf);
        }

        private void DrawBoardArrow(Graphics g, Point from, Point to, Color color)
        {
            PointF p1 = new PointF(boardOriginX + from.X * squareSize + squareSize / 2f, boardOriginY + from.Y * squareSize + squareSize / 2f);
            PointF p2 = new PointF(boardOriginX + to.X * squareSize + squareSize / 2f, boardOriginY + to.Y * squareSize + squareSize / 2f);

            using var pen = new Pen(color, Math.Max(6f, squareSize * 0.14f));
            using var arrowCap = new AdjustableArrowCap(4.5f, 4.5f, true);
            pen.StartCap = LineCap.Round;
            pen.CustomEndCap = arrowCap;
            g.DrawLine(pen, p1, p2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { flashTimer.Dispose(); } catch { }
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