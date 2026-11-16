using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
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

        public float ZoomFactor { get; private set; } = 1.0f;
        private int currentSquareSize = SquareSize;
        private int boardOriginX = BoardOffset;
        private int boardOriginY = BoardOffset;

        public string CurrentTheme { get; private set; } = "Madeira";
        public bool IsDarkModeUI { get; set; } = false;
        public bool HighContrastPieces { get; set; } = false;
        public bool IsEnglish { get; set; } = false;

        public Point? SelectedSquare { get; set; } = null;
        public List<Point> LegalMoves { get; set; } = new List<Point>();
        public Point? PremoveSelection { get; set; } = null;
        public (Point from, Point to)? QueuedPremove { get; set; } = null;
        public HashSet<Point> MarkedSquares { get; } = new HashSet<Point>();
        public List<(Point from, Point to)> DrawnArrows { get; } = new List<(Point from, Point to)>();

        // Seta 1º Melhor Lance (Verde) e 2º Lance Alternativo do Modo Professor (Azul/Ciano)
        public (Point from, Point to)? HintArrow { get; set; } = null;
        public (Point from, Point to)? AlternativeHintArrow { get; set; } = null;

        public (Point from, Point to)? LastMove { get; set; } = null;
        public Point? LastPromotionSquare { get; set; } = null;
        private Point? invalidFlashSquare = null;
        private readonly System.Windows.Forms.Timer invalidFlashTimer;

        public Point KeyboardCursor { get; private set; } = new Point(4, 6);
        public bool ShowKeyboardFocus { get; private set; } = false;

        private Point? rightDragStart = null;

        public event Action<Point>? SquareLeftClicked;
        public event Action<string>? ScreenReaderAnnouncement;

        public BoardView(ChessRules rules)
        {
            this.rules = rules;
            Size = new Size(TotalBoardSize, TotalBoardSize);
            BackColor = Color.White;
            BorderStyle = BorderStyle.Fixed3D;
            TabStop = true;

            AccessibleRole = AccessibleRole.Client;
            AccessibleName = "Tabuleiro de Xadrez 8x8";
            AccessibleDescription = "Use as setas do teclado para navegar pelas casas e Enter ou Espaço para selecionar ou mover uma peça.";

            invalidFlashTimer = new System.Windows.Forms.Timer { Interval = 450 };
            invalidFlashTimer.Tick += (s, e) =>
            {
                invalidFlashSquare = null;
                invalidFlashTimer.Stop();
                Invalidate();
            };

            LoadPieceImages();
            UpdateAccessibilityInfo(announceToStatus: false);
        }

        public void SetTheme(string themeName)
        {
            CurrentTheme = themeName;
            Invalidate();
        }

        public void SetZoom(float zoom)
        {
            ZoomFactor = Math.Clamp(zoom, 0.70f, 1.40f);
            RecalculateBoardGeometry();
            Invalidate();
        }

        public void AdjustZoom(float delta) => SetZoom(ZoomFactor + delta);

        public void TriggerInvalidMoveFlash(Point square)
        {
            invalidFlashSquare = square;
            invalidFlashTimer.Stop();
            invalidFlashTimer.Start();
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
            AlternativeHintArrow = null;
            LastMove = null;
            LastPromotionSquare = null;
            invalidFlashSquare = null;
            rightDragStart = null;
        }

        public void ClearActiveSelectionAndPremove()
        {
            SelectedSquare = null;
            LegalMoves.Clear();
            PremoveSelection = null;
            QueuedPremove = null;
            HintArrow = null;
            AlternativeHintArrow = null;
        }

        public void ClearSelectionAndHint()
        {
            SelectedSquare = null;
            LegalMoves.Clear();
            HintArrow = null;
            AlternativeHintArrow = null;
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            RecalculateBoardGeometry();
            Invalidate();
        }

        private void RecalculateBoardGeometry()
        {
            int minPadding = BoardOffset;
            int available = Math.Min(ClientSize.Width, ClientSize.Height) - minPadding * 2;
            int baseSq = Math.Max(36, available / 8);
            currentSquareSize = Math.Clamp((int)Math.Round(baseSq * ZoomFactor), 36, 96);

            int boardPixels = currentSquareSize * 8;
            boardOriginX = Math.Max(minPadding, (ClientSize.Width - boardPixels) / 2);
            boardOriginY = Math.Max(minPadding, (ClientSize.Height - boardPixels) / 2);
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

        public static Point? ScreenToSquare(int x, int y) =>
            ScreenToSquare(x, y, BoardOffset, BoardOffset, SquareSize);

        public static Point? ScreenToSquare(int x, int y, int originX, int originY, int sqSize)
        {
            if (x < originX || y < originY || sqSize <= 0) return null;
            int col = (x - originX) / sqSize;
            int row = (y - originY) / sqSize;
            if (col > 7 || row > 7) return null;
            return new Point(col, row);
        }

        public Point? HitTestSquare(int x, int y) =>
            ScreenToSquare(x, y, boardOriginX, boardOriginY, currentSquareSize);

        protected override bool IsInputKey(Keys keyData)
        {
            Keys keyCode = keyData & Keys.KeyCode;
            if (keyCode is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Enter or Keys.Space or Keys.Escape)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            ShowKeyboardFocus = true;
            UpdateAccessibilityInfo(announceToStatus: false);
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

            if (e.Control)
            {
                if (e.KeyCode is Keys.Oemplus or Keys.Add)
                {
                    AdjustZoom(0.10f);
                    e.Handled = true;
                    return;
                }
                if (e.KeyCode is Keys.OemMinus or Keys.Subtract)
                {
                    AdjustZoom(-0.10f);
                    e.Handled = true;
                    return;
                }
                if (e.KeyCode is Keys.D0 or Keys.NumPad0)
                {
                    SetZoom(1.0f);
                    e.Handled = true;
                    return;
                }
            }

            ShowKeyboardFocus = true;

            switch (e.KeyCode)
            {
                case Keys.Left:
                    KeyboardCursor = new Point(Math.Max(0, KeyboardCursor.X - 1), KeyboardCursor.Y);
                    UpdateAccessibilityInfo(announceToStatus: true);
                    Invalidate();
                    e.Handled = true;
                    break;
                case Keys.Right:
                    KeyboardCursor = new Point(Math.Min(7, KeyboardCursor.X + 1), KeyboardCursor.Y);
                    UpdateAccessibilityInfo(announceToStatus: true);
                    Invalidate();
                    e.Handled = true;
                    break;
                case Keys.Up:
                    KeyboardCursor = new Point(KeyboardCursor.X, Math.Max(0, KeyboardCursor.Y - 1));
                    UpdateAccessibilityInfo(announceToStatus: true);
                    Invalidate();
                    e.Handled = true;
                    break;
                case Keys.Down:
                    KeyboardCursor = new Point(KeyboardCursor.X, Math.Min(7, KeyboardCursor.Y + 1));
                    UpdateAccessibilityInfo(announceToStatus: true);
                    Invalidate();
                    e.Handled = true;
                    break;
                case Keys.Enter:
                case Keys.Space:
                    if (MarkedSquares.Count > 0 || DrawnArrows.Count > 0)
                    {
                        MarkedSquares.Clear();
                        DrawnArrows.Clear();
                    }
                    SquareLeftClicked?.Invoke(KeyboardCursor);
                    UpdateAccessibilityInfo(announceToStatus: true);
                    e.Handled = true;
                    break;
                case Keys.Escape:
                    ClearActiveSelectionAndPremove();
                    MarkedSquares.Clear();
                    DrawnArrows.Clear();
                    Invalidate();
                    string clearMsg = IsEnglish ? "Selection and board markings cleared." : "Seleção e marcações do tabuleiro limpas.";
                    ScreenReaderAnnouncement?.Invoke(clearMsg);
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) == Keys.Control)
            {
                AdjustZoom(e.Delta > 0 ? 0.08f : -0.08f);
            }
        }

        public void UpdateAccessibilityInfo(bool announceToStatus)
        {
            string sqAlg = ChessRules.SquareToAlgebraic(KeyboardCursor);
            char piece = rules.Board[KeyboardCursor.Y, KeyboardCursor.X];
            string pieceDesc = DescribePieceForScreenReader(piece, IsEnglish);

            bool isLegalDest = LegalMoves.Contains(KeyboardCursor);
            bool isSelected = SelectedSquare == KeyboardCursor;
            bool kingCheck = (piece == 'K' && rules.IsKingInCheck(rules.Board, true)) ||
                             (piece == 'k' && rules.IsKingInCheck(rules.Board, false));

            var extras = new List<string>();
            if (isSelected) extras.Add(IsEnglish ? "selected" : "selecionada");
            if (isLegalDest) extras.Add(IsEnglish ? "legal destination" : "destino legal disponível");
            if (kingCheck) extras.Add(IsEnglish ? "IN CHECK" : "EM XEQUE");

            string extraStr = extras.Count > 0 ? $" ({string.Join(", ", extras)})" : "";
            string description = IsEnglish
                ? $"Square {sqAlg}: {pieceDesc}{extraStr}"
                : $"Casa {sqAlg}: {pieceDesc}{extraStr}";

            AccessibleName = description;
            AccessibleDescription = description;

            try
            {
                AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
                AccessibilityNotifyClients(AccessibleEvents.Focus, -1);
            }
            catch { }

            if (announceToStatus)
                ScreenReaderAnnouncement?.Invoke(description);
        }

        private static string DescribePieceForScreenReader(char p, bool english)
        {
            if (p == '.') return english ? "empty" : "vazia";
            bool isWhite = char.IsUpper(p);
            if (english)
            {
                string color = isWhite ? "White" : "Black";
                string name = char.ToLower(p) switch
                {
                    'p' => "Pawn",
                    'n' => "Knight",
                    'b' => "Bishop",
                    'r' => "Rook",
                    'q' => "Queen",
                    'k' => "King",
                    _ => "Piece"
                };
                return $"{color} {name}";
            }
            else
            {
                return p switch
                {
                    'P' => "Peão Branco",
                    'N' => "Cavalo Branco",
                    'B' => "Bispo Branco",
                    'R' => "Torre Branca",
                    'Q' => "Rainha Branca",
                    'K' => "Rei Branco",
                    'p' => "Peão Preto",
                    'n' => "Cavalo Preto",
                    'b' => "Bispo Preto",
                    'r' => "Torre Preta",
                    'q' => "Rainha Preta",
                    'k' => "Rei Preto",
                    _ => "Peça"
                };
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Focused) Focus();

            var sq = HitTestSquare(e.X, e.Y);
            if (!sq.HasValue) return;

            var clicked = sq.Value;
            KeyboardCursor = clicked;

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
                UpdateAccessibilityInfo(announceToStatus: false);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button != MouseButtons.Right || !rightDragStart.HasValue) return;

            var sq = HitTestSquare(e.X, e.Y);
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
            RecalculateBoardGeometry();

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            int sqSize = currentSquareSize;
            int ox = boardOriginX;
            int oy = boardOriginY;

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
            else if (CurrentTheme == "Alto Contraste")
            {
                lightColor = Color.FromArgb(255, 255, 235);
                darkColor = Color.FromArgb(35, 55, 90);
            }

            using var coordFont = new Font("Tahoma", Math.Max(8f, sqSize * 0.135f), FontStyle.Bold);
            Color coordColor = IsDarkModeUI ? Color.FromArgb(230, 230, 235) : Color.FromArgb(60, 60, 60);
            using var coordBrush = new SolidBrush(coordColor);

            for (int i = 0; i < 8; i++)
            {
                string file = ((char)('a' + i)).ToString();
                string rank = (8 - i).ToString();
                g.DrawString(file, coordFont, coordBrush, ox + i * sqSize + sqSize / 2 - 5, oy + sqSize * 8 + 4);
                g.DrawString(rank, coordFont, coordBrush, Math.Max(2, ox - 18), oy + i * sqSize + sqSize / 2 - 7);
            }

            bool whiteInCheck = rules.IsKingInCheck(rules.Board, true);
            bool blackInCheck = rules.IsKingInCheck(rules.Board, false);
            bool currentSideHasNoMoves = rules.GetAllLegalMoves(rules.WhiteTurn).Count == 0;

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    int x = ox + c * sqSize;
                    int y = oy + r * sqSize;
                    var pt = new Point(c, r);
                    bool isLight = (r + c) % 2 == 0;

                    Color sqColor = isLight ? lightColor : darkColor;

                    if (LastMove.HasValue && (LastMove.Value.from == pt || LastMove.Value.to == pt))
                    {
                        sqColor = isLight
                            ? Color.FromArgb(235, 242, 155)
                            : Color.FromArgb(185, 202, 95);
                    }

                    if (SelectedSquare == pt || PremoveSelection == pt)
                        sqColor = Color.FromArgb(246, 246, 105);

                    using (var brush = new SolidBrush(sqColor))
                        g.FillRectangle(brush, x, y, sqSize, sqSize);

                    if (LastPromotionSquare == pt)
                    {
                        using var promoBrush = new SolidBrush(Color.FromArgb(110, 255, 215, 0));
                        using var promoPen = new Pen(Color.FromArgb(230, 180, 0), 3f);
                        g.FillRectangle(promoBrush, x, y, sqSize, sqSize);
                        g.DrawRectangle(promoPen, x + 1, y + 1, sqSize - 2, sqSize - 2);
                    }

                    char piece = rules.Board[r, c];
                    if ((piece == 'K' && whiteInCheck) || (piece == 'k' && blackInCheck))
                    {
                        bool isMate = currentSideHasNoMoves && ((piece == 'K' && rules.WhiteTurn) || (piece == 'k' && !rules.WhiteTurn));
                        Color checkFill = isMate
                            ? Color.FromArgb(215, 175, 0, 20)
                            : Color.FromArgb(185, 240, 45, 35);
                        Color checkBorder = isMate ? Color.Gold : Color.Yellow;

                        using var checkBrush = new SolidBrush(checkFill);
                        using var checkPen = new Pen(checkBorder, 3f);
                        g.FillRectangle(checkBrush, x, y, sqSize, sqSize);
                        g.DrawRectangle(checkPen, x + 1, y + 1, sqSize - 2, sqSize - 2);
                    }

                    if (invalidFlashSquare == pt)
                    {
                        using var invBrush = new SolidBrush(Color.FromArgb(190, 225, 30, 30));
                        using var invPen = new Pen(Color.White, 2.5f);
                        g.FillRectangle(invBrush, x, y, sqSize, sqSize);
                        g.DrawRectangle(invPen, x + 2, y + 2, sqSize - 4, sqSize - 4);
                    }

                    if (MarkedSquares.Contains(pt))
                    {
                        using var markBrush = new SolidBrush(Color.FromArgb(150, 235, 65, 50));
                        g.FillRectangle(markBrush, x, y, sqSize, sqSize);
                    }

                    if (QueuedPremove.HasValue && (QueuedPremove.Value.from == pt || QueuedPremove.Value.to == pt))
                    {
                        using var preBrush = new SolidBrush(Color.FromArgb(150, 50, 140, 240));
                        g.FillRectangle(preBrush, x, y, sqSize, sqSize);
                    }

                    if (LegalMoves.Contains(pt))
                    {
                        using var orangeBrush = new SolidBrush(Color.FromArgb(175, 255, 145, 0));
                        using var orangePen = new Pen(Color.FromArgb(220, 200, 90, 0), 2);
                        g.FillRectangle(orangeBrush, x + 2, y + 2, sqSize - 4, sqSize - 4);
                        g.DrawRectangle(orangePen, x + 2, y + 2, sqSize - 4, sqSize - 4);
                    }

                    if (piece != '.')
                    {
                        int variant = rules.PieceVariants[r, c];
                        string key2 = $"{piece}2";
                        string key1 = $"{piece}1";
                        string chosenKey = (variant == 2 && pieceImages.ContainsKey(key2)) ? key2 : key1;

                        if (pieceImages.TryGetValue(chosenKey, out Image? img))
                        {
                            DrawPieceSprite(g, img, piece, x, y, sqSize);
                        }
                        else
                        {
                            DrawFallbackPiece(g, piece, x, y, sqSize);
                        }
                    }

                    if ((piece == 'K' && whiteInCheck) || (piece == 'k' && blackInCheck))
                    {
                        bool isMate = currentSideHasNoMoves && ((piece == 'K' && rules.WhiteTurn) || (piece == 'k' && !rules.WhiteTurn));
                        string badgeText = isMate ? "# MATE" : (IsEnglish ? "CHECK!" : "XEQUE!");
                        DrawCornerBadge(g, badgeText, x + 2, y + 2, isMate ? Color.FromArgb(140, 0, 0) : Color.FromArgb(210, 30, 0), Color.Yellow);
                    }
                    else if (LastPromotionSquare == pt && piece != '.')
                    {
                        DrawCornerBadge(g, "★ PROMO", x + 2, y + 2, Color.FromArgb(180, 130, 0), Color.White);
                    }

                    if (invalidFlashSquare == pt)
                    {
                        DrawCornerBadge(g, "✖", x + sqSize - 20, y + 2, Color.FromArgb(190, 0, 0), Color.White);
                    }

                    if ((Focused || ShowKeyboardFocus) && KeyboardCursor == pt)
                    {
                        using var outerPen = new Pen(Color.Black, 3.5f);
                        using var innerPen = new Pen(Color.FromArgb(0, 225, 255), 2f);
                        g.DrawRectangle(outerPen, x + 2, y + 2, sqSize - 4, sqSize - 4);
                        g.DrawRectangle(innerPen, x + 4, y + 4, sqSize - 8, sqSize - 8);
                    }
                }
            }

            foreach (var arrow in DrawnArrows)
                DrawBoardArrow(g, arrow.from, arrow.to, Color.FromArgb(200, 55, 70, 75), ox, oy, sqSize, null);

            // Seta 2º Lance Alternativo (Modo Professor - Azul/Ciano)
            if (AlternativeHintArrow.HasValue)
            {
                DrawBoardArrow(g, AlternativeHintArrow.Value.from, AlternativeHintArrow.Value.to,
                    Color.FromArgb(210, 30, 125, 220), ox, oy, sqSize, "2º");
            }

            // Seta 1º Melhor Lance (Verde)
            if (HintArrow.HasValue)
            {
                DrawBoardArrow(g, HintArrow.Value.from, HintArrow.Value.to,
                    Color.FromArgb(220, 30, 160, 50), ox, oy, sqSize, AlternativeHintArrow.HasValue ? "1º" : null);
            }
        }

        private void DrawPieceSprite(Graphics g, Image img, char piece, int x, int y, int sqSize)
        {
            int pad = Math.Max(4, sqSize / 12);
            var destRect = new Rectangle(x + pad, y + pad, sqSize - pad * 2, sqSize - pad * 2);

            if (!HighContrastPieces)
            {
                g.DrawImage(img, destRect);
                return;
            }

            bool isWhite = char.IsUpper(piece);
            Color badgeFill = isWhite ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(235, 18, 18, 22);
            Color badgeBorder = isWhite ? Color.Black : Color.FromArgb(255, 215, 0);

            using (var bgBrush = new SolidBrush(badgeFill))
            using (var borderPen = new Pen(badgeBorder, 2.5f))
            {
                g.FillEllipse(bgBrush, x + 3, y + 3, sqSize - 6, sqSize - 6);
                g.DrawEllipse(borderPen, x + 3, y + 3, sqSize - 6, sqSize - 6);
            }

            using var ia = new ImageAttributes();
            float contrast = 1.45f;
            float offset = isWhite ? 0.08f : -0.08f;
            var cm = new ColorMatrix(new float[][]
            {
                new float[] { contrast, 0, 0, 0, 0 },
                new float[] { 0, contrast, 0, 0, 0 },
                new float[] { 0, 0, contrast, 0, 0 },
                new float[] { 0, 0, 0, 1f, 0 },
                new float[] { offset, offset, offset, 0, 1f }
            });
            ia.SetColorMatrix(cm);

            int hcPad = Math.Max(6, sqSize / 8);
            var hcRect = new Rectangle(x + hcPad, y + hcPad, sqSize - hcPad * 2, sqSize - hcPad * 2);
            g.DrawImage(img, hcRect, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
        }

        private static void DrawCornerBadge(Graphics g, string text, int x, int y, Color bgColor, Color fgColor)
        {
            using var font = new Font("Tahoma", 6.8f, FontStyle.Bold);
            SizeF sz = g.MeasureString(text, font);
            var rect = new RectangleF(x, y, sz.Width + 4, sz.Height + 1);
            using var bg = new SolidBrush(bgColor);
            using var fg = new SolidBrush(fgColor);
            g.FillRectangle(bg, rect);
            g.DrawString(text, font, fg, x + 2, y);
        }

        private static void DrawFallbackPiece(Graphics g, char piece, int x, int y, int sqSize)
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
            float fontSize = Math.Max(16f, sqSize * 0.45f);
            using var f = new Font("Segoe UI Symbol", fontSize, FontStyle.Regular);
            var color = char.IsUpper(piece) ? Color.White : Color.Black;
            var outline = char.IsUpper(piece) ? Color.Black : Color.White;
            using var bOut = new SolidBrush(outline);
            using var bMain = new SolidBrush(color);
            g.DrawString(symbol, f, bOut, x + 5, y + 3);
            g.DrawString(symbol, f, bMain, x + 4, y + 2);
        }

        private static void DrawBoardArrow(Graphics g, Point from, Point to, Color color, int ox, int oy, int sqSize, string? rankTag)
        {
            PointF p1 = new PointF(ox + from.X * sqSize + sqSize / 2f, oy + from.Y * sqSize + sqSize / 2f);
            PointF p2 = new PointF(ox + to.X * sqSize + sqSize / 2f, oy + to.Y * sqSize + sqSize / 2f);

            using var pen = new Pen(color, Math.Max(6f, sqSize * 0.145f));
            using var arrowCap = new AdjustableArrowCap(4.5f, 4.5f, true);
            pen.StartCap = LineCap.Round;
            pen.CustomEndCap = arrowCap;
            g.DrawLine(pen, p1, p2);

            if (!string.IsNullOrEmpty(rankTag))
            {
                DrawCornerBadge(g, rankTag, (int)p2.X - 10, (int)p2.Y - 10, Color.FromArgb(230, 20, 20, 25), Color.White);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { invalidFlashTimer.Dispose(); } catch { }
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