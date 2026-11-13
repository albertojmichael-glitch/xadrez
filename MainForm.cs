using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XadrezApp
{
    public class MainForm : Form
    {
        private readonly GameController game = new GameController();
        private readonly AnalysisController analysis = new AnalysisController();
        private readonly ClockController clock;
        private readonly BoardView boardView;

        private bool isDarkMode = false;
        private bool isEnglish = false;

        // Controles de UI
        private Panel toolPanel = null!;
        private Panel rightSidePanel = null!;
        private Panel bottomPanel = null!;
        private ListView moveHistoryList = null!;
        private ListView legalMovesList = null!;
        private TextBox engineLogBox = null!;
        private TextBox nodesTextBox = null!;
        private Label tutorStatusLabel = null!;
        private Label openingBookLabel = null!;
        private Label whiteClockLabel = null!;
        private Label blackClockLabel = null!;

        private Button btnReset = null!;
        private Button btnResign = null!;
        private Button btnDraw = null!;
        private Button btnTakeback = null!;
        private Button btnHint = null!;
        private Button btnPuzzles = null!;
        private Button btnPgn = null!;
        private Button btnZoomIn = null!;
        private Button btnZoomOut = null!;
        private Button btnStartSearch = null!;
        private Button btnAbortSearch = null!;

        private ComboBox difficultyCombo = null!;
        private ComboBox themeCombo = null!;
        private ComboBox clockCombo = null!;

        public MainForm()
        {
            Text = "♟️ MainForm - Lucas Chess & Classic Study Suite";
            ClientSize = new Size(1020, 780);
            MinimumSize = new Size(880, 680);
            FormBorderStyle = FormBorderStyle.Sizable; // Permite resize responsivo
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(236, 233, 216);
            Font = new Font("Tahoma", 8.25f, FontStyle.Regular);
            KeyPreview = true;

            boardView = new BoardView(game.Rules);
            boardView.SquareLeftClicked += OnBoardSquareLeftClicked;
            boardView.AccessibilityAnnouncement += msg => tutorStatusLabel.Text = $"🔊 {msg}";

            clock = new ClockController(
                () => game.Rules.WhiteTurn,
                () => game.GameOver || game.ActivePuzzle != null);
            clock.ClockUpdated += UpdateClockLabels;
            clock.TimeExpired += OnClockTimeExpired;

            InitLayoutUI();
            ResetBoard();
        }

        private void InitLayoutUI()
        {
            // 1. Menu Superior com Acessibilidade, Idioma, Modo Escuro e Alto Contraste
            var menuStrip = new MenuStrip
            {
                BackColor = Color.FromArgb(236, 233, 216),
                RenderMode = ToolStripRenderMode.System
            };

            var fileMenu = new ToolStripMenuItem("📁 File (PGN / FEN)");
            fileMenu.DropDownItems.Add("🔄 Novo Jogo / New Game", null, (s, e) => ResetBoard());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("📋 Copiar FEN / Copy FEN", null, (s, e) => CopyFen());
            fileMenu.DropDownItems.Add("📥 Carregar FEN / Load FEN...", null, (s, e) => PromptLoadFen());
            fileMenu.DropDownItems.Add("💾 Exportar PGN / Save PGN...", null, (s, e) => ExportPgn());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("🚪 Sair / Exit", null, (s, e) => Close());

            var puzzlesMenu = new ToolStripMenuItem("🧩 Puzzles Táticos (15)");
            foreach (var pz in StudyLibrary.Puzzles)
            {
                var captured = pz;
                puzzlesMenu.DropDownItems.Add(captured.Title, null, (s, e) => StartPuzzle(captured));
            }
            puzzlesMenu.DropDownItems.Add(new ToolStripSeparator());
            puzzlesMenu.DropDownItems.Add("🔙 Voltar para Partida Normal", null, (s, e) => ResetBoard());

            var viewMenu = new ToolStripMenuItem("🎨 View & UX (Acessibilidade)");
            viewMenu.DropDownItems.Add("🪵 Tabuleiro: Madeira", null, (s, e) => SetBoardTheme("Madeira"));
            viewMenu.DropDownItems.Add("🏁 Tabuleiro: Preto e Branco", null, (s, e) => SetBoardTheme("Preto e Branco"));
            viewMenu.DropDownItems.Add("🌿 Tabuleiro: Branco e Verde", null, (s, e) => SetBoardTheme("Branco e Verde"));
            viewMenu.DropDownItems.Add("👁️ Tabuleiro: Alto Contraste", null, (s, e) => SetBoardTheme("Alto Contraste"));
            viewMenu.DropDownItems.Add(new ToolStripSeparator());
            viewMenu.DropDownItems.Add("🌙 Alternar Tema Claro / Escuro (Dark Mode)", null, (s, e) => ToggleDarkMode());
            viewMenu.DropDownItems.Add("🔲 Alternar Peças em Alto Contraste", null, (s, e) => ToggleHighContrastPieces());
            viewMenu.DropDownItems.Add("🌐 Alternar Idioma: Português / English", null, (s, e) => ToggleLanguage());

            var searchMenu = new ToolStripMenuItem("🔍 Search & Estudo");
            searchMenu.DropDownItems.Add("🧠 Analisar Posição (IA)", null, (s, e) => RunSafe(() => RunAnalysisAndHintAsync(false)));
            searchMenu.DropDownItems.Add("💡 Ajuda de Lance (Help to Move)", null, (s, e) => RunSafe(() => RunAnalysisAndHintAsync(true)));
            searchMenu.DropDownItems.Add(new ToolStripSeparator());
            searchMenu.DropDownItems.Add("📊 Relatório de Precisão da Partida (IA)", null, (s, e) => ShowGameAnalysisReport());

            menuStrip.Items.Add(fileMenu);
            menuStrip.Items.Add(puzzlesMenu);
            menuStrip.Items.Add(searchMenu);
            menuStrip.Items.Add(viewMenu);
            MainMenuStrip = menuStrip;
            Controls.Add(menuStrip);

            // 2. Barra de Ferramentas Superior com Ícones e Zoom
            toolPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 46,
                BackColor = Color.FromArgb(245, 244, 238),
                BorderStyle = BorderStyle.Fixed3D
            };

            Button CreateToolBtn(string text, int x, int width, EventHandler onClick)
            {
                var btn = new Button
                {
                    Text = text,
                    Location = new Point(x, 6),
                    Size = new Size(width, 30),
                    FlatStyle = FlatStyle.System,
                    Font = new Font("Tahoma", 8f, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btn.Click += onClick;
                toolPanel.Controls.Add(btn);
                return btn;
            }

            btnReset = CreateToolBtn("🔄 Reiniciar", 6, 78, (s, e) => ResetBoard());
            btnResign = CreateToolBtn("🏳️ Desistir", 86, 76, (s, e) => ResignGame());
            btnDraw = CreateToolBtn("🤝 Empate", 164, 74, (s, e) => OfferDraw());
            btnTakeback = CreateToolBtn("↩️ Voltar", 240, 74, (s, e) => TakebackMove());
            btnHint = CreateToolBtn("💡 Dica IA", 316, 78, (s, e) => RunSafe(() => RunAnalysisAndHintAsync(true)));
            btnPuzzles = CreateToolBtn("🧩 Puzzles", 396, 76, (s, e) =>
            {
                if (game.ActivePuzzle != null)
                    game.CurrentPuzzleIndex = (game.CurrentPuzzleIndex + 1) % StudyLibrary.Puzzles.Count;
                StartPuzzle(StudyLibrary.Puzzles[game.CurrentPuzzleIndex]);
            });
            btnPgn = CreateToolBtn("💾 PGN", 474, 62, (s, e) => ExportPgn());
            btnZoomOut = CreateToolBtn("🔍-", 538, 36, (s, e) => boardView.AdjustZoom(-0.10f));
            btnZoomIn = CreateToolBtn("🔍+", 576, 36, (s, e) => boardView.AdjustZoom(0.10f));

            difficultyCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(620, 10), Width = 115 };
            difficultyCombo.Items.AddRange(new object[] { "Fácil (Iniciante)", "Médio (Clube)", "Difícil (Forte)", "Mestre (Máximo)" });
            difficultyCombo.SelectedIndex = 0;

            clockCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(740, 10), Width = 105 };
            clockCombo.Items.AddRange(new object[] { "Fischer 5+3", "Fischer 3+2", "Fischer 10+0", "Bronstein 5+3", "Sem Relógio" });
            clockCombo.SelectedIndex = 0;
            clockCombo.SelectedIndexChanged += (s, e) =>
            {
                bool inProgress = game.Rules.SanMoves.Count > 0 && !game.GameOver && game.ActivePuzzle == null;
                clock.ConfigureMode(clockCombo.SelectedItem?.ToString() ?? "Fischer 5+3", resetRemainingTime: !inProgress);
            };

            themeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(850, 10), Width = 105 };
            themeCombo.Items.AddRange(new object[] { "Madeira", "Preto e Branco", "Branco e Verde", "Alto Contraste" });
            themeCombo.SelectedIndex = 0;
            themeCombo.SelectedIndexChanged += (s, e) => SetBoardTheme(themeCombo.SelectedItem?.ToString() ?? "Madeira");

            toolPanel.Controls.Add(difficultyCombo);
            toolPanel.Controls.Add(clockCombo);
            toolPanel.Controls.Add(themeCombo);

            // 3. Painel Inferior (Log UCI + Tutor + Nodes)
            bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 165,
                Padding = new Padding(8)
            };

            btnStartSearch = new Button { Text = "▶ Start search", Location = new Point(10, 6), Size = new Size(98, 25), FlatStyle = FlatStyle.System };
            btnStartSearch.Click += (s, e) => RunSafe(() => RunAnalysisAndHintAsync(false));

            var lblNodes = new Label { Text = "Nodes:", Location = new Point(118, 11), AutoSize = true };
            nodesTextBox = new TextBox { Text = "0", Location = new Point(162, 8), Size = new Size(90, 22), ReadOnly = true, BorderStyle = BorderStyle.Fixed3D };

            btnAbortSearch = new Button { Text = "⏹ Abort search", Location = new Point(260, 6), Size = new Size(98, 25), FlatStyle = FlatStyle.System };
            btnAbortSearch.Click += (s, e) => analysis.StopSearch();

            tutorStatusLabel = new Label
            {
                Text = "Tutor ativo | Setas do teclado + Enter movem peças | Ctrl+Scroll ajusta Zoom",
                Location = new Point(368, 6),
                Size = new Size(635, 25),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BorderStyle = BorderStyle.Fixed3D,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(230, 240, 250)
            };

            engineLogBox = new TextBox
            {
                Location = new Point(10, 36),
                Size = new Size(995, 120),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                Font = new Font("Consolas", 8.5f)
            };

            bottomPanel.Controls.Add(btnStartSearch);
            bottomPanel.Controls.Add(lblNodes);
            bottomPanel.Controls.Add(nodesTextBox);
            bottomPanel.Controls.Add(btnAbortSearch);
            bottomPanel.Controls.Add(tutorStatusLabel);
            bottomPanel.Controls.Add(engineLogBox);

            // 4. Painel Lateral Direito (Relógios, Lances Legais e Histórico)
            rightSidePanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 395,
                Padding = new Padding(6)
            };

            whiteClockLabel = new Label
            {
                Text = "⏱ Brancas: 05:00",
                Location = new Point(135, 6),
                Size = new Size(124, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.White,
                ForeColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f, FontStyle.Bold)
            };
            blackClockLabel = new Label
            {
                Text = "⏱ Pretas: 05:00",
                Location = new Point(263, 6),
                Size = new Size(124, 24),
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Black,
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f, FontStyle.Bold)
            };

            legalMovesList = new ListView
            {
                Location = new Point(6, 34),
                Size = new Size(123, 495),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BorderStyle = BorderStyle.Fixed3D
            };
            legalMovesList.Columns.Add("Lance", 58);
            legalMovesList.Columns.Add("UCI", 48);

            moveHistoryList = new ListView
            {
                Location = new Point(135, 34),
                Size = new Size(252, 495),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BorderStyle = BorderStyle.Fixed3D
            };
            moveHistoryList.Columns.Add("N.", 28);
            moveHistoryList.Columns.Add("White", 60);
            moveHistoryList.Columns.Add("Black", 52);
            moveHistoryList.Columns.Add("Aval.", 46);
            moveHistoryList.Columns.Add("Análise", 60);

            rightSidePanel.Controls.Add(whiteClockLabel);
            rightSidePanel.Controls.Add(blackClockLabel);
            rightSidePanel.Controls.Add(legalMovesList);
            rightSidePanel.Controls.Add(moveHistoryList);

            // 5. Painel Central (Faixa de Aberturas + BoardView Responsivo)
            var centerContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 6, 6, 6) };

            openingBookLabel = new Label
            {
                Text = "📖 Abertura: Posição Inicial",
                Dock = DockStyle.Top,
                Height = 24,
                BorderStyle = BorderStyle.Fixed3D,
                BackColor = Color.FromArgb(255, 252, 230),
                Font = new Font("Tahoma", 8.25f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };

            boardView.Dock = DockStyle.Fill;
            boardView.BorderStyle = BorderStyle.Fixed3D;
            boardView.BackColor = Color.White;

            centerContainer.Controls.Add(boardView);
            centerContainer.Controls.Add(openingBookLabel);

            Controls.Add(centerContainer);
            Controls.Add(rightSidePanel);
            Controls.Add(bottomPanel);
            Controls.Add(toolPanel);
        }

        private void ToggleDarkMode()
        {
            isDarkMode = !isDarkMode;
            boardView.IsDarkModeUI = isDarkMode;

            Color bgMain = isDarkMode ? Color.FromArgb(30, 32, 38) : Color.FromArgb(236, 233, 216);
            Color bgPanel = isDarkMode ? Color.FromArgb(42, 45, 54) : Color.FromArgb(245, 244, 238);
            Color bgInput = isDarkMode ? Color.FromArgb(24, 26, 30) : Color.White;
            Color fgText = isDarkMode ? Color.FromArgb(235, 235, 240) : Color.Black;

            BackColor = bgMain;
            toolPanel.BackColor = bgPanel;
            rightSidePanel.BackColor = bgMain;
            bottomPanel.BackColor = bgMain;
            boardView.BackColor = isDarkMode ? Color.FromArgb(38, 40, 48) : Color.White;

            openingBookLabel.BackColor = isDarkMode ? Color.FromArgb(50, 54, 66) : Color.FromArgb(255, 252, 230);
            openingBookLabel.ForeColor = fgText;

            tutorStatusLabel.BackColor = isDarkMode ? Color.FromArgb(45, 55, 70) : Color.FromArgb(230, 240, 250);
            tutorStatusLabel.ForeColor = fgText;

            engineLogBox.BackColor = bgInput;
            engineLogBox.ForeColor = fgText;
            legalMovesList.BackColor = bgInput;
            legalMovesList.ForeColor = fgText;
            moveHistoryList.BackColor = bgInput;
            moveHistoryList.ForeColor = fgText;

            boardView.Invalidate();
        }

        private void ToggleHighContrastPieces()
        {
            boardView.HighContrastPieces = !boardView.HighContrastPieces;
            boardView.Invalidate();
            tutorStatusLabel.Text = boardView.HighContrastPieces
                ? (isEnglish ? "👁️ High-contrast pieces enabled." : "👁️ Peças em Alto Contraste ativadas.")
                : (isEnglish ? "👁️ Standard piece sprites enabled." : "👁️ Imagens originais das peças ativadas.");
        }

        private void ToggleLanguage()
        {
            isEnglish = !isEnglish;
            boardView.IsEnglish = isEnglish;

            btnReset.Text = isEnglish ? "🔄 New" : "🔄 Reiniciar";
            btnResign.Text = isEnglish ? "🏳️ Resign" : "🏳️ Desistir";
            btnDraw.Text = isEnglish ? "🤝 Draw" : "🤝 Empate";
            btnTakeback.Text = isEnglish ? "↩️ Undo" : "↩️ Voltar";
            btnHint.Text = isEnglish ? "💡 Hint" : "💡 Dica IA";
            btnPuzzles.Text = isEnglish ? "🧩 Puzzles" : "🧩 Puzzles";

            UpdateClockLabels();
            UpdateOpeningBanner();
            boardView.UpdateAccessibilityMetadata();
            tutorStatusLabel.Text = isEnglish
                ? "🌐 Language switched to English | Keyboard arrows + Enter supported."
                : "🌐 Idioma alterado para Português | Setas do teclado + Enter ativos.";
        }

        private void SetBoardTheme(string themeName)
        {
            boardView.SetTheme(themeName);
            if (themeCombo.SelectedItem?.ToString() != themeName)
                themeCombo.SelectedItem = themeName;
        }

        private void UpdateClockLabels()
        {
            var (w, b) = clock.FormatClocks(isEnglish, game.ActivePuzzle != null);
            whiteClockLabel.Text = w;
            blackClockLabel.Text = b;
        }

        private void OnClockTimeExpired(bool whiteExpired)
        {
            game.Rules.GameResult = whiteExpired ? "0-1" : "1-0";
            string msg = whiteExpired
                ? (isEnglish ? "⏰ Time out! Black wins on time." : "⏰ Tempo esgotado! Vitória das Pretas no relógio.")
                : (isEnglish ? "⏰ Bot time out! You win on time." : "⏰ Tempo do bot esgotado! Vitória sua no relógio.");

            EndCurrentGame(msg);
            MessageBox.Show(msg, isEnglish ? "Time Out" : "Relógio Esgotado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void UpdateOpeningBanner()
        {
            if (game.ActivePuzzle != null)
            {
                openingBookLabel.Text = $"🧩 PUZZLE: {game.ActivePuzzle.Title} [{game.ActivePuzzle.Theme}]";
                return;
            }
            string prefix = isEnglish ? "📖 Opening" : "📖 Abertura";
            openingBookLabel.Text = $"{prefix}: {StudyLibrary.DetectOpening(game.Rules.SanMoves)}";
        }

        private void ResetBoard()
        {
            analysis.StopSearch();
            analysis.ClearHistory();
            game.ResetStateForNewGame();

            boardView.SelectedSquare = null;
            boardView.LegalMoves.Clear();
            boardView.PremoveSelection = null;
            boardView.QueuedPremove = null;
            boardView.LastMove = null;
            boardView.HintArrow = null;
            boardView.ClearAnnotations();

            clock.ConfigureMode(clockCombo.SelectedItem?.ToString() ?? "Fischer 5+3", resetRemainingTime: true);
            moveHistoryList.Items.Clear();
            UpdateOpeningBanner();

            string sfStatus = analysis.IsUsingStockfish
                ? (isEnglish ? "Official Stockfish Connected" : "Stockfish Oficial Conectado")
                : (isEnglish ? "Internal Positional Engine Active" : "Engine Tática Posicional Interna Ativa");
            engineLogBox.Text = $"[{sfStatus}] {(isEnglish ? "Ready." : "Pronto para jogar.")}" + Environment.NewLine;
            tutorStatusLabel.Text = isEnglish ? "Your turn (White)" : "Sua vez (Brancas)";

            RefreshLegalMovesTable();
            boardView.UpdateAccessibilityMetadata();
            boardView.Invalidate();
        }

        private void EndCurrentGame(string statusText)
        {
            game.MarkGameEnded();
            clock.Stop();
            analysis.StopSearch();

            boardView.SelectedSquare = null;
            boardView.LegalMoves.Clear();
            boardView.PremoveSelection = null;
            boardView.QueuedPremove = null;
            boardView.HintArrow = null;

            tutorStatusLabel.Text = statusText;
            boardView.Invalidate();
        }

        private void StartPuzzle(TacticalPuzzle puzzle)
        {
            ResetBoard();
            if (!game.StartPuzzle(puzzle, out string error))
            {
                MessageBox.Show($"FEN inválido no puzzle \"{puzzle.Title}\":\n\n{error}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            UpdateOpeningBanner();
            tutorStatusLabel.Text = $"💡 {(isEnglish ? "Hint" : "Dica")}: {puzzle.Hint}";
            UpdateClockLabels();
            RefreshLegalMovesTable();
            boardView.Invalidate();
        }

        private void OnBoardSquareLeftClicked(Point clicked)
        {
            if (game.GameOver)
            {
                boardView.Invalidate();
                return;
            }

            int col = clicked.X;
            int row = clicked.Y;

            // Agendamento de Premove enquanto o bot pensa
            if (game.IsBotThinking || !game.Rules.WhiteTurn)
            {
                if (boardView.PremoveSelection.HasValue && boardView.PremoveSelection.Value != clicked)
                {
                    boardView.QueuedPremove = (boardView.PremoveSelection.Value, clicked);
                    boardView.PremoveSelection = null;
                    tutorStatusLabel.Text = $"⚡ Premove: {ChessRules.SquareToAlgebraic(boardView.QueuedPremove.Value.from)}->{ChessRules.SquareToAlgebraic(boardView.QueuedPremove.Value.to)}";
                }
                else
                {
                    char piece = game.Rules.Board[row, col];
                    if (piece != '.' && char.IsUpper(piece))
                    {
                        boardView.PremoveSelection = clicked;
                        boardView.QueuedPremove = null;
                    }
                    else
                    {
                        boardView.PremoveSelection = null;
                        boardView.QueuedPremove = null;
                    }
                }
                boardView.Invalidate();
                return;
            }

            // Execução de lance legal selecionado
            if (boardView.SelectedSquare.HasValue && boardView.LegalMoves.Contains(clicked))
            {
                var fromSq = boardView.SelectedSquare.Value;
                RunSafe(() => ExecutePlayerAndBotTurnAsync(fromSq, clicked));
                return;
            }

            char p = game.Rules.Board[row, col];
            if (p != '.' && char.IsUpper(p))
            {
                boardView.SelectedSquare = clicked;
                boardView.LegalMoves = game.Rules.GetLegalMovesForSquare(col, row, true);
            }
            else
            {
                // Se já havia uma peça selecionada e o usuário clicou numa casa inválida, dispara feedback visual!
                if (boardView.SelectedSquare.HasValue)
                {
                    boardView.TriggerInvalidMoveFlash(clicked);
                    tutorStatusLabel.Text = isEnglish
                        ? $"⚠️ Illegal move to {ChessRules.SquareToAlgebraic(clicked)}!"
                        : $"⚠️ Lance inválido para a casa {ChessRules.SquareToAlgebraic(clicked)}!";
                }
                boardView.SelectedSquare = null;
                boardView.LegalMoves.Clear();
            }

            boardView.Invalidate();
        }

        private async Task ExecutePlayerAndBotTurnAsync(Point from, Point to)
        {
            if (game.ActivePuzzle != null)
            {
                HandlePuzzleMove(from, to);
                return;
            }

            if (game.GameOver || game.IsBotThinking || !game.Rules.WhiteTurn) return;

            int generation = game.GameGeneration;
            var preMoveSnapshot = game.Rules.CreateSnapshot();

            char? promoPiece = null;
            if (game.Rules.Board[from.Y, from.X] == 'P' && to.Y == 0)
                promoPiece = PromotionDialog.AskPiece(true, this, isEnglish);

            if (generation != game.GameGeneration || game.GameOver) return;

            game.IncrementHintGeneration();
            if (game.IsHintRunning)
            {
                game.IsHintRunning = false;
                analysis.StopSearch();
            }

            game.History.Push(preMoveSnapshot);

            int moveNum = game.Rules.FullMoveNumber;
            string whiteSan = game.Rules.ExecuteMove(from, to, promoPiece);
            bool isBookMove = StudyLibrary.IsBookMove(game.Rules.SanMoves);
            clock.ApplyIncrement(true);

            boardView.SelectedSquare = null;
            boardView.LegalMoves.Clear();
            boardView.HintArrow = null;
            boardView.LastMove = (from, to);
            UpdateOpeningBanner();
            boardView.Invalidate();

            var rowItem = new ListViewItem(moveNum.ToString()) { UseItemStyleForSubItems = false };
            rowItem.SubItems.Add(whiteSan);
            rowItem.SubItems.Add("...");
            rowItem.SubItems.Add("");
            rowItem.SubItems.Add("");
            moveHistoryList.Items.Add(rowItem);
            rowItem.EnsureVisible();

            if (whiteSan.EndsWith('#'))
            {
                rowItem.SubItems[1].Text = $"{whiteSan} !";
                rowItem.SubItems[1].ForeColor = Color.FromArgb(20, 150, 80);
                rowItem.SubItems[2].Text = "-";
                rowItem.SubItems[3].Text = "#+";
                rowItem.SubItems[4].Text = isEnglish ? "Checkmate!" : "Xeque-Mate!";
                rowItem.SubItems[4].ForeColor = Color.FromArgb(20, 150, 80);
                analysis.RecordCheckmateMove(moveNum, whiteSan, isEnglish);
            }

            if (CheckAndAnnounceEndGame()) return;

            await RunBotTurnAsync(generation, rowItem, preMoveSnapshot, from, to, promoPiece, whiteSan, isBookMove, moveNum);
        }

        private async Task RunBotTurnAsync(
            int generation,
            ListViewItem rowItem,
            GameSnapshot? beforeWhiteSnapshot,
            Point playerFrom,
            Point playerTo,
            char? playerPromo,
            string? whiteSan,
            bool isBookMove,
            int moveNum)
        {
            game.IsBotThinking = true;
            tutorStatusLabel.Text = isEnglish
                ? "🤖 Analyzing your move and calculating bot reply..."
                : "🤖 Analisando a precisão do seu lance e calculando resposta do Bot...";

            bool botTurnCompleted = false;
            try
            {
                await Task.Delay(120);
                if (generation != game.GameGeneration) return;

                int diffIdx = difficultyCombo.SelectedIndex;
                EngineAnalysisResult evalAfterAnalysis;
                EngineAnalysisResult botAnalysis;
                MoveEvaluationRecord? evalRecord = null;

                if (beforeWhiteSnapshot != null && whiteSan != null)
                {
                    var res = await analysis.EvaluatePlayerTurnAndBotReplyAsync(
                        beforeWhiteSnapshot, game.Rules, playerFrom, playerTo, playerPromo, whiteSan, isBookMove, moveNum, diffIdx);
                    if (generation != game.GameGeneration) return;

                    evalAfterAnalysis = res.evalAfter;
                    botAnalysis = res.botPlay;
                    evalRecord = res.record;

                    if (!string.IsNullOrEmpty(evalRecord.Badge))
                    {
                        rowItem.SubItems[1].Text = $"{whiteSan} {evalRecord.Badge}";
                        rowItem.SubItems[1].ForeColor = evalRecord.BadgeColor;
                    }
                    rowItem.SubItems[4].Text = evalRecord.Description;
                    rowItem.SubItems[4].ForeColor = evalRecord.BadgeColor;
                }
                else
                {
                    botAnalysis = await analysis.AnalyzePositionAsync(game.Rules, false, diffIdx, isHintOrEvalOnly: false);
                    if (generation != game.GameGeneration) return;
                    evalAfterAnalysis = botAnalysis;
                }

                var botMove = botAnalysis.BestMove;
                char? botPromo = botAnalysis.Promotion;
                if (!botMove.HasValue || !game.Rules.GetLegalMovesForSquare(botMove.Value.from.X, botMove.Value.from.Y, false).Contains(botMove.Value.to))
                {
                    var legal = game.Rules.GetAllLegalMoves(false);
                    if (legal.Count == 0)
                        throw new InvalidOperationException("Bot has no legal moves.");
                    botMove = legal[Random.Shared.Next(legal.Count)];
                    botPromo = null;
                }

                string blackSan = game.Rules.ExecuteMove(botMove.Value.from, botMove.Value.to, botPromo ?? 'q');
                clock.ApplyIncrement(false);
                boardView.LastMove = botMove.Value;

                rowItem.SubItems[2].Text = blackSan;
                rowItem.SubItems[3].Text = evalAfterAnalysis.EvalText;

                nodesTextBox.Text = (evalAfterAnalysis.Nodes + botAnalysis.Nodes).ToString();
                if (!string.IsNullOrWhiteSpace(evalAfterAnalysis.LogLine))
                    engineLogBox.AppendText(evalAfterAnalysis.LogLine + Environment.NewLine);

                game.IsBotThinking = false;
                UpdateOpeningBanner();
                RefreshLegalMovesTable();
                boardView.UpdateAccessibilityMetadata();
                boardView.Invalidate();

                if (CheckAndAnnounceEndGame()) return;

                string checkAlert = game.Rules.IsKingInCheck(game.Rules.Board, true)
                    ? (isEnglish ? " ⚠️ CHECK!" : " ⚠️ XEQUE!")
                    : "";

                if (evalRecord != null)
                {
                    double avgAcc = analysis.GetAverageAccuracy();
                    tutorStatusLabel.Text = $"{evalRecord.Description} {evalRecord.Badge} ({evalRecord.CoachNote}) | Aval: {evalAfterAnalysis.EvalText} | Precisão: {avgAcc:0.0}%{checkAlert}";
                }
                else
                {
                    tutorStatusLabel.Text = $"Bot: {blackSan} | Aval: {evalAfterAnalysis.EvalText}{checkAlert}";
                }

                botTurnCompleted = true;
            }
            catch (Exception ex)
            {
                if (generation == game.GameGeneration)
                    tutorStatusLabel.Text = $"⚠️ Erro: {ex.Message}";
            }
            finally
            {
                if (generation == game.GameGeneration)
                {
                    game.IsBotThinking = false;
                    RefreshLegalMovesTable();
                    boardView.Invalidate();
                }
            }

            if (!botTurnCompleted || generation != game.GameGeneration || game.GameOver) return;

            if (boardView.QueuedPremove.HasValue)
            {
                var pre = boardView.QueuedPremove.Value;
                boardView.QueuedPremove = null;
                var validMoves = game.Rules.GetLegalMovesForSquare(pre.from.X, pre.from.Y, true);
                if (game.Rules.Board[pre.from.Y, pre.from.X] != '.' && char.IsUpper(game.Rules.Board[pre.from.Y, pre.from.X]) && validMoves.Contains(pre.to))
                {
                    await Task.Delay(100);
                    if (generation != game.GameGeneration || game.GameOver) return;
                    await ExecutePlayerAndBotTurnAsync(pre.from, pre.to);
                }
                else
                {
                    boardView.TriggerInvalidMoveFlash(pre.to);
                    tutorStatusLabel.Text = isEnglish ? "⚠️ Premove canceled (illegal move)." : "⚠️ Premove cancelado (lance ilegal na nova posição).";
                    boardView.Invalidate();
                }
            }
        }

        private void HandlePuzzleMove(Point from, Point to)
        {
            if (game.ActivePuzzle == null || game.GameOver) return;

            string playedUci = $"{ChessRules.SquareToAlgebraic(from)}{ChessRules.SquareToAlgebraic(to)}";
            if (playedUci == game.ActivePuzzle.SolutionUci)
            {
                var solvedPuzzle = game.ActivePuzzle;
                game.Rules.ExecuteMove(from, to, 'Q');
                if (game.Rules.GameResult == "*") game.Rules.GameResult = "1-0";

                boardView.SelectedSquare = null;
                boardView.LegalMoves.Clear();
                boardView.LastMove = (from, to);
                game.GameOver = true;
                RefreshLegalMovesTable();
                boardView.Invalidate();

                tutorStatusLabel.Text = $"🏆 PARABÉNS! {solvedPuzzle.Title}!";
                int currentIdx = StudyLibrary.Puzzles.IndexOf(solvedPuzzle);
                var nextPuzzle = StudyLibrary.Puzzles[(currentIdx + 1) % StudyLibrary.Puzzles.Count];
                var ans = MessageBox.Show(
                    $"Excelente visão tática! Lance correto ({playedUci}).\n\nDeseja ir para o próximo desafio ({nextPuzzle.Title})?",
                    "🏆 Puzzle Resolvido!", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (ans == DialogResult.Yes && game.ActivePuzzle == solvedPuzzle)
                    StartPuzzle(nextPuzzle);
            }
            else
            {
                boardView.TriggerInvalidMoveFlash(to);
                tutorStatusLabel.Text = $"❌ Lance incorreto ({playedUci}). Dica: {game.ActivePuzzle.Hint}";
            }
        }

        private async Task RunAnalysisAndHintAsync(bool showHintArrow)
        {
            if (game.GameOver || game.IsBotThinking || game.IsHintRunning) return;

            int generation = game.GameGeneration;
            int myHintGen = game.IncrementHintGeneration();
            int moveCountAtStart = game.Rules.SanMoves.Count;
            string fenAtStart = game.Rules.ToFen();
            var snapshotClone = game.Rules.Clone();

            game.IsHintRunning = true;
            tutorStatusLabel.Text = isEnglish ? "🔎 Calculating best move..." : "🔎 Calculando o melhor lance tático/posicional...";

            EngineAnalysisResult res;
            try
            {
                res = await analysis.AnalyzePositionAsync(snapshotClone, snapshotClone.WhiteTurn, 3, isHintOrEvalOnly: true);
            }
            finally
            {
                if (myHintGen == game.HintGeneration)
                    game.IsHintRunning = false;
            }

            if (generation != game.GameGeneration || myHintGen != game.HintGeneration ||
                game.GameOver || game.IsBotThinking ||
                game.Rules.SanMoves.Count != moveCountAtStart || game.Rules.ToFen() != fenAtStart)
            {
                return;
            }

            if (res.BestMove.HasValue)
            {
                var mv = res.BestMove.Value;
                if (!game.Rules.GetLegalMovesForSquare(mv.from.X, mv.from.Y, game.Rules.WhiteTurn).Contains(mv.to))
                    return;

                if (showHintArrow)
                {
                    boardView.HintArrow = mv;
                    boardView.Invalidate();
                }
                nodesTextBox.Text = res.Nodes.ToString();
                if (!string.IsNullOrWhiteSpace(res.LogLine))
                    engineLogBox.AppendText(res.LogLine + Environment.NewLine);

                if (!showHintArrow)
                    engineLogBox.AppendText(EngineService.BuildPositionReport(snapshotClone, res) + Environment.NewLine);

                string promoSuffix = res.Promotion.HasValue ? $"={char.ToUpper(res.Promotion.Value)}" : "";
                string mvStr = $"{ChessRules.SquareToAlgebraic(mv.from)} -> {ChessRules.SquareToAlgebraic(mv.to)}{promoSuffix}";
                tutorStatusLabel.Text = $"💡 IA: {mvStr} (Aval: {res.EvalText})";
            }
        }

        private void TakebackMove()
        {
            if (!game.TryTakeback()) return;
            analysis.StopSearch();
            analysis.RemoveLastEvaluation();

            boardView.SelectedSquare = null;
            boardView.LegalMoves.Clear();
            boardView.HintArrow = null;
            boardView.LastMove = null;

            if (moveHistoryList.Items.Count > 0)
                moveHistoryList.Items.RemoveAt(moveHistoryList.Items.Count - 1);

            if (game.Rules.SanMoves.Count == 0)
                clock.Stop();

            UpdateOpeningBanner();
            tutorStatusLabel.Text = isEnglish ? "↩️ Move undone (Takeback). Your turn!" : "↩️ Lance desfeito (Takeback). Sua vez!";
            RefreshLegalMovesTable();
            boardView.Invalidate();
        }

        private void ResignGame()
        {
            if (game.GameOver) return;
            game.Rules.GameResult = "0-1";
            string msg = isEnglish ? "🏳️ You resigned. Black wins." : "🏳️ Você desistiu. Vitória das Pretas.";
            EndCurrentGame(msg);
            MessageBox.Show(msg, isEnglish ? "Game Over" : "Fim de Jogo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OfferDraw()
        {
            if (game.GameOver) return;
            int evalWhite = EngineService.EvaluateBoardWhiteRelative(game.Rules.Board);
            if (evalWhite >= -150)
            {
                game.Rules.GameResult = "1/2-1/2";
                string msg = isEnglish ? "🤝 Draw agreed!" : "🤝 Empate de comum acordo!";
                EndCurrentGame(msg);
                MessageBox.Show(msg, isEnglish ? "Draw Accepted" : "Empate Aceito", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                tutorStatusLabel.Text = isEnglish ? "❌ Draw offer declined by Bot!" : "❌ O bot recusou o empate (está em vantagem)!";
                MessageBox.Show(tutorStatusLabel.Text, isEnglish ? "Draw Declined" : "Empate Recusado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CopyFen()
        {
            string fen = game.Rules.ToFen();
            try { Clipboard.SetText(fen); } catch { }
            MessageBox.Show($"FEN:\n\n{fen}", "📋 FEN", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void PromptLoadFen()
        {
            using var form = new Form
            {
                Text = isEnglish ? "Load FEN Position" : "Carregar Posição FEN",
                ClientSize = new Size(520, 110),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent
            };
            var txt = new TextBox { Text = game.Rules.ToFen(), Location = new Point(12, 18), Width = 495 };
            var btn = new Button { Text = "OK", Location = new Point(400, 55), Size = new Size(105, 32) };
            string? validatedFen = null;
            btn.Click += (s, e) =>
            {
                var probe = new ChessRules();
                if (probe.TryLoadFen(txt.Text, out string error))
                {
                    validatedFen = txt.Text.Trim();
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else MessageBox.Show($"FEN inválido!\n\n{error}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            form.Controls.Add(txt);
            form.Controls.Add(btn);
            form.AcceptButton = btn;

            if (form.ShowDialog(this) == DialogResult.OK && validatedFen != null)
            {
                ResetBoard();
                game.StartFromValidatedFen(validatedFen);
                RefreshLegalMovesTable();
                boardView.Invalidate();
                if (!CheckAndAnnounceEndGame() && !game.Rules.WhiteTurn)
                {
                    var rowItem = new ListViewItem(game.Rules.FullMoveNumber.ToString()) { UseItemStyleForSubItems = false };
                    rowItem.SubItems.Add("...");
                    rowItem.SubItems.Add("...");
                    rowItem.SubItems.Add("");
                    rowItem.SubItems.Add("");
                    moveHistoryList.Items.Add(rowItem);
                    RunSafe(() => RunBotTurnAsync(game.GameGeneration, rowItem, null, Point.Empty, Point.Empty, null, null, false, game.Rules.FullMoveNumber));
                }
            }
        }

        private void ExportPgn()
        {
            string blackName = analysis.IsUsingStockfish ? "Stockfish IA" : "Engine Tática Interna";
            string? evtName = game.ActivePuzzle != null ? $"Puzzle: {game.ActivePuzzle.Title}" : null;
            string pgn = game.Rules.ToPgn(blackPlayerName: blackName, eventName: evtName);
            try { Clipboard.SetText(pgn); } catch { }

            using var sfd = new SaveFileDialog
            {
                Filter = "Arquivo PGN (*.pgn)|*.pgn|Texto (*.txt)|*.txt",
                FileName = $"partida_{DateTime.Now:yyyyMMdd_HHmm}.pgn"
            };
            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                File.WriteAllText(sfd.FileName, pgn);
                MessageBox.Show("PGN salvo e copiado!", "💾 PGN", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ShowGameAnalysisReport()
        {
            string summary = analysis.BuildGameAccuracySummaryText(game.Rules.SanMoves, isEnglish);
            engineLogBox.AppendText(summary + Environment.NewLine);
            MessageBox.Show(summary, isEnglish ? "📊 AI Accuracy Report" : "📊 Relatório de Precisão da Partida (IA)", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RefreshLegalMovesTable()
        {
            legalMovesList.Items.Clear();
            foreach (var mv in game.Rules.GetAllLegalMoves(game.Rules.WhiteTurn))
            {
                string f = ChessRules.SquareToAlgebraic(mv.from);
                string t = ChessRules.SquareToAlgebraic(mv.to);
                var item = new ListViewItem($"{f}-{t}");
                item.SubItems.Add($"{f}{t}");
                legalMovesList.Items.Add(item);
            }
        }

        private bool CheckAndAnnounceEndGame()
        {
            if (game.Rules.CheckEndGame(out string title, out string detail))
            {
                if (analysis.MoveEvaluations.Count > 0)
                {
                    double avgAcc = analysis.GetAverageAccuracy();
                    engineLogBox.AppendText(analysis.BuildGameAccuracySummaryText(game.Rules.SanMoves, isEnglish) + Environment.NewLine);
                    detail = $"{detail}\n\nPrecisão Média das Brancas: {avgAcc:0.1}%";
                }
                EndCurrentGame($"🏁 {detail.Replace("\n\n", " | ")}");
                MessageBox.Show(detail, $"🏁 {title}", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            return false;
        }

        private async void RunSafe(Func<Task> action)
        {
            try { await action(); }
            catch (Exception ex) { tutorStatusLabel.Text = $"⚠️ {ex.Message}"; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            game.IncrementGameGeneration();
            game.IncrementHintGeneration();
            clock.Dispose();
            analysis.Dispose();
            boardView.Dispose();
            base.OnFormClosed(e);
        }
    }
}