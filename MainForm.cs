using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XadrezApp
{
    public class MainForm : Form
    {
        private const int SquareSize = 62;
        private const int BoardOffset = 24;

        private readonly ChessRules rules = new ChessRules();
        private readonly EngineService engine = new EngineService();
        private readonly Stack<GameSnapshot> history = new Stack<GameSnapshot>();

        private bool isBotThinking = false;
        private bool gameOver = false;
        private double lastWhiteEval = 0.0;

        // Modo Quebra-Cabeças (Puzzles)
        private TacticalPuzzle? activePuzzle = null;

        // Relógio Digital (Fischer & Bronstein)
        private System.Windows.Forms.Timer clockTimer = null!;
        private string timeControlMode = "Fischer 5+3";
        private double whiteSecondsLeft = 300;
        private double blackSecondsLeft = 300;
        private double incrementSeconds = 3;
        private bool isBronsteinMode = false;
        private double turnElapsedSeconds = 0;

        // Seleção, Quadrados Laranjas, Premove e Setas de Botão Direito
        private Point? selectedSquare = null;
        private List<Point> legalMoves = new List<Point>();
        private Point? premoveSelection = null;
        private (Point from, Point to)? queuedPremove = null;
        private readonly HashSet<Point> markedSquares = new HashSet<Point>();
        private readonly List<(Point from, Point to)> drawnArrows = new List<(Point from, Point to)>();
        private Point? rightDragStart = null;
        private (Point from, Point to)? hintArrow = null;

        private string currentTheme = "Madeira";

        // Controles da Interface Clássica
        private Panel boardPanel = null!;
        private ListView moveHistoryList = null!;
        private ListView legalMovesList = null!;
        private TextBox engineLogBox = null!;
        private TextBox nodesTextBox = null!;
        private Label tutorStatusLabel = null!;
        private Label openingBookLabel = null!;
        private Label whiteClockLabel = null!;
        private Label blackClockLabel = null!;
        private ComboBox difficultyCombo = null!;
        private ComboBox themeCombo = null!;
        private ComboBox clockCombo = null!;

        private readonly Dictionary<string, Image> pieceImages = new Dictionary<string, Image>();

        public MainForm()
        {
            Text = "MainForm - Lucas Chess & Classic Study Suite";
            ClientSize = new Size(960, 760);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(236, 233, 216);
            Font = new Font("Tahoma", 8.25f, FontStyle.Regular);

            InitClassicUI();
            InitDigitalClock();
            LoadPieceImages();
            ResetBoard();
        }

        private void InitClassicUI()
        {
            // 1. Barra de Menu Superior Clássica
            var menuStrip = new MenuStrip
            {
                BackColor = Color.FromArgb(236, 233, 216),
                RenderMode = ToolStripRenderMode.System
            };

            var fileMenu = new ToolStripMenuItem("File (PGN / FEN)");
            fileMenu.DropDownItems.Add("Novo Jogo Clássico", null, (s, e) => ResetBoard());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("Copiar FEN para Área de Transferência", null, (s, e) => CopyFen());
            fileMenu.DropDownItems.Add("Carregar Posição FEN...", null, (s, e) => PromptLoadFen());
            fileMenu.DropDownItems.Add("Exportar / Salvar Partida PGN...", null, (s, e) => ExportPgn());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("Sair", null, (s, e) => Close());

            var puzzlesMenu = new ToolStripMenuItem("Puzzles Táticos");
            for (int i = 0; i < StudyLibrary.Puzzles.Count; i++)
            {
                var pz = StudyLibrary.Puzzles[i];
                puzzlesMenu.DropDownItems.Add(pz.Title, null, (s, e) => StartPuzzle(pz));
            }
            puzzlesMenu.DropDownItems.Add(new ToolStripSeparator());
            puzzlesMenu.DropDownItems.Add("Sair do Modo Puzzle (Partida Normal)", null, (s, e) => ResetBoard());

            var viewMenu = new ToolStripMenuItem("View (Temas)");
            viewMenu.DropDownItems.Add("Madeira", null, (s, e) => SetTheme("Madeira"));
            viewMenu.DropDownItems.Add("Preto e Branco", null, (s, e) => SetTheme("Preto e Branco"));
            viewMenu.DropDownItems.Add("Branco e Verde", null, (s, e) => SetTheme("Branco e Verde"));

            var searchMenu = new ToolStripMenuItem("Search & Estudo");
            searchMenu.DropDownItems.Add("Analisar Posição (Stockfish)", null, async (s, e) => await RunAnalysisAndHintAsync(false));
            searchMenu.DropDownItems.Add("Ajuda de Lance (Help to Move)", null, async (s, e) => await RunAnalysisAndHintAsync(true));

            menuStrip.Items.Add(fileMenu);
            menuStrip.Items.Add(puzzlesMenu);
            menuStrip.Items.Add(searchMenu);
            menuStrip.Items.Add(viewMenu);
            MainMenuStrip = menuStrip;
            Controls.Add(menuStrip);

            // 2. Barra de Ferramentas Quadrada (Estilo Lucas Chess)
            var toolPanel = new Panel
            {
                Location = new Point(0, 24),
                Size = new Size(960, 48),
                BackColor = Color.FromArgb(245, 244, 238),
                BorderStyle = BorderStyle.Fixed3D
            };

            void AddToolbarBtn(string text, int x, int width, EventHandler onClick)
            {
                var btn = new Button
                {
                    Text = text,
                    Location = new Point(x, 6),
                    Size = new Size(width, 32),
                    FlatStyle = FlatStyle.System,
                    Font = new Font("Tahoma", 8f, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btn.Click += onClick;
                toolPanel.Controls.Add(btn);
            }

            AddToolbarBtn("Reiniciar", 6, 72, (s, e) => ResetBoard());
            AddToolbarBtn("Desistir", 82, 70, (s, e) => ResignGame());
            AddToolbarBtn("Empate", 156, 68, (s, e) => OfferDraw());
            AddToolbarBtn("Voltar Lance", 228, 88, (s, e) => TakebackMove());
            AddToolbarBtn("Ajuda Lance", 320, 88, async (s, e) => await RunAnalysisAndHintAsync(true));
            AddToolbarBtn("Puzzles", 412, 68, (s, e) => StartPuzzle(StudyLibrary.Puzzles[0]));
            AddToolbarBtn("PGN/FEN", 484, 72, (s, e) => ExportPgn());

            var lblDiff = new Label { Text = "IA:", Location = new Point(564, 15), AutoSize = true, Font = new Font("Tahoma", 8f, FontStyle.Bold) };
            difficultyCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(586, 11), Width = 112 };
            difficultyCombo.Items.AddRange(new object[] { "Fácil (Nível 1)", "Médio (Nível 7)", "Difícil (Nível 14)", "Mestre (Nível 20)" });
            difficultyCombo.SelectedIndex = 1;

            var lblClock = new Label { Text = "Relógio:", Location = new Point(704, 15), AutoSize = true, Font = new Font("Tahoma", 8f, FontStyle.Bold) };
            clockCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(754, 11), Width = 105 };
            clockCombo.Items.AddRange(new object[] { "Fischer 5+3", "Fischer 3+2", "Fischer 10+0", "Bronstein 5+3", "Sem Relógio" });
            clockCombo.SelectedIndex = 0;
            clockCombo.SelectedIndexChanged += (s, e) => ConfigureClockMode(clockCombo.SelectedItem?.ToString() ?? "Fischer 5+3");

            themeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(864, 11), Width = 86 };
            themeCombo.Items.AddRange(new object[] { "Madeira", "Preto e Branco", "Branco e Verde" });
            themeCombo.SelectedIndex = 0;
            themeCombo.SelectedIndexChanged += (s, e) => SetTheme(themeCombo.SelectedItem?.ToString() ?? "Madeira");

            toolPanel.Controls.Add(lblDiff);
            toolPanel.Controls.Add(difficultyCombo);
            toolPanel.Controls.Add(lblClock);
            toolPanel.Controls.Add(clockCombo);
            toolPanel.Controls.Add(themeCombo);
            Controls.Add(toolPanel);

            // 3. Faixa da Biblioteca de Aberturas (Opening Book)
            openingBookLabel = new Label
            {
                Text = "Abertura: Posição Inicial",
                Location = new Point(12, 76),
                Size = new Size(544, 22),
                BorderStyle = BorderStyle.Fixed3D,
                BackColor = Color.FromArgb(255, 252, 230),
                Font = new Font("Tahoma", 8.25f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(openingBookLabel);

            // 4. Painel do Tabuleiro 8x8
            int boardTotalSize = SquareSize * 8 + BoardOffset * 2;
            boardPanel = new DoubleBufferedPanel
            {
                Location = new Point(12, 102),
                Size = new Size(boardTotalSize, boardTotalSize),
                BackColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D
            };
            boardPanel.Paint += BoardPanel_Paint;
            boardPanel.MouseDown += BoardPanel_MouseDown;
            boardPanel.MouseUp += BoardPanel_MouseUp;
            Controls.Add(boardPanel);

            // 5. Relógios Digitais e Tabelas Laterais
            legalMovesList = new ListView
            {
                Location = new Point(564, 102),
                Size = new Size(125, 480),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BorderStyle = BorderStyle.Fixed3D
            };
            legalMovesList.Columns.Add("Lance", 58);
            legalMovesList.Columns.Add("UCI", 48);
            Controls.Add(legalMovesList);

            // Relógio Brancas e Pretas
            whiteClockLabel = new Label
            {
                Text = "Brancas: 05:00",
                Location = new Point(696, 76),
                Size = new Size(124, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.White,
                ForeColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold)
            };
            blackClockLabel = new Label
            {
                Text = "Pretas: 05:00",
                Location = new Point(824, 76),
                Size = new Size(124, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Black,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold)
            };
            Controls.Add(whiteClockLabel);
            Controls.Add(blackClockLabel);

            // Tabela de Histórico com Classificação de Lances (!!, !, ?!, ?, ??)
            moveHistoryList = new ListView
            {
                Location = new Point(696, 102),
                Size = new Size(252, 480),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BorderStyle = BorderStyle.Fixed3D
            };
            moveHistoryList.Columns.Add("N.", 28);
            moveHistoryList.Columns.Add("White", 62);
            moveHistoryList.Columns.Add("Black", 58);
            moveHistoryList.Columns.Add("Aval.", 46);
            moveHistoryList.Columns.Add("Análise", 52);
            Controls.Add(moveHistoryList);

            // 6. Controles de Busca e Tutor Abaixo do Tabuleiro
            var btnStartSearch = new Button
            {
                Text = "Start search",
                Location = new Point(12, 590),
                Size = new Size(95, 25),
                FlatStyle = FlatStyle.System
            };
            btnStartSearch.Click += async (s, e) => await RunAnalysisAndHintAsync(false);

            var lblNodes = new Label { Text = "Nodes:", Location = new Point(130, 595), AutoSize = true };
            nodesTextBox = new TextBox
            {
                Text = "0",
                Location = new Point(175, 592),
                Size = new Size(95, 22),
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D
            };

            var btnAbortSearch = new Button
            {
                Text = "Abort search",
                Location = new Point(285, 590),
                Size = new Size(95, 25),
                FlatStyle = FlatStyle.System
            };
            btnAbortSearch.Click += (s, e) => engine.StopSearch();

            tutorStatusLabel = new Label
            {
                Text = "Tutor ativo | Roque, En Passant, Relógio Fischer/Bronstein e Análise prontos",
                Location = new Point(400, 590),
                Size = new Size(548, 24),
                BorderStyle = BorderStyle.Fixed3D,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(230, 240, 250)
            };

            Controls.Add(btnStartSearch);
            Controls.Add(lblNodes);
            Controls.Add(nodesTextBox);
            Controls.Add(btnAbortSearch);
            Controls.Add(tutorStatusLabel);

            // 7. Log de Análise UCI Inferior
            engineLogBox = new TextBox
            {
                Location = new Point(12, 622),
                Size = new Size(936, 126),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                Font = new Font("Consolas", 8.5f)
            };
            Controls.Add(engineLogBox);
        }

        private void InitDigitalClock()
        {
            clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            clockTimer.Tick += (s, e) =>
            {
                if (gameOver || timeControlMode == "Sem Relógio" || activePuzzle != null) return;

                turnElapsedSeconds += 1;
                if (rules.WhiteTurn)
                {
                    whiteSecondsLeft -= 1;
                    if (whiteSecondsLeft <= 0)
                    {
                        whiteSecondsLeft = 0;
                        UpdateClockLabels();
                        gameOver = true;
                        tutorStatusLabel.Text = "Tempo esgotado! Vitória do Stockfish (Pretas) no relógio.";
                        MessageBox.Show("Seu tempo acabou! Vitória das Pretas por tempo.", "Relógio Esgotado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    blackSecondsLeft -= 1;
                    if (blackSecondsLeft <= 0)
                    {
                        blackSecondsLeft = 0;
                        UpdateClockLabels();
                        gameOver = true;
                        tutorStatusLabel.Text = "Tempo do bot esgotado! Vitória sua no relógio.";
                        MessageBox.Show("O tempo das Pretas acabou! Você venceu por tempo.", "Relógio Esgotado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }
                UpdateClockLabels();
            };
            clockTimer.Start();
        }

        private void ConfigureClockMode(string mode)
        {
            timeControlMode = mode;
            isBronsteinMode = mode.StartsWith("Bronstein");

            if (mode == "Fischer 3+2") { whiteSecondsLeft = 180; blackSecondsLeft = 180; incrementSeconds = 2; }
            else if (mode == "Fischer 10+0") { whiteSecondsLeft = 600; blackSecondsLeft = 600; incrementSeconds = 0; }
            else { whiteSecondsLeft = 300; blackSecondsLeft = 300; incrementSeconds = 3; }

            turnElapsedSeconds = 0;
            UpdateClockLabels();
        }

        private void ApplyClockIncrement(bool forWhite)
        {
            if (timeControlMode == "Sem Relógio" || activePuzzle != null) return;

            // Fischer adiciona todo o acréscimo; Bronstein devolve apenas até o tempo gasto no lance
            double bonus = isBronsteinMode ? Math.Min(turnElapsedSeconds, incrementSeconds) : incrementSeconds;
            if (forWhite) whiteSecondsLeft += bonus;
            else blackSecondsLeft += bonus;

            turnElapsedSeconds = 0;
            UpdateClockLabels();
        }

        private void UpdateClockLabels()
        {
            if (timeControlMode == "Sem Relógio" || activePuzzle != null)
            {
                whiteClockLabel.Text = "Brancas: --:--";
                blackClockLabel.Text = "Pretas: --:--";
                return;
            }
            TimeSpan w = TimeSpan.FromSeconds(Math.Max(0, whiteSecondsLeft));
            TimeSpan b = TimeSpan.FromSeconds(Math.Max(0, blackSecondsLeft));
            whiteClockLabel.Text = $"Brancas: {w.Minutes:D2}:{w.Seconds:D2}";
            blackClockLabel.Text = $"Pretas: {b.Minutes:D2}:{b.Seconds:D2}";
        }

        private void SetTheme(string themeName)
        {
            currentTheme = themeName;
            boardPanel.Invalidate();
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

        private void ResetBoard()
        {
            activePuzzle = null;
            rules.Reset();
            history.Clear();
            isBotThinking = false;
            gameOver = false;
            lastWhiteEval = 0.0;
            selectedSquare = null;
            legalMoves.Clear();
            premoveSelection = null;
            queuedPremove = null;
            markedSquares.Clear();
            drawnArrows.Clear();
            hintArrow = null;

            ConfigureClockMode(clockCombo.SelectedItem?.ToString() ?? "Fischer 5+3");
            moveHistoryList.Items.Clear();
            openingBookLabel.Text = "Abertura: Posição Inicial (Aguardando 1º lance)";
            engineLogBox.Text = "Motor pronto. Todas as regras FIDE (Roque, En Passant, 50 lances, 3 repetições) ativas." + Environment.NewLine;
            tutorStatusLabel.Text = "Sua vez (Brancas)";

            RefreshLegalMovesTable();
            boardPanel.Invalidate();
        }

        private void StartPuzzle(TacticalPuzzle puzzle)
        {
            ResetBoard();
            activePuzzle = puzzle;
            rules.LoadFen(puzzle.Fen);
            openingBookLabel.Text = $"MODO PUZZLE: {puzzle.Title} [{puzzle.Theme}]";
            tutorStatusLabel.Text = $"Dica: {puzzle.Hint}";
            UpdateClockLabels();
            RefreshLegalMovesTable();
            boardPanel.Invalidate();
        }

        private void CopyFen()
        {
            string fen = rules.ToFen();
            Clipboard.SetText(fen);
            MessageBox.Show($"FEN copiado para a área de transferência:\n\n{fen}", "FEN Copiado", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void PromptLoadFen()
        {
            using var form = new Form
            {
                Text = "Carregar Posição FEN",
                ClientSize = new Size(520, 110),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent
            };
            var txt = new TextBox { Text = rules.ToFen(), Location = new Point(12, 18), Width = 495 };
            var btn = new Button { Text = "Carregar FEN", Location = new Point(400, 55), Size = new Size(105, 32) };
            btn.Click += (s, e) =>
            {
                if (rules.LoadFen(txt.Text))
                {
                    history.Clear();
                    moveHistoryList.Items.Clear();
                    RefreshLegalMovesTable();
                    boardPanel.Invalidate();
                    form.Close();
                }
                else MessageBox.Show("FEN inválido!", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            form.Controls.Add(txt);
            form.Controls.Add(btn);
            form.ShowDialog();
        }

        private void ExportPgn()
        {
            string pgn = rules.ToPgn();
            Clipboard.SetText(pgn);

            using var sfd = new SaveFileDialog
            {
                Filter = "Arquivo PGN (*.pgn)|*.pgn|Texto (*.txt)|*.txt",
                FileName = $"partida_{DateTime.Now:yyyyMMdd_HHmm}.pgn"
            };
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                File.WriteAllText(sfd.FileName, pgn);
                MessageBox.Show("Arquivo PGN salvo e copiado para a área de transferência!", "PGN Exportado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show($"O PGN também já foi copiado para sua área de transferência:\n\n{pgn}", "PGN Copiado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void TakebackMove()
        {
            if (isBotThinking || history.Count == 0) return;

            rules.RestoreSnapshot(history.Pop());
            gameOver = false;
            selectedSquare = null;
            legalMoves.Clear();
            hintArrow = null;

            if (moveHistoryList.Items.Count > 0)
                moveHistoryList.Items.RemoveAt(moveHistoryList.Items.Count - 1);

            openingBookLabel.Text = $"Abertura: {StudyLibrary.DetectOpening(rules.SanMoves)}";
            tutorStatusLabel.Text = "Lance desfeito (Takeback). Sua vez!";
            RefreshLegalMovesTable();
            boardPanel.Invalidate();
        }

        private void ResignGame()
        {
            if (gameOver) return;
            gameOver = true;
            tutorStatusLabel.Text = "Você desistiu. Vitória do Stockfish (Pretas).";
            MessageBox.Show("Você desistiu da partida. Vitória das Pretas!", "Fim de Jogo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OfferDraw()
        {
            if (gameOver) return;
            int evalWhite = EngineService.EvaluateBoardWhiteRelative(rules.Board);
            if (evalWhite >= -150)
            {
                gameOver = true;
                tutorStatusLabel.Text = "Empate de comum acordo!";
                MessageBox.Show("O bot analisou a posição e ACEITOU o seu pedido de empate!", "Empate Aceito", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                tutorStatusLabel.Text = "O bot recusou o empate (ele está em vantagem)!";
                MessageBox.Show("O bot recusou o empate porque avalia que está em vantagem.", "Empate Recusado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RefreshLegalMovesTable()
        {
            legalMovesList.Items.Clear();
            foreach (var mv in rules.GetAllLegalMoves(rules.WhiteTurn))
            {
                string f = ChessRules.SquareToAlgebraic(mv.from);
                string t = ChessRules.SquareToAlgebraic(mv.to);
                var item = new ListViewItem($"{f}-{t}");
                item.SubItems.Add($"{f}{t}");
                legalMovesList.Items.Add(item);
            }
        }

        private void BoardPanel_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            Color lightColor = Color.FromArgb(238, 226, 213);
            Color darkColor = Color.FromArgb(166, 117, 90);

            if (currentTheme == "Preto e Branco")
            {
                lightColor = Color.FromArgb(225, 225, 225);
                darkColor = Color.FromArgb(135, 135, 135);
            }
            else if (currentTheme == "Branco e Verde")
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

            var pieceCounts = new Dictionary<char, int>();

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    int x = BoardOffset + c * SquareSize;
                    int y = BoardOffset + r * SquareSize;
                    var pt = new Point(c, r);
                    bool isLight = (r + c) % 2 == 0;

                    Color sqColor = isLight ? lightColor : darkColor;
                    if (selectedSquare == pt || premoveSelection == pt)
                        sqColor = Color.FromArgb(246, 246, 105);

                    using (var brush = new SolidBrush(sqColor))
                        g.FillRectangle(brush, x, y, SquareSize, SquareSize);

                    if (markedSquares.Contains(pt))
                    {
                        using var markBrush = new SolidBrush(Color.FromArgb(150, 235, 65, 50));
                        g.FillRectangle(markBrush, x, y, SquareSize, SquareSize);
                    }

                    if (queuedPremove.HasValue && (queuedPremove.Value.from == pt || queuedPremove.Value.to == pt))
                    {
                        using var preBrush = new SolidBrush(Color.FromArgb(150, 50, 140, 240));
                        g.FillRectangle(preBrush, x, y, SquareSize, SquareSize);
                    }

                    if (legalMoves.Contains(pt))
                    {
                        using var orangeBrush = new SolidBrush(Color.FromArgb(175, 255, 145, 0));
                        using var orangePen = new Pen(Color.FromArgb(220, 200, 90, 0), 2);
                        g.FillRectangle(orangeBrush, x + 2, y + 2, SquareSize - 4, SquareSize - 4);
                        g.DrawRectangle(orangePen, x + 2, y + 2, SquareSize - 4, SquareSize - 4);
                    }

                    char piece = rules.Board[r, c];
                    if (piece != '.')
                    {
                        pieceCounts[piece] = pieceCounts.GetValueOrDefault(piece, 0) + 1;
                        string key2 = $"{piece}2";
                        string key1 = $"{piece}1";
                        string chosenKey = (pieceCounts[piece] >= 2 && pieceImages.ContainsKey(key2)) ? key2 : key1;

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

            foreach (var arrow in drawnArrows)
                DrawBoardArrow(g, arrow.from, arrow.to, Color.FromArgb(200, 55, 70, 75));

            if (hintArrow.HasValue)
                DrawBoardArrow(g, hintArrow.Value.from, hintArrow.Value.to, Color.FromArgb(215, 30, 160, 50));
        }

        private void DrawFallbackPiece(Graphics g, char piece, int x, int y)
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

        private void DrawBoardArrow(Graphics g, Point from, Point to, Color color)
        {
            PointF p1 = new PointF(BoardOffset + from.X * SquareSize + SquareSize / 2f, BoardOffset + from.Y * SquareSize + SquareSize / 2f);
            PointF p2 = new PointF(BoardOffset + to.X * SquareSize + SquareSize / 2f, BoardOffset + to.Y * SquareSize + SquareSize / 2f);

            using var pen = new Pen(color, 9);
            pen.StartCap = LineCap.Round;
            pen.CustomEndCap = new AdjustableArrowCap(4.5f, 4.5f, true);
            g.DrawLine(pen, p1, p2);
        }

        private void BoardPanel_MouseDown(object? sender, MouseEventArgs e)
        {
            int col = (e.X - BoardOffset) / SquareSize;
            int row = (e.Y - BoardOffset) / SquareSize;
            if (col < 0 || col > 7 || row < 0 || row > 7) return;

            var clicked = new Point(col, row);

            if (e.Button == MouseButtons.Right)
            {
                rightDragStart = clicked;
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (markedSquares.Count > 0 || drawnArrows.Count > 0)
                {
                    markedSquares.Clear();
                    drawnArrows.Clear();
                }

                if (gameOver)
                {
                    boardPanel.Invalidate();
                    return;
                }

                if (isBotThinking || !rules.WhiteTurn)
                {
                    if (premoveSelection.HasValue && premoveSelection.Value != clicked)
                    {
                        queuedPremove = (premoveSelection.Value, clicked);
                        premoveSelection = null;
                        tutorStatusLabel.Text = $"Jogada automática (Premove) agendada: {ChessRules.SquareToAlgebraic(queuedPremove.Value.from)}->{ChessRules.SquareToAlgebraic(queuedPremove.Value.to)}";
                    }
                    else
                    {
                        char piece = rules.Board[row, col];
                        if (piece != '.' && char.IsUpper(piece))
                        {
                            premoveSelection = clicked;
                            queuedPremove = null;
                        }
                        else
                        {
                            premoveSelection = null;
                            queuedPremove = null;
                        }
                    }
                    boardPanel.Invalidate();
                    return;
                }

                if (selectedSquare.HasValue && legalMoves.Contains(clicked))
                {
                    _ = ExecutePlayerAndBotTurnAsync(selectedSquare.Value, clicked);
                    return;
                }

                char p = rules.Board[row, col];
                if (p != '.' && char.IsUpper(p))
                {
                    selectedSquare = clicked;
                    legalMoves = rules.GetLegalMovesForSquare(col, row, true);
                }
                else
                {
                    selectedSquare = null;
                    legalMoves.Clear();
                }

                boardPanel.Invalidate();
            }
        }

        private void BoardPanel_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || !rightDragStart.HasValue) return;

            int col = (e.X - BoardOffset) / SquareSize;
            int row = (e.Y - BoardOffset) / SquareSize;
            if (col >= 0 && col <= 7 && row >= 0 && row <= 7)
            {
                var endPt = new Point(col, row);
                if (endPt == rightDragStart.Value)
                {
                    if (!markedSquares.Remove(endPt))
                        markedSquares.Add(endPt);
                }
                else
                {
                    var arrow = (rightDragStart.Value, endPt);
                    if (!drawnArrows.Remove(arrow))
                        drawnArrows.Add(arrow);
                }
            }

            rightDragStart = null;
            boardPanel.Invalidate();
        }

        private async Task ExecutePlayerAndBotTurnAsync(Point from, Point to)
        {
            // Se estiver no Modo Puzzle, valida se o lance corresponde à solução tática
            if (activePuzzle != null)
            {
                string playedUci = $"{ChessRules.SquareToAlgebraic(from)}{ChessRules.SquareToAlgebraic(to)}";
                if (playedUci == activePuzzle.SolutionUci)
                {
                    rules.ExecuteMove(from, to, 'Q');
                    selectedSquare = null;
                    legalMoves.Clear();
                    boardPanel.Invalidate();
                    tutorStatusLabel.Text = $"PARABÉNS! Você resolveu o {activePuzzle.Title}!";
                    MessageBox.Show($"Excelente visão tática! Lance correto ({playedUci}).\n\nEscolha outro Puzzle no menu superior ou clique em Reiniciar.", "Puzzle Resolvido!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    tutorStatusLabel.Text = $"Lance incorreto ({playedUci}). Tente novamente! Dica: {activePuzzle.Hint}";
                }
                return;
            }

            history.Push(rules.CreateSnapshot());

            // Verifica se o lance é Promoção de Peão e abre a janela de escolha
            char? promoPiece = null;
            if (rules.Board[from.Y, from.X] == 'P' && to.Y == 0)
                promoPiece = PromotionDialog.AskPiece(true);

            int moveNum = rules.FullMoveNumber;
            string whiteSan = rules.ExecuteMove(from, to, promoPiece);
            ApplyClockIncrement(true);

            selectedSquare = null;
            legalMoves.Clear();
            hintArrow = null;
            openingBookLabel.Text = $"Abertura: {StudyLibrary.DetectOpening(rules.SanMoves)}";
            boardPanel.Invalidate();

            var rowItem = new ListViewItem(moveNum.ToString());
            rowItem.SubItems.Add(whiteSan);
            rowItem.SubItems.Add("...");
            rowItem.SubItems.Add("");
            rowItem.SubItems.Add("");
            moveHistoryList.Items.Add(rowItem);
            rowItem.EnsureVisible();

            if (CheckAndAnnounceEndGame()) return;

            isBotThinking = true;
            tutorStatusLabel.Text = "Stockfish está pensando e classificando o seu lance...";
            await Task.Delay(150);

            int idx = difficultyCombo.SelectedIndex;
            int skill = idx switch { 0 => 1, 1 => 7, 2 => 14, _ => 20 };
            int depth = idx switch { 0 => 3, 1 => 6, 2 => 10, _ => 14 };

            var botAnalysis = await Task.Run(() => engine.Analyze(rules, false, skill, depth));

            // Classifica o lance que o jogador acabou de fazer (!!, !, ?!, ?, ??)
            var classification = EngineService.ClassifyMove(lastWhiteEval, botAnalysis.EvalWhitePawns, false);
            lastWhiteEval = botAnalysis.EvalWhitePawns;

            if (!string.IsNullOrEmpty(classification.badge))
                rowItem.SubItems[1].Text = $"{whiteSan} {classification.badge}";
            rowItem.SubItems[4].Text = classification.description;

            if (botAnalysis.BestMove.HasValue)
            {
                string blackSan = rules.ExecuteMove(botAnalysis.BestMove.Value.from, botAnalysis.BestMove.Value.to, 'q');
                ApplyClockIncrement(false);
                rowItem.SubItems[2].Text = blackSan;
                rowItem.SubItems[3].Text = botAnalysis.EvalText;
            }

            nodesTextBox.Text = botAnalysis.Nodes.ToString();
            if (!string.IsNullOrWhiteSpace(botAnalysis.LogLine))
                engineLogBox.AppendText(botAnalysis.LogLine + Environment.NewLine);

            isBotThinking = false;
            openingBookLabel.Text = $"Abertura: {StudyLibrary.DetectOpening(rules.SanMoves)}";
            RefreshLegalMovesTable();
            boardPanel.Invalidate();

            if (CheckAndAnnounceEndGame()) return;

            tutorStatusLabel.Text = $"Seu lance foi: {classification.description} {classification.badge} | Avaliação: {botAnalysis.EvalText}";

            // Executa Premove (Jogada Automática) se agendada
            if (queuedPremove.HasValue)
            {
                var pre = queuedPremove.Value;
                queuedPremove = null;
                var validMoves = rules.GetLegalMovesForSquare(pre.from.X, pre.from.Y, true);
                if (rules.Board[pre.from.Y, pre.from.X] != '.' && char.IsUpper(rules.Board[pre.from.Y, pre.from.X]) && validMoves.Contains(pre.to))
                {
                    await Task.Delay(100);
                    await ExecutePlayerAndBotTurnAsync(pre.from, pre.to);
                }
                else
                {
                    tutorStatusLabel.Text = "Premove cancelado (lance ilegal na nova posição).";
                    boardPanel.Invalidate();
                }
            }
        }

        private async Task RunAnalysisAndHintAsync(bool showHintArrow)
        {
            if (gameOver || isBotThinking) return;
            tutorStatusLabel.Text = "Calculando melhor lance e analisando posição...";

            var res = await Task.Run(() => engine.Analyze(rules, rules.WhiteTurn, 20, 10));
            if (res.BestMove.HasValue)
            {
                if (showHintArrow)
                {
                    hintArrow = res.BestMove.Value;
                    boardPanel.Invalidate();
                }
                nodesTextBox.Text = res.Nodes.ToString();
                if (!string.IsNullOrWhiteSpace(res.LogLine))
                    engineLogBox.AppendText(res.LogLine + Environment.NewLine);

                string mvStr = $"{ChessRules.SquareToAlgebraic(res.BestMove.Value.from)}-{ChessRules.SquareToAlgebraic(res.BestMove.Value.to)}";
                tutorStatusLabel.Text = $"Melhor lance sugerido pela IA: {mvStr} (Avaliação: {res.EvalText})";
            }
        }

        private bool CheckAndAnnounceEndGame()
        {
            if (rules.CheckEndGame(out string title, out string detail))
            {
                gameOver = true;
                tutorStatusLabel.Text = detail;
                MessageBox.Show(detail, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            return false;
        }
    }
}