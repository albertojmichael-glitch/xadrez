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
        private InteractiveTutorial? activeTutorial = null;

        private MenuStrip menuStrip = null!;
        private Panel toolPanel = null!;
        private ListView moveHistoryList = null!;
        private ListView legalMovesList = null!;
        private TextBox engineLogBox = null!;
        private TextBox nodesTextBox = null!;
        private Label lblNodes = null!;
        private Label lblDiff = null!;
        private Label lblClock = null!;
        private Label tutorStatusLabel = null!;
        private Label openingBookLabel = null!;
        private Label whiteClockLabel = null!;
        private Label blackClockLabel = null!;

        private Button btnReset = null!;
        private Button btnResign = null!;
        private Button btnDraw = null!;
        private Button btnTakeback = null!;
        private Button btnHint = null!;
        private Button btnTeacher = null!;
        private Button btnPuzzles = null!;
        private Button btnPgn = null!;
        private Button btnZoomOut = null!;
        private Button btnZoomIn = null!;
        private Button btnStartSearch = null!;
        private Button btnAbortSearch = null!;

        private ComboBox difficultyCombo = null!;
        private ComboBox themeCombo = null!;
        private ComboBox clockCombo = null!;

        public MainForm()
        {
            Text = "MainForm - Lucas Chess & Classic Study Suite";
            ClientSize = new Size(960, 760);
            MinimumSize = new Size(960, 760);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(236, 233, 216);
            Font = new Font("Tahoma", 8.25f, FontStyle.Regular);

            boardView = new BoardView(game.Rules)
            {
                Location = new Point(12, 102)
            };
            boardView.SquareLeftClicked += OnBoardSquareLeftClicked;
            boardView.ScreenReaderAnnouncement += msg => tutorStatusLabel.Text = $"🔊 {msg}";

            clock = new ClockController(
                () => game.Rules.WhiteTurn,
                () => game.GameOver,
                () => game.ActivePuzzle != null || activeTutorial != null);
            clock.ClockUpdated += UpdateClockLabels;
            clock.TimeExpired += OnClockTimeExpired;

            InitClassicUI();
            Resize += (s, e) => ApplyResponsiveLayout();
            ResetBoard();
        }

        private void InitClassicUI()
        {
            menuStrip = new MenuStrip
            {
                BackColor = Color.FromArgb(236, 233, 216),
                RenderMode = ToolStripRenderMode.System
            };

            var fileMenu = new ToolStripMenuItem("📁 File (PGN / FEN)");
            fileMenu.DropDownItems.Add("🔄 Novo Jogo Clássico / New Game", null, (s, e) => ResetBoard());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("📋 Copiar FEN para Área de Transferência", null, (s, e) => CopyFen());
            fileMenu.DropDownItems.Add("📥 Carregar Posição FEN...", null, (s, e) => PromptLoadFen());
            fileMenu.DropDownItems.Add("💾 Exportar / Salvar Partida PGN...", null, (s, e) => ExportPgn());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("🚪 Sair / Exit", null, (s, e) => Close());

            var puzzlesMenu = new ToolStripMenuItem("🧩 Puzzles Táticos (15 Desafios)");
            for (int i = 0; i < StudyLibrary.Puzzles.Count; i++)
            {
                var pz = StudyLibrary.Puzzles[i];
                puzzlesMenu.DropDownItems.Add(pz.Title, null, (s, e) => StartPuzzle(pz));
            }
            puzzlesMenu.DropDownItems.Add(new ToolStripSeparator());
            puzzlesMenu.DropDownItems.Add("🔙 Sair do Modo Puzzle (Partida Normal)", null, (s, e) => ResetBoard());

            var tutorialsMenu = new ToolStripMenuItem("📚 Mini-Tutoriais & Lições");
            foreach (var tut in StudyLibrary.MiniTutorials)
            {
                var capturedTut = tut;
                tutorialsMenu.DropDownItems.Add($"📖 {capturedTut.Title}", null, (s, e) => StartInteractiveTutorial(capturedTut));
            }
            tutorialsMenu.DropDownItems.Add(new ToolStripSeparator());
            tutorialsMenu.DropDownItems.Add("🎓 Alternar Modo Professor (Melhor Lance + Alternativo + Antes/Depois)", null, (s, e) => ToggleTeacherMode());
            tutorialsMenu.DropDownItems.Add("💡 Ver Lição Estratégica da Abertura Atual", null, (s, e) => ShowCurrentOpeningLesson());

            var viewMenu = new ToolStripMenuItem("🎨 View & Acessibilidade");
            viewMenu.DropDownItems.Add("🪵 Tabuleiro: Madeira", null, (s, e) => SetTheme("Madeira"));
            viewMenu.DropDownItems.Add("🏁 Tabuleiro: Preto e Branco", null, (s, e) => SetTheme("Preto e Branco"));
            viewMenu.DropDownItems.Add("🌿 Tabuleiro: Branco e Verde", null, (s, e) => SetTheme("Branco e Verde"));
            viewMenu.DropDownItems.Add("👁️ Tabuleiro: Alto Contraste", null, (s, e) => SetTheme("Alto Contraste"));
            viewMenu.DropDownItems.Add(new ToolStripSeparator());
            viewMenu.DropDownItems.Add("🌙 Alternar Tema Claro / Escuro (Dark Mode)", null, (s, e) => ToggleDarkMode());
            viewMenu.DropDownItems.Add("🔲 Recolorir Peças em Alto Contraste", null, (s, e) => ToggleHighContrastPieces());
            viewMenu.DropDownItems.Add("🌐 Alternar Idioma: Português / English", null, (s, e) => ToggleLanguage());
            viewMenu.DropDownItems.Add(new ToolStripSeparator());
            viewMenu.DropDownItems.Add("🔍+ Aumentar Zoom do Tabuleiro (Ctrl++)", null, (s, e) => boardView.AdjustZoom(0.10f));
            viewMenu.DropDownItems.Add("🔍- Diminuir Zoom do Tabuleiro (Ctrl+-)", null, (s, e) => boardView.AdjustZoom(-0.10f));
            viewMenu.DropDownItems.Add("🔍 100% Resetar Zoom (Ctrl+0)", null, (s, e) => boardView.SetZoom(1.0f));

            var searchMenu = new ToolStripMenuItem("🔍 Search & Estudo (MultiPV)");
            searchMenu.DropDownItems.Add("🧠 Analisar Posição + Melhores 3 Lances (MultiPV)", null, (s, e) => RunSafe(() => RunAnalysisAndHintAsync(false)));
            searchMenu.DropDownItems.Add("💡 Ajuda de Lance (Seta 1º e 2º Lance)", null, (s, e) => RunSafe(() => RunAnalysisAndHintAsync(true)));
            searchMenu.DropDownItems.Add(new ToolStripSeparator());
            searchMenu.DropDownItems.Add("📊 Relatório de Precisão da Partida (IA)", null, (s, e) => ShowGameAnalysisReport());

            menuStrip.Items.Add(fileMenu);
            menuStrip.Items.Add(puzzlesMenu);
            menuStrip.Items.Add(tutorialsMenu);
            menuStrip.Items.Add(searchMenu);
            menuStrip.Items.Add(viewMenu);
            MainMenuStrip = menuStrip;
            Controls.Add(menuStrip);

            toolPanel = new Panel
            {
                Location = new Point(0, 24),
                Size = new Size(960, 48),
                BackColor = Color.FromArgb(245, 244, 238),
                BorderStyle = BorderStyle.Fixed3D
            };

            Button AddToolbarBtn(string text, int x, int width, EventHandler onClick)
            {
                var btn = new Button
                {
                    Text = text,
                    Location = new Point(x, 6),
                    Size = new Size(width, 32),
                    FlatStyle = FlatStyle.System,
                    Font = new Font("Tahoma", 7.8f, FontStyle.Bold),
                    Cursor = Cursors.Hand,
                    AccessibleName = text
                };
                btn.Click += onClick;
                toolPanel.Controls.Add(btn);
                return btn;
            }

            btnReset = AddToolbarBtn("🔄 Reiniciar", 4, 68, (s, e) => ResetBoard());
            btnResign = AddToolbarBtn("🏳️ Desistir", 74, 64, (s, e) => ResignGame());
            btnDraw = AddToolbarBtn("🤝 Empate", 140, 62, (s, e) => OfferDraw());
            btnTakeback = AddToolbarBtn("↩️ Voltar", 204, 64, (s, e) => TakebackMove());
            btnHint = AddToolbarBtn("💡 Dica IA", 270, 68, (s, e) => RunSafe(() => RunAnalysisAndHintAsync(true)));
            btnTeacher = AddToolbarBtn("🎓 Prof: OFF", 340, 76, (s, e) => ToggleTeacherMode());
            btnPuzzles = AddToolbarBtn("🧩 Puzzles", 418, 66, (s, e) =>
            {
                if (game.ActivePuzzle != null)
                    game.CurrentPuzzleIndex = (game.CurrentPuzzleIndex + 1) % StudyLibrary.Puzzles.Count;
                StartPuzzle(StudyLibrary.Puzzles[game.CurrentPuzzleIndex]);
            });
            btnPgn = AddToolbarBtn("💾 PGN", 486, 52, (s, e) => ExportPgn());
            btnZoomOut = AddToolbarBtn("🔍-", 540, 28, (s, e) => boardView.AdjustZoom(-0.10f));
            btnZoomIn = AddToolbarBtn("🔍+", 570, 28, (s, e) => boardView.AdjustZoom(0.10f));

            lblDiff = new Label { Text = "IA:", Location = new Point(602, 15), AutoSize = true, Font = new Font("Tahoma", 8f, FontStyle.Bold) };
            difficultyCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(622, 11), Width = 96 };
            difficultyCombo.Items.AddRange(new object[] { "Iniciante", "Amador", "Clube", "Mestre" });
            difficultyCombo.SelectedIndex = 0;

            lblClock = new Label { Text = "Rel:", Location = new Point(722, 15), AutoSize = true, Font = new Font("Tahoma", 8f, FontStyle.Bold) };
            clockCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(748, 11), Width = 104 };
            clockCombo.Items.AddRange(new object[] { "Fischer 5+3", "Fischer 3+2", "Fischer 10+0", "Bronstein 5+3", "Sem Relógio" });
            clockCombo.SelectedIndex = 0;
            clockCombo.SelectedIndexChanged += (s, e) =>
            {
                bool gameInProgress = game.Rules.SanMoves.Count > 0 && !game.GameOver && game.ActivePuzzle == null && activeTutorial == null;
                clock.ConfigureClockMode(clockCombo.SelectedItem?.ToString() ?? "Fischer 5+3", resetRemainingTime: !gameInProgress);
            };

            themeCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(856, 11), Width = 96 };
            themeCombo.Items.AddRange(new object[] { "Madeira", "Preto e Branco", "Branco e Verde", "Alto Contraste" });
            themeCombo.SelectedIndex = 0;
            themeCombo.SelectedIndexChanged += (s, e) => SetTheme(themeCombo.SelectedItem?.ToString() ?? "Madeira");

            toolPanel.Controls.Add(lblDiff);
            toolPanel.Controls.Add(difficultyCombo);
            toolPanel.Controls.Add(lblClock);
            toolPanel.Controls.Add(clockCombo);
            toolPanel.Controls.Add(themeCombo);
            Controls.Add(toolPanel);

            openingBookLabel = new Label
            {
                Text = "📖 Abertura: Posição Inicial",
                Location = new Point(12, 76),
                Size = new Size(544, 22),
                BorderStyle = BorderStyle.Fixed3D,
                BackColor = Color.FromArgb(255, 252, 230),
                Font = new Font("Tahoma", 8.25f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(openingBookLabel);

            Controls.Add(boardView);

            legalMovesList = new ListView
            {
                Location = new Point(564, 102),
                Size = new Size(125, 480),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BorderStyle = BorderStyle.Fixed3D,
                AccessibleName = "Lista de Lances Legais"
            };
            legalMovesList.Columns.Add("Lance", 58);
            legalMovesList.Columns.Add("UCI", 48);
            Controls.Add(legalMovesList);

            whiteClockLabel = new Label
            {
                Text = "⏱ Brancas: 05:00",
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
                Text = "⏱ Pretas: 05:00",
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

            moveHistoryList = new ListView
            {
                Location = new Point(696, 102),
                Size = new Size(252, 480),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BorderStyle = BorderStyle.Fixed3D,
                AccessibleName = "Histórico de Lances da Partida"
            };
            moveHistoryList.Columns.Add("N.", 28);
            moveHistoryList.Columns.Add("White", 60);
            moveHistoryList.Columns.Add("Black", 52);
            moveHistoryList.Columns.Add("Aval.", 46);
            moveHistoryList.Columns.Add("Análise", 60);
            Controls.Add(moveHistoryList);

            btnStartSearch = new Button
            {
                Text = "▶ Start search",
                Location = new Point(12, 590),
                Size = new Size(95, 25),
                FlatStyle = FlatStyle.System
            };
            btnStartSearch.Click += (s, e) => RunSafe(() => RunAnalysisAndHintAsync(false));

            lblNodes = new Label { Text = "Nodes:", Location = new Point(130, 595), AutoSize = true };
            nodesTextBox = new TextBox
            {
                Text = "0",
                Location = new Point(175, 592),
                Size = new Size(95, 22),
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D
            };

            btnAbortSearch = new Button
            {
                Text = "⏹ Abort search",
                Location = new Point(285, 590),
                Size = new Size(95, 25),
                FlatStyle = FlatStyle.System
            };
            btnAbortSearch.Click += (s, e) => analysis.StopSearch();

            tutorStatusLabel = new Label
            {
                Text = "Tutor ativo | Ative '🎓 Prof' para ver Melhor Lance, Alternativo e Antes/Depois!",
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

            engineLogBox = new TextBox
            {
                Location = new Point(12, 622),
                Size = new Size(936, 126),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.Fixed3D,
                Font = new Font("Consolas", 8.5f),
                AccessibleName = "Log de Análise da Engine e Modo Professor"
            };
            Controls.Add(engineLogBox);
        }

        private void ToggleTeacherMode()
        {
            analysis.IsTeacherMode = !analysis.IsTeacherMode;
            UpdateTeacherButtonText();

            if (analysis.IsTeacherMode)
            {
                tutorStatusLabel.Text = isEnglish
                    ? "🎓 Teacher Mode ON: showing #1 Best (Green), #2 Alternative (Blue) & Before/After Eval!"
                    : "🎓 Modo Professor ATIVO: mostrando 1º Melhor (Verde), 2º Alternativo (Azul) e Antes/Depois!";
                if (!game.GameOver && !game.IsBotThinking && game.Rules.WhiteTurn)
                    RunSafe(() => RunAnalysisAndHintAsync(true));
            }
            else
            {
                boardView.HintArrow = null;
                boardView.AlternativeHintArrow = null;
                boardView.Invalidate();
                tutorStatusLabel.Text = isEnglish ? "🎓 Teacher Mode OFF." : "🎓 Modo Professor desativado.";
            }
        }

        private void UpdateTeacherButtonText()
        {
            if (analysis.IsTeacherMode)
                btnTeacher.Text = isEnglish ? "🎓 Teach: ON" : "🎓 Prof: ON";
            else
                btnTeacher.Text = isEnglish ? "🎓 Teach: OFF" : "🎓 Prof: OFF";
        }

        private void ShowCurrentOpeningLesson()
        {
            string lesson = StudyLibrary.GetOpeningLesson(game.Rules.SanMoves, isEnglish);
            engineLogBox.AppendText($"[📖 Lição de Abertura] {lesson}" + Environment.NewLine);
            MessageBox.Show(
                lesson,
                isEnglish ? "📖 Opening Strategy Lesson" : "📖 Lição Estratégica da Abertura",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void StartInteractiveTutorial(InteractiveTutorial tutorial)
        {
            ResetBoard();
            if (!game.Rules.TryLoadFen(tutorial.Fen, out string error))
            {
                game.Rules.Reset();
                MessageBox.Show($"Erro no FEN do tutorial: {error}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            activeTutorial = tutorial;
            openingBookLabel.Text = $"📚 {(isEnglish ? "MINI-TUTORIAL" : "MINI-TUTORIAL")}: {tutorial.Title}";
            string lessonBody = isEnglish ? tutorial.LessonTextEn : tutorial.LessonTextPt;
            string stepMsg = isEnglish ? tutorial.StepInstructionEn : tutorial.StepInstructionPt;

            engineLogBox.Text = lessonBody + Environment.NewLine;
            tutorStatusLabel.Text = $"📚 {stepMsg}";

            // Destaca com seta verde o lance demonstrativo do tutorial
            if (tutorial.PracticeMoveUci.Length >= 4)
            {
                var from = ChessRules.AlgebraicToSquare(tutorial.PracticeMoveUci[..2]);
                var to = ChessRules.AlgebraicToSquare(tutorial.PracticeMoveUci.Substring(2, 2));
                if (from.HasValue && to.HasValue)
                    boardView.HintArrow = (from.Value, to.Value);
            }

            UpdateClockLabels();
            RefreshLegalMovesTable();
            boardView.Invalidate();

            MessageBox.Show(lessonBody, $"📚 {tutorial.Title}", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ApplyResponsiveLayout()
        {
            if (WindowState == FormWindowState.Minimized || boardView == null) return;

            int w = ClientSize.Width;
            int h = ClientSize.Height;

            toolPanel.Width = w;

            int bottomLogHeight = Math.Max(126, (int)(h * 0.17));
            int logTop = h - bottomLogHeight - 12;
            int statusRowTop = logTop - 32;

            int boardTop = 102;
            int availableBoardHeight = Math.Max(BoardView.TotalBoardSize, statusRowTop - boardTop - 8);
            int rightListsWidth = 125 + 8 + 252;
            int availableBoardWidth = Math.Max(BoardView.TotalBoardSize, w - rightListsWidth - 36);
            int boardBoxSize = Math.Min(availableBoardHeight, availableBoardWidth);

            openingBookLabel.Location = new Point(12, 76);
            openingBookLabel.Size = new Size(boardBoxSize, 22);

            boardView.Location = new Point(12, boardTop);
            boardView.Size = new Size(boardBoxSize, boardBoxSize);

            int legalLeft = 12 + boardBoxSize + 8;
            legalMovesList.Location = new Point(legalLeft, boardTop);
            legalMovesList.Size = new Size(125, boardBoxSize);

            int historyLeft = legalLeft + 125 + 7;
            int historyWidth = Math.Max(252, w - historyLeft - 12);

            int clockWidth = (historyWidth - 4) / 2;
            whiteClockLabel.Location = new Point(historyLeft, 76);
            whiteClockLabel.Size = new Size(clockWidth, 24);
            blackClockLabel.Location = new Point(historyLeft + clockWidth + 4, 76);
            blackClockLabel.Size = new Size(historyWidth - clockWidth - 4, 24);

            moveHistoryList.Location = new Point(historyLeft, boardTop);
            moveHistoryList.Size = new Size(historyWidth, boardBoxSize);

            btnStartSearch.Location = new Point(12, statusRowTop);
            lblNodes.Location = new Point(130, statusRowTop + 5);
            nodesTextBox.Location = new Point(175, statusRowTop + 2);
            btnAbortSearch.Location = new Point(285, statusRowTop);

            tutorStatusLabel.Location = new Point(400, statusRowTop);
            tutorStatusLabel.Size = new Size(Math.Max(300, w - 412), 24);

            engineLogBox.Location = new Point(12, logTop);
            engineLogBox.Size = new Size(w - 24, bottomLogHeight);
        }

        private void ToggleDarkMode()
        {
            isDarkMode = !isDarkMode;
            boardView.IsDarkModeUI = isDarkMode;

            Color bgForm = isDarkMode ? Color.FromArgb(30, 32, 38) : Color.FromArgb(236, 233, 216);
            Color bgToolbar = isDarkMode ? Color.FromArgb(42, 45, 54) : Color.FromArgb(245, 244, 238);
            Color bgBox = isDarkMode ? Color.FromArgb(22, 24, 29) : Color.White;
            Color fgText = isDarkMode ? Color.FromArgb(235, 235, 240) : Color.Black;

            BackColor = bgForm;
            ForeColor = fgText;
            menuStrip.BackColor = bgForm;
            menuStrip.ForeColor = fgText;
            toolPanel.BackColor = bgToolbar;
            toolPanel.ForeColor = fgText;

            boardView.BackColor = isDarkMode ? Color.FromArgb(38, 40, 48) : Color.White;
            openingBookLabel.BackColor = isDarkMode ? Color.FromArgb(48, 52, 64) : Color.FromArgb(255, 252, 230);
            openingBookLabel.ForeColor = fgText;

            tutorStatusLabel.BackColor = isDarkMode ? Color.FromArgb(40, 52, 68) : Color.FromArgb(230, 240, 250);
            tutorStatusLabel.ForeColor = fgText;

            legalMovesList.BackColor = bgBox;
            legalMovesList.ForeColor = fgText;
            moveHistoryList.BackColor = bgBox;
            moveHistoryList.ForeColor = fgText;
            engineLogBox.BackColor = bgBox;
            engineLogBox.ForeColor = fgText;
            nodesTextBox.BackColor = bgBox;
            nodesTextBox.ForeColor = fgText;

            boardView.Invalidate();
        }

        private void ToggleHighContrastPieces()
        {
            boardView.HighContrastPieces = !boardView.HighContrastPieces;
            boardView.Invalidate();
            tutorStatusLabel.Text = boardView.HighContrastPieces
                ? (isEnglish ? "👁️ High-contrast pieces enabled." : "👁️ Peças recoloridas em Alto Contraste ativadas.")
                : (isEnglish ? "👁️ Classic piece sprites restored." : "👁️ Aparência original das peças restaurada.");
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
            UpdateTeacherButtonText();
            lblDiff.Text = isEnglish ? "AI:" : "IA:";
            lblClock.Text = isEnglish ? "Clk:" : "Rel:";

            int selDiff = difficultyCombo.SelectedIndex;
            difficultyCombo.Items.Clear();
            if (isEnglish)
                difficultyCombo.Items.AddRange(new object[] { "Beginner", "Amateur", "Club", "Master" });
            else
                difficultyCombo.Items.AddRange(new object[] { "Iniciante", "Amador", "Clube", "Mestre" });
            difficultyCombo.SelectedIndex = Math.Max(0, selDiff);

            UpdateClockLabels();
            UpdateOpeningBannerText();
            boardView.UpdateAccessibilityInfo(announceToStatus: false);
            tutorStatusLabel.Text = isEnglish
                ? "🌐 Language switched to English | Teacher Mode & Mini-Tutorials ready!"
                : "🌐 Idioma alterado para Português | Modo Professor e Mini-Tutoriais prontos!";
        }

        private void UpdateOpeningBannerText()
        {
            if (activeTutorial != null)
            {
                openingBookLabel.Text = $"📚 MINI-TUTORIAL: {activeTutorial.Title}";
                return;
            }
            if (game.ActivePuzzle != null)
            {
                openingBookLabel.Text = $"🧩 {(isEnglish ? "PUZZLE MODE" : "MODO PUZZLE")}: {game.ActivePuzzle.Title} [{game.ActivePuzzle.Theme}]";
                return;
            }
            string prefix = isEnglish ? "📖 Opening" : "📖 Abertura";
            openingBookLabel.Text = $"{prefix}: {StudyLibrary.DetectOpening(game.Rules.SanMoves)}";
        }

        private void UpdateClockLabels()
        {
            var (w, b) = clock.GetFormattedLabels(isEnglish);
            whiteClockLabel.Text = w;
            blackClockLabel.Text = b;
        }

        private void OnClockTimeExpired(bool whiteExpired)
        {
            if (whiteExpired)
            {
                game.Rules.GameResult = "0-1";
                string status = isEnglish
                    ? "⏰ Time expired! Black wins on time."
                    : "⏰ Tempo esgotado! Vitória das Pretas no relógio.";
                EndCurrentGame(status);
                MessageBox.Show(
                    isEnglish ? "Your time expired! Black wins on time." : "Seu tempo acabou! Vitória das Pretas por tempo.",
                    isEnglish ? "Time Out" : "Relógio Esgotado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            else
            {
                game.Rules.GameResult = "1-0";
                string status = isEnglish
                    ? "⏰ Bot time expired! You win on time."
                    : "⏰ Tempo do bot esgotado! Vitória sua no relógio.";
                EndCurrentGame(status);
                MessageBox.Show(
                    isEnglish ? "Black's time expired! You win on time." : "O tempo das Pretas acabou! Você venceu por tempo.",
                    isEnglish ? "Time Out" : "Relógio Esgotado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void SetTheme(string themeName)
        {
            boardView.SetTheme(themeName);
            if (themeCombo.SelectedItem?.ToString() != themeName)
                themeCombo.SelectedItem = themeName;
        }

        private void ResetGameState()
        {
            activeTutorial = null;
            game.ResetGameState(analysis, clock, boardView, clockCombo.SelectedItem?.ToString() ?? "Fischer 5+3");
            moveHistoryList.Items.Clear();
        }

        private void EndCurrentGame(string statusText)
        {
            game.EndCurrentGame(analysis, clock, boardView);
            tutorStatusLabel.Text = statusText;
        }

        private void ResetBoard()
        {
            ResetGameState();
            game.Rules.Reset();

            openingBookLabel.Text = isEnglish
                ? "📖 Opening: Initial Position (Waiting for 1st move)"
                : "📖 Abertura: Posição Inicial (Aguardando 1º lance)";
            string sfStatus = analysis.IsUsingStockfish
                ? (isEnglish ? "Official Stockfish MultiPV Connected" : "Stockfish Oficial (MultiPV) Conectado")
                : (isEnglish ? "Internal Positional/Endgame Engine Active" : "Engine Tática/Finais Interna (MultiPV) Ativa");
            engineLogBox.Text = $"[{sfStatus}] {(isEnglish ? "Teacher Mode, 4 Mini-Tutorials & 15 Puzzles ready." : "Modo Professor, 4 Mini-Tutoriais e 15 Puzzles prontos.")}" + Environment.NewLine;
            tutorStatusLabel.Text = isEnglish ? "Your turn (White)" : "Sua vez (Brancas)";

            RefreshLegalMovesTable();
            boardView.UpdateAccessibilityInfo(announceToStatus: false);
            boardView.Invalidate();
        }

        private void StartPuzzle(TacticalPuzzle puzzle)
        {
            ResetBoard();
            if (!game.Rules.TryLoadFen(puzzle.Fen, out string error))
            {
                game.Rules.Reset();
                MessageBox.Show($"O puzzle \"{puzzle.Title}\" tem um FEN inválido:\n\n{error}", "Puzzle Inválido", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            int idx = StudyLibrary.Puzzles.IndexOf(puzzle);
            if (idx >= 0)
                game.CurrentPuzzleIndex = idx;

            game.ActivePuzzle = puzzle;
            UpdateOpeningBannerText();
            tutorStatusLabel.Text = $"💡 {(isEnglish ? "Hint" : "Dica")}: {puzzle.Hint}";
            UpdateClockLabels();
            RefreshLegalMovesTable();
            boardView.UpdateAccessibilityInfo(announceToStatus: false);
            boardView.Invalidate();
        }

        private static bool TrySetClipboardText(string text, out string? error)
        {
            error = null;
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void CopyFen()
        {
            string fen = game.Rules.ToFen();
            if (TrySetClipboardText(fen, out string? err))
                MessageBox.Show($"FEN copiado:\n\n{fen}", "📋 FEN Copiado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show($"Não foi possível acessar a área de transferência ({err}).\n\nFEN atual:\n{fen}", "Aviso de Área de Transferência", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void PromptLoadFen()
        {
            using var form = new Form
            {
                Text = isEnglish ? "📥 Load FEN Position" : "📥 Carregar Posição FEN",
                ClientSize = new Size(520, 110),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent
            };
            var txt = new TextBox { Text = game.Rules.ToFen(), Location = new Point(12, 18), Width = 495 };
            var btn = new Button { Text = isEnglish ? "Load FEN" : "Carregar FEN", Location = new Point(400, 55), Size = new Size(105, 32) };
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
                StartGameFromFen(validatedFen);
        }

        private void StartGameFromFen(string fen)
        {
            ResetGameState();
            game.Rules.LoadFen(fen);

            openingBookLabel.Text = isEnglish ? "📖 Opening: Position loaded from FEN" : "📖 Abertura: Posição carregada via FEN";
            engineLogBox.AppendText($"[FEN] Posição carregada: {game.Rules.ToFen()}" + Environment.NewLine);
            UpdateClockLabels();
            RefreshLegalMovesTable();
            boardView.UpdateAccessibilityInfo(announceToStatus: false);
            boardView.Invalidate();

            if (CheckAndAnnounceEndGame()) return;

            if (game.Rules.WhiteTurn)
                tutorStatusLabel.Text = isEnglish ? "Position loaded. Your turn (White)" : "Posição carregada. Sua vez (Brancas)";
            else
                RunSafe(StartBotTurnFromPositionAsync);
        }

        private void ExportPgn()
        {
            string pgn = game.BuildPgn(analysis.IsUsingStockfish);
            bool copied = TrySetClipboardText(pgn, out string? clipErr);

            using var sfd = new SaveFileDialog
            {
                Filter = "Arquivo PGN (*.pgn)|*.pgn|Texto (*.txt)|*.txt",
                FileName = $"partida_{DateTime.Now:yyyyMMdd_HHmm}.pgn"
            };
            if (sfd.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    File.WriteAllText(sfd.FileName, pgn);
                    string msg = copied
                        ? "Arquivo PGN salvo e copiado para a área de transferência!"
                        : $"Arquivo PGN salvo com sucesso! (Aviso: área de transferência ocupada: {clipErr})";
                    MessageBox.Show(msg, "💾 PGN Exportado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Não foi possível salvar o arquivo PGN:\n\n{ex.Message}", "Erro ao Salvar PGN", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (copied)
            {
                MessageBox.Show($"PGN copiado para sua área de transferência:\n\n{pgn}", "💾 PGN Copiado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show($"Não foi possível copiar para a área de transferência ({clipErr}).\n\nPGN:\n{pgn}", "PGN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void TakebackMove()
        {
            if (!game.TryTakeback(analysis, clock, boardView)) return;

            boardView.LastMove = null;
            boardView.LastPromotionSquare = null;

            if (moveHistoryList.Items.Count > 0)
                moveHistoryList.Items.RemoveAt(moveHistoryList.Items.Count - 1);

            UpdateOpeningBannerText();
            tutorStatusLabel.Text = isEnglish ? "↩️ Move undone (Takeback). Your turn!" : "↩️ Lance desfeito (Takeback). Sua vez!";
            RefreshLegalMovesTable();
            boardView.UpdateAccessibilityInfo(announceToStatus: false);
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
            if (game.EvaluateDrawOffer())
            {
                string msg = isEnglish ? "🤝 Draw by mutual agreement!" : "🤝 Empate de comum acordo!";
                EndCurrentGame(msg);
                MessageBox.Show(
                    isEnglish ? "The bot analyzed the position and ACCEPTED your draw offer!" : "O bot analisou a posição e ACEITOU o seu pedido de empate!",
                    isEnglish ? "Draw Accepted" : "Empate Aceito",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                tutorStatusLabel.Text = isEnglish
                    ? "❌ The bot declined the draw offer (it has the advantage)!"
                    : "❌ O bot recusou o empate (ele está em vantagem)!";
                MessageBox.Show(
                    isEnglish ? "The bot declined the draw because it evaluates the position in its favor." : "O bot recusou o empate porque avalia que está em vantagem.",
                    isEnglish ? "Draw Declined" : "Empate Recusado",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
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

        private void OnBoardSquareLeftClicked(Point clicked)
        {
            if (game.GameOver)
            {
                boardView.Invalidate();
                return;
            }

            int col = clicked.X;
            int row = clicked.Y;

            // Em partidas normais o jogador controla as Brancas; no Modo Puzzle controla o lado da vez (Brancas ou Pretas)
            bool isPuzzleMode = game.ActivePuzzle != null;
            bool playerControlsWhite = isPuzzleMode ? game.Rules.WhiteTurn : true;

            if (!isPuzzleMode && (game.IsBotThinking || !game.Rules.WhiteTurn))
            {
                if (boardView.PremoveSelection.HasValue && boardView.PremoveSelection.Value != clicked)
                {
                    boardView.QueuedPremove = (boardView.PremoveSelection.Value, clicked);
                    boardView.PremoveSelection = null;
                    tutorStatusLabel.Text = isEnglish
                        ? $"⚡ Premove scheduled: {ChessRules.SquareToAlgebraic(boardView.QueuedPremove.Value.from)}->{ChessRules.SquareToAlgebraic(boardView.QueuedPremove.Value.to)}"
                        : $"⚡ Jogada automática (Premove) agendada: {ChessRules.SquareToAlgebraic(boardView.QueuedPremove.Value.from)}->{ChessRules.SquareToAlgebraic(boardView.QueuedPremove.Value.to)}";
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

            if (boardView.SelectedSquare.HasValue && boardView.LegalMoves.Contains(clicked))
            {
                var fromSq = boardView.SelectedSquare.Value;
                RunSafe(() => ExecutePlayerAndBotTurnAsync(fromSq, clicked));
                return;
            }

            char p = game.Rules.Board[row, col];
            if (p != '.' && char.IsUpper(p) == playerControlsWhite)
            {
                boardView.SelectedSquare = clicked;
                boardView.LegalMoves = game.Rules.GetLegalMovesForSquare(col, row, playerControlsWhite);
            }
            else
            {
                if (boardView.SelectedSquare.HasValue)
                {
                    string fromAlg = ChessRules.SquareToAlgebraic(boardView.SelectedSquare.Value);
                    string toAlg = ChessRules.SquareToAlgebraic(clicked);
                    boardView.TriggerInvalidMoveFlash(clicked);
                    tutorStatusLabel.Text = isEnglish
                        ? $"✖ Invalid move ({fromAlg} -> {toAlg})! Choose one of the orange squares."
                        : $"✖ Lance inválido ({fromAlg} -> {toAlg})! Escolha uma das casas laranjas.";
                }
                boardView.SelectedSquare = null;
                boardView.LegalMoves.Clear();
            }

            boardView.Invalidate();
        }

        private async void RunSafe(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                ReportError("Erro inesperado", ex);
            }
        }

        private void ReportError(string context, Exception ex)
        {
            try
            {
                engineLogBox.AppendText($"[ERRO] {context}: {ex.GetType().Name}: {ex.Message}" + Environment.NewLine);
                tutorStatusLabel.Text = $"⚠️ {context}: {ex.Message}";
            }
            catch { /* formulário já descartado */ }
        }

        private async Task ExecutePlayerAndBotTurnAsync(Point from, Point to)
        {
            // Se estiver em um Mini-Tutorial Interativo, valida a prática e continua a partida didática!
            if (activeTutorial != null)
            {
                string playedUci = $"{ChessRules.SquareToAlgebraic(from)}{ChessRules.SquareToAlgebraic(to)}";
                bool validTutorialMove = playedUci == activeTutorial.PracticeMoveUci ||
                                         (activeTutorial.PracticeMoveUci == "e1g1" && playedUci == "e1c1") ||
                                         (activeTutorial.PracticeMoveUci == "e6d6" && playedUci == "e6f6") ||
                                         (activeTutorial.PracticeMoveUci == "f1c4" && playedUci == "f1b5");

                if (!validTutorialMove)
                {
                    boardView.TriggerInvalidMoveFlash(to);
                    tutorStatusLabel.Text = isEnglish ? $"✖ Follow the tutorial step: {activeTutorial.StepInstructionEn}" : $"✖ Siga o passo do tutorial: {activeTutorial.StepInstructionPt}";
                    return;
                }

                string completedTitle = activeTutorial.Title;
                activeTutorial = null; // Concluiu o exercício guiado e segue jogando a posição contra a IA!
                engineLogBox.AppendText($"[✅ Tutorial Concluído] {completedTitle}! Agora continue jogando a posição contra a IA." + Environment.NewLine);
            }

            if (game.ActivePuzzle != null)
            {
                HandlePuzzleMove(from, to);
                return;
            }

            if (game.GameOver || game.IsBotThinking || !game.Rules.WhiteTurn) return;

            int generation = game.GameGeneration;
            var preMoveSnapshot = game.Rules.CreateSnapshot();

            char? promoPiece = null;
            bool isWhitePromotion = game.Rules.Board[from.Y, from.X] == 'P' && to.Y == 0;
            if (isWhitePromotion)
                promoPiece = PromotionDialog.AskPiece(true, this, isEnglish, isDarkMode);

            if (generation != game.GameGeneration || game.GameOver) return;

            game.CancelRunningHint(analysis);
            game.History.Push(preMoveSnapshot);

            int moveNum = game.Rules.FullMoveNumber;
            string whiteSan = game.Rules.ExecuteMove(from, to, promoPiece);
            bool isBookMove = StudyLibrary.IsBookMove(game.Rules.SanMoves);
            clock.ApplyClockIncrement(true);

            boardView.ClearSelectionAndHint();
            boardView.LastMove = (from, to);
            boardView.LastPromotionSquare = isWhitePromotion ? to : null;
            UpdateOpeningBannerText();
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

            await RunBotTurnAsync(generation, rowItem, new PlayerMoveInfo(preMoveSnapshot, from, to, promoPiece, whiteSan, isBookMove));
        }

        private async Task StartBotTurnFromPositionAsync()
        {
            if (game.GameOver || game.IsBotThinking || game.Rules.WhiteTurn) return;

            clock.StartIfEnabled();

            int generation = game.GameGeneration;
            var rowItem = new ListViewItem(game.Rules.FullMoveNumber.ToString()) { UseItemStyleForSubItems = false };
            rowItem.SubItems.Add("...");
            rowItem.SubItems.Add("...");
            rowItem.SubItems.Add("");
            rowItem.SubItems.Add("");
            moveHistoryList.Items.Add(rowItem);
            rowItem.EnsureVisible();

            await RunBotTurnAsync(generation, rowItem, null);
        }

        private async Task RunBotTurnAsync(int generation, ListViewItem rowItem, PlayerMoveInfo? playerMove)
        {
            game.IsBotThinking = true;
            tutorStatusLabel.Text = playerMove != null
                ? (isEnglish ? "🤖 Analyzing your move accuracy and calculating Bot reply..." : "🤖 Analisando a precisão do seu lance e calculando resposta do Bot...")
                : (isEnglish ? "🤖 Bot (Black) calculating move..." : "🤖 Bot (Pretas) calculando o lance...");

            bool botTurnCompleted = false;
            try
            {
                await Task.Delay(120);
                if (generation != game.GameGeneration) return;

                int diffIdx = difficultyCombo.SelectedIndex;
                int moveNumber = int.TryParse(rowItem.Text, out int mn) ? mn : game.Rules.FullMoveNumber;

                var evalSummary = await analysis.EvaluateTurnAndChooseBotMoveAsync(
                    game.Rules,
                    playerMove,
                    moveNumber,
                    diffIdx,
                    () => generation != game.GameGeneration);

                if (evalSummary == null || generation != game.GameGeneration) return;

                if (playerMove != null && evalSummary.BestWhiteBefore != null)
                {
                    if (!string.IsNullOrEmpty(evalSummary.Classification.badge))
                    {
                        rowItem.SubItems[1].Text = $"{playerMove.San} {evalSummary.Classification.badge}";
                        rowItem.SubItems[1].ForeColor = evalSummary.Classification.color;
                    }
                    rowItem.SubItems[4].Text = evalSummary.Classification.description;
                    rowItem.SubItems[4].ForeColor = evalSummary.Classification.color;

                    // Se o Modo Professor estiver ativo, registra o comparativo Antes/Depois e Melhor/Alternativo no painel!
                    if (analysis.IsTeacherMode)
                    {
                        var rulesBefore = new ChessRules();
                        rulesBefore.RestoreSnapshot(playerMove.Before);
                        string teacherBox = AnalysisController.BuildTeacherComparisonBox(
                            rulesBefore, playerMove.From, playerMove.To, playerMove.San, evalSummary, isEnglish);
                        engineLogBox.AppendText(teacherBox + Environment.NewLine);
                    }
                }

                var (botFrom, botTo, botPromo, usedFallback) = game.EnsureValidBotMove(
                    evalSummary.BotAnalysis.BestMove,
                    evalSummary.BotAnalysis.Promotion);

                if (usedFallback)
                    engineLogBox.AppendText("[Aviso] A engine não devolveu um lance legal; usando um lance alternativo." + Environment.NewLine);

                bool isBlackPromo = game.Rules.Board[botFrom.Y, botFrom.X] == 'p' && botTo.Y == 7;
                string blackSan = game.Rules.ExecuteMove(botFrom, botTo, botPromo ?? 'q');
                clock.ApplyClockIncrement(false);

                boardView.LastMove = (botFrom, botTo);
                if (isBlackPromo) boardView.LastPromotionSquare = botTo;

                rowItem.SubItems[2].Text = blackSan;
                rowItem.SubItems[3].Text = evalSummary.EvalAfterAnalysis.EvalText;

                nodesTextBox.Text = (evalSummary.EvalAfterAnalysis.Nodes + evalSummary.BotAnalysis.Nodes).ToString();
                if (!string.IsNullOrWhiteSpace(evalSummary.EvalAfterAnalysis.LogLine))
                    engineLogBox.AppendText(evalSummary.EvalAfterAnalysis.LogLine + Environment.NewLine);

                game.IsBotThinking = false;
                UpdateOpeningBannerText();
                RefreshLegalMovesTable();
                boardView.UpdateAccessibilityInfo(announceToStatus: false);
                boardView.Invalidate();

                if (CheckAndAnnounceEndGame()) return;

                string checkAlert = game.Rules.IsKingInCheck(game.Rules.Board, true)
                    ? (isEnglish ? " | ⚠️ CHECK!" : " | ⚠️ XEQUE!")
                    : "";

                if (playerMove != null && evalSummary.BestWhiteBefore != null)
                {
                    double avgAcc = analysis.GetAverageAccuracy();
                    string teacherCompare = analysis.IsTeacherMode
                        ? $" [{evalSummary.EvalComparisonText} | 1º:{evalSummary.BestAlternativeStr} / 2º:{evalSummary.SecondAlternativeStr}]"
                        : ((!evalSummary.PlayedBest && !playerMove.IsBookMove && evalSummary.BestWhiteBefore.BestMove.HasValue)
                            ? $" | {(isEnglish ? "Best" : "Melhor")}: {evalSummary.BestAlternativeStr}"
                            : "");

                    tutorStatusLabel.Text = $"{evalSummary.Classification.description} {evalSummary.Classification.badge} ({evalSummary.CoachNote}){teacherCompare} | {(isEnglish ? "Acc" : "Precisão")}: {avgAcc:0.0}%{checkAlert}";
                }
                else
                {
                    tutorStatusLabel.Text = $"Bot: {blackSan} | Aval: {evalSummary.EvalAfterAnalysis.EvalText}{checkAlert}";
                }

                botTurnCompleted = true;

                // Se o Modo Professor estiver ativo, já calcula e mostra automaticamente as setas 1º e 2º lance para o novo turno!
                if (analysis.IsTeacherMode && !boardView.QueuedPremove.HasValue && !game.GameOver)
                {
                    _ = RunAnalysisAndHintAsync(true);
                }
            }
            catch (Exception ex)
            {
                if (generation == game.GameGeneration)
                    ReportError("Erro ao calcular o lance do bot (use 'Voltar Lance' ou 'Reiniciar')", ex);
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
                    tutorStatusLabel.Text = isEnglish
                        ? "✖ Premove canceled (illegal move in the new position)."
                        : "✖ Premove cancelado (lance ilegal na nova posição).";
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
                game.CancelRunningHint(analysis);

                var solvedPuzzle = game.ActivePuzzle;
                game.Rules.ExecuteMove(from, to, 'Q');
                if (game.Rules.GameResult == "*")
                    game.Rules.GameResult = "1-0";

                boardView.ClearSelectionAndHint();
                boardView.LastMove = (from, to);
                game.GameOver = true;
                RefreshLegalMovesTable();
                boardView.Invalidate();
                tutorStatusLabel.Text = $"🏆 PARABÉNS! Você acertou o {solvedPuzzle.Title}!";

                int currentIdx = StudyLibrary.Puzzles.IndexOf(solvedPuzzle);
                var nextPuzzle = StudyLibrary.Puzzles[(currentIdx + 1) % StudyLibrary.Puzzles.Count];
                var ans = MessageBox.Show(
                    $"Excelente visão tática! Lance correto ({playedUci}).\n\nDeseja ir para o próximo desafio ({nextPuzzle.Title})?",
                    "🏆 Puzzle Resolvido!",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (ans == DialogResult.Yes && game.ActivePuzzle == solvedPuzzle)
                    StartPuzzle(nextPuzzle);
            }
            else
            {
                boardView.TriggerInvalidMoveFlash(to);
                tutorStatusLabel.Text = $"✖ Lance incorreto ({playedUci}). Dica: {game.ActivePuzzle.Hint}";
            }
        }

        private async Task RunAnalysisAndHintAsync(bool showHintArrow)
        {
            if (game.GameOver || game.IsBotThinking || game.IsHintRunning) return;

            int generation = game.GameGeneration;
            int myHintGen = game.IncrementHintGeneration();
            int moveCountAtStart = game.Rules.SanMoves.Count;
            string fenAtStart = game.Rules.ToFen();
            var analysisRules = game.Rules.Clone();
            bool sideToMove = game.Rules.WhiteTurn;

            game.IsHintRunning = true;
            tutorStatusLabel.Text = isEnglish
                ? "🔎 Calculating MultiPV (Top 3 Moves & Tactical Why)..."
                : "🔎 Calculando MultiPV (Melhores 3 Lances, Alternativo e Por Quê)...";

            EngineAnalysisResult res;
            try
            {
                res = await analysis.AnalyzePositionOrHintAsync(analysisRules, sideToMove);
            }
            finally
            {
                if (myHintGen == game.HintGeneration)
                    game.IsHintRunning = false;
            }

            if (generation != game.GameGeneration ||
                myHintGen != game.HintGeneration ||
                game.GameOver ||
                game.IsBotThinking ||
                game.Rules.SanMoves.Count != moveCountAtStart ||
                game.Rules.ToFen() != fenAtStart)
            {
                return;
            }

            if (res.BestMove.HasValue)
            {
                var mv = res.BestMove.Value;
                bool moveStillLegal = game.Rules.GetLegalMovesForSquare(mv.from.X, mv.from.Y, game.Rules.WhiteTurn).Contains(mv.to);
                if (!moveStillLegal) return;

                if (showHintArrow)
                {
                    boardView.HintArrow = mv;
                    // No Modo Professor (ou na Dica), exibe também a seta do 2º lance alternativo se for legal!
                    if (res.SecondBestMove.HasValue &&
                        game.Rules.GetLegalMovesForSquare(res.SecondBestMove.Value.from.X, res.SecondBestMove.Value.from.Y, game.Rules.WhiteTurn)
                            .Contains(res.SecondBestMove.Value.to))
                    {
                        boardView.AlternativeHintArrow = res.SecondBestMove.Value;
                    }
                    else
                    {
                        boardView.AlternativeHintArrow = null;
                    }
                    boardView.Invalidate();
                }

                nodesTextBox.Text = res.Nodes.ToString();
                if (!string.IsNullOrWhiteSpace(res.LogLine))
                    engineLogBox.AppendText(res.LogLine + Environment.NewLine);

                if (!showHintArrow || analysis.IsTeacherMode)
                {
                    string report = analysis.BuildPositionReport(analysisRules, res);
                    engineLogBox.AppendText(report + Environment.NewLine);
                }

                string whyBest = EngineService.ExplainWhyMoveIsRecommended(analysisRules, mv.from, mv.to, res.Promotion, isEnglish);
                string top3 = res.TopMovesSummary.Count > 0 ? string.Join(" | ", res.TopMovesSummary) : "";
                tutorStatusLabel.Text = $"💡 Top 3: {top3} — {(isEnglish ? "Why #1" : "Por quê 1º")}: {whyBest}";
            }
        }

        private void ShowGameAnalysisReport()
        {
            string summary = analysis.BuildGameAccuracySummaryText(game.Rules.SanMoves, isEnglish);
            engineLogBox.AppendText(summary + Environment.NewLine);
            MessageBox.Show(
                summary,
                isEnglish ? "📊 AI Game Accuracy Report" : "📊 Relatório de Precisão da Partida (IA)",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private bool CheckAndAnnounceEndGame()
        {
            if (game.Rules.CheckEndGame(out string title, out string detail))
            {
                if (analysis.MoveEvaluations.Count > 0)
                {
                    double avgAcc = analysis.GetAverageAccuracy();
                    engineLogBox.AppendText(analysis.BuildGameAccuracySummaryText(game.Rules.SanMoves, isEnglish) + Environment.NewLine);
                    detail = $"{detail}\n\n{(isEnglish ? "White Accuracy" : "Precisão das Brancas na partida")}: {avgAcc:0.1}%";
                }
                EndCurrentGame($"🏁 {detail.Replace("\n\n", " | ")}");
                MessageBox.Show(detail, $"🏁 {title}", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            return false;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            game.InvalidateGenerations();
            clock.Dispose();
            analysis.Dispose();
            boardView.Dispose();
            base.OnFormClosed(e);
        }
    }
}