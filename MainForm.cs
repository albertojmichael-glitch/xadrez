using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace XadrezApp
{
    public class MainForm : Form
    {
        private int squareSize = 62;
        private int boardOriginX = 8;
        private int boardOriginY = 8;
        private const int CoordMargin = 6;

        private readonly ChessRules rules = new ChessRules();
        private readonly ChessRules reviewRules = new ChessRules();
        private readonly EngineService engine = new EngineService();
        private readonly Stack<GameSnapshot> history = new Stack<GameSnapshot>();

        // Histórico linear de snapshots e avaliações por meio-lance (ply) para revisão, barra e gráfico
        private readonly List<GameSnapshot> plySnapshots = new List<GameSnapshot>();
        private readonly List<double> plyEvaluations = new List<double>();
        private int? reviewPlyIndex = null; // null = posição ao vivo

        private readonly UserSettings userSettings;
        private bool playerIsWhite = true;
        private bool isBoardFlipped = false;

        private bool isBotThinking = false;
        private bool isHintRunning = false;
        private bool gameOver = false;
        private bool suppressComboEvents = false;

        private CancellationTokenSource? gameSessionCts;
        private CancellationTokenSource? hintSessionCts;

        // Cache de pré-análise em segundo plano enquanto o jogador pensa na sua vez
        private string? preAnalysisFen = null;
        private Task<EngineAnalysisResult>? preAnalysisTask = null;
        private double currentDisplayedEvalWhite = 0.0;
        private string currentDisplayedEvalText = "+0.00";

        private int gameGeneration = 0;
        private int hintGeneration = 0;

        private TacticalPuzzle? activePuzzle = null;
        private int currentPuzzleIndex = 0;

        private System.Windows.Forms.Timer clockTimer = null!;
        private readonly Stopwatch clockStopwatch = new Stopwatch();
        private bool clockRunning = false;
        private bool isPromotionDialogOpen = false;
        private string timeControlMode = "Fischer 5+3";
        private double whiteSecondsLeft = 300;
        private double blackSecondsLeft = 300;
        private double incrementSeconds = 3;
        private bool isBronsteinMode = false;
        private double turnElapsedSeconds = 0;

        private sealed record MoveEvaluationRecord(
            int MoveNumber,
            bool IsWhiteMove,
            int PlyIndex,
            string San,
            string Badge,
            string Description,
            double EvalBeforeWhite,
            double EvalAfterWhite,
            double Accuracy,
            string BestAlternative,
            string CoachNote);

        private readonly List<MoveEvaluationRecord> moveEvaluations = new List<MoveEvaluationRecord>();

        private Point? selectedSquare = null;
        private List<Point> legalMoves = new List<Point>();
        private Point? premoveSelection = null;
        private (Point from, Point to)? queuedPremove = null;
        private readonly HashSet<Point> markedSquares = new HashSet<Point>();
        private readonly List<(Point from, Point to)> drawnArrows = new List<(Point from, Point to)>();
        private Point? rightDragStart = null;
        private (Point from, Point to)? hintArrow = null;

        // Estado de arrastar e soltar (Drag & Drop)
        private Point? dragOriginSquare = null;
        private Point? dragHoverSquare = null;
        private Point dragStartScreenPt;
        private Point dragCurrentScreenPt;
        private bool isDraggingPiece = false;

        // Estado de animação de deslize do lance (~120 ms) e efeitos sonoros
        private System.Windows.Forms.Timer animTimer = null!;
        private readonly Stopwatch animStopwatch = new Stopwatch();
        private Point? animFromSquare = null;
        private Point? animToSquare = null;
        private char animPieceChar = '.';
        private const double MoveAnimationDurationMs = 125.0;
        private bool soundEnabled = true;
        private string botDelayMode = "Variável (0 a 1s)";
        private ToolStripMenuItem soundMenuItem = null!;
        private readonly List<ToolStripMenuItem> botDelayMenuItems = new List<ToolStripMenuItem>();
        private ToolTip mainToolTip = null!;

        // Estado do seletor de promoção estilo Lichess diretamente sobre a casa de destino
        private Point? activePromotionTarget = null;
        private bool activePromotionIsWhite = true;
        private int promotionHoverIndex = -1;
        private TaskCompletionSource<char?>? promotionTcs = null;
        private static readonly (char code, string keyHint, string namePt)[] PromotionChoices =
        {
            ('Q', "Q", "Dama"),
            ('R', "R", "Torre"),
            ('B', "B", "Bispo"),
            ('N', "N", "Cavalo")
        };

        private string currentTheme = "Madeira";

        // Controles de Interface
        private Button btnNewGame = null!;
        private Button btnTakeback = null!;
        private Button btnHint = null!;
        private Button btnFlip = null!;
        private Button btnDraw = null!;
        private Button btnResign = null!;

        private DoubleBufferedPanel boardPanel = null!;
        private DoubleBufferedPanel evalBarPanel = null!;
        private DoubleBufferedPanel materialPanel = null!;
        private DoubleBufferedPanel advantageGraphPanel = null!;
        private ListView moveHistoryList = null!;
        private TextBox engineLogBox = null!;
        private TextBox nodesTextBox = null!;
        private Panel statusBannerPanel = null!;
        private Label tutorStatusLabel = null!;
        private Button bannerActionButton = null!;
        private Action? bannerActionCallback = null;
        private Label openingBookLabel = null!;
        private Label thinkingStatusLabel = null!;
        private Label reviewStatusLabel = null!;
        private Label whiteClockLabel = null!;
        private Label blackClockLabel = null!;
        private ComboBox colorCombo = null!;
        private ComboBox difficultyCombo = null!;
        private ComboBox themeCombo = null!;
        private ComboBox clockCombo = null!;

        private readonly Dictionary<string, Image> pieceImages = new Dictionary<string, Image>();
        private readonly Dictionary<string, Bitmap> scaledPieceCache = new Dictionary<string, Bitmap>();
        private int cachedScaledSize = -1;

        private ChessRules DisplayedRules =>
            reviewPlyIndex.HasValue && reviewPlyIndex.Value >= 0 && reviewPlyIndex.Value < plySnapshots.Count
                ? reviewRules
                : rules;

        private bool IsReviewingHistory =>
            reviewPlyIndex.HasValue && reviewPlyIndex.Value < plySnapshots.Count - 1;

        public MainForm()
        {
            userSettings = UserSettings.Load();
            playerIsWhite = userSettings.PlayAsWhite;
            isBoardFlipped = userSettings.BoardFlipped;
            soundEnabled = userSettings.SoundEnabled;
            botDelayMode = string.IsNullOrWhiteSpace(userSettings.BotDelayMode) ? "Variável (0 a 1s)" : userSettings.BotDelayMode;
            currentTheme = string.IsNullOrWhiteSpace(userSettings.Theme) ? "Madeira" : userSettings.Theme;
            timeControlMode = string.IsNullOrWhiteSpace(userSettings.ClockMode) ? "Fischer 5+3" : userSettings.ClockMode;

            Text = "Xadrez Clássico — Estudo, Tática e Análise IA";
            ClientSize = new Size(1120, 780);
            MinimumSize = new Size(960, 700);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(236, 233, 216);
            Font = new Font("Segoe UI", 8.75f, FontStyle.Regular);
            KeyPreview = true;

            InitResponsiveUI();
            InitDigitalClock();
            InitMoveAnimationTimer();
            LoadPieceImages();
            ApplyLoadedSettingsToCombos();
            ResetBoard(confirmIfInProgress: false);
        }

        private void ApplyLoadedSettingsToCombos()
        {
            suppressComboEvents = true;
            try
            {
                colorCombo.SelectedIndex = playerIsWhite ? 0 : 1;
                difficultyCombo.SelectedIndex = Math.Clamp(userSettings.DifficultyIndex, 0, difficultyCombo.Items.Count - 1);

                int clockIdx = clockCombo.Items.IndexOf(timeControlMode);
                clockCombo.SelectedIndex = clockIdx >= 0 ? clockIdx : 0;

                int themeIdx = themeCombo.Items.IndexOf(currentTheme);
                themeCombo.SelectedIndex = themeIdx >= 0 ? themeIdx : 0;

                if (soundMenuItem != null)
                    soundMenuItem.Checked = soundEnabled;

                foreach (var item in botDelayMenuItems)
                    item.Checked = (item.Tag as string) == botDelayMode;
            }
            finally
            {
                suppressComboEvents = false;
            }
        }

        private void SaveCurrentUserSettings()
        {
            if (suppressComboEvents) return;
            userSettings.DifficultyIndex = difficultyCombo.SelectedIndex;
            userSettings.Theme = currentTheme;
            userSettings.ClockMode = timeControlMode;
            userSettings.PlayAsWhite = playerIsWhite;
            userSettings.BoardFlipped = isBoardFlipped;
            userSettings.SoundEnabled = soundEnabled;
            userSettings.BotDelayMode = botDelayMode;
            userSettings.Save();
        }

        private int GetTargetBotMoveDelayMs() => botDelayMode switch
        {
            "Instantâneo (0 ms)" => 0,
            "Rápido (250 ms)" => 250,
            "Médio (500 ms)" => 500,
            "1 Segundo (1000 ms)" => 1000,
            _ => Random.Shared.Next(0, 1001) // Alterna naturalmente entre 0 ms e 1 segundo (1000 ms)
        };

        private void UpdateActionButtonsState()
        {
            if (btnTakeback == null) return;
            bool promoActive = activePromotionTarget.HasValue || isPromotionDialogOpen;

            btnTakeback.Enabled = !isBotThinking && !promoActive && history.Count > 0;
            btnHint.Enabled = !gameOver && !isBotThinking && !isHintRunning && !promoActive;
            btnDraw.Enabled = !gameOver && !isBotThinking && !promoActive && activePuzzle == null && rules.SanMoves.Count > 0;
            btnResign.Enabled = !gameOver && !promoActive && activePuzzle == null && rules.SanMoves.Count > 0;
        }

        private void InitResponsiveUI()
        {
            mainToolTip = new ToolTip
            {
                AutoPopDelay = 9000,
                InitialDelay = 300,
                ReshowDelay = 150,
                ShowAlways = true
            };

            var menuStrip = new MenuStrip
            {
                BackColor = Color.FromArgb(236, 233, 216),
                RenderMode = ToolStripRenderMode.System,
                Dock = DockStyle.Top
            };

            ToolStripMenuItem CreateMenuItem(string text, EventHandler onClick, Keys shortcutKeys = Keys.None)
            {
                var item = new ToolStripMenuItem(text, null, onClick);
                if (shortcutKeys != Keys.None)
                {
                    item.ShortcutKeys = shortcutKeys;
                    item.ShowShortcutKeys = true;
                }
                return item;
            }

            var fileMenu = new ToolStripMenuItem("Arquivo");
            fileMenu.DropDownItems.Add(CreateMenuItem("Novo Jogo Clássico", (s, e) => ResetBoard(confirmIfInProgress: true), Keys.Control | Keys.N));
            fileMenu.DropDownItems.Add(CreateMenuItem("Desfazer Lance (Voltar)", (s, e) => TakebackMove(), Keys.Control | Keys.Z));
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(CreateMenuItem("Jogar de Brancas", (s, e) => SetPlayerColor(true)));
            fileMenu.DropDownItems.Add(CreateMenuItem("Jogar de Pretas", (s, e) => SetPlayerColor(false)));
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(CreateMenuItem("Importar Partida PGN...", (s, e) => PromptImportPgn(), Keys.Control | Keys.O));
            fileMenu.DropDownItems.Add(CreateMenuItem("Exportar / Salvar Partida PGN...", (s, e) => ExportPgn(), Keys.Control | Keys.S));
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(CreateMenuItem("Copiar FEN para Área de Transferência", (s, e) => CopyFen()));
            fileMenu.DropDownItems.Add(CreateMenuItem("Carregar Posição FEN...", (s, e) => PromptLoadFen()));
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(CreateMenuItem("Sair", (s, e) => Close()));

            var puzzlesMenu = new ToolStripMenuItem($"Puzzles Táticos ({StudyLibrary.Puzzles.Count} Desafios)");
            puzzlesMenu.DropDownItems.Add("Próximo Desafio Tático", null, (s, e) =>
            {
                if (activePuzzle != null)
                    currentPuzzleIndex = (currentPuzzleIndex + 1) % StudyLibrary.Puzzles.Count;
                StartPuzzle(StudyLibrary.Puzzles[currentPuzzleIndex]);
            });
            puzzlesMenu.DropDownItems.Add(new ToolStripSeparator());
            for (int i = 0; i < StudyLibrary.Puzzles.Count; i++)
            {
                var pz = StudyLibrary.Puzzles[i];
                puzzlesMenu.DropDownItems.Add(pz.Title, null, (s, e) => StartPuzzle(pz));
            }
            puzzlesMenu.DropDownItems.Add(new ToolStripSeparator());
            puzzlesMenu.DropDownItems.Add("Sair do Modo Puzzle (Partida Normal)", null, (s, e) => ResetBoard(confirmIfInProgress: false));

            var studyMenu = new ToolStripMenuItem("Estudo e Análise");
            studyMenu.DropDownItems.Add(CreateMenuItem("Analisar Posição Atual (IA)", (s, e) => RunSafe(() => RunAnalysisAndHintAsync(false))));
            studyMenu.DropDownItems.Add(CreateMenuItem("Ajuda de Lance / Dica", (s, e) => RunSafe(() => RunAnalysisAndHintAsync(true)), Keys.F1));
            studyMenu.DropDownItems.Add(new ToolStripSeparator());
            studyMenu.DropDownItems.Add(CreateMenuItem("Relatório de Precisão da Partida (IA)", (s, e) => ShowGameAnalysisReport()));

            var viewMenu = new ToolStripMenuItem("Exibir");
            viewMenu.DropDownItems.Add(CreateMenuItem("Virar Tabuleiro", (s, e) => FlipBoard(), Keys.Control | Keys.F));
            soundMenuItem = new ToolStripMenuItem("Ativar Sons dos Lances")
            {
                Checked = soundEnabled,
                CheckOnClick = true
            };
            soundMenuItem.CheckedChanged += (s, e) =>
            {
                if (suppressComboEvents) return;
                soundEnabled = soundMenuItem.Checked;
                SaveCurrentUserSettings();
                SetStatusBanner(soundEnabled ? "Sons dos lances ativados." : "Sons dos lances desativados.", StatusBannerKind.Info);
            };
            viewMenu.DropDownItems.Add(soundMenuItem);

            var delaySubMenu = new ToolStripMenuItem("Delay do Lance da IA (0 ms a 1 s)");
            string[] delayOptions =
            {
                "Variável (0 a 1s)",
                "Instantâneo (0 ms)",
                "Rápido (250 ms)",
                "Médio (500 ms)",
                "1 Segundo (1000 ms)"
            };
            foreach (string opt in delayOptions)
            {
                var delayItem = new ToolStripMenuItem(opt)
                {
                    Tag = opt,
                    Checked = (opt == botDelayMode)
                };
                delayItem.Click += (s, e) =>
                {
                    botDelayMode = opt;
                    foreach (var it in botDelayMenuItems)
                        it.Checked = (it.Tag as string) == botDelayMode;
                    SaveCurrentUserSettings();
                    SetStatusBanner($"Delay do lance da IA ajustado para: {botDelayMode}", StatusBannerKind.Info);
                };
                botDelayMenuItems.Add(delayItem);
                delaySubMenu.DropDownItems.Add(delayItem);
            }
            viewMenu.DropDownItems.Add(delaySubMenu);
            viewMenu.DropDownItems.Add(new ToolStripSeparator());
            viewMenu.DropDownItems.Add("Tema: Madeira", null, (s, e) => SetTheme("Madeira"));
            viewMenu.DropDownItems.Add("Tema: Preto e Branco", null, (s, e) => SetTheme("Preto e Branco"));
            viewMenu.DropDownItems.Add("Tema: Branco e Verde", null, (s, e) => SetTheme("Branco e Verde"));

            var helpMenu = new ToolStripMenuItem("Ajuda");
            helpMenu.DropDownItems.Add(CreateMenuItem("Atalhos e Dicas de Uso...", (s, e) => ShowHelpAndShortcutsDialog()));

            menuStrip.Items.Add(fileMenu);
            menuStrip.Items.Add(puzzlesMenu);
            menuStrip.Items.Add(studyMenu);
            menuStrip.Items.Add(viewMenu);
            menuStrip.Items.Add(helpMenu);
            MainMenuStrip = menuStrip;

            // Barra de ferramentas superior enxuta (apenas as 6 ações essenciais de partida, com AutoSize e WrapContents)
            var toolPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(0, 42),
                Padding = new Padding(8, 5, 8, 5),
                BackColor = Color.FromArgb(245, 244, 238),
                BorderStyle = BorderStyle.FixedSingle,
                WrapContents = true,
                AutoScroll = false
            };

            Button CreateToolbarBtn(string text, int width, string tooltipText, EventHandler onClick, Padding? customMargin = null)
            {
                var btn = new Button
                {
                    Text = text,
                    Size = new Size(width, 30),
                    Margin = customMargin ?? new Padding(3, 0, 3, 0),
                    FlatStyle = FlatStyle.System,
                    Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                btn.Click += onClick;
                mainToolTip.SetToolTip(btn, tooltipText);
                toolPanel.Controls.Add(btn);
                return btn;
            }

            btnNewGame = CreateToolbarBtn("Novo Jogo", 86, "Iniciar uma nova partida (Ctrl+N)", (s, e) => ResetBoard(confirmIfInProgress: true));
            btnTakeback = CreateToolbarBtn("Voltar (Ctrl+Z)", 102, "Desfazer o último par de lances (Ctrl+Z)", (s, e) => TakebackMove());
            btnHint = CreateToolbarBtn("Dica (F1)", 82, "Pedir dica da IA com seta da melhor jogada (F1)", (s, e) => RunSafe(() => RunAnalysisAndHintAsync(true)));
            btnFlip = CreateToolbarBtn("Virar (Ctrl+F)", 94, "Virar a orientação do tabuleiro (Ctrl+F)", (s, e) => FlipBoard());

            // Separador visual e afastamento antes de Empate e Desistir para evitar clique acidental
            var sepPanel = new Panel
            {
                Size = new Size(2, 26),
                Margin = new Padding(22, 2, 18, 2),
                BackColor = Color.FromArgb(195, 190, 178)
            };
            toolPanel.Controls.Add(sepPanel);

            btnDraw = CreateToolbarBtn("Empate", 76, "Propor empate por acordo à IA (pede confirmação)", (s, e) => OfferDraw());
            btnResign = CreateToolbarBtn("Desistir", 80, "Desistir da partida atual (pede confirmação)", (s, e) => ResignGame(), new Padding(14, 0, 3, 0));

            // Layout principal responsivo em 2 colunas (Esquerda: Tabuleiro + Barra + Status/Log; Direita: Configurações + Relógios + Material + Histórico + Gráfico)
            var mainSplit = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(8, 6, 8, 8)
            };
            mainSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 61f));
            mainSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39f));
            mainSplit.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // === COLUNA ESQUERDA ===
            var leftLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(0, 0, 6, 0)
            };
            leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f)); // Abertura + Indicador Pensando
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Barra de Avaliação + Tabuleiro
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f)); // Faixa de Status + Controles de Busca
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110f)); // Log de Análise IA

            // Linha 0: Abertura + Indicador de pensamento
            var topBoardHeader = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            topBoardHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));
            topBoardHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));

            openingBookLabel = new Label
            {
                Text = "Abertura: Posição Inicial",
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(255, 252, 230),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0),
                Margin = new Padding(0, 0, 4, 0)
            };

            thinkingStatusLabel = new Label
            {
                Text = "✔ Pronto",
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(232, 245, 233),
                ForeColor = Color.FromArgb(25, 110, 45),
                Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0)
            };

            topBoardHeader.Controls.Add(openingBookLabel, 0, 0);
            topBoardHeader.Controls.Add(thinkingStatusLabel, 1, 0);
            leftLayout.Controls.Add(topBoardHeader, 0, 0);

            // Linha 1: Barra de Avaliação + Tabuleiro
            var boardAreaLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            boardAreaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32f));
            boardAreaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            evalBarPanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(55, 55, 55),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 4, 0)
            };
            evalBarPanel.Paint += EvalBarPanel_Paint;

            boardPanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(228, 224, 210),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0)
            };
            boardPanel.Paint += BoardPanel_Paint;
            boardPanel.Resize += (s, e) =>
            {
                RecalculateBoardGeometry();
                boardPanel.Invalidate();
                evalBarPanel.Invalidate();
            };
            boardPanel.MouseDown += BoardPanel_MouseDown;
            boardPanel.MouseMove += BoardPanel_MouseMove;
            boardPanel.MouseUp += BoardPanel_MouseUp;

            boardAreaLayout.Controls.Add(evalBarPanel, 0, 0);
            boardAreaLayout.Controls.Add(boardPanel, 1, 0);
            leftLayout.Controls.Add(boardAreaLayout, 0, 1);

            // Linha 2: Controles de Busca + Faixa de Status Não-Intrusiva
            var statusRowLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            statusRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76f)); // Analisar
            statusRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64f)); // Parar
            statusRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38f)); // Nós:
            statusRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78f)); // Nodes box
            statusRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f)); // Faixa de status

            var btnStartSearch = new Button
            {
                Text = "Analisar",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.System,
                Margin = new Padding(0, 2, 4, 2),
                Cursor = Cursors.Hand
            };
            btnStartSearch.Click += (s, e) => RunSafe(() => RunAnalysisAndHintAsync(false));
            mainToolTip.SetToolTip(btnStartSearch, "Analisar profundamente a posição atual com a IA");

            var btnAbortSearch = new Button
            {
                Text = "Parar",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.System,
                Margin = new Padding(0, 2, 4, 2),
                Cursor = Cursors.Hand
            };
            btnAbortSearch.Click += (s, e) => AbortActiveSearches();
            mainToolTip.SetToolTip(btnAbortSearch, "Interromper a busca da IA em andamento");

            var lblNodes = new Label
            {
                Text = "Nós:",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Margin = new Padding(0)
            };

            nodesTextBox = new TextBox
            {
                Text = "0",
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Right,
                Margin = new Padding(2, 5, 6, 5)
            };

            statusBannerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(230, 240, 250),
                Margin = new Padding(0, 1, 0, 1)
            };

            bannerActionButton = new Button
            {
                Text = "Ação",
                Dock = DockStyle.Right,
                Width = 128,
                Visible = false,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 130, 65),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            bannerActionButton.FlatAppearance.BorderSize = 0;
            bannerActionButton.Click += (s, e) => bannerActionCallback?.Invoke();

            tutorStatusLabel = new Label
            {
                Text = "Tutor ativo | Clique em 'Dica (F1)' para ver a seta da melhor jogada!",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 4, 0),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                AutoEllipsis = true
            };

            statusBannerPanel.Controls.Add(tutorStatusLabel);
            statusBannerPanel.Controls.Add(bannerActionButton);

            statusRowLayout.Controls.Add(btnStartSearch, 0, 0);
            statusRowLayout.Controls.Add(btnAbortSearch, 1, 0);
            statusRowLayout.Controls.Add(lblNodes, 2, 0);
            statusRowLayout.Controls.Add(nodesTextBox, 3, 0);
            statusRowLayout.Controls.Add(statusBannerPanel, 4, 0);
            leftLayout.Controls.Add(statusRowLayout, 0, 2);

            // Linha 3: Log da Engine / Relatórios
            engineLogBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 8.5f),
                Margin = new Padding(0)
            };
            leftLayout.Controls.Add(engineLogBox, 0, 3);

            // === COLUNA DIREITA ===
            var rightLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Margin = new Padding(0)
            };
            rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f)); // Painel de Configurações (Cor, IA, Relógio, Tema)
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f)); // Relógios
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f)); // Peças Capturadas e Saldo de Material
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // Lista de Lances (Histórico)
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f)); // Barra de Navegação da Revisão (|< < > >|)
            rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 114f)); // Gráfico de Vantagem

            // Linha 0 Direita: Painel de Configurações (Cor, IA, Relógio, Tema)
            var settingsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                BackColor = Color.FromArgb(245, 244, 238),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(6, 4, 6, 4),
                Margin = new Padding(0, 0, 0, 4)
            };
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62f));
            settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
            settingsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            settingsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            Label CreateSettingLabel(string text) => new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 8f, FontStyle.Bold),
                Margin = new Padding(0, 0, 4, 0)
            };

            colorCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 1, 6, 1)
            };
            colorCombo.Items.AddRange(new object[] { "Brancas", "Pretas" });
            colorCombo.SelectedIndex = 0;
            colorCombo.SelectedIndexChanged += (s, e) =>
            {
                if (suppressComboEvents) return;
                SetPlayerColor(colorCombo.SelectedIndex == 0);
            };

            themeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 1, 0, 1)
            };
            themeCombo.Items.AddRange(new object[] { "Madeira", "Preto e Branco", "Branco e Verde" });
            themeCombo.SelectedIndex = 0;
            themeCombo.SelectedIndexChanged += (s, e) =>
            {
                if (suppressComboEvents) return;
                SetTheme(themeCombo.SelectedItem?.ToString() ?? "Madeira");
            };

            difficultyCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 1, 6, 1)
            };
            difficultyCombo.Items.AddRange(new object[] { "Fácil (Iniciante)", "Médio (Clube)", "Difícil (Forte)", "Mestre (Máximo)" });
            difficultyCombo.SelectedIndex = 0;
            difficultyCombo.SelectedIndexChanged += (s, e) => SaveCurrentUserSettings();

            clockCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 1, 0, 1)
            };
            clockCombo.Items.AddRange(new object[] { "Fischer 5+3", "Fischer 3+2", "Fischer 10+0", "Bronstein 5+3", "Sem Relógio" });
            clockCombo.SelectedIndex = 0;
            clockCombo.SelectedIndexChanged += (s, e) =>
            {
                if (suppressComboEvents) return;
                bool gameInProgress = rules.SanMoves.Count > 0 && !gameOver && activePuzzle == null;
                ConfigureClockMode(clockCombo.SelectedItem?.ToString() ?? "Fischer 5+3", resetRemainingTime: !gameInProgress);
                SaveCurrentUserSettings();
            };

            settingsPanel.Controls.Add(CreateSettingLabel("Cor:"), 0, 0);
            settingsPanel.Controls.Add(colorCombo, 1, 0);
            settingsPanel.Controls.Add(CreateSettingLabel("Tema:"), 2, 0);
            settingsPanel.Controls.Add(themeCombo, 3, 0);
            settingsPanel.Controls.Add(CreateSettingLabel("IA:"), 0, 1);
            settingsPanel.Controls.Add(difficultyCombo, 1, 1);
            settingsPanel.Controls.Add(CreateSettingLabel("Relógio:"), 2, 1);
            settingsPanel.Controls.Add(clockCombo, 3, 1);
            rightLayout.Controls.Add(settingsPanel, 0, 0);

            // Linha 1 Direita: Relógios
            var clocksLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            clocksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            clocksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            whiteClockLabel = new Label
            {
                Text = "▶ Brancas: 05:00",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.White,
                ForeColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10f, FontStyle.Bold),
                Margin = new Padding(0, 0, 3, 0)
            };
            whiteClockLabel.Paint += (s, e) => DrawClockTurnIndicator(e.Graphics, whiteClockLabel, isWhiteClock: true);

            blackClockLabel = new Label
            {
                Text = "  Pretas: 05:00",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(35, 35, 35),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10f, FontStyle.Bold),
                Margin = new Padding(3, 0, 0, 0)
            };
            blackClockLabel.Paint += (s, e) => DrawClockTurnIndicator(e.Graphics, blackClockLabel, isWhiteClock: false);
            clocksLayout.Controls.Add(whiteClockLabel, 0, 0);
            clocksLayout.Controls.Add(blackClockLabel, 1, 0);
            rightLayout.Controls.Add(clocksLayout, 0, 1);

            // Linha 2 Direita: Painel de Peças Capturadas e Saldo de Material
            materialPanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(248, 246, 240),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 0, 4)
            };
            materialPanel.Paint += MaterialPanel_Paint;
            rightLayout.Controls.Add(materialPanel, 0, 2);

            // Linha 3 Direita: Lista de Histórico de Lances Interativa
            moveHistoryList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 0, 4)
            };
            moveHistoryList.Columns.Add("N.", 36);
            moveHistoryList.Columns.Add("Brancas", 76);
            moveHistoryList.Columns.Add("Pretas", 76);
            moveHistoryList.Columns.Add("Aval.", 56);
            moveHistoryList.Columns.Add("Análise", 130);
            moveHistoryList.Resize += (s, e) => AdjustMoveHistoryColumns();
            moveHistoryList.MouseUp += MoveHistoryList_MouseUp;
            rightLayout.Controls.Add(moveHistoryList, 0, 3);

            // Linha 4 Direita: Navegação de Revisão (|<  <  >  >|)
            var reviewNavLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4)
            };
            reviewNavLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
            reviewNavLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
            reviewNavLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
            reviewNavLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42f));
            reviewNavLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            Button CreateNavBtn(string label, string tooltipText, Action onClick)
            {
                var b = new Button
                {
                    Text = label,
                    Dock = DockStyle.Fill,
                    FlatStyle = FlatStyle.System,
                    Font = new Font("Segoe UI", 8.25f, FontStyle.Bold),
                    Margin = new Padding(0, 1, 3, 1),
                    Cursor = Cursors.Hand
                };
                b.Click += (s, e) => onClick();
                mainToolTip.SetToolTip(b, tooltipText);
                return b;
            }

            reviewNavLayout.Controls.Add(CreateNavBtn("|<", "Ir para a posição inicial (Home)", () => JumpToPly(0)), 0, 0);
            reviewNavLayout.Controls.Add(CreateNavBtn("<", "Voltar um meio-lance na revisão (Seta ←)", () => StepReviewPly(-1)), 1, 0);
            reviewNavLayout.Controls.Add(CreateNavBtn(">", "Avançar um meio-lance na revisão (Seta →)", () => StepReviewPly(1)), 2, 0);
            reviewNavLayout.Controls.Add(CreateNavBtn(">|", "Voltar para a posição ao vivo (End)", () => JumpToLivePosition()), 3, 0);

            reviewStatusLabel = new Label
            {
                Text = "Posição: Ao vivo (use ←/→ ou clique num lance)",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(65, 65, 65),
                AutoEllipsis = true,
                Padding = new Padding(4, 0, 0, 0)
            };
            reviewNavLayout.Controls.Add(reviewStatusLabel, 4, 0);
            rightLayout.Controls.Add(reviewNavLayout, 0, 4);

            // Linha 5 Direita: Gráfico de Vantagem Interativo
            advantageGraphPanel = new DoubleBufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(250, 250, 248),
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0),
                Cursor = Cursors.Hand
            };
            advantageGraphPanel.Paint += AdvantageGraphPanel_Paint;
            advantageGraphPanel.MouseDown += AdvantageGraphPanel_MouseDownOrMove;
            advantageGraphPanel.MouseMove += AdvantageGraphPanel_MouseDownOrMove;
            rightLayout.Controls.Add(advantageGraphPanel, 0, 5);

            mainSplit.Controls.Add(leftLayout, 0, 0);
            mainSplit.Controls.Add(rightLayout, 1, 0);

            Controls.Add(mainSplit);
            Controls.Add(toolPanel);
            Controls.Add(menuStrip);

            RecalculateBoardGeometry();
            AdjustMoveHistoryColumns();
            UpdateActionButtonsState();
        }

        private void AdjustMoveHistoryColumns()
        {
            if (moveHistoryList == null || moveHistoryList.Columns.Count < 5) return;
            int fixedWidth = 36 + 76 + 76 + 56 + 22;
            int remaining = Math.Max(110, moveHistoryList.ClientSize.Width - fixedWidth);
            moveHistoryList.Columns[4].Width = remaining;
        }

        private void RecalculateBoardGeometry()
        {
            if (boardPanel == null) return;
            int availW = Math.Max(200, boardPanel.ClientSize.Width - CoordMargin * 2);
            int availH = Math.Max(200, boardPanel.ClientSize.Height - CoordMargin * 2);
            int newSq = Math.Max(28, Math.Min(availW, availH) / 8);

            squareSize = newSq;
            int boardPixels = squareSize * 8;
            boardOriginX = Math.Max(CoordMargin, (boardPanel.ClientSize.Width - boardPixels) / 2);
            boardOriginY = Math.Max(CoordMargin, (boardPanel.ClientSize.Height - boardPixels) / 2);

            EnsureScaledPieceCache();
        }

        private void EnsureScaledPieceCache()
        {
            int targetSize = Math.Max(16, squareSize - Math.Max(6, squareSize / 7) * 2);
            if (targetSize == cachedScaledSize && scaledPieceCache.Count > 0)
                return;

            foreach (var bmp in scaledPieceCache.Values)
            {
                try { bmp.Dispose(); } catch { }
            }
            scaledPieceCache.Clear();
            cachedScaledSize = targetSize;

            foreach (var kv in pieceImages)
            {
                var scaled = new Bitmap(targetSize, targetSize);
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(kv.Value, 0, 0, targetSize, targetSize);
                }
                scaledPieceCache[kv.Key] = scaled;
            }
        }

        private enum StatusBannerKind
        {
            Info,
            Success,
            Warning,
            Error,
            Review
        }

        private void SetStatusBanner(
            string message,
            StatusBannerKind kind = StatusBannerKind.Info,
            string? actionButtonText = null,
            Action? actionCallback = null)
        {
            tutorStatusLabel.Text = message;
            bannerActionCallback = actionCallback;

            if (!string.IsNullOrWhiteSpace(actionButtonText) && actionCallback != null)
            {
                bannerActionButton.Text = actionButtonText;
                bannerActionButton.Visible = true;
            }
            else
            {
                bannerActionButton.Visible = false;
            }

            (Color bg, Color fg) = kind switch
            {
                StatusBannerKind.Success => (Color.FromArgb(220, 245, 225), Color.FromArgb(18, 95, 42)),
                StatusBannerKind.Warning => (Color.FromArgb(255, 243, 205), Color.FromArgb(125, 80, 0)),
                StatusBannerKind.Error => (Color.FromArgb(253, 226, 226), Color.FromArgb(145, 25, 25)),
                StatusBannerKind.Review => (Color.FromArgb(232, 226, 250), Color.FromArgb(65, 35, 130)),
                _ => (Color.FromArgb(230, 240, 250), Color.FromArgb(20, 55, 95))
            };

            statusBannerPanel.BackColor = bg;
            tutorStatusLabel.ForeColor = fg;
        }

        private void UpdateThinkingIndicator(string text, bool isBusy)
        {
            thinkingStatusLabel.Text = text;
            if (isBusy)
            {
                thinkingStatusLabel.BackColor = Color.FromArgb(255, 244, 214);
                thinkingStatusLabel.ForeColor = Color.FromArgb(140, 82, 0);
            }
            else
            {
                thinkingStatusLabel.BackColor = Color.FromArgb(232, 245, 233);
                thinkingStatusLabel.ForeColor = Color.FromArgb(25, 110, 45);
            }
        }

        // === SELETOR DE PROMOÇÃO ESTILO LICHESS SOBRE A CASA DE DESTINO ===
        private static Point GetPromotionOptionBoardSquare(Point targetSquare, int optionIndex)
        {
            int dir = targetSquare.Y == 0 ? 1 : -1;
            return new Point(targetSquare.X, targetSquare.Y + dir * optionIndex);
        }

        private int HitTestPromotionOptionIndex(Point boardSquare)
        {
            if (!activePromotionTarget.HasValue) return -1;
            var target = activePromotionTarget.Value;
            for (int i = 0; i < PromotionChoices.Length; i++)
            {
                if (GetPromotionOptionBoardSquare(target, i) == boardSquare)
                    return i;
            }
            return -1;
        }

        private void CompletePromotionSelection(char? chosenPieceCode)
        {
            var tcs = promotionTcs;
            activePromotionTarget = null;
            promotionHoverIndex = -1;
            promotionTcs = null;
            UpdateActionButtonsState();
            boardPanel.Invalidate();

            if (tcs != null)
            {
                char? finalChar = chosenPieceCode.HasValue
                    ? (activePromotionIsWhite ? char.ToUpper(chosenPieceCode.Value) : char.ToLower(chosenPieceCode.Value))
                    : null;
                tcs.TrySetResult(finalChar);
            }
        }

        private Task<char?> AskPromotionOnBoardAsync(Point toSquare, bool isWhite)
        {
            promotionTcs?.TrySetResult(null);
            activePromotionTarget = toSquare;
            activePromotionIsWhite = isWhite;
            promotionHoverIndex = 0;
            promotionTcs = new TaskCompletionSource<char?>(TaskCreationOptions.RunContinuationsAsynchronously);

            SetStatusBanner(
                "Promoção de peão: clique na peça desejada sobre o tabuleiro (ou pressione Q, R, B, N | Esc cancela).",
                StatusBannerKind.Info);
            UpdateActionButtonsState();
            boardPanel.Invalidate();
            return promotionTcs.Task;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Se o seletor de promoção no tabuleiro estiver aberto, trata Q/R/B/N e Esc imediatamente
            if (activePromotionTarget.HasValue)
            {
                switch (keyData)
                {
                    case Keys.Escape:
                        CompletePromotionSelection(null);
                        return true;
                    case Keys.Q:
                    case Keys.D:
                    case Keys.D1:
                    case Keys.NumPad1:
                        CompletePromotionSelection('Q');
                        return true;
                    case Keys.R:
                    case Keys.T:
                    case Keys.D2:
                    case Keys.NumPad2:
                        CompletePromotionSelection('R');
                        return true;
                    case Keys.B:
                    case Keys.D3:
                    case Keys.NumPad3:
                        CompletePromotionSelection('B');
                        return true;
                    case Keys.N:
                    case Keys.C:
                    case Keys.D4:
                    case Keys.NumPad4:
                        CompletePromotionSelection('N');
                        return true;
                }
            }

            // Evita interceptar setas quando um TextBox ou ComboBox tem foco!
            bool inputControlFocused = ActiveControl is TextBox or ComboBox;

            if (keyData == (Keys.Control | Keys.N))
            {
                ResetBoard(confirmIfInProgress: true);
                return true;
            }
            if (keyData == (Keys.Control | Keys.Z))
            {
                TakebackMove();
                return true;
            }
            if (keyData == (Keys.Control | Keys.F))
            {
                FlipBoard();
                return true;
            }
            if (keyData == (Keys.Control | Keys.O))
            {
                PromptImportPgn();
                return true;
            }
            if (keyData == (Keys.Control | Keys.S))
            {
                ExportPgn();
                return true;
            }
            if (keyData == Keys.F1 || keyData == (Keys.Control | Keys.H))
            {
                RunSafe(() => RunAnalysisAndHintAsync(true));
                return true;
            }

            if (!inputControlFocused)
            {
                if (keyData == Keys.Left)
                {
                    StepReviewPly(-1);
                    return true;
                }
                if (keyData == Keys.Right)
                {
                    StepReviewPly(1);
                    return true;
                }
                if (keyData == Keys.Home)
                {
                    JumpToPly(0);
                    return true;
                }
                if (keyData == Keys.End)
                {
                    JumpToLivePosition();
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void FlipBoard()
        {
            isBoardFlipped = !isBoardFlipped;
            SaveCurrentUserSettings();
            boardPanel.Invalidate();
            evalBarPanel.Invalidate();
            materialPanel.Invalidate();
        }

        private void SetPlayerColor(bool playWhite)
        {
            if (playerIsWhite == playWhite && rules.SanMoves.Count == 0)
                return;

            if (rules.SanMoves.Count > 0 && !gameOver && activePuzzle == null)
            {
                var confirm = MessageBox.Show(
                    "Trocar de cor iniciará uma nova partida. Deseja continuar?",
                    "Trocar Cor do Jogador",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes)
                {
                    suppressComboEvents = true;
                    colorCombo.SelectedIndex = playerIsWhite ? 0 : 1;
                    suppressComboEvents = false;
                    return;
                }
            }

            playerIsWhite = playWhite;
            isBoardFlipped = !playWhite;
            suppressComboEvents = true;
            colorCombo.SelectedIndex = playerIsWhite ? 0 : 1;
            suppressComboEvents = false;

            SaveCurrentUserSettings();
            ResetBoard(confirmIfInProgress: false);
        }

        private void InitDigitalClock()
        {
            clockTimer = new System.Windows.Forms.Timer { Interval = 100 };
            clockTimer.Tick += (s, e) =>
            {
                if (gameOver || !clockRunning || isPromotionDialogOpen || activePromotionTarget.HasValue || timeControlMode == "Sem Relógio" || activePuzzle != null)
                {
                    clockStopwatch.Reset();
                    return;
                }

                if (!clockStopwatch.IsRunning)
                {
                    clockStopwatch.Restart();
                    return;
                }

                double dt = clockStopwatch.Elapsed.TotalSeconds;
                if (dt <= 0) return;
                clockStopwatch.Restart();

                turnElapsedSeconds += dt;
                if (rules.WhiteTurn)
                {
                    whiteSecondsLeft -= dt;
                    if (whiteSecondsLeft <= 0)
                    {
                        whiteSecondsLeft = 0;
                        UpdateClockLabels();
                        rules.GameResult = "0-1";
                        string msg = playerIsWhite
                            ? "Seu tempo acabou! Vitória das Pretas no relógio."
                            : "O tempo das Brancas (IA) acabou! Você venceu no relógio.";
                        EndCurrentGame(msg, StatusBannerKind.Warning);
                        ShowEndGameDialog("Tempo Esgotado!", msg);
                        return;
                    }
                }
                else
                {
                    blackSecondsLeft -= dt;
                    if (blackSecondsLeft <= 0)
                    {
                        blackSecondsLeft = 0;
                        UpdateClockLabels();
                        rules.GameResult = "1-0";
                        string msg = playerIsWhite
                            ? "O tempo das Pretas (IA) acabou! Você venceu no relógio."
                            : "Seu tempo acabou! Vitória das Brancas no relógio.";
                        EndCurrentGame(msg, StatusBannerKind.Warning);
                        ShowEndGameDialog("Tempo Esgotado!", msg);
                        return;
                    }
                }
                UpdateClockLabels();
            };
            clockTimer.Start();
        }

        private void ConfigureClockMode(string mode, bool resetRemainingTime = true)
        {
            timeControlMode = mode;
            isBronsteinMode = mode.StartsWith("Bronstein");

            double initialSeconds = mode switch
            {
                "Fischer 3+2" => 180,
                "Fischer 10+0" => 600,
                _ => 300
            };
            incrementSeconds = mode switch
            {
                "Fischer 3+2" => 2,
                "Fischer 10+0" => 0,
                _ => 3
            };

            if (resetRemainingTime || whiteSecondsLeft <= 0 || blackSecondsLeft <= 0)
            {
                whiteSecondsLeft = initialSeconds;
                blackSecondsLeft = initialSeconds;
                turnElapsedSeconds = 0;
                clockRunning = false;
                clockStopwatch.Reset();
            }
            else if (mode == "Sem Relógio")
            {
                clockStopwatch.Reset();
            }
            else if (clockRunning && !gameOver && activePuzzle == null)
            {
                clockStopwatch.Restart();
            }

            UpdateClockLabels();
        }

        private void ApplyClockIncrement(bool forWhite)
        {
            if (activePuzzle != null) return;

            if (clockRunning && clockStopwatch.IsRunning && timeControlMode != "Sem Relógio")
            {
                double dt = clockStopwatch.Elapsed.TotalSeconds;
                turnElapsedSeconds += dt;
                if (forWhite) whiteSecondsLeft = Math.Max(0, whiteSecondsLeft - dt);
                else blackSecondsLeft = Math.Max(0, blackSecondsLeft - dt);
            }

            clockRunning = true;

            if (timeControlMode == "Sem Relógio")
            {
                turnElapsedSeconds = 0;
                clockStopwatch.Reset();
                UpdateClockLabels();
                return;
            }

            double bonus = isBronsteinMode ? Math.Min(turnElapsedSeconds, incrementSeconds) : incrementSeconds;
            if (forWhite) whiteSecondsLeft += bonus;
            else blackSecondsLeft += bonus;
            turnElapsedSeconds = 0;
            clockStopwatch.Restart();
            UpdateClockLabels();
        }

        private void InitMoveAnimationTimer()
        {
            animTimer = new System.Windows.Forms.Timer { Interval = 15 };
            animTimer.Tick += (s, e) =>
            {
                if (!animFromSquare.HasValue || !animToSquare.HasValue || animStopwatch.Elapsed.TotalMilliseconds >= MoveAnimationDurationMs)
                {
                    StopMoveSlideAnimation();
                    return;
                }
                boardPanel.Invalidate();
            };
        }

        private void StartMoveSlideAnimation(Point from, Point to, char pieceChar)
        {
            if (from == to || pieceChar == '.')
            {
                StopMoveSlideAnimation();
                return;
            }
            animFromSquare = from;
            animToSquare = to;
            animPieceChar = pieceChar;
            animStopwatch.Restart();
            animTimer.Start();
            boardPanel.Invalidate();
        }

        private void StopMoveSlideAnimation()
        {
            bool hadAnim = animFromSquare.HasValue;
            animFromSquare = null;
            animToSquare = null;
            animPieceChar = '.';
            animStopwatch.Reset();
            animTimer?.Stop();
            if (hadAnim && boardPanel != null)
                boardPanel.Invalidate();
        }

        private void PlayMoveSoundForSan(string san)
        {
            if (!soundEnabled || string.IsNullOrEmpty(san)) return;
            if (san.EndsWith('#'))
                ChessSoundEffects.PlayGameEnd();
            else if (san.Contains('+'))
                ChessSoundEffects.PlayCheck();
            else if (san.Contains('x'))
                ChessSoundEffects.PlayCapture();
            else
                ChessSoundEffects.PlayMove();
        }

        private void DrawClockTurnIndicator(Graphics g, Label clockLbl, bool isWhiteClock)
        {
            bool isActiveTurn = !gameOver && activePuzzle == null && (rules.WhiteTurn == isWhiteClock);
            bool isLowTime = timeControlMode != "Sem Relógio" && activePuzzle == null &&
                             (isWhiteClock ? whiteSecondsLeft < 10.0 : blackSecondsLeft < 10.0);

            int w = clockLbl.ClientSize.Width;
            int h = clockLbl.ClientSize.Height;
            if (w <= 4 || h <= 4) return;

            if (isActiveTurn)
            {
                Color barColor = isLowTime
                    ? Color.FromArgb(255, 235, 60)
                    : Color.FromArgb(30, 175, 75);
                using var barBrush = new SolidBrush(barColor);
                g.FillRectangle(barBrush, 0, h - 4, w, 4);

                using var borderPen = new Pen(barColor, 2f);
                g.DrawRectangle(borderPen, 1, 1, w - 2, h - 2);
            }
            else if (isLowTime)
            {
                using var warnPen = new Pen(Color.FromArgb(255, 220, 50), 2f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(warnPen, 1, 1, w - 2, h - 2);
            }
        }

        private void UpdateClockLabels()
        {
            bool whiteTurnActive = !gameOver && (rules.WhiteTurn);
            bool blackTurnActive = !gameOver && (!rules.WhiteTurn);
            string wTurnPrefix = whiteTurnActive ? "▶ " : "  ";
            string bTurnPrefix = blackTurnActive ? "▶ " : "  ";

            if (timeControlMode == "Sem Relógio" || activePuzzle != null)
            {
                whiteClockLabel.Text = $"{wTurnPrefix}Brancas: --:--";
                whiteClockLabel.BackColor = Color.White;
                whiteClockLabel.ForeColor = Color.Black;

                blackClockLabel.Text = $"{bTurnPrefix}Pretas: --:--";
                blackClockLabel.BackColor = Color.FromArgb(35, 35, 35);
                blackClockLabel.ForeColor = Color.White;

                whiteClockLabel.Invalidate();
                blackClockLabel.Invalidate();
                return;
            }

            bool wLow = whiteSecondsLeft < 10.0;
            bool bLow = blackSecondsLeft < 10.0;
            string wWarn = wLow ? "⚠ " : "";
            string bWarn = bLow ? "⚠ " : "";

            whiteClockLabel.Text = $"{wWarn}{wTurnPrefix}Brancas: {FormatClockTime(whiteSecondsLeft)}";
            blackClockLabel.Text = $"{bWarn}{bTurnPrefix}Pretas: {FormatClockTime(blackSecondsLeft)}";

            if (wLow)
            {
                whiteClockLabel.BackColor = Color.FromArgb(205, 30, 30);
                whiteClockLabel.ForeColor = Color.White;
            }
            else
            {
                whiteClockLabel.BackColor = Color.White;
                whiteClockLabel.ForeColor = Color.Black;
            }

            if (bLow)
            {
                blackClockLabel.BackColor = Color.FromArgb(205, 30, 30);
                blackClockLabel.ForeColor = Color.White;
            }
            else
            {
                blackClockLabel.BackColor = Color.FromArgb(35, 35, 35);
                blackClockLabel.ForeColor = Color.White;
            }

            whiteClockLabel.Invalidate();
            blackClockLabel.Invalidate();
        }

        private static string FormatClockTime(double secondsLeft)
        {
            double clamped = Math.Max(0.0, secondsLeft);
            if (clamped < 10.0 && clamped > 0.0)
                return $"00:{clamped:00.0}";
            TimeSpan ts = TimeSpan.FromSeconds(Math.Ceiling(clamped));
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }

        private void SetTheme(string themeName)
        {
            currentTheme = themeName;
            suppressComboEvents = true;
            int idx = themeCombo.Items.IndexOf(themeName);
            if (idx >= 0) themeCombo.SelectedIndex = idx;
            suppressComboEvents = false;

            SaveCurrentUserSettings();
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

            cachedScaledSize = -1;
            EnsureScaledPieceCache();
        }

        private void AbortActiveSearches()
        {
            hintGeneration++;
            try { hintSessionCts?.Cancel(); } catch { }
            engine.StopSearch();
            isHintRunning = false;
            UpdateThinkingIndicator("✔ Busca interrompida", false);
            UpdateActionButtonsState();
            SetStatusBanner("Busca da IA interrompida pelo usuário.", StatusBannerKind.Info);
        }

        private void ResetGameState()
        {
            CompletePromotionSelection(null);
            StopMoveSlideAnimation();
            gameGeneration++;
            hintGeneration++;
            try { gameSessionCts?.Cancel(); } catch { }
            try { hintSessionCts?.Cancel(); } catch { }
            gameSessionCts = new CancellationTokenSource();
            hintSessionCts = new CancellationTokenSource();
            engine.StopSearch();

            activePuzzle = null;
            history.Clear();
            plySnapshots.Clear();
            plyEvaluations.Clear();
            reviewPlyIndex = null;
            moveEvaluations.Clear();
            preAnalysisFen = null;
            preAnalysisTask = null;
            currentDisplayedEvalWhite = 0.0;
            currentDisplayedEvalText = "+0.00";

            isBotThinking = false;
            isHintRunning = false;
            gameOver = false;
            clockRunning = false;
            clockStopwatch.Reset();
            selectedSquare = null;
            legalMoves.Clear();
            premoveSelection = null;
            queuedPremove = null;
            dragOriginSquare = null;
            dragHoverSquare = null;
            isDraggingPiece = false;
            markedSquares.Clear();
            drawnArrows.Clear();
            hintArrow = null;

            ConfigureClockMode(clockCombo.SelectedItem?.ToString() ?? "Fischer 5+3", resetRemainingTime: true);
            moveHistoryList.Items.Clear();
            UpdateThinkingIndicator("✔ Pronto", false);
            UpdateReviewStatusLabel();
            UpdateActionButtonsState();
        }

        private void EndCurrentGame(string statusText, StatusBannerKind bannerKind = StatusBannerKind.Info)
        {
            CompletePromotionSelection(null);
            gameOver = true;
            clockRunning = false;
            clockStopwatch.Reset();
            gameGeneration++;
            hintGeneration++;
            try { gameSessionCts?.Cancel(); } catch { }
            try { hintSessionCts?.Cancel(); } catch { }
            if (isBotThinking || isHintRunning)
            {
                isBotThinking = false;
                isHintRunning = false;
                engine.StopSearch();
            }
            selectedSquare = null;
            legalMoves.Clear();
            premoveSelection = null;
            queuedPremove = null;
            dragOriginSquare = null;
            dragHoverSquare = null;
            isDraggingPiece = false;
            hintArrow = null;
            if (soundEnabled)
                ChessSoundEffects.PlayGameEnd();
            UpdateThinkingIndicator("🏁 Fim de Partida", false);
            UpdateClockLabels();
            UpdateActionButtonsState();
            SetStatusBanner(statusText, bannerKind);
            RefreshAllVisualPanels();
        }

        private void ResetBoard(bool confirmIfInProgress = false)
        {
            if (confirmIfInProgress && rules.SanMoves.Count > 0 && !gameOver && activePuzzle == null)
            {
                var ans = MessageBox.Show(
                    "Há uma partida em andamento. Deseja realmente reiniciar o tabuleiro?",
                    "Confirmar Novo Jogo",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (ans != DialogResult.Yes)
                    return;
            }

            ResetGameState();
            rules.Reset();
            RecordCurrentPlySnapshot(0.0);

            openingBookLabel.Text = "Abertura: Posição Inicial (Aguardando 1º lance)";
            string sfStatus = engine.IsUsingStockfish ? "Stockfish Oficial Conectado" : "Engine Tática Posicional Interna Ativa";
            engineLogBox.Text = $"[{sfStatus}] {StudyLibrary.Puzzles.Count} Puzzles, Livro de Aberturas Expandido, Revisão Interativa e Análise Pós-Lance prontos." + Environment.NewLine;

            UpdateClockLabels();
            UpdateActionButtonsState();
            RefreshAllVisualPanels();

            if (playerIsWhite)
            {
                SetStatusBanner("Sua vez (Brancas) — clique ou arraste uma peça para jogar.", StatusBannerKind.Info);
                StartBackgroundTurnAnalysis();
            }
            else
            {
                SetStatusBanner("Você está jogando de Pretas. Aguardando 1º lance das Brancas (IA)...", StatusBannerKind.Info);
                RunSafe(StartBotTurnFromPositionAsync);
            }
        }

        private void RecordCurrentPlySnapshot(double evalWhitePawns)
        {
            plySnapshots.Add(rules.CreateSnapshot());
            plyEvaluations.Add(Math.Clamp(evalWhitePawns, -15.0, 15.0));
            reviewPlyIndex = null;
            currentDisplayedEvalWhite = evalWhitePawns;
            currentDisplayedEvalText = FormatEvalPawnsText(evalWhitePawns);
            UpdateReviewStatusLabel();
        }

        private static string FormatEvalPawnsText(double evalWhitePawns)
        {
            if (evalWhitePawns >= 90.0) return "#+";
            if (evalWhitePawns <= -90.0) return "#-";
            return (evalWhitePawns >= 0 ? "+" : "") + evalWhitePawns.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private void RefreshAllVisualPanels()
        {
            boardPanel.Invalidate();
            evalBarPanel.Invalidate();
            materialPanel.Invalidate();
            advantageGraphPanel.Invalidate();
        }

        // === REVISÃO INTERATIVA DE LANCES (CLIQUE NA LISTA, SETAS <- / -> E GRÁFICO) ===
        private void StepReviewPly(int delta)
        {
            if (plySnapshots.Count <= 1)
            {
                SetStatusBanner("Ainda não há lances realizados para revisar no histórico.", StatusBannerKind.Info);
                return;
            }
            int current = reviewPlyIndex ?? (plySnapshots.Count - 1);
            int target = Math.Clamp(current + delta, 0, plySnapshots.Count - 1);
            JumpToPly(target);
        }

        private void JumpToLivePosition()
        {
            if (plySnapshots.Count == 0) return;
            JumpToPly(plySnapshots.Count - 1);
        }

        private void JumpToPly(int plyIdx)
        {
            if (plySnapshots.Count == 0) return;
            plyIdx = Math.Clamp(plyIdx, 0, plySnapshots.Count - 1);

            StopMoveSlideAnimation();
            selectedSquare = null;
            legalMoves.Clear();
            dragOriginSquare = null;
            dragHoverSquare = null;
            isDraggingPiece = false;
            hintArrow = null;

            if (plyIdx >= plySnapshots.Count - 1)
            {
                reviewPlyIndex = null;
            }
            else
            {
                reviewPlyIndex = plyIdx;
                reviewRules.RestoreSnapshot(plySnapshots[plyIdx]);
            }

            if (plyIdx < plyEvaluations.Count)
            {
                currentDisplayedEvalWhite = plyEvaluations[plyIdx];
                currentDisplayedEvalText = FormatEvalPawnsText(currentDisplayedEvalWhite);
            }

            if (plyIdx > 0)
            {
                int rowIdx = (plyIdx - 1) / 2;
                if (rowIdx >= 0 && rowIdx < moveHistoryList.Items.Count)
                {
                    moveHistoryList.SelectedIndices.Clear();
                    moveHistoryList.Items[rowIdx].Selected = true;
                    moveHistoryList.Items[rowIdx].EnsureVisible();
                }
            }
            else
            {
                moveHistoryList.SelectedIndices.Clear();
            }

            UpdateReviewStatusLabel();

            if (IsReviewingHistory)
            {
                var snap = plySnapshots[plyIdx];
                string lastSan = snap.SanMoves.Count > 0 ? snap.SanMoves[^1] : "Posição Inicial";
                var evalRec = moveEvaluations.FirstOrDefault(m => m.PlyIndex == plyIdx);
                string note = evalRec != null
                    ? $"Revisando lance #{plyIdx} ({lastSan}): {evalRec.Description} {evalRec.Badge} — {evalRec.CoachNote}"
                    : $"Revisando meio-lance #{plyIdx}/{plySnapshots.Count - 1} ({lastSan})";
                SetStatusBanner(note, StatusBannerKind.Review, "Voltar ao Jogo ➔", JumpToLivePosition);
            }
            else if (!gameOver && !isBotThinking)
            {
                SetStatusBanner($"Posição atual ao vivo. Sua vez ({(playerIsWhite ? "Brancas" : "Pretas")}).", StatusBannerKind.Info);
            }

            RefreshAllVisualPanels();
        }

        private void UpdateReviewStatusLabel()
        {
            if (reviewStatusLabel == null) return;
            int total = Math.Max(0, plySnapshots.Count - 1);
            if (!IsReviewingHistory)
            {
                reviewStatusLabel.Text = $"Ao vivo (lance {total}/{total})";
                reviewStatusLabel.ForeColor = Color.FromArgb(25, 105, 45);
            }
            else
            {
                int cur = reviewPlyIndex ?? 0;
                reviewStatusLabel.Text = $"Revisando {cur}/{total} (clique >| p/ voltar)";
                reviewStatusLabel.ForeColor = Color.FromArgb(115, 40, 145);
            }
        }

        private void MoveHistoryList_MouseUp(object? sender, MouseEventArgs e)
        {
            var hit = moveHistoryList.HitTest(e.Location);
            if (hit.Item == null) return;

            int rowIdx = hit.Item.Index;
            int subIdx = hit.SubItem != null ? hit.Item.SubItems.IndexOf(hit.SubItem) : 1;

            int plyOffset = (subIdx == 2) ? 2 : 1;
            int targetPly = rowIdx * 2 + plyOffset;
            if (targetPly >= plySnapshots.Count)
                targetPly = plySnapshots.Count - 1;

            if (targetPly >= 1)
                JumpToPly(targetPly);
        }

        private void StartPuzzle(TacticalPuzzle puzzle)
        {
            ResetGameState();
            rules.Reset();

            if (!rules.TryLoadFen(puzzle.Fen, out string error))
            {
                rules.Reset();
                SetStatusBanner($"O puzzle \"{puzzle.Title}\" tem um FEN inválido: {error}", StatusBannerKind.Error);
                return;
            }

            int idx = StudyLibrary.Puzzles.IndexOf(puzzle);
            if (idx >= 0)
                currentPuzzleIndex = idx;

            activePuzzle = puzzle;
            playerIsWhite = rules.WhiteTurn;
            isBoardFlipped = !playerIsWhite;
            RecordCurrentPlySnapshot(EngineService.EvaluateBoardWhiteRelative(rules.Board) / 100.0);

            openingBookLabel.Text = $"MODO PUZZLE: {puzzle.Title} [{puzzle.Theme}]";
            SetStatusBanner($"Desafio: {puzzle.Title} | Dica: {puzzle.Hint}", StatusBannerKind.Info);
            UpdateClockLabels();
            UpdateActionButtonsState();
            RefreshAllVisualPanels();
            StartBackgroundTurnAnalysis();
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
            string fen = DisplayedRules.ToFen();
            if (TrySetClipboardText(fen, out string? err))
            {
                engineLogBox.AppendText($"[FEN Copiado] {fen}" + Environment.NewLine);
                SetStatusBanner($"FEN copiado para a área de transferência: {fen}", StatusBannerKind.Success);
            }
            else
            {
                engineLogBox.AppendText($"[FEN] {fen}" + Environment.NewLine);
                SetStatusBanner($"FEN atual registrado no log ({err}): {fen}", StatusBannerKind.Warning);
            }
        }

        private void PromptLoadFen()
        {
            using var form = new Form
            {
                Text = "Carregar Posição FEN",
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(540, 120),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            };
            var lbl = new Label { Text = "Cole a string FEN completa abaixo:", Location = new Point(12, 10), AutoSize = true };
            var txt = new TextBox { Text = DisplayedRules.ToFen(), Location = new Point(12, 32), Width = 515 };
            var btn = new Button { Text = "Carregar FEN", Location = new Point(412, 68), Size = new Size(115, 34), Cursor = Cursors.Hand };
            var btnCancel = new Button { Text = "Cancelar", Location = new Point(290, 68), Size = new Size(115, 34), Cursor = Cursors.Hand, DialogResult = DialogResult.Cancel };
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
                else
                {
                    MessageBox.Show($"FEN inválido!\n\n{error}", "Erro no FEN", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            form.Controls.Add(lbl);
            form.Controls.Add(txt);
            form.Controls.Add(btnCancel);
            form.Controls.Add(btn);
            form.AcceptButton = btn;
            form.CancelButton = btnCancel;

            if (form.ShowDialog(this) == DialogResult.OK && validatedFen != null)
                StartGameFromFen(validatedFen);
        }

        private void StartGameFromFen(string fen)
        {
            ResetGameState();
            rules.LoadFen(fen);
            double initialEval = EngineService.EvaluateBoardWhiteRelative(rules.Board) / 100.0;
            RecordCurrentPlySnapshot(initialEval);

            openingBookLabel.Text = "Abertura: Posição carregada via FEN";
            engineLogBox.AppendText($"[FEN] Posição carregada: {rules.ToFen()}" + Environment.NewLine);
            UpdateClockLabels();
            UpdateActionButtonsState();
            RefreshAllVisualPanels();

            if (CheckAndAnnounceEndGame()) return;

            if (rules.WhiteTurn == playerIsWhite)
            {
                SetStatusBanner($"Posição FEN carregada. Sua vez ({(playerIsWhite ? "Brancas" : "Pretas")}).", StatusBannerKind.Info);
                StartBackgroundTurnAnalysis();
            }
            else
            {
                RunSafe(StartBotTurnFromPositionAsync);
            }
        }

        // === IMPORTAÇÃO DE PGN (ARQUIVO OU TEXTO COLADO) ===
        private void PromptImportPgn()
        {
            using var form = new Form
            {
                Text = "Importar Partida PGN",
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(620, 420),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(242, 240, 232)
            };

            var lbl = new Label
            {
                Text = "Cole o conteúdo PGN abaixo ou clique em 'Abrir Arquivo .PGN...':",
                Location = new Point(14, 12),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            var txtPgn = new TextBox
            {
                Location = new Point(14, 38),
                Size = new Size(592, 320),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f),
                Text = "[Event \"Exemplo\"]\r\n1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 *"
            };

            var btnOpenFile = new Button
            {
                Text = "Abrir Arquivo .PGN...",
                Location = new Point(14, 368),
                Size = new Size(160, 36),
                Cursor = Cursors.Hand
            };
            btnOpenFile.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog
                {
                    Filter = "Arquivo PGN (*.pgn)|*.pgn|Texto (*.txt)|*.txt|Todos os arquivos (*.*)|*.*"
                };
                if (ofd.ShowDialog(form) == DialogResult.OK)
                {
                    try
                    {
                        txtPgn.Text = File.ReadAllText(ofd.FileName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Erro ao ler arquivo:\n{ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };

            var btnImport = new Button
            {
                Text = "Importar e Revisar Partida",
                Location = new Point(406, 368),
                Size = new Size(200, 36),
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            List<GameSnapshot>? importedSnapshots = null;
            Dictionary<string, string>? importedHeaders = null;

            btnImport.Click += (s, e) =>
            {
                var probe = new ChessRules();
                if (probe.TryLoadPgn(txtPgn.Text, out var snaps, out var hdrs, out string err))
                {
                    importedSnapshots = snaps;
                    importedHeaders = hdrs;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                }
                else
                {
                    MessageBox.Show($"Não foi possível importar o PGN:\n\n{err}", "PGN Inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            form.Controls.Add(lbl);
            form.Controls.Add(txtPgn);
            form.Controls.Add(btnOpenFile);
            form.Controls.Add(btnImport);

            if (form.ShowDialog(this) == DialogResult.OK && importedSnapshots != null && importedSnapshots.Count > 0)
            {
                LoadImportedPgnGame(importedSnapshots, importedHeaders ?? new Dictionary<string, string>());
            }
        }

        private void LoadImportedPgnGame(List<GameSnapshot> snapshots, Dictionary<string, string> headers)
        {
            ResetGameState();

            foreach (var snap in snapshots)
            {
                double eval = EngineService.EvaluateBoardWhiteRelative(snap.Board) / 100.0;
                plySnapshots.Add(snap);
                plyEvaluations.Add(Math.Clamp(eval, -15.0, 15.0));
            }

            for (int i = 0; i < snapshots.Count - 1; i++)
            {
                history.Push(snapshots[i]);
            }

            rules.RestoreSnapshot(snapshots[^1]);
            currentDisplayedEvalWhite = plyEvaluations[^1];
            currentDisplayedEvalText = FormatEvalPawnsText(currentDisplayedEvalWhite);

            RebuildMoveHistoryListFromSnapshots(snapshots);

            string whiteName = headers.GetValueOrDefault("White", "Brancas");
            string blackName = headers.GetValueOrDefault("Black", "Pretas");
            string opening = StudyLibrary.DetectOpening(rules.SanMoves);
            openingBookLabel.Text = $"PGN: {whiteName} vs {blackName} | {opening}";
            engineLogBox.AppendText($"[PGN Importado] {rules.SanMoves.Count} meio-lances carregados ({whiteName} vs {blackName})." + Environment.NewLine);

            if (rules.GameResult is "1-0" or "0-1" or "1/2-1/2" || rules.CheckEndGame(out _, out _))
            {
                gameOver = true;
                SetStatusBanner(
                    $"Partida PGN importada ({rules.SanMoves.Count} meio-lances, resultado {rules.GameResult}). Use ← / → ou clique na lista/gráfico para revisar!",
                    StatusBannerKind.Success);
            }
            else
            {
                SetStatusBanner(
                    $"Partida PGN importada ({rules.SanMoves.Count} meio-lances). Você pode revisar com ← / → ou continuar jogando!",
                    StatusBannerKind.Success);
                if (rules.WhiteTurn == playerIsWhite)
                    StartBackgroundTurnAnalysis();
            }

            UpdateActionButtonsState();
            RefreshAllVisualPanels();
        }

        private void RebuildMoveHistoryListFromSnapshots(List<GameSnapshot> snapshots)
        {
            moveHistoryList.BeginUpdate();
            try
            {
                moveHistoryList.Items.Clear();
                ListViewItem? currentRow = null;

                for (int ply = 1; ply < snapshots.Count; ply++)
                {
                    var before = snapshots[ply - 1];
                    var after = snapshots[ply];
                    string san = after.SanMoves.Count > 0 ? after.SanMoves[^1] : "";
                    double evalBefore = plyEvaluations[ply - 1];
                    double evalAfter = plyEvaluations[ply];

                    bool wasWhite = before.WhiteTurn;
                    int moveNum = before.FullMoveNumber;

                    var classif = EngineService.ClassifyMove(
                        evalBefore,
                        evalAfter,
                        playedEngineBestMove: false,
                        isBookMove: StudyLibrary.IsBookMove(after.SanMoves),
                        isSacrificeOrTactical: false,
                        forWhite: wasWhite,
                        mateInMoves: null,
                        isImmediateCheckmate: san.EndsWith('#'));

                    double acc = EngineService.ComputeMoveAccuracy(
                        evalBefore,
                        evalAfter,
                        playedBest: false,
                        isBookMove: StudyLibrary.IsBookMove(after.SanMoves),
                        forWhite: wasWhite);

                    moveEvaluations.Add(new MoveEvaluationRecord(
                        moveNum,
                        wasWhite,
                        ply,
                        san,
                        classif.badge,
                        classif.description,
                        evalBefore,
                        evalAfter,
                        acc,
                        "-",
                        "lance importado de PGN"));

                    if (wasWhite || currentRow == null)
                    {
                        currentRow = new ListViewItem(moveNum.ToString()) { UseItemStyleForSubItems = false };
                        currentRow.SubItems.Add(wasWhite ? $"{san}{(string.IsNullOrEmpty(classif.badge) ? "" : " " + classif.badge)}" : "...");
                        currentRow.SubItems.Add(wasWhite ? "" : $"{san}{(string.IsNullOrEmpty(classif.badge) ? "" : " " + classif.badge)}");
                        currentRow.SubItems.Add(FormatEvalPawnsText(evalAfter));
                        currentRow.SubItems.Add(classif.description);
                        currentRow.SubItems[4].ForeColor = classif.color;
                        if (wasWhite) currentRow.SubItems[1].ForeColor = classif.color;
                        else currentRow.SubItems[2].ForeColor = classif.color;
                        moveHistoryList.Items.Add(currentRow);
                    }
                    else
                    {
                        currentRow.SubItems[2].Text = $"{san}{(string.IsNullOrEmpty(classif.badge) ? "" : " " + classif.badge)}";
                        currentRow.SubItems[2].ForeColor = classif.color;
                        currentRow.SubItems[3].Text = FormatEvalPawnsText(evalAfter);
                        if (string.IsNullOrEmpty(currentRow.SubItems[4].Text))
                        {
                            currentRow.SubItems[4].Text = classif.description;
                            currentRow.SubItems[4].ForeColor = classif.color;
                        }
                        currentRow = null;
                    }
                }
            }
            finally
            {
                moveHistoryList.EndUpdate();
            }
        }

        private void ExportPgn()
        {
            string engineName = engine.IsUsingStockfish ? "Stockfish IA" : "Engine Tática Interna";
            string whiteName = playerIsWhite ? "Jogador" : engineName;
            string blackName = playerIsWhite ? engineName : "Jogador";
            string? evtName = activePuzzle != null ? $"Puzzle: {activePuzzle.Title}" : null;
            string pgn = rules.ToPgn(blackPlayerName: blackName, eventName: evtName, whitePlayerName: whiteName);
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
                    SetStatusBanner("Arquivo PGN salvo e copiado para a área de transferência!", StatusBannerKind.Success);
                }
                catch (Exception ex)
                {
                    SetStatusBanner($"Erro ao salvar PGN: {ex.Message}", StatusBannerKind.Error);
                }
            }
            else if (copied)
            {
                SetStatusBanner("PGN copiado para a área de transferência!", StatusBannerKind.Success);
            }
            else
            {
                SetStatusBanner($"Não foi possível copiar o PGN ({clipErr}).", StatusBannerKind.Warning);
            }
        }

        private void TakebackMove()
        {
            if (activePromotionTarget.HasValue)
            {
                CompletePromotionSelection(null);
                return;
            }

            if (isBotThinking)
            {
                SetStatusBanner("Aguarde a IA terminar o lance atual (ou clique em 'Parar') antes de desfazer.", StatusBannerKind.Warning);
                return;
            }

            if (history.Count == 0)
            {
                SetStatusBanner("Ainda não há lances para desfazer nesta partida.", StatusBannerKind.Info);
                return;
            }

            JumpToLivePosition();
            hintGeneration++;
            try { hintSessionCts?.Cancel(); } catch { }
            if (isHintRunning)
            {
                isHintRunning = false;
                engine.StopSearch();
            }

            rules.RestoreSnapshot(history.Pop());
            rules.GameResult = "*";
            gameOver = false;
            selectedSquare = null;
            legalMoves.Clear();
            hintArrow = null;

            int targetSnapCount = rules.SanMoves.Count + 1;
            while (plySnapshots.Count > targetSnapCount && plySnapshots.Count > 1)
                plySnapshots.RemoveAt(plySnapshots.Count - 1);
            while (plyEvaluations.Count > targetSnapCount && plyEvaluations.Count > 1)
                plyEvaluations.RemoveAt(plyEvaluations.Count - 1);

            reviewPlyIndex = null;
            currentDisplayedEvalWhite = plyEvaluations.Count > 0 ? plyEvaluations[^1] : 0.0;
            currentDisplayedEvalText = FormatEvalPawnsText(currentDisplayedEvalWhite);

            if (moveHistoryList.Items.Count > 0)
                moveHistoryList.Items.RemoveAt(moveHistoryList.Items.Count - 1);
            moveEvaluations.RemoveAll(m => m.PlyIndex > rules.SanMoves.Count);

            if (rules.SanMoves.Count == 0)
            {
                clockRunning = false;
                clockStopwatch.Reset();
            }

            openingBookLabel.Text = $"Abertura: {StudyLibrary.DetectOpening(rules.SanMoves)}";
            SetStatusBanner("Lance desfeito (Voltar). Sua vez!", StatusBannerKind.Info);
            UpdateClockLabels();
            UpdateReviewStatusLabel();
            UpdateActionButtonsState();
            RefreshAllVisualPanels();
            StartBackgroundTurnAnalysis();
        }

        private void ResignGame()
        {
            if (gameOver)
            {
                SetStatusBanner("A partida já foi encerrada.", StatusBannerKind.Info);
                return;
            }

            if (rules.SanMoves.Count > 0)
            {
                var confirm = MessageBox.Show(
                    "Tem certeza de que deseja desistir desta partida?",
                    "Confirmar Desistência",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes)
                    return;
            }

            rules.GameResult = playerIsWhite ? "0-1" : "1-0";
            string winner = playerIsWhite ? "Pretas (IA)" : "Brancas (IA)";
            string msg = $"Você desistiu da partida. Vitória de {winner}!";
            EndCurrentGame(msg, StatusBannerKind.Warning);
            ShowEndGameDialog("Desistência", msg);
        }

        private void OfferDraw()
        {
            if (gameOver)
            {
                SetStatusBanner("A partida já foi encerrada.", StatusBannerKind.Info);
                return;
            }
            if (isBotThinking)
            {
                SetStatusBanner("Aguarde a IA terminar o lance atual antes de propor empate.", StatusBannerKind.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                "Deseja propor empate por acordo à IA nesta posição?",
                "Confirmar Pedido de Empate",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return;

            int evalWhite = EngineService.EvaluateBoardWhiteRelative(rules.Board);
            int botAdvantageCp = playerIsWhite ? -evalWhite : evalWhite;

            if (botAdvantageCp <= 150)
            {
                rules.GameResult = "1/2-1/2";
                string msg = "O bot analisou a posição e aceitou o pedido de empate!";
                EndCurrentGame(msg, StatusBannerKind.Info);
                ShowEndGameDialog("Empate por Acordo", msg);
            }
            else
            {
                SetStatusBanner("O bot recusou o empate porque avalia que está em vantagem na posição.", StatusBannerKind.Warning);
            }
        }

        // === DESENHO DA BARRA DE AVALIAÇÃO, PAINEL DE MATERIAL E GRÁFICO DE VANTAGEM ===
        private void EvalBarPanel_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = evalBarPanel.ClientSize.Width;
            int h = evalBarPanel.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            double whiteWinProb = EngineService.EvalToWinProbability(currentDisplayedEvalWhite);
            double topRatio = isBoardFlipped ? whiteWinProb : (1.0 - whiteWinProb);
            int topPixels = (int)Math.Round(h * Math.Clamp(topRatio, 0.04, 0.96));

            Color topColor = isBoardFlipped ? Color.FromArgb(242, 240, 234) : Color.FromArgb(48, 46, 44);
            Color bottomColor = isBoardFlipped ? Color.FromArgb(48, 46, 44) : Color.FromArgb(242, 240, 234);

            using (var topBrush = new SolidBrush(topColor))
                g.FillRectangle(topBrush, 0, 0, w, topPixels);
            using (var bottomBrush = new SolidBrush(bottomColor))
                g.FillRectangle(bottomBrush, 0, topPixels, w, h - topPixels);

            using (var midPen = new Pen(Color.FromArgb(140, 125, 125, 125), 1f))
                g.DrawLine(midPen, 0, h / 2, w, h / 2);

            string scoreLabel = currentDisplayedEvalText;
            if (scoreLabel.Length > 5) scoreLabel = scoreLabel[..5];

            bool whiteAhead = currentDisplayedEvalWhite >= 0;
            bool drawAtBottom = (whiteAhead && !isBoardFlipped) || (!whiteAhead && isBoardFlipped);
            Color textColor = whiteAhead ? Color.FromArgb(35, 35, 35) : Color.FromArgb(240, 240, 240);

            using var font = new Font("Segoe UI", 7.25f, FontStyle.Bold);
            using var textBrush = new SolidBrush(textColor);
            var sf = new StringFormat { Alignment = StringAlignment.Center };
            float textY = drawAtBottom ? Math.Max(4, h - 18) : 4;
            g.DrawString(scoreLabel, font, textBrush, new RectangleF(0, textY, w, 16), sf);
        }

        private void MaterialPanel_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var (capturedByWhite, capturedByBlack, matDiffWhite) = DisplayedRules.GetCapturedPiecesAndMaterial();

            using var titleFont = new Font("Segoe UI", 8f, FontStyle.Bold);
            using var symFont = new Font("Segoe UI Symbol", 11.5f, FontStyle.Regular);
            using var badgeFont = new Font("Segoe UI", 8f, FontStyle.Bold);
            using var darkBrush = new SolidBrush(Color.FromArgb(35, 35, 35));
            using var mutedBrush = new SolidBrush(Color.FromArgb(110, 110, 110));
            using var advBrush = new SolidBrush(Color.FromArgb(20, 125, 55));

            static string SymbolsForCaptured(List<char> pieces)
            {
                if (pieces.Count == 0) return "nenhuma captura";
                var sb = new StringBuilder();
                foreach (char p in pieces)
                {
                    sb.Append(char.ToLower(p) switch
                    {
                        'q' => '♛',
                        'r' => '♜',
                        'b' => '♝',
                        'n' => '♞',
                        _ => '♟'
                    });
                }
                return sb.ToString();
            }

            string whiteAdv = matDiffWhite > 0 ? $"+{matDiffWhite}" : "";
            string blackAdv = matDiffWhite < 0 ? $"+{Math.Abs(matDiffWhite)}" : "";

            g.DrawString("Brancas:", titleFont, darkBrush, 8, 6);
            string wSyms = SymbolsForCaptured(capturedByWhite);
            if (capturedByWhite.Count == 0)
                g.DrawString(wSyms, titleFont, mutedBrush, 68, 6);
            else
                g.DrawString(wSyms, symFont, darkBrush, 64, 2);

            if (!string.IsNullOrEmpty(whiteAdv))
                g.DrawString(whiteAdv, badgeFont, advBrush, materialPanel.ClientSize.Width - 38, 6);

            using (var sepPen = new Pen(Color.FromArgb(220, 215, 205), 1))
                g.DrawLine(sepPen, 6, 28, materialPanel.ClientSize.Width - 6, 28);

            g.DrawString("Pretas:", titleFont, darkBrush, 8, 33);
            string bSyms = SymbolsForCaptured(capturedByBlack);
            if (capturedByBlack.Count == 0)
                g.DrawString(bSyms, titleFont, mutedBrush, 68, 33);
            else
                g.DrawString(bSyms, symFont, darkBrush, 64, 29);

            if (!string.IsNullOrEmpty(blackAdv))
                g.DrawString(blackAdv, badgeFont, advBrush, materialPanel.ClientSize.Width - 38, 33);
        }

        private void AdvantageGraphPanel_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = advantageGraphPanel.ClientSize.Width;
            int h = advantageGraphPanel.ClientSize.Height;
            if (w <= 10 || h <= 10) return;

            int padLeft = 8, padRight = 8, padTop = 18, padBottom = 10;
            int plotW = Math.Max(10, w - padLeft - padRight);
            int plotH = Math.Max(10, h - padTop - padBottom);
            float midY = padTop + plotH / 2f;

            using (var bgBrush = new SolidBrush(Color.FromArgb(46, 46, 48)))
                g.FillRectangle(bgBrush, padLeft, padTop, plotW, plotH);

            using var headerFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            using var headerBrush = new SolidBrush(Color.FromArgb(70, 70, 70));
            g.DrawString("Gráfico de Vantagem (clique para navegar pelos lances)", headerFont, headerBrush, 6, 2);

            if (plyEvaluations.Count <= 1)
            {
                using var midPen0 = new Pen(Color.FromArgb(120, 180, 180, 180), 1f);
                g.DrawLine(midPen0, padLeft, midY, padLeft + plotW, midY);
                return;
            }

            float PointX(int idx) => padLeft + (idx / (float)(plyEvaluations.Count - 1)) * plotW;
            float PointY(double evalWhite)
            {
                double clamped = Math.Clamp(evalWhite, -8.0, 8.0);
                return (float)(midY - (clamped / 8.0) * (plotH / 2.0 - 3));
            }

            var polyPts = new List<PointF> { new PointF(PointX(0), midY) };
            for (int i = 0; i < plyEvaluations.Count; i++)
                polyPts.Add(new PointF(PointX(i), PointY(plyEvaluations[i])));
            polyPts.Add(new PointF(PointX(plyEvaluations.Count - 1), midY));

            using (var areaBrush = new SolidBrush(Color.FromArgb(165, 238, 238, 232)))
                g.FillPolygon(areaBrush, polyPts.ToArray());

            using (var zeroPen = new Pen(Color.FromArgb(150, 140, 140, 140), 1f) { DashStyle = DashStyle.Dash })
                g.DrawLine(zeroPen, padLeft, midY, padLeft + plotW, midY);

            var curvePts = new PointF[plyEvaluations.Count];
            for (int i = 0; i < plyEvaluations.Count; i++)
                curvePts[i] = new PointF(PointX(i), PointY(plyEvaluations[i]));

            using (var linePen = new Pen(Color.FromArgb(65, 175, 240), 2f))
                g.DrawLines(linePen, curvePts);

            int activePly = reviewPlyIndex ?? (plyEvaluations.Count - 1);
            activePly = Math.Clamp(activePly, 0, plyEvaluations.Count - 1);
            float cursorX = PointX(activePly);
            float cursorY = PointY(plyEvaluations[activePly]);

            using (var cursorPen = new Pen(Color.FromArgb(220, 255, 195, 40), 1.5f))
                g.DrawLine(cursorPen, cursorX, padTop, cursorX, padTop + plotH);

            using (var dotBrush = new SolidBrush(Color.FromArgb(255, 210, 50)))
                g.FillEllipse(dotBrush, cursorX - 4, cursorY - 4, 8, 8);
        }

        private void AdvantageGraphPanel_MouseDownOrMove(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || plyEvaluations.Count <= 1) return;
            int padLeft = 8, padRight = 8;
            int plotW = Math.Max(10, advantageGraphPanel.ClientSize.Width - padLeft - padRight);
            double ratio = Math.Clamp((e.X - padLeft) / (double)plotW, 0.0, 1.0);
            int targetPly = (int)Math.Round(ratio * (plyEvaluations.Count - 1));
            JumpToPly(targetPly);
        }

        // === DESENHO DO TABULEIRO (DESTAQUES, BOLINHAS/ANÉIS, DRAG & DROP, PROMOÇÃO LICHESS) ===
        private Point BoardToScreenSquareTopLeft(int col, int row)
        {
            int displayCol = isBoardFlipped ? (7 - col) : col;
            int displayRow = isBoardFlipped ? (7 - row) : row;
            return new Point(boardOriginX + displayCol * squareSize, boardOriginY + displayRow * squareSize);
        }

        private Point? ScreenToSquare(int x, int y)
        {
            if (x < boardOriginX || y < boardOriginY) return null;
            int displayCol = (x - boardOriginX) / squareSize;
            int displayRow = (y - boardOriginY) / squareSize;
            if (displayCol < 0 || displayCol > 7 || displayRow < 0 || displayRow > 7) return null;

            int col = isBoardFlipped ? (7 - displayCol) : displayCol;
            int row = isBoardFlipped ? (7 - displayRow) : displayRow;
            return new Point(col, row);
        }

        private void BoardPanel_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            var boardState = DisplayedRules;

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

            EnsureScaledPieceCache();

            Point? checkedKingSq = boardState.GetCheckedKingSquare();

            using var coordFont = new Font("Segoe UI", Math.Max(7.25f, squareSize * 0.145f), FontStyle.Bold);
            Color coordOnLightColor = currentTheme == "Preto e Branco"
                ? Color.FromArgb(95, 95, 95)
                : darkColor;
            Color coordOnDarkColor = currentTheme == "Branco e Verde"
                ? Color.FromArgb(235, 248, 238)
                : lightColor;
            using var coordOnLightBrush = new SolidBrush(coordOnLightColor);
            using var coordOnDarkBrush = new SolidBrush(coordOnDarkColor);

            using var lightBrush = new SolidBrush(lightColor);
            using var darkBrush = new SolidBrush(darkColor);
            using var selectedBrush = new SolidBrush(Color.FromArgb(246, 246, 105));
            using var lastMoveLightBrush = new SolidBrush(Color.FromArgb(165, 215, 225, 95));
            using var lastMoveDarkBrush = new SolidBrush(Color.FromArgb(165, 180, 190, 55));
            using var checkBrush = new SolidBrush(Color.FromArgb(205, 230, 45, 45));
            using var checkPen = new Pen(Color.FromArgb(245, 150, 0, 0), 2.5f);
            using var markBrush = new SolidBrush(Color.FromArgb(150, 235, 65, 50));
            using var preBrush = new SolidBrush(Color.FromArgb(150, 50, 140, 240));
            using var dragTargetBrush = new SolidBrush(Color.FromArgb(115, 65, 195, 90));
            using var dragTargetPen = new Pen(Color.FromArgb(235, 25, 145, 50), 2.5f);
            using var dotBrush = new SolidBrush(Color.FromArgb(135, 30, 45, 35));
            using var ringPen = new Pen(Color.FromArgb(150, 30, 45, 35), Math.Max(3.5f, squareSize * 0.085f));

            bool isSlideAnimActive = animFromSquare.HasValue && animToSquare.HasValue && animPieceChar != '.' && !IsReviewingHistory;

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    Point topLeft = BoardToScreenSquareTopLeft(c, r);
                    int x = topLeft.X;
                    int y = topLeft.Y;
                    var pt = new Point(c, r);
                    bool isLight = (r + c) % 2 == 0;

                    g.FillRectangle(isLight ? lightBrush : darkBrush, x, y, squareSize, squareSize);

                    // 1. Destaque do último lance executado (origem e destino)
                    if (boardState.LastMoveFrom == pt || boardState.LastMoveTo == pt)
                    {
                        g.FillRectangle(isLight ? lastMoveLightBrush : lastMoveDarkBrush, x, y, squareSize, squareSize);
                    }

                    // 2. Destaque de casa selecionada / premove
                    if (selectedSquare == pt || premoveSelection == pt)
                    {
                        g.FillRectangle(selectedBrush, x, y, squareSize, squareSize);
                    }

                    // 2b. Destaque da casa sob o cursor durante o arrasto quando é um destino legal
                    if (isDraggingPiece && dragHoverSquare.HasValue && dragHoverSquare.Value == pt && legalMoves.Contains(pt))
                    {
                        g.FillRectangle(dragTargetBrush, x, y, squareSize, squareSize);
                        g.DrawRectangle(dragTargetPen, x + 1, y + 1, squareSize - 2, squareSize - 2);
                    }

                    // 3. Destaque do Rei em xeque
                    if (checkedKingSq.HasValue && checkedKingSq.Value == pt)
                    {
                        g.FillRectangle(checkBrush, x, y, squareSize, squareSize);
                        g.DrawRectangle(checkPen, x + 1, y + 1, squareSize - 2, squareSize - 2);
                    }

                    // 4. Casas marcadas com botão direito
                    if (markedSquares.Contains(pt))
                    {
                        g.FillRectangle(markBrush, x, y, squareSize, squareSize);
                    }

                    // 5. Premove agendado
                    if (queuedPremove.HasValue && (queuedPremove.Value.from == pt || queuedPremove.Value.to == pt))
                    {
                        g.FillRectangle(preBrush, x, y, squareSize, squareSize);
                    }

                    // Coordenadas desenhadas dentro das casas (fileira no canto superior esquerdo e coluna no canto inferior esquerdo)
                    int displayCol = isBoardFlipped ? (7 - c) : c;
                    int displayRow = isBoardFlipped ? (7 - r) : r;
                    Brush cBrush = isLight ? coordOnLightBrush : coordOnDarkBrush;

                    if (displayCol == 0)
                    {
                        string rankStr = (8 - r).ToString();
                        g.DrawString(rankStr, coordFont, cBrush, x + 2, y + 1);
                    }
                    if (displayRow == 7)
                    {
                        string fileStr = ((char)('a' + c)).ToString();
                        g.DrawString(fileStr, coordFont, cBrush, x + 2, y + squareSize - coordFont.Height - 1);
                    }

                    // 6. Peça na casa (oculta na origem durante arrasto ou no destino durante animação de deslize)
                    char piece = boardState.Board[r, c];
                    bool isBeingDraggedFromHere = isDraggingPiece && dragOriginSquare.HasValue && dragOriginSquare.Value == pt;
                    bool isAnimatingToHere = isSlideAnimActive && animToSquare!.Value == pt;
                    if (piece != '.' && !isBeingDraggedFromHere && !isAnimatingToHere)
                    {
                        DrawPieceAtScreen(g, boardState, piece, r, c, x, y);
                    }

                    // 7. Destinos legais como bolinhas (casa vazia) ou anéis (captura / en passant)
                    if (!activePromotionTarget.HasValue && legalMoves.Contains(pt))
                    {
                        bool isCapture = boardState.IsCaptureDestination(selectedSquare, pt);
                        if (isCapture)
                        {
                            float pad = Math.Max(3f, squareSize * 0.07f);
                            g.DrawEllipse(ringPen, x + pad, y + pad, squareSize - pad * 2f, squareSize - pad * 2f);
                        }
                        else
                        {
                            float d = squareSize * 0.30f;
                            g.FillEllipse(dotBrush, x + (squareSize - d) / 2f, y + (squareSize - d) / 2f, d, d);
                        }
                    }
                }
            }

            // Animação suave de deslize da peça (origem -> destino em ~125 ms)
            if (isSlideAnimActive)
            {
                double t = Math.Clamp(animStopwatch.Elapsed.TotalMilliseconds / MoveAnimationDurationMs, 0.0, 1.0);
                double eased = 1.0 - Math.Pow(1.0 - t, 3);
                Point startTl = BoardToScreenSquareTopLeft(animFromSquare!.Value.X, animFromSquare.Value.Y);
                Point endTl = BoardToScreenSquareTopLeft(animToSquare!.Value.X, animToSquare.Value.Y);
                int curX = (int)Math.Round(startTl.X + (endTl.X - startTl.X) * eased);
                int curY = (int)Math.Round(startTl.Y + (endTl.Y - startTl.Y) * eased);
                DrawPieceAtScreen(g, boardState, animPieceChar, animToSquare.Value.Y, animToSquare.Value.X, curX, curY);
            }

            foreach (var arrow in drawnArrows)
                DrawBoardArrow(g, arrow.from, arrow.to, Color.FromArgb(200, 55, 70, 75));

            if (hintArrow.HasValue)
                DrawBoardArrow(g, hintArrow.Value.from, hintArrow.Value.to, Color.FromArgb(215, 30, 160, 50));

            if (isDraggingPiece && dragOriginSquare.HasValue)
            {
                Point orig = dragOriginSquare.Value;
                char draggedPiece = boardState.Board[orig.Y, orig.X];
                if (draggedPiece != '.')
                {
                    int drawX = dragCurrentScreenPt.X - squareSize / 2;
                    int drawY = dragCurrentScreenPt.Y - squareSize / 2;
                    DrawPieceAtScreen(g, boardState, draggedPiece, orig.Y, orig.X, drawX, drawY);
                }
            }

            // 8. Seletor de promoção estilo Lichess desenhado diretamente sobre a casa de destino
            if (activePromotionTarget.HasValue)
            {
                DrawPromotionOverlayOnBoard(g, boardState, activePromotionTarget.Value);
            }
        }

        private void DrawPromotionOverlayOnBoard(Graphics g, ChessRules boardState, Point targetSquare)
        {
            using var dimBrush = new SolidBrush(Color.FromArgb(115, 18, 18, 22));
            g.FillRectangle(dimBrush, boardOriginX, boardOriginY, squareSize * 8, squareSize * 8);

            using var cardNormalBrush = new SolidBrush(Color.FromArgb(245, 250, 248, 240));
            using var cardHoverBrush = new SolidBrush(Color.FromArgb(255, 255, 232, 148));
            using var borderPen = new Pen(Color.FromArgb(230, 55, 90, 130), 2f);
            using var keyFont = new Font("Segoe UI", Math.Max(7f, squareSize * 0.13f), FontStyle.Bold);
            using var keyBrush = new SolidBrush(Color.FromArgb(50, 70, 95));

            for (int i = 0; i < PromotionChoices.Length; i++)
            {
                Point optSq = GetPromotionOptionBoardSquare(targetSquare, i);
                Point tl = BoardToScreenSquareTopLeft(optSq.X, optSq.Y);

                bool hovered = (promotionHoverIndex == i);
                g.FillRectangle(hovered ? cardHoverBrush : cardNormalBrush, tl.X + 1, tl.Y + 1, squareSize - 2, squareSize - 2);
                g.DrawEllipse(borderPen, tl.X + 3, tl.Y + 3, squareSize - 6, squareSize - 6);

                char pieceChar = activePromotionIsWhite
                    ? char.ToUpper(PromotionChoices[i].code)
                    : char.ToLower(PromotionChoices[i].code);
                string imgKey = $"{pieceChar}1";

                if (scaledPieceCache.TryGetValue(imgKey, out Bitmap? bmp))
                {
                    int pad = (squareSize - bmp.Width) / 2;
                    g.DrawImage(bmp, tl.X + pad, tl.Y + pad, bmp.Width, bmp.Height);
                }
                else
                {
                    DrawFallbackPiece(g, pieceChar, tl.X, tl.Y);
                }

                g.DrawString(PromotionChoices[i].keyHint, keyFont, keyBrush, tl.X + 4, tl.Y + 2);
            }
        }

        private void DrawPieceAtScreen(Graphics g, ChessRules boardState, char piece, int r, int c, int x, int y)
        {
            int variant = boardState.PieceVariants[r, c];
            string key2 = $"{piece}2";
            string key1 = $"{piece}1";
            string chosenKey = (variant == 2 && scaledPieceCache.ContainsKey(key2)) ? key2 : key1;

            if (scaledPieceCache.TryGetValue(chosenKey, out Bitmap? bmp))
            {
                int pad = (squareSize - bmp.Width) / 2;
                g.DrawImage(bmp, x + pad, y + pad, bmp.Width, bmp.Height);
            }
            else
            {
                DrawFallbackPiece(g, piece, x, y);
            }
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
            float fontSize = Math.Max(14f, squareSize * 0.45f);
            using var f = new Font("Segoe UI Symbol", fontSize, FontStyle.Regular);
            var color = char.IsUpper(piece) ? Color.White : Color.Black;
            var outline = char.IsUpper(piece) ? Color.Black : Color.White;
            using var bOut = new SolidBrush(outline);
            using var bMain = new SolidBrush(color);
            g.DrawString(symbol, f, bOut, x + 5, y + 3);
            g.DrawString(symbol, f, bMain, x + 4, y + 2);
        }

        private void DrawBoardArrow(Graphics g, Point from, Point to, Color color)
        {
            Point tl1 = BoardToScreenSquareTopLeft(from.X, from.Y);
            Point tl2 = BoardToScreenSquareTopLeft(to.X, to.Y);
            PointF p1 = new PointF(tl1.X + squareSize / 2f, tl1.Y + squareSize / 2f);
            PointF p2 = new PointF(tl2.X + squareSize / 2f, tl2.Y + squareSize / 2f);

            using var pen = new Pen(color, Math.Max(5f, squareSize * 0.14f));
            using var arrowCap = new AdjustableArrowCap(4.5f, 4.5f, true);
            pen.StartCap = LineCap.Round;
            pen.CustomEndCap = arrowCap;
            g.DrawLine(pen, p1, p2);
        }

        private void BoardPanel_MouseDown(object? sender, MouseEventArgs e)
        {
            var sq = ScreenToSquare(e.X, e.Y);

            // Se o seletor de promoção sobre o tabuleiro estiver ativo, clique numa opção escolhe a peça e clique fora cancela
            if (activePromotionTarget.HasValue)
            {
                if (e.Button == MouseButtons.Left && sq.HasValue)
                {
                    int idx = HitTestPromotionOptionIndex(sq.Value);
                    if (idx >= 0)
                    {
                        CompletePromotionSelection(PromotionChoices[idx].code);
                        return;
                    }
                }
                CompletePromotionSelection(null);
                return;
            }

            if (!sq.HasValue) return;

            var clicked = sq.Value;
            int col = clicked.X;
            int row = clicked.Y;

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

                if (IsReviewingHistory)
                {
                    if (!gameOver)
                    {
                        JumpToLivePosition();
                    }
                    else
                    {
                        boardPanel.Invalidate();
                        return;
                    }
                }

                if (gameOver)
                {
                    boardPanel.Invalidate();
                    return;
                }

                bool isPlayerTurn = !isBotThinking && (rules.WhiteTurn == playerIsWhite);

                if (!isPlayerTurn)
                {
                    if (premoveSelection.HasValue && premoveSelection.Value != clicked)
                    {
                        queuedPremove = (premoveSelection.Value, clicked);
                        premoveSelection = null;
                        SetStatusBanner(
                            $"Premove agendado: {ChessRules.SquareToAlgebraic(queuedPremove.Value.from)} ➔ {ChessRules.SquareToAlgebraic(queuedPremove.Value.to)}",
                            StatusBannerKind.Info);
                    }
                    else
                    {
                        char piece = rules.Board[row, col];
                        if (piece != '.' && char.IsUpper(piece) == playerIsWhite)
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
                    var fromSq = selectedSquare.Value;
                    dragOriginSquare = null;
                    dragHoverSquare = null;
                    isDraggingPiece = false;
                    RunSafe(() => ExecutePlayerAndBotTurnAsync(fromSq, clicked, animateSlide: true));
                    return;
                }

                char p = rules.Board[row, col];
                if (p != '.' && char.IsUpper(p) == playerIsWhite)
                {
                    selectedSquare = clicked;
                    legalMoves = rules.GetLegalMovesForSquare(col, row, playerIsWhite);
                    dragOriginSquare = clicked;
                    dragHoverSquare = clicked;
                    dragStartScreenPt = e.Location;
                    dragCurrentScreenPt = e.Location;
                    isDraggingPiece = false;
                }
                else
                {
                    selectedSquare = null;
                    legalMoves.Clear();
                    dragOriginSquare = null;
                    dragHoverSquare = null;
                    isDraggingPiece = false;
                }

                boardPanel.Invalidate();
            }
        }

        private void BoardPanel_MouseMove(object? sender, MouseEventArgs e)
        {
            if (activePromotionTarget.HasValue)
            {
                var sq = ScreenToSquare(e.X, e.Y);
                int newHover = sq.HasValue ? HitTestPromotionOptionIndex(sq.Value) : -1;
                boardPanel.Cursor = newHover >= 0 ? Cursors.Hand : Cursors.Default;
                if (newHover != promotionHoverIndex)
                {
                    promotionHoverIndex = newHover;
                    boardPanel.Invalidate();
                }
                return;
            }

            if (e.Button == MouseButtons.Left && dragOriginSquare.HasValue && !gameOver && !isBotThinking)
            {
                int dx = e.X - dragStartScreenPt.X;
                int dy = e.Y - dragStartScreenPt.Y;
                if (!isDraggingPiece && (dx * dx + dy * dy) >= 25)
                    isDraggingPiece = true;

                if (isDraggingPiece)
                {
                    dragCurrentScreenPt = e.Location;
                    dragHoverSquare = ScreenToSquare(e.X, e.Y);
                    boardPanel.Cursor = Cursors.Hand;
                    boardPanel.Invalidate();
                    return;
                }
            }
            else if (!gameOver && !isBotThinking && !IsReviewingHistory)
            {
                var sq = ScreenToSquare(e.X, e.Y);
                bool mine = sq.HasValue && rules.Board[sq.Value.Y, sq.Value.X] is var p
                            && p != '.' && char.IsUpper(p) == playerIsWhite
                            && rules.WhiteTurn == playerIsWhite;
                boardPanel.Cursor = (mine || (sq.HasValue && legalMoves.Contains(sq.Value)))
                    ? Cursors.Hand : Cursors.Default;
            }
            else
            {
                boardPanel.Cursor = Cursors.Default;
            }
        }

        private void BoardPanel_MouseUp(object? sender, MouseEventArgs e)
        {
            if (activePromotionTarget.HasValue)
                return;

            if (e.Button == MouseButtons.Left)
            {
                if (isDraggingPiece && dragOriginSquare.HasValue)
                {
                    var fromSq = dragOriginSquare.Value;
                    isDraggingPiece = false;
                    dragOriginSquare = null;
                    dragHoverSquare = null;

                    var dropSq = ScreenToSquare(e.X, e.Y);
                    if (dropSq.HasValue && dropSq.Value != fromSq && legalMoves.Contains(dropSq.Value))
                    {
                        RunSafe(() => ExecutePlayerAndBotTurnAsync(fromSq, dropSq.Value, animateSlide: false));
                        return;
                    }
                    boardPanel.Invalidate();
                }
                else
                {
                    dragOriginSquare = null;
                    dragHoverSquare = null;
                    isDraggingPiece = false;
                }
                return;
            }

            if (e.Button == MouseButtons.Right && rightDragStart.HasValue)
            {
                var sq = ScreenToSquare(e.X, e.Y);
                if (sq.HasValue)
                {
                    var endPt = sq.Value;
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
        }

        // === PRÉ-ANÁLISE EM SEGUNDO PLANO ENQUANTO O JOGADOR PENSA ===
        private void StartBackgroundTurnAnalysis()
        {
            if (gameOver || isBotThinking || activePuzzle != null) return;
            if (rules.WhiteTurn != playerIsWhite) return;

            string currentFen = rules.ToFen();
            if (preAnalysisFen == currentFen && preAnalysisTask != null)
                return;

            var cloneRules = rules.Clone();
            bool sideToMove = cloneRules.WhiteTurn;
            var token = gameSessionCts?.Token ?? CancellationToken.None;

            preAnalysisFen = currentFen;
            preAnalysisTask = Task.Run(() => engine.Analyze(cloneRules, sideToMove, 3, isHintOrEvaluationOnly: true, cancellationToken: token));
        }

        private sealed record PlayerMoveInfo(
            GameSnapshot Before,
            string BeforeFen,
            Task<EngineAnalysisResult>? CachedBeforeAnalysisTask,
            Point From,
            Point To,
            char? Promotion,
            string San,
            bool IsBookMove,
            bool PlayerWasWhite,
            int PlyIndex);

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
                SetStatusBanner($"{context}: {ex.Message}", StatusBannerKind.Error);
            }
            catch { }
        }

        private async Task ExecutePlayerAndBotTurnAsync(Point from, Point to, bool animateSlide = true)
        {
            if (activePuzzle != null)
            {
                HandlePuzzleMove(from, to, animateSlide);
                return;
            }

            if (gameOver || isBotThinking || rules.WhiteTurn != playerIsWhite) return;

            int generation = gameGeneration;
            var preMoveSnapshot = rules.CreateSnapshot();
            string beforeFen = rules.ToFen();
            var cachedBeforeTask = (preAnalysisFen == beforeFen) ? preAnalysisTask : null;

            char? promoPiece = null;
            char movingPiece = rules.Board[from.Y, from.X];
            if (char.ToUpper(movingPiece) == 'P' && (to.Y == 0 || to.Y == 7))
            {
                // Pausa o relógio e exibe o seletor de promoção sobre a casa de destino (suporta Q/R/B/N e Esc para cancelar)
                isPromotionDialogOpen = true;
                clockStopwatch.Stop();
                UpdateActionButtonsState();
                try
                {
                    promoPiece = await AskPromotionOnBoardAsync(to, playerIsWhite);
                }
                finally
                {
                    isPromotionDialogOpen = false;
                    if (clockRunning && !gameOver && timeControlMode != "Sem Relógio")
                        clockStopwatch.Restart();
                    UpdateActionButtonsState();
                }

                // Se o usuário cancelou a promoção (Esc ou clique fora), mantém a posição e não executa o lance!
                if (!promoPiece.HasValue)
                {
                    selectedSquare = from;
                    legalMoves = rules.GetLegalMovesForSquare(from.X, from.Y, playerIsWhite);
                    SetStatusBanner("Promoção cancelada. Escolha outro lance.", StatusBannerKind.Info);
                    boardPanel.Invalidate();
                    return;
                }
            }

            if (generation != gameGeneration || gameOver) return;

            hintGeneration++;
            try { hintSessionCts?.Cancel(); } catch { }
            if (isHintRunning)
            {
                isHintRunning = false;
            }

            history.Push(preMoveSnapshot);

            int moveNum = rules.FullMoveNumber;
            bool wasWhiteMove = rules.WhiteTurn;
            string playedSan = rules.ExecuteMove(from, to, promoPiece);
            bool isBookMove = StudyLibrary.IsBookMove(rules.SanMoves);
            ApplyClockIncrement(wasWhiteMove);
            PlayMoveSoundForSan(playedSan);
            if (animateSlide)
                StartMoveSlideAnimation(from, to, rules.Board[to.Y, to.X]);

            double quickEvalWhite = EngineService.EvaluateBoardWhiteRelative(rules.Board) / 100.0;
            RecordCurrentPlySnapshot(quickEvalWhite);
            int playerPlyIndex = plySnapshots.Count - 1;

            selectedSquare = null;
            legalMoves.Clear();
            hintArrow = null;
            openingBookLabel.Text = $"Abertura: {StudyLibrary.DetectOpening(rules.SanMoves)}";
            UpdateClockLabels();
            UpdateActionButtonsState();
            RefreshAllVisualPanels();

            ListViewItem rowItem;
            if (wasWhiteMove || moveHistoryList.Items.Count == 0)
            {
                rowItem = new ListViewItem(moveNum.ToString()) { UseItemStyleForSubItems = false };
                rowItem.SubItems.Add(wasWhiteMove ? playedSan : "...");
                rowItem.SubItems.Add(wasWhiteMove ? "..." : playedSan);
                rowItem.SubItems.Add("");
                rowItem.SubItems.Add("");
                moveHistoryList.Items.Add(rowItem);
            }
            else
            {
                rowItem = moveHistoryList.Items[^1];
                rowItem.SubItems[2].Text = playedSan;
            }
            rowItem.EnsureVisible();

            if (playedSan.EndsWith('#'))
            {
                int colIdx = wasWhiteMove ? 1 : 2;
                rowItem.SubItems[colIdx].Text = $"{playedSan} !";
                rowItem.SubItems[colIdx].ForeColor = Color.FromArgb(20, 150, 80);
                if (wasWhiteMove) rowItem.SubItems[2].Text = "-";
                rowItem.SubItems[3].Text = wasWhiteMove ? "#+1" : "#-1";
                rowItem.SubItems[4].Text = "Xeque-Mate!";
                rowItem.SubItems[4].ForeColor = Color.FromArgb(20, 150, 80);

                double mateEval = wasWhiteMove ? 99.0 : -99.0;
                plyEvaluations[playerPlyIndex] = 15.0 * (wasWhiteMove ? 1 : -1);
                currentDisplayedEvalWhite = mateEval;
                currentDisplayedEvalText = wasWhiteMove ? "#+1" : "#-1";

                moveEvaluations.Add(new MoveEvaluationRecord(
                    moveNum,
                    wasWhiteMove,
                    playerPlyIndex,
                    playedSan,
                    "!",
                    "Xeque-Mate!",
                    wasWhiteMove ? 10.0 : -10.0,
                    mateEval,
                    100.0,
                    "-",
                    "lance decisivo de xeque-mate"));
            }

            if (CheckAndAnnounceEndGame()) return;

            var moveInfo = new PlayerMoveInfo(
                preMoveSnapshot,
                beforeFen,
                cachedBeforeTask,
                from,
                to,
                promoPiece,
                playedSan,
                isBookMove,
                wasWhiteMove,
                playerPlyIndex);

            await RunBotTurnAsync(generation, rowItem, moveInfo);
        }

        private async Task StartBotTurnFromPositionAsync()
        {
            if (gameOver || isBotThinking || rules.WhiteTurn == playerIsWhite) return;

            if (timeControlMode != "Sem Relógio" && activePuzzle == null)
            {
                clockRunning = true;
                clockStopwatch.Restart();
            }

            int generation = gameGeneration;
            var rowItem = new ListViewItem(rules.FullMoveNumber.ToString()) { UseItemStyleForSubItems = false };
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
            isBotThinking = true;
            UpdateActionButtonsState();
            bool botIsWhite = rules.WhiteTurn;
            var sessionToken = gameSessionCts?.Token ?? CancellationToken.None;

            UpdateThinkingIndicator("⏳ IA pensando...", true);
            SetStatusBanner(
                playerMove != null
                    ? "Analisando seu lance e calculando resposta da IA..."
                    : $"IA ({(botIsWhite ? "Brancas" : "Pretas")}) calculando o lance...",
                StatusBannerKind.Info);

            bool botTurnCompleted = false;
            var botTurnSw = Stopwatch.StartNew();
            try
            {
                await Task.Delay(30);
                if (generation != gameGeneration || sessionToken.IsCancellationRequested) return;

                int diffIdx = difficultyCombo.SelectedIndex;

                EngineAnalysisResult? bestBefore = null;
                ChessRules? rulesBefore = null;
                if (playerMove != null)
                {
                    rulesBefore = new ChessRules();
                    rulesBefore.RestoreSnapshot(playerMove.Before);

                    if (playerMove.CachedBeforeAnalysisTask != null)
                    {
                        try
                        {
                            var cached = await playerMove.CachedBeforeAnalysisTask;
                            if (!cached.WasCancelled && cached.BestMove.HasValue)
                                bestBefore = cached;
                        }
                        catch { }
                    }

                    if (bestBefore == null)
                    {
                        bestBefore = await Task.Run(() => engine.Analyze(rulesBefore, playerMove.PlayerWasWhite, diffIdx, isHintOrEvaluationOnly: true, cancellationToken: sessionToken));
                    }
                    if (generation != gameGeneration || sessionToken.IsCancellationRequested) return;
                }

                var botRules = rules.Clone();
                EngineAnalysisResult evalAfterAnalysis;
                EngineAnalysisResult botAnalysis;

                if (playerMove != null && diffIdx < 2)
                {
                    evalAfterAnalysis = await Task.Run(() => engine.Analyze(botRules, botIsWhite, diffIdx, isHintOrEvaluationOnly: true, cancellationToken: sessionToken));
                    if (generation != gameGeneration || sessionToken.IsCancellationRequested) return;

                    var botPlayCopy = rules.Clone();
                    botAnalysis = await Task.Run(() => engine.Analyze(botPlayCopy, botIsWhite, diffIdx, isHintOrEvaluationOnly: false, cancellationToken: sessionToken));
                    if (generation != gameGeneration || sessionToken.IsCancellationRequested) return;
                }
                else
                {
                    botAnalysis = await Task.Run(() => engine.Analyze(botRules, botIsWhite, diffIdx, isHintOrEvaluationOnly: false, cancellationToken: sessionToken));
                    if (generation != gameGeneration || sessionToken.IsCancellationRequested) return;
                    evalAfterAnalysis = botAnalysis;
                }

                if (botAnalysis.WasCancelled) return;

                bool samePromo = bestBefore?.Promotion == null ||
                                 playerMove?.Promotion == null ||
                                 char.ToUpper(bestBefore.Promotion.Value) == char.ToUpper(playerMove.Promotion.Value);

                bool playedBest = playerMove != null &&
                                  bestBefore?.BestMove != null &&
                                  bestBefore.BestMove.Value.from == playerMove.From &&
                                  bestBefore.BestMove.Value.to == playerMove.To &&
                                  samePromo;

                (string badge, string description, Color color) classification = ("", "", Color.Black);
                string coachNote = "";
                string bestAlternativeStr = "-";

                if (playerMove != null && bestBefore != null && rulesBefore != null)
                {
                    if (playerMove.PlyIndex >= 0 && playerMove.PlyIndex < plyEvaluations.Count)
                        plyEvaluations[playerMove.PlyIndex] = Math.Clamp(evalAfterAnalysis.EvalWhitePawns, -15.0, 15.0);

                    bool isRealSacrifice = EngineService.IsMaterialSacrifice(
                        rulesBefore,
                        playerMove.From,
                        playerMove.To,
                        playerMove.Promotion,
                        bestBefore.EvalWhitePawns,
                        evalAfterAnalysis.EvalWhitePawns);

                    classification = EngineService.ClassifyMove(
                        bestBefore.EvalWhitePawns,
                        evalAfterAnalysis.EvalWhitePawns,
                        playedBest,
                        playerMove.IsBookMove,
                        isRealSacrifice,
                        forWhite: playerMove.PlayerWasWhite,
                        mateInMoves: evalAfterAnalysis.MateInMoves,
                        isImmediateCheckmate: playerMove.San.EndsWith('#'));

                    int playerColIdx = playerMove.PlayerWasWhite ? 1 : 2;
                    if (!string.IsNullOrEmpty(classification.badge))
                    {
                        rowItem.SubItems[playerColIdx].Text = $"{playerMove.San} {classification.badge}";
                        rowItem.SubItems[playerColIdx].ForeColor = classification.color;
                    }
                    rowItem.SubItems[4].Text = classification.description;
                    rowItem.SubItems[4].ForeColor = classification.color;

                    double moveAcc = EngineService.ComputeMoveAccuracy(
                        bestBefore.EvalWhitePawns,
                        evalAfterAnalysis.EvalWhitePawns,
                        playedBest,
                        playerMove.IsBookMove,
                        forWhite: playerMove.PlayerWasWhite);

                    if (bestBefore.BestMove.HasValue)
                        bestAlternativeStr = EngineService.FormatMoveUciOrAlg(bestBefore.BestMove.Value.from, bestBefore.BestMove.Value.to, bestBefore.Promotion);

                    coachNote = EngineService.ExplainMoveCoach(
                        rulesBefore,
                        playerMove.From,
                        playerMove.To,
                        playerMove.Promotion,
                        bestBefore,
                        evalAfterAnalysis,
                        playedBest,
                        playerMove.IsBookMove);

                    int moveNumber = int.TryParse(rowItem.Text, out int mn) ? mn : rules.FullMoveNumber;
                    moveEvaluations.Add(new MoveEvaluationRecord(
                        moveNumber,
                        playerMove.PlayerWasWhite,
                        playerMove.PlyIndex,
                        playerMove.San,
                        classification.badge,
                        classification.description,
                        bestBefore.EvalWhitePawns,
                        evalAfterAnalysis.EvalWhitePawns,
                        moveAcc,
                        bestAlternativeStr,
                        coachNote));
                }

                var botMove = botAnalysis.BestMove;
                char? botPromo = botAnalysis.Promotion;
                if (!botMove.HasValue ||
                    !rules.GetLegalMovesForSquare(botMove.Value.from.X, botMove.Value.from.Y, botIsWhite).Contains(botMove.Value.to) ||
                    rules.Board[botMove.Value.from.Y, botMove.Value.from.X] == '.' ||
                    char.IsUpper(rules.Board[botMove.Value.from.Y, botMove.Value.from.X]) != botIsWhite)
                {
                    var legal = rules.GetAllLegalMoves(botIsWhite);
                    if (legal.Count == 0)
                        throw new InvalidOperationException("O bot não tem lances legais nesta posição.");
                    botMove = legal[Random.Shared.Next(legal.Count)];
                    botPromo = null;
                }

                // Aplica o delay configurável do lance da IA (0 ms a 1000 ms) antes de executar o movimento no tabuleiro
                int targetDelayMs = GetTargetBotMoveDelayMs();
                int remainingDelayMs = targetDelayMs - (int)botTurnSw.ElapsedMilliseconds;
                if (remainingDelayMs > 0)
                {
                    try
                    {
                        await Task.Delay(remainingDelayMs, sessionToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    if (generation != gameGeneration || sessionToken.IsCancellationRequested || gameOver)
                        return;
                }

                int botMoveNumber = rules.FullMoveNumber;
                char defaultBotPromo = botIsWhite ? 'Q' : 'q';
                string botSan = rules.ExecuteMove(botMove.Value.from, botMove.Value.to, botPromo ?? defaultBotPromo);
                ApplyClockIncrement(botIsWhite);
                PlayMoveSoundForSan(botSan);
                StartMoveSlideAnimation(botMove.Value.from, botMove.Value.to, rules.Board[botMove.Value.to.Y, botMove.Value.to.X]);

                RecordCurrentPlySnapshot(evalAfterAnalysis.EvalWhitePawns);
                currentDisplayedEvalWhite = evalAfterAnalysis.EvalWhitePawns;
                currentDisplayedEvalText = evalAfterAnalysis.EvalText;

                if (botIsWhite)
                {
                    rowItem.Text = botMoveNumber.ToString();
                    rowItem.SubItems[1].Text = botSan;
                    rowItem.SubItems[2].Text = "...";
                    rowItem.SubItems[3].Text = evalAfterAnalysis.EvalText;
                }
                else
                {
                    rowItem.SubItems[2].Text = botSan;
                    rowItem.SubItems[3].Text = evalAfterAnalysis.EvalText;
                }

                nodesTextBox.Text = (evalAfterAnalysis.Nodes + botAnalysis.Nodes).ToString();
                if (!string.IsNullOrWhiteSpace(evalAfterAnalysis.LogLine))
                    engineLogBox.AppendText(evalAfterAnalysis.LogLine + Environment.NewLine);

                isBotThinking = false;
                UpdateThinkingIndicator("✔ Pronto", false);
                UpdateClockLabels();
                UpdateActionButtonsState();
                openingBookLabel.Text = $"Abertura: {StudyLibrary.DetectOpening(rules.SanMoves)}";
                RefreshAllVisualPanels();

                if (CheckAndAnnounceEndGame()) return;

                if (playerMove != null && bestBefore != null)
                {
                    var playerMovesOnly = moveEvaluations.Where(m => m.IsWhiteMove == playerIsWhite).ToList();
                    double avgAcc = playerMovesOnly.Count > 0 ? playerMovesOnly.Average(m => m.Accuracy) : 100.0;
                    string bestSuggestion = (!playedBest && !playerMove.IsBookMove && bestBefore.BestMove.HasValue)
                        ? $" | Melhor: {bestAlternativeStr}"
                        : "";
                    SetStatusBanner(
                        $"{classification.description} {classification.badge} ({coachNote}){bestSuggestion} | Aval: {evalAfterAnalysis.EvalText} | Precisão: {avgAcc:0.0}%",
                        classification.badge is "??" or "?" ? StatusBannerKind.Warning : StatusBannerKind.Info);
                }
                else
                {
                    SetStatusBanner($"IA jogou {botSan}. Sua vez ({(playerIsWhite ? "Brancas" : "Pretas")}) | Aval: {evalAfterAnalysis.EvalText}", StatusBannerKind.Info);
                }

                StartBackgroundTurnAnalysis();
                botTurnCompleted = true;
            }
            catch (Exception ex)
            {
                if (generation == gameGeneration)
                    ReportError("Erro ao calcular o lance da IA", ex);
            }
            finally
            {
                if (generation == gameGeneration)
                {
                    isBotThinking = false;
                    UpdateThinkingIndicator("✔ Pronto", false);
                    UpdateActionButtonsState();
                    RefreshAllVisualPanels();
                }
            }

            if (!botTurnCompleted || generation != gameGeneration || gameOver) return;

            if (queuedPremove.HasValue)
            {
                var pre = queuedPremove.Value;
                queuedPremove = null;
                var validMoves = rules.GetLegalMovesForSquare(pre.from.X, pre.from.Y, playerIsWhite);
                char p = rules.Board[pre.from.Y, pre.from.X];
                if (p != '.' && char.IsUpper(p) == playerIsWhite && validMoves.Contains(pre.to))
                {
                    await Task.Delay(80);
                    if (generation != gameGeneration || gameOver) return;
                    await ExecutePlayerAndBotTurnAsync(pre.from, pre.to);
                }
                else
                {
                    SetStatusBanner("Premove cancelado (lance ilegal na nova posição).", StatusBannerKind.Warning);
                    boardPanel.Invalidate();
                }
            }
        }

        private void HandlePuzzleMove(Point from, Point to, bool animateSlide = true)
        {
            if (activePuzzle == null || gameOver) return;

            string playedUci = $"{ChessRules.SquareToAlgebraic(from)}{ChessRules.SquareToAlgebraic(to)}";
            if (playedUci == activePuzzle.SolutionUci)
            {
                hintGeneration++;
                try { hintSessionCts?.Cancel(); } catch { }
                if (isHintRunning)
                {
                    isHintRunning = false;
                    engine.StopSearch();
                }

                var solvedPuzzle = activePuzzle;
                string puzzleSan = rules.ExecuteMove(from, to, 'Q');
                if (rules.GameResult == "*")
                    rules.GameResult = "1-0";

                PlayMoveSoundForSan(puzzleSan.EndsWith('#') ? puzzleSan : puzzleSan + "#");
                if (animateSlide)
                    StartMoveSlideAnimation(from, to, rules.Board[to.Y, to.X]);

                RecordCurrentPlySnapshot(99.0);
                selectedSquare = null;
                legalMoves.Clear();
                hintArrow = null;
                gameOver = true;
                UpdateClockLabels();
                UpdateActionButtonsState();
                RefreshAllVisualPanels();

                int currentIdx = StudyLibrary.Puzzles.IndexOf(solvedPuzzle);
                var nextPuzzle = StudyLibrary.Puzzles[(currentIdx + 1) % StudyLibrary.Puzzles.Count];

                SetStatusBanner(
                    $"✔ PARABÉNS! Lance correto ({playedUci}) no {solvedPuzzle.Title}!",
                    StatusBannerKind.Success,
                    "Próximo Desafio ➔",
                    () => StartPuzzle(nextPuzzle));
            }
            else
            {
                SetStatusBanner($"Lance incorreto ({playedUci}). Dica: {activePuzzle.Hint}", StatusBannerKind.Warning);
            }
        }

        private async Task RunAnalysisAndHintAsync(bool showHintArrow)
        {
            if (gameOver && showHintArrow)
            {
                SetStatusBanner("A partida já terminou. Use ← / → para revisar os lances ou clique em 'Analisar'.", StatusBannerKind.Info);
                return;
            }
            if (isBotThinking)
            {
                SetStatusBanner("Aguarde a IA terminar o lance atual antes de pedir uma dica.", StatusBannerKind.Warning);
                return;
            }
            if (isHintRunning)
            {
                SetStatusBanner("A IA já está calculando a análise desta posição...", StatusBannerKind.Info);
                return;
            }

            var targetRules = DisplayedRules.Clone();
            int generation = gameGeneration;
            int myHintGen = ++hintGeneration;
            string fenAtStart = targetRules.ToFen();
            bool sideToMove = targetRules.WhiteTurn;

            try { hintSessionCts?.Cancel(); } catch { }
            hintSessionCts = new CancellationTokenSource();
            var hintToken = hintSessionCts.Token;

            isHintRunning = true;
            UpdateActionButtonsState();
            UpdateThinkingIndicator("🔍 Analisando...", true);
            SetStatusBanner("Calculando o melhor lance tático/posicional para você...", StatusBannerKind.Info);

            EngineAnalysisResult res;
            try
            {
                if (!IsReviewingHistory && preAnalysisFen == fenAtStart && preAnalysisTask != null)
                {
                    res = await preAnalysisTask;
                    if (res.WasCancelled || !res.BestMove.HasValue)
                    {
                        res = await Task.Run(() => engine.Analyze(targetRules, sideToMove, 3, isHintOrEvaluationOnly: true, cancellationToken: hintToken));
                    }
                }
                else
                {
                    res = await Task.Run(() => engine.Analyze(targetRules, sideToMove, 3, isHintOrEvaluationOnly: true, cancellationToken: hintToken));
                }
            }
            finally
            {
                if (myHintGen == hintGeneration)
                {
                    isHintRunning = false;
                    UpdateThinkingIndicator("✔ Pronto", false);
                    UpdateActionButtonsState();
                }
            }

            if (generation != gameGeneration ||
                myHintGen != hintGeneration ||
                isBotThinking ||
                DisplayedRules.ToFen() != fenAtStart ||
                res.WasCancelled)
            {
                return;
            }

            if (res.BestMove.HasValue)
            {
                var mv = res.BestMove.Value;
                bool moveStillLegal = DisplayedRules.GetLegalMovesForSquare(mv.from.X, mv.from.Y, DisplayedRules.WhiteTurn).Contains(mv.to);
                if (!moveStillLegal) return;

                currentDisplayedEvalWhite = res.EvalWhitePawns;
                currentDisplayedEvalText = res.EvalText;

                if (showHintArrow)
                {
                    hintArrow = mv;
                }
                RefreshAllVisualPanels();

                nodesTextBox.Text = res.Nodes.ToString();
                if (!string.IsNullOrWhiteSpace(res.LogLine))
                    engineLogBox.AppendText(res.LogLine + Environment.NewLine);

                if (!showHintArrow)
                {
                    string report = EngineService.BuildPositionReport(targetRules, res);
                    engineLogBox.AppendText(report + Environment.NewLine);
                }

                string promoSuffix = res.Promotion.HasValue ? $"={char.ToUpper(res.Promotion.Value)}" : "";
                string mvStr = $"{ChessRules.SquareToAlgebraic(mv.from)} ➔ {ChessRules.SquareToAlgebraic(mv.to)}{promoSuffix}";
                string pvSan = ChessRules.FormatUciPvToSan(targetRules, res.PrincipalVariation, 6);
                string pvSuffix = !string.IsNullOrWhiteSpace(pvSan) ? $" | Linha: {pvSan}" : "";
                SetStatusBanner($"Melhor lance IA: {mvStr} (Aval: {res.EvalText}){pvSuffix}", StatusBannerKind.Info);
            }
        }

        private string BuildGameAccuracySummaryText()
        {
            if (moveEvaluations.Count == 0)
                return "Ainda não há lances avaliados nesta partida.";

            var playerMoves = moveEvaluations.Where(m => m.IsWhiteMove == playerIsWhite).ToList();
            if (playerMoves.Count == 0)
                playerMoves = moveEvaluations.ToList();

            double avgAcc = playerMoves.Average(m => m.Accuracy);
            int brilliant = playerMoves.Count(m => m.Badge == "!!");
            int best = playerMoves.Count(m => m.Badge == "!");
            int book = playerMoves.Count(m => m.Badge == "📖");
            int good = playerMoves.Count(m => string.IsNullOrEmpty(m.Badge));
            int inaccuracies = playerMoves.Count(m => m.Badge == "?!");
            int mistakes = playerMoves.Count(m => m.Badge == "?");
            int blunders = playerMoves.Count(m => m.Badge == "??");

            var worst = playerMoves
                .OrderBy(m => m.IsWhiteMove ? (m.EvalAfterWhite - m.EvalBeforeWhite) : (m.EvalBeforeWhite - m.EvalAfterWhite))
                .First();
            double worstDrop = worst.IsWhiteMove
                ? (worst.EvalAfterWhite - worst.EvalBeforeWhite)
                : (worst.EvalBeforeWhite - worst.EvalAfterWhite);

            string sideLabel = playerIsWhite ? "Brancas" : "Pretas";
            var sb = new StringBuilder();
            sb.AppendLine("=== RELATÓRIO DE ANÁLISE DA PARTIDA (IA) ===");
            sb.AppendLine($"Precisão Média ({sideLabel}): {avgAcc:0.0}% ({playerMoves.Count} lances analisados)");
            sb.AppendLine($"Abertura jogada: {StudyLibrary.DetectOpening(rules.SanMoves)}");
            sb.AppendLine();
            sb.AppendLine($"• Brilhantes (!!): {brilliant}   |   Melhores Lances (!): {best}   |   Teóricos (📖): {book}");
            sb.AppendLine($"• Lances Bons: {good}        |   Imprecisões (?!): {inaccuracies}");
            sb.AppendLine($"• Erros (?): {mistakes}           |   Capivaradas (??): {blunders}");

            if (worstDrop < -0.45)
            {
                sb.AppendLine();
                sb.AppendLine($"Momento crítico: Lance {worst.MoveNumber}. {worst.San} ({worst.Description}) — {worst.CoachNote}.");
                sb.AppendLine($"Recomendação da IA naquele momento: {worst.BestAlternative}.");
            }
            return sb.ToString().TrimEnd();
        }

        private void ShowGameAnalysisReport()
        {
            string summary = BuildGameAccuracySummaryText();
            engineLogBox.AppendText(summary + Environment.NewLine);
            ShowEndGameDialog("Relatório de Precisão da Partida (IA)", summary);
        }

        private bool CheckAndAnnounceEndGame()
        {
            if (rules.CheckEndGame(out string title, out string detail))
            {
                if (moveEvaluations.Count > 0)
                {
                    var playerMoves = moveEvaluations.Where(m => m.IsWhiteMove == playerIsWhite).ToList();
                    if (playerMoves.Count == 0) playerMoves = moveEvaluations;
                    double avgAcc = playerMoves.Average(m => m.Accuracy);
                    engineLogBox.AppendText(BuildGameAccuracySummaryText() + Environment.NewLine);
                    detail = $"{detail}\n\nPrecisão na partida: {avgAcc:0.0}%";
                }
                EndCurrentGame(detail.Replace("\n\n", " | "), StatusBannerKind.Info);
                ShowEndGameDialog(title, detail);
                return true;
            }
            return false;
        }

        private void ShowEndGameDialog(string title, string detail)
        {
            using var dialog = new Form
            {
                Text = title,
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(560, 310),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(245, 243, 235),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };

            string scoreDisplay = rules.GameResult switch
            {
                "1-0" => "1 - 0",
                "0-1" => "0 - 1",
                "1/2-1/2" => "½ - ½",
                _ => "—"
            };

            var headerPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 52,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(14, 8, 14, 4)
            };
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72f));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));

            var lblTitle = new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 55, 90),
                AutoEllipsis = true
            };

            var lblScoreBadge = new Label
            {
                Text = $"Placar: {scoreDisplay}",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(38, 62, 94),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(6, 2, 0, 2)
            };

            headerPanel.Controls.Add(lblTitle, 0, 0);
            headerPanel.Controls.Add(lblScoreBadge, 1, 0);

            var txtDetail = new TextBox
            {
                Text = detail.Replace("\n", Environment.NewLine),
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(253, 252, 248),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };

            var contentWrapper = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14, 4, 14, 6)
            };
            contentWrapper.Controls.Add(txtDetail);

            var footerFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(12, 6, 12, 8),
                BackColor = Color.FromArgb(238, 235, 224)
            };

            Button CreateDialogBtn(string text, int width, bool bold = false)
            {
                return new Button
                {
                    Text = text,
                    Size = new Size(width, 36),
                    Margin = new Padding(4, 0, 4, 0),
                    FlatStyle = FlatStyle.System,
                    Font = new Font("Segoe UI", 8.75f, bold ? FontStyle.Bold : FontStyle.Regular),
                    Cursor = Cursors.Hand
                };
            }

            var btnReview = CreateDialogBtn("Revisar Partida (Esc)", 142, bold: true);
            var btnNewGameDialog = CreateDialogBtn("Nova Partida", 116, bold: true);
            var btnAccuracy = CreateDialogBtn("Relatório de Precisão", 142);
            var btnCopyPgn = CreateDialogBtn("Copiar PGN", 104);

            bool startNew = false;
            btnNewGameDialog.Click += (s, e) =>
            {
                startNew = true;
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            };
            btnReview.Click += (s, e) =>
            {
                startNew = false;
                dialog.DialogResult = DialogResult.Cancel;
                dialog.Close();
            };
            btnAccuracy.Click += (s, e) =>
            {
                string summary = BuildGameAccuracySummaryText();
                txtDetail.Text = summary.Replace("\n", Environment.NewLine);
            };
            btnCopyPgn.Click += (s, e) =>
            {
                string engineName = engine.IsUsingStockfish ? "Stockfish IA" : "Engine Tática Interna";
                string whiteName = playerIsWhite ? "Jogador" : engineName;
                string blackName = playerIsWhite ? engineName : "Jogador";
                string pgn = rules.ToPgn(blackPlayerName: blackName, whitePlayerName: whiteName);
                if (TrySetClipboardText(pgn, out _))
                {
                    btnCopyPgn.Text = "✔ Copiado!";
                    SetStatusBanner("PGN da partida copiado para a área de transferência!", StatusBannerKind.Success);
                }
            };

            footerFlow.Controls.Add(btnReview);
            footerFlow.Controls.Add(btnNewGameDialog);
            footerFlow.Controls.Add(btnAccuracy);
            footerFlow.Controls.Add(btnCopyPgn);

            dialog.Controls.Add(contentWrapper);
            dialog.Controls.Add(headerPanel);
            dialog.Controls.Add(footerFlow);
            dialog.AcceptButton = btnReview;
            dialog.CancelButton = btnReview;

            dialog.ShowDialog(this);

            if (startNew)
            {
                ResetBoard(confirmIfInProgress: false);
            }
            else if (plySnapshots.Count > 1)
            {
                SetStatusBanner("Modo Revisão ativo: clique em um lance na lista/gráfico ou use as setas ← / →.", StatusBannerKind.Review);
            }
        }

        private void ShowHelpAndShortcutsDialog()
        {
            using var dialog = new Form
            {
                Text = "Atalhos de Teclado e Dicas de Uso",
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(580, 420),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = Color.FromArgb(245, 243, 235),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };

            var lblHeader = new Label
            {
                Text = "Guia Rápido de Atalhos e Recursos Interativos",
                Dock = DockStyle.Top,
                Height = 38,
                Padding = new Padding(14, 10, 14, 0),
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(30, 55, 90)
            };

            string helpText =
                "=== ATALHOS DE TECLADO ===\r\n" +
                "• Ctrl + N : Iniciar Novo Jogo Clássico\r\n" +
                "• Ctrl + Z : Voltar / Desfazer o último par de lances\r\n" +
                "• F1 (ou Ctrl + H) : Pedir Dica da IA (desenha seta verde da melhor jogada)\r\n" +
                "• Ctrl + F : Virar a orientação do tabuleiro\r\n" +
                "• Ctrl + O : Importar partida PGN (arquivo ou texto colado)\r\n" +
                "• Ctrl + S : Exportar / Salvar partida PGN\r\n" +
                "• Setas ← e → : Navegar lance a lance pelo histórico da partida\r\n" +
                "• Home / End : Ir para o início da partida / Voltar para a posição ao vivo\r\n" +
                "• Q, R, B, N (ou 1–4) : Escolher peça imediatamente durante a promoção de peão\r\n" +
                "• Esc : Cancelar promoção de peão ou fechar janelas de diálogo\r\n\r\n" +
                "=== RECURSOS AVANÇADOS DO TABULEIRO ===\r\n" +
                "• Clique ou Arrastar e Soltar (Drag & Drop): mova peças clicando na casa de origem e destino ou arrastando a peça diretamente.\r\n" +
                "• Premove (Jogada Antecipada): enquanto a IA está pensando, clique na sua peça e na casa de destino (destacada em azul). O lance será executado automaticamente assim que a IA responder.\r\n" +
                "• Setas e Marcações Táticas (Botão Direito): clique com o botão direito em uma casa para demarcá-la em vermelho, ou segure e arraste com o botão direito entre duas casas para desenhar setas de plano. Um clique esquerdo limpa as anotações.\r\n" +
                "• Revisão Interativa e Gráfico de Vantagem: clique em qualquer lance da tabela de histórico ou arraste sobre o Gráfico de Vantagem para inspecionar a posição e a avaliação da IA naquele momento.";

            var txtHelp = new TextBox
            {
                Text = helpText,
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(253, 252, 248),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                SelectionStart = 0,
                SelectionLength = 0
            };

            var bodyPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14, 6, 14, 6)
            };
            bodyPanel.Controls.Add(txtHelp);

            var bottomPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(14, 6, 14, 6),
                BackColor = Color.FromArgb(238, 235, 224)
            };

            var btnClose = new Button
            {
                Text = "Fechar (Esc)",
                Size = new Size(120, 34),
                FlatStyle = FlatStyle.System,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                DialogResult = DialogResult.OK
            };
            bottomPanel.Controls.Add(btnClose);

            dialog.Controls.Add(bodyPanel);
            dialog.Controls.Add(lblHeader);
            dialog.Controls.Add(bottomPanel);
            dialog.AcceptButton = btnClose;
            dialog.CancelButton = btnClose;

            dialog.ShowDialog(this);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CompletePromotionSelection(null);
            StopMoveSlideAnimation();
            gameGeneration++;
            hintGeneration++;
            try { gameSessionCts?.Cancel(); } catch { }
            try { hintSessionCts?.Cancel(); } catch { }

            try
            {
                clockTimer?.Stop();
                clockTimer?.Dispose();
            }
            catch { }

            try
            {
                animTimer?.Stop();
                animTimer?.Dispose();
            }
            catch { }

            try
            {
                mainToolTip?.Dispose();
            }
            catch { }

            try
            {
                engine.Dispose();
            }
            catch { }

            foreach (var bmp in scaledPieceCache.Values)
            {
                try { bmp.Dispose(); } catch { }
            }
            scaledPieceCache.Clear();

            foreach (var img in pieceImages.Values)
            {
                try { img.Dispose(); } catch { }
            }
            pieceImages.Clear();

            base.OnFormClosed(e);
        }
    }
}
