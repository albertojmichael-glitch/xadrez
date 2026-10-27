using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
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
        private bool isHintRunning = false;
        private bool gameOver = false;

        // "Geração" da partida: incrementada a cada reinício / FEN / puzzle / fim de jogo.
        // Toda tarefa assíncrona guarda o valor no início e o confere depois de cada await;
        // se mudou, o resultado é descartado (evita o bot jogar no tabuleiro novo).
        private int gameGeneration = 0;

        // "Geração" da dica/análise: incrementada a cada lance ou mudança de posição para
        // invalidar qualquer dica atrasada calculada para uma posição anterior.
        private int hintGeneration = 0;

        private TacticalPuzzle? activePuzzle = null;
        private int currentPuzzleIndex = 0;

        private System.Windows.Forms.Timer clockTimer = null!;
        private readonly Stopwatch clockStopwatch = new Stopwatch();
        private bool clockRunning = false;
        private string timeControlMode = "Fischer 5+3";
        private double whiteSecondsLeft = 300;
        private double blackSecondsLeft = 300;
        private double incrementSeconds = 3;
        private bool isBronsteinMode = false;
        private double turnElapsedSeconds = 0;

        private sealed record MoveEvaluationRecord(
            int MoveNumber,
            string WhiteSan,
            string Badge,
            string Description,
            double EvalBefore,
            double EvalAfter,
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

        private string currentTheme = "Madeira";

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

            var puzzlesMenu = new ToolStripMenuItem("Puzzles Táticos (15 Desafios)");
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
            searchMenu.DropDownItems.Add("Analisar Posição (IA)", null, (s, e) => RunSafe(() => RunAnalysisAndHintAsync(false)));
            searchMenu.DropDownItems.Add("Ajuda de Lance (Help to Move)", null, (s, e) => RunSafe(() => RunAnalysisAndHintAsync(true)));
            searchMenu.DropDownItems.Add(new ToolStripSeparator());
            searchMenu.DropDownItems.Add("Relatório de Precisão da Partida (IA)", null, (s, e) => ShowGameAnalysisReport());

            menuStrip.Items.Add(fileMenu);
            menuStrip.Items.Add(puzzlesMenu);
            menuStrip.Items.Add(searchMenu);
            menuStrip.Items.Add(viewMenu);
            MainMenuStrip = menuStrip;
            Controls.Add(menuStrip);

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
            AddToolbarBtn("Ajuda Lance", 320, 88, (s, e) => RunSafe(() => RunAnalysisAndHintAsync(true)));
            AddToolbarBtn("Puzzles", 412, 68, (s, e) =>
            {
                if (activePuzzle != null)
                    currentPuzzleIndex = (currentPuzzleIndex + 1) % StudyLibrary.Puzzles.Count;
                StartPuzzle(StudyLibrary.Puzzles[currentPuzzleIndex]);
            });
            AddToolbarBtn("PGN/FEN", 484, 72, (s, e) => ExportPgn());

            var lblDiff = new Label { Text = "IA:", Location = new Point(564, 15), AutoSize = true, Font = new Font("Tahoma", 8f, FontStyle.Bold) };
            difficultyCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(586, 11), Width = 112 };
            difficultyCombo.Items.AddRange(new object[] { "Fácil (Iniciante)", "Médio (Clube)", "Difícil (Forte)", "Mestre (Máximo)" });
            difficultyCombo.SelectedIndex = 0; // Começa no Fácil por padrão

            var lblClock = new Label { Text = "Relógio:", Location = new Point(704, 15), AutoSize = true, Font = new Font("Tahoma", 8f, FontStyle.Bold) };
            clockCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(754, 11), Width = 105 };
            clockCombo.Items.AddRange(new object[] { "Fischer 5+3", "Fischer 3+2", "Fischer 10+0", "Bronstein 5+3", "Sem Relógio" });
            clockCombo.SelectedIndex = 0;
            clockCombo.SelectedIndexChanged += (s, e) =>
            {
                bool gameInProgress = rules.SanMoves.Count > 0 && !gameOver && activePuzzle == null;
                ConfigureClockMode(clockCombo.SelectedItem?.ToString() ?? "Fischer 5+3", resetRemainingTime: !gameInProgress);
            };

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
            moveHistoryList.Columns.Add("White", 60);
            moveHistoryList.Columns.Add("Black", 52);
            moveHistoryList.Columns.Add("Aval.", 46);
            moveHistoryList.Columns.Add("Análise", 60);
            Controls.Add(moveHistoryList);

            var btnStartSearch = new Button
            {
                Text = "Start search",
                Location = new Point(12, 590),
                Size = new Size(95, 25),
                FlatStyle = FlatStyle.System
            };
            btnStartSearch.Click += (s, e) => RunSafe(() => RunAnalysisAndHintAsync(false));

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
                Text = "Tutor ativo | Clique em 'Ajuda Lance' para ver a seta da melhor jogada!",
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
                Font = new Font("Consolas", 8.5f)
            };
            Controls.Add(engineLogBox);
        }

        private void InitDigitalClock()
        {
            // Intervalo curto + Stopwatch de alta precisão: não atrasa se a UI travar
            // e só começa a descontar depois que o 1º lance é efetuado (clockRunning = true).
            clockTimer = new System.Windows.Forms.Timer { Interval = 200 };
            clockTimer.Tick += (s, e) =>
            {
                if (gameOver || !clockRunning || timeControlMode == "Sem Relógio" || activePuzzle != null)
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
                        EndCurrentGame("Tempo esgotado! Vitória das Pretas no relógio.");
                        MessageBox.Show("Seu tempo acabou! Vitória das Pretas por tempo.", "Relógio Esgotado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                        // EndCurrentGame descarta o lance que o bot ainda esteja calculando
                        EndCurrentGame("Tempo do bot esgotado! Vitória sua no relógio.");
                        MessageBox.Show("O tempo das Pretas acabou! Você venceu por tempo.", "Relógio Esgotado", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            // Só reinicia o tempo dos dois jogadores se a partida não estiver em andamento
            // (ou se ainda não havia tempo configurado ao sair de "Sem Relógio").
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

            // Desconta a fração de segundo decorrida desde o último tick antes de aplicar o bônus
            if (clockRunning && clockStopwatch.IsRunning && timeControlMode != "Sem Relógio")
            {
                double dt = clockStopwatch.Elapsed.TotalSeconds;
                turnElapsedSeconds += dt;
                if (forWhite) whiteSecondsLeft = Math.Max(0, whiteSecondsLeft - dt);
                else blackSecondsLeft = Math.Max(0, blackSecondsLeft - dt);
            }

            // O relógio passa a correr assim que o 1º lance é feito
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

        private void UpdateClockLabels()
        {
            if (timeControlMode == "Sem Relógio" || activePuzzle != null)
            {
                whiteClockLabel.Text = "Brancas: --:--";
                blackClockLabel.Text = "Pretas: --:--";
                return;
            }
            TimeSpan w = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, whiteSecondsLeft)));
            TimeSpan b = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, blackSecondsLeft)));
            whiteClockLabel.Text = $"Brancas: {(int)w.TotalMinutes:D2}:{w.Seconds:D2}";
            blackClockLabel.Text = $"Pretas: {(int)b.TotalMinutes:D2}:{b.Seconds:D2}";
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

        // Zera todo o estado da partida (sem mexer no tabuleiro) e invalida tarefas assíncronas antigas.
        private void ResetGameState()
        {
            gameGeneration++;          // qualquer bot/análise em andamento passa a ser descartado
            hintGeneration++;
            engine.StopSearch();       // libera o Stockfish mais rápido (ignorado se estiver ocioso)

            activePuzzle = null;
            history.Clear();
            moveEvaluations.Clear();
            isBotThinking = false;
            isHintRunning = false;
            gameOver = false;
            clockRunning = false;
            clockStopwatch.Reset();
            selectedSquare = null;
            legalMoves.Clear();
            premoveSelection = null;
            queuedPremove = null;
            markedSquares.Clear();
            drawnArrows.Clear();
            hintArrow = null;

            ConfigureClockMode(clockCombo.SelectedItem?.ToString() ?? "Fischer 5+3", resetRemainingTime: true);
            moveHistoryList.Items.Clear();
        }

        // Encerra a partida atual (desistência, empate, tempo, mate) descartando o lance do bot em cálculo.
        private void EndCurrentGame(string statusText)
        {
            gameOver = true;
            clockRunning = false;
            clockStopwatch.Reset();
            gameGeneration++;
            hintGeneration++;
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
            hintArrow = null;
            tutorStatusLabel.Text = statusText;
            boardPanel.Invalidate();
        }

        private void ResetBoard()
        {
            ResetGameState();
            rules.Reset();

            openingBookLabel.Text = "Abertura: Posição Inicial (Aguardando 1º lance)";
            string sfStatus = engine.IsUsingStockfish ? "Stockfish Oficial Conectado" : "Engine Tática Posicional Interna Ativa";
            engineLogBox.Text = $"[{sfStatus}] 15 Puzzles e Análise Pós-Lance prontos." + Environment.NewLine;
            tutorStatusLabel.Text = "Sua vez (Brancas)";

            RefreshLegalMovesTable();
            boardPanel.Invalidate();
        }

        private void StartPuzzle(TacticalPuzzle puzzle)
        {
            ResetBoard();
            if (!rules.TryLoadFen(puzzle.Fen, out string error))
            {
                rules.Reset();
                MessageBox.Show($"O puzzle \"{puzzle.Title}\" tem um FEN inválido:\n\n{error}", "Puzzle Inválido", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            int idx = StudyLibrary.Puzzles.IndexOf(puzzle);
            if (idx >= 0)
                currentPuzzleIndex = idx;

            activePuzzle = puzzle;
            openingBookLabel.Text = $"MODO PUZZLE: {puzzle.Title} [{puzzle.Theme}]";
            tutorStatusLabel.Text = $"Dica: {puzzle.Hint}";
            UpdateClockLabels();
            RefreshLegalMovesTable();
            boardPanel.Invalidate();
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
            string fen = rules.ToFen();
            if (TrySetClipboardText(fen, out string? err))
                MessageBox.Show($"FEN copiado:\n\n{fen}", "FEN Copiado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show($"Não foi possível acessar a área de transferência ({err}).\n\nFEN atual:\n{fen}", "Aviso de Área de Transferência", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            string? validatedFen = null;
            btn.Click += (s, e) =>
            {
                // Valida numa instância separada: o tabuleiro atual só é trocado depois que o
                // diálogo fechar, junto com o reset do estado (sem brecha para o bot antigo jogar).
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

        // Inicia uma partida normal a partir de um FEN já validado
        private void StartGameFromFen(string fen)
        {
            ResetGameState();          // sai do modo puzzle, destrava o fim de jogo, zera relógio e bot
            rules.LoadFen(fen);

            openingBookLabel.Text = "Abertura: Posição carregada via FEN";
            engineLogBox.AppendText($"[FEN] Posição carregada: {rules.ToFen()}" + Environment.NewLine);
            UpdateClockLabels();
            RefreshLegalMovesTable();
            boardPanel.Invalidate();

            if (CheckAndAnnounceEndGame()) return;

            if (rules.WhiteTurn)
                tutorStatusLabel.Text = "Posição carregada. Sua vez (Brancas)";
            else
                RunSafe(StartBotTurnFromPositionAsync); // pretas a jogar: o bot precisa iniciar
        }

        private void ExportPgn()
        {
            string blackName = engine.IsUsingStockfish ? "Stockfish IA" : "Engine Tática Interna";
            string? evtName = activePuzzle != null ? $"Puzzle: {activePuzzle.Title}" : null;
            string pgn = rules.ToPgn(blackPlayerName: blackName, eventName: evtName);
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
                    MessageBox.Show(msg, "PGN Exportado", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Não foi possível salvar o arquivo PGN:\n\n{ex.Message}", "Erro ao Salvar PGN", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else if (copied)
            {
                MessageBox.Show($"PGN copiado para sua área de transferência:\n\n{pgn}", "PGN Copiado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show($"Não foi possível copiar para a área de transferência ({clipErr}).\n\nPGN:\n{pgn}", "PGN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void TakebackMove()
        {
            if (isBotThinking || history.Count == 0) return;

            hintGeneration++;
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

            if (moveHistoryList.Items.Count > 0)
                moveHistoryList.Items.RemoveAt(moveHistoryList.Items.Count - 1);
            if (moveEvaluations.Count > 0)
                moveEvaluations.RemoveAt(moveEvaluations.Count - 1);

            if (rules.SanMoves.Count == 0)
            {
                clockRunning = false;
                clockStopwatch.Reset();
            }

            openingBookLabel.Text = $"Abertura: {StudyLibrary.DetectOpening(rules.SanMoves)}";
            tutorStatusLabel.Text = "Lance desfeito (Takeback). Sua vez!";
            RefreshLegalMovesTable();
            boardPanel.Invalidate();
        }

        private void ResignGame()
        {
            if (gameOver) return;
            rules.GameResult = "0-1";
            EndCurrentGame("Você desistiu. Vitória das Pretas.");
            MessageBox.Show("Você desistiu da partida. Vitória das Pretas!", "Fim de Jogo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OfferDraw()
        {
            if (gameOver) return;
            int evalWhite = EngineService.EvaluateBoardWhiteRelative(rules.Board);
            if (evalWhite >= -150)
            {
                rules.GameResult = "1/2-1/2";
                EndCurrentGame("Empate de comum acordo!");
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
            using var arrowCap = new AdjustableArrowCap(4.5f, 4.5f, true);
            pen.StartCap = LineCap.Round;
            pen.CustomEndCap = arrowCap;
            g.DrawLine(pen, p1, p2);
        }

        // Converte coordenadas do mouse em casa do tabuleiro. Retorna null fora das 64 casas.
        // A checagem explícita da margem é necessária porque a divisão inteira arredonda
        // para zero: um clique em x = 10 daria (10 - 24) / 62 = 0, ou seja, a coluna "a".
        private static Point? ScreenToSquare(int x, int y)
        {
            if (x < BoardOffset || y < BoardOffset) return null;
            int col = (x - BoardOffset) / SquareSize;
            int row = (y - BoardOffset) / SquareSize;
            if (col > 7 || row > 7) return null;
            return new Point(col, row);
        }

        private void BoardPanel_MouseDown(object? sender, MouseEventArgs e)
        {
            var sq = ScreenToSquare(e.X, e.Y);
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
                    var fromSq = selectedSquare.Value;
                    RunSafe(() => ExecutePlayerAndBotTurnAsync(fromSq, clicked));
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

        // Dados do lance do jogador, usados para classificar a precisão depois que o bot responder
        private sealed record PlayerMoveInfo(GameSnapshot Before, Point From, Point To, char? Promotion, string San, bool IsBookMove);

        // Executa uma tarefa assíncrona da UI sem perder exceções em silêncio (substitui o "_ = ...")
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
                tutorStatusLabel.Text = $"{context}: {ex.Message}";
            }
            catch { /* formulário já descartado */ }
        }

        private async Task ExecutePlayerAndBotTurnAsync(Point from, Point to)
        {
            // Se estiver no Modo Puzzle, valida e avança automaticamente para o próximo!
            if (activePuzzle != null)
            {
                HandlePuzzleMove(from, to);
                return;
            }

            if (gameOver || isBotThinking || !rules.WhiteTurn) return;

            int generation = gameGeneration;
            var preMoveSnapshot = rules.CreateSnapshot();

            char? promoPiece = null;
            if (rules.Board[from.Y, from.X] == 'P' && to.Y == 0)
                promoPiece = PromotionDialog.AskPiece(true, this);

            // O diálogo de promoção é modal: o relógio pode ter estourado enquanto estava aberto
            if (generation != gameGeneration || gameOver) return;

            // Qualquer dica em andamento para a posição anterior deve ser abortada e descartada
            hintGeneration++;
            if (isHintRunning)
            {
                isHintRunning = false;
                engine.StopSearch();
            }

            history.Push(preMoveSnapshot);

            int moveNum = rules.FullMoveNumber;
            string whiteSan = rules.ExecuteMove(from, to, promoPiece);
            bool isBookMove = StudyLibrary.IsBookMove(rules.SanMoves);
            ApplyClockIncrement(true);

            selectedSquare = null;
            legalMoves.Clear();
            hintArrow = null;
            openingBookLabel.Text = $"Abertura: {StudyLibrary.DetectOpening(rules.SanMoves)}";
            boardPanel.Invalidate();

            var rowItem = new ListViewItem(moveNum.ToString()) { UseItemStyleForSubItems = false };
            rowItem.SubItems.Add(whiteSan);
            rowItem.SubItems.Add("...");
            rowItem.SubItems.Add("");
            rowItem.SubItems.Add("");
            moveHistoryList.Items.Add(rowItem);
            rowItem.EnsureVisible();

            // Se o próprio lance das Brancas encerrou a partida (ex.: xeque-mate), registra na análise antes de anunciar
            if (whiteSan.EndsWith('#'))
            {
                rowItem.SubItems[1].Text = $"{whiteSan} !";
                rowItem.SubItems[1].ForeColor = Color.FromArgb(20, 150, 80);
                rowItem.SubItems[2].Text = "-";
                rowItem.SubItems[3].Text = "#+";
                rowItem.SubItems[4].Text = "Xeque-Mate!";
                rowItem.SubItems[4].ForeColor = Color.FromArgb(20, 150, 80);
                moveEvaluations.Add(new MoveEvaluationRecord(moveNum, whiteSan, "!", "Xeque-Mate!", 10.0, 99.0, 100.0, "-", "lance decisivo de xeque-mate"));
            }

            if (CheckAndAnnounceEndGame()) return;

            await RunBotTurnAsync(generation, rowItem, new PlayerMoveInfo(preMoveSnapshot, from, to, promoPiece, whiteSan, isBookMove));
        }

        // Usado quando a partida começa com as Pretas para jogar (ex.: FEN carregado com "b")
        private async Task StartBotTurnFromPositionAsync()
        {
            if (gameOver || isBotThinking || rules.WhiteTurn) return;

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
            tutorStatusLabel.Text = playerMove != null
                ? "Analisando a precisão do seu lance e calculando resposta do Bot..."
                : "Bot (Pretas) calculando o lance...";

            bool botTurnCompleted = false;
            try
            {
                await Task.Delay(120);
                if (generation != gameGeneration) return;

                int diffIdx = difficultyCombo.SelectedIndex;

                // 1. Avalia qual era o melhor lance das Brancas ANTES da sua jogada (sempre em força máxima)
                EngineAnalysisResult? bestWhiteBefore = null;
                ChessRules? rulesBefore = null;
                if (playerMove != null)
                {
                    rulesBefore = new ChessRules();
                    rulesBefore.RestoreSnapshot(playerMove.Before);
                    bestWhiteBefore = await Task.Run(() => engine.Analyze(rulesBefore, true, diffIdx, isHintOrEvaluationOnly: true));
                    if (generation != gameGeneration) return;
                }

                // 2. Avalia a posição DEPOIS da sua jogada em FORÇA MÁXIMA (isHintOrEvaluationOnly: true)
                //    para classificar o lance das Brancas sem a distorção do bot enfraquecido (Fácil/Médio).
                var botRules = rules.Clone();
                EngineAnalysisResult evalAfterAnalysis;
                EngineAnalysisResult botAnalysis;

                if (playerMove != null && diffIdx < 3)
                {
                    evalAfterAnalysis = await Task.Run(() => engine.Analyze(botRules, false, diffIdx, isHintOrEvaluationOnly: true));
                    if (generation != gameGeneration) return;

                    var botPlayCopy = rules.Clone();
                    botAnalysis = await Task.Run(() => engine.Analyze(botPlayCopy, false, diffIdx, isHintOrEvaluationOnly: false));
                    if (generation != gameGeneration) return;
                }
                else
                {
                    botAnalysis = await Task.Run(() => engine.Analyze(botRules, false, diffIdx, isHintOrEvaluationOnly: false));
                    if (generation != gameGeneration) return;
                    evalAfterAnalysis = botAnalysis;
                }

                bool samePromo = bestWhiteBefore?.Promotion == null ||
                                 playerMove?.Promotion == null ||
                                 char.ToUpper(bestWhiteBefore.Promotion.Value) == char.ToUpper(playerMove.Promotion.Value);

                bool playedBest = playerMove != null &&
                                  bestWhiteBefore?.BestMove != null &&
                                  bestWhiteBefore.BestMove.Value.from == playerMove.From &&
                                  bestWhiteBefore.BestMove.Value.to == playerMove.To &&
                                  samePromo;

                (string badge, string description, Color color) classification = ("", "", Color.Black);
                string coachNote = "";
                string bestAlternativeStr = "-";

                if (playerMove != null && bestWhiteBefore != null && rulesBefore != null)
                {
                    bool isTactical = playerMove.San.Contains('x') || playerMove.San.Contains('+');
                    classification = EngineService.ClassifyMove(
                        bestWhiteBefore.EvalWhitePawns,
                        evalAfterAnalysis.EvalWhitePawns,
                        playedBest,
                        playerMove.IsBookMove,
                        isTactical);

                    if (!string.IsNullOrEmpty(classification.badge))
                    {
                        rowItem.SubItems[1].Text = $"{playerMove.San} {classification.badge}";
                        rowItem.SubItems[1].ForeColor = classification.color;
                    }
                    rowItem.SubItems[4].Text = classification.description;
                    rowItem.SubItems[4].ForeColor = classification.color;

                    double moveAcc = EngineService.ComputeMoveAccuracy(
                        bestWhiteBefore.EvalWhitePawns,
                        evalAfterAnalysis.EvalWhitePawns,
                        playedBest,
                        playerMove.IsBookMove);

                    if (bestWhiteBefore.BestMove.HasValue)
                        bestAlternativeStr = EngineService.FormatMoveUciOrAlg(bestWhiteBefore.BestMove.Value.from, bestWhiteBefore.BestMove.Value.to, bestWhiteBefore.Promotion);

                    coachNote = EngineService.ExplainMoveCoach(
                        rulesBefore,
                        playerMove.From,
                        playerMove.To,
                        playerMove.Promotion,
                        bestWhiteBefore,
                        evalAfterAnalysis,
                        playedBest,
                        playerMove.IsBookMove);

                    int moveNumber = int.TryParse(rowItem.Text, out int mn) ? mn : rules.FullMoveNumber;
                    moveEvaluations.Add(new MoveEvaluationRecord(
                        moveNumber,
                        playerMove.San,
                        classification.badge,
                        classification.description,
                        bestWhiteBefore.EvalWhitePawns,
                        evalAfterAnalysis.EvalWhitePawns,
                        moveAcc,
                        bestAlternativeStr,
                        coachNote));
                }

                // Confere se o lance devolvido é legal na posição atual antes de executar
                var botMove = botAnalysis.BestMove;
                char? botPromo = botAnalysis.Promotion;
                if (!botMove.HasValue || !rules.GetLegalMovesForSquare(botMove.Value.from.X, botMove.Value.from.Y, false).Contains(botMove.Value.to)
                    || rules.Board[botMove.Value.from.Y, botMove.Value.from.X] == '.' || char.IsUpper(rules.Board[botMove.Value.from.Y, botMove.Value.from.X]))
                {
                    var legal = rules.GetAllLegalMoves(false);
                    if (legal.Count == 0)
                        throw new InvalidOperationException("O bot não tem lances legais nesta posição.");
                    botMove = legal[Random.Shared.Next(legal.Count)];
                    botPromo = null;
                    engineLogBox.AppendText("[Aviso] A engine não devolveu um lance legal; usando um lance alternativo." + Environment.NewLine);
                }

                // Respeita a peça de promoção escolhida pelo Stockfish / motor interno (ex.: e2e1n ou e2e1q)
                string blackSan = rules.ExecuteMove(botMove.Value.from, botMove.Value.to, botPromo ?? 'q');
                ApplyClockIncrement(false);
                rowItem.SubItems[2].Text = blackSan;
                rowItem.SubItems[3].Text = evalAfterAnalysis.EvalText;

                nodesTextBox.Text = (evalAfterAnalysis.Nodes + botAnalysis.Nodes).ToString();
                if (!string.IsNullOrWhiteSpace(evalAfterAnalysis.LogLine))
                    engineLogBox.AppendText(evalAfterAnalysis.LogLine + Environment.NewLine);

                isBotThinking = false;
                openingBookLabel.Text = $"Abertura: {StudyLibrary.DetectOpening(rules.SanMoves)}";
                RefreshLegalMovesTable();
                boardPanel.Invalidate();

                if (CheckAndAnnounceEndGame()) return;

                if (playerMove != null && bestWhiteBefore != null)
                {
                    double avgAcc = moveEvaluations.Count > 0 ? moveEvaluations.Average(m => m.Accuracy) : 100.0;
                    string bestSuggestion = (!playedBest && !playerMove.IsBookMove && bestWhiteBefore.BestMove.HasValue)
                        ? $" | Melhor: {bestAlternativeStr}"
                        : "";
                    tutorStatusLabel.Text = $"{classification.description} {classification.badge} ({coachNote}){bestSuggestion} | Aval: {evalAfterAnalysis.EvalText} | Precisão: {avgAcc:0.0}%";
                }
                else
                {
                    tutorStatusLabel.Text = $"Bot jogou {blackSan}. Sua vez (Brancas) | Aval: {evalAfterAnalysis.EvalText}";
                }

                botTurnCompleted = true;
            }
            catch (Exception ex)
            {
                if (generation == gameGeneration)
                    ReportError("Erro ao calcular o lance do bot (use 'Voltar Lance' ou 'Reiniciar')", ex);
            }
            finally
            {
                // Nunca deixa o jogo congelado em "Analisando...". Só mexe na flag se a partida
                // ainda for a mesma (senão o reset já a zerou e outra partida pode estar usando).
                if (generation == gameGeneration)
                {
                    isBotThinking = false;
                    RefreshLegalMovesTable();
                    boardPanel.Invalidate();
                }
            }

            if (!botTurnCompleted || generation != gameGeneration || gameOver) return;

            if (queuedPremove.HasValue)
            {
                var pre = queuedPremove.Value;
                queuedPremove = null;
                var validMoves = rules.GetLegalMovesForSquare(pre.from.X, pre.from.Y, true);
                if (rules.Board[pre.from.Y, pre.from.X] != '.' && char.IsUpper(rules.Board[pre.from.Y, pre.from.X]) && validMoves.Contains(pre.to))
                {
                    await Task.Delay(100);
                    if (generation != gameGeneration || gameOver) return;
                    await ExecutePlayerAndBotTurnAsync(pre.from, pre.to);
                }
                else
                {
                    tutorStatusLabel.Text = "Premove cancelado (lance ilegal na nova posição).";
                    boardPanel.Invalidate();
                }
            }
        }

        private void HandlePuzzleMove(Point from, Point to)
        {
            if (activePuzzle == null || gameOver) return;

            string playedUci = $"{ChessRules.SquareToAlgebraic(from)}{ChessRules.SquareToAlgebraic(to)}";
            if (playedUci == activePuzzle.SolutionUci)
            {
                hintGeneration++;
                if (isHintRunning)
                {
                    isHintRunning = false;
                    engine.StopSearch();
                }

                var solvedPuzzle = activePuzzle;
                rules.ExecuteMove(from, to, 'Q');
                if (rules.GameResult == "*")
                    rules.GameResult = "1-0";

                selectedSquare = null;
                legalMoves.Clear();
                hintArrow = null;
                gameOver = true; // puzzle concluído: trava o tabuleiro até escolher outro desafio
                RefreshLegalMovesTable();
                boardPanel.Invalidate();
                tutorStatusLabel.Text = $"PARABÉNS! Você acertou o {solvedPuzzle.Title}!";

                int currentIdx = StudyLibrary.Puzzles.IndexOf(solvedPuzzle);
                var nextPuzzle = StudyLibrary.Puzzles[(currentIdx + 1) % StudyLibrary.Puzzles.Count];
                var ans = MessageBox.Show($"Excelente visão tática! Lance correto ({playedUci}).\n\nDeseja ir para o próximo desafio ({nextPuzzle.Title})?", "Puzzle Resolvido!", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (ans == DialogResult.Yes && activePuzzle == solvedPuzzle)
                    StartPuzzle(nextPuzzle);
            }
            else
            {
                tutorStatusLabel.Text = $"Lance incorreto ({playedUci}). Dica: {activePuzzle.Hint}";
            }
        }

        private async Task RunAnalysisAndHintAsync(bool showHintArrow)
        {
            if (gameOver || isBotThinking || isHintRunning) return;

            int generation = gameGeneration;
            int myHintGen = ++hintGeneration;
            int moveCountAtStart = rules.SanMoves.Count;
            string fenAtStart = rules.ToFen();
            var analysisRules = rules.Clone(); // cópia: o jogador pode mover enquanto a análise roda
            bool sideToMove = rules.WhiteTurn;

            isHintRunning = true;
            tutorStatusLabel.Text = "Calculando o melhor lance tático/posicional para você...";

            EngineAnalysisResult res;
            try
            {
                res = await Task.Run(() => engine.Analyze(analysisRules, sideToMove, 3, isHintOrEvaluationOnly: true));
            }
            finally
            {
                if (myHintGen == hintGeneration)
                    isHintRunning = false;
            }

            // Descarta a sugestão se a partida mudou, se o jogador se moveu ou se o bot já começou a pensar
            if (generation != gameGeneration ||
                myHintGen != hintGeneration ||
                gameOver ||
                isBotThinking ||
                rules.SanMoves.Count != moveCountAtStart ||
                rules.ToFen() != fenAtStart)
            {
                return;
            }

            if (res.BestMove.HasValue)
            {
                var mv = res.BestMove.Value;
                bool moveStillLegal = rules.GetLegalMovesForSquare(mv.from.X, mv.from.Y, rules.WhiteTurn).Contains(mv.to);
                if (!moveStillLegal) return;

                if (showHintArrow)
                {
                    hintArrow = mv;
                    boardPanel.Invalidate();
                }
                nodesTextBox.Text = res.Nodes.ToString();
                if (!string.IsNullOrWhiteSpace(res.LogLine))
                    engineLogBox.AppendText(res.LogLine + Environment.NewLine);

                if (!showHintArrow)
                {
                    string report = EngineService.BuildPositionReport(analysisRules, res);
                    engineLogBox.AppendText(report + Environment.NewLine);
                }

                string promoSuffix = res.Promotion.HasValue ? $"={char.ToUpper(res.Promotion.Value)}" : "";
                string mvStr = $"{ChessRules.SquareToAlgebraic(mv.from)} -> {ChessRules.SquareToAlgebraic(mv.to)}{promoSuffix}";
                string topCandidates = res.TopMovesSummary.Count > 1 ? $" | Candidatos: {string.Join(" / ", res.TopMovesSummary)}" : "";
                tutorStatusLabel.Text = $"Melhor lance IA: {mvStr} (Aval: {res.EvalText}){topCandidates}";
            }
        }

        private string BuildGameAccuracySummaryText()
        {
            if (moveEvaluations.Count == 0)
                return "Ainda não há lances avaliados nesta partida.";

            double avgAcc = moveEvaluations.Average(m => m.Accuracy);
            int brilliant = moveEvaluations.Count(m => m.Badge == "!!");
            int best = moveEvaluations.Count(m => m.Badge == "!");
            int book = moveEvaluations.Count(m => m.Badge == "📖");
            int good = moveEvaluations.Count(m => string.IsNullOrEmpty(m.Badge));
            int inaccuracies = moveEvaluations.Count(m => m.Badge == "?!");
            int mistakes = moveEvaluations.Count(m => m.Badge == "?");
            int blunders = moveEvaluations.Count(m => m.Badge == "??");

            var worst = moveEvaluations.OrderBy(m => m.EvalAfter - m.EvalBefore).First();
            double worstDrop = worst.EvalAfter - worst.EvalBefore;

            var sb = new StringBuilder();
            sb.AppendLine($"=== RELATÓRIO DE ANÁLISE DA PARTIDA (IA) ===");
            sb.AppendLine($"Precisão Média das Brancas: {avgAcc:0.1}% ({moveEvaluations.Count} lances analisados)");
            sb.AppendLine($"Abertura jogada: {StudyLibrary.DetectOpening(rules.SanMoves)}");
            sb.AppendLine();
            sb.AppendLine($"• Brilhantes (!!): {brilliant}   |   Melhores Lances (!): {best}   |   Teóricos (📖): {book}");
            sb.AppendLine($"• Lances Bons: {good}        |   Imprecisões (?!): {inaccuracies}");
            sb.AppendLine($"• Erros (?): {mistakes}           |   Capivaradas (??): {blunders}");

            if (worstDrop < -0.45)
            {
                sb.AppendLine();
                sb.AppendLine($"Momento crítico: Lance {worst.MoveNumber}. {worst.WhiteSan} ({worst.Description}) — {worst.CoachNote}.");
                sb.AppendLine($"Recomendação da IA naquele momento: {worst.BestAlternative}.");
            }
            return sb.ToString().TrimEnd();
        }

        private void ShowGameAnalysisReport()
        {
            string summary = BuildGameAccuracySummaryText();
            engineLogBox.AppendText(summary + Environment.NewLine);
            MessageBox.Show(summary, "Relatório de Precisão da Partida (IA)", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private bool CheckAndAnnounceEndGame()
        {
            if (rules.CheckEndGame(out string title, out string detail))
            {
                if (moveEvaluations.Count > 0)
                {
                    double avgAcc = moveEvaluations.Average(m => m.Accuracy);
                    engineLogBox.AppendText(BuildGameAccuracySummaryText() + Environment.NewLine);
                    detail = $"{detail}\n\nPrecisão das Brancas na partida: {avgAcc:0.1}%";
                }
                EndCurrentGame(detail.Replace("\n\n", " | "));
                MessageBox.Show(detail, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
            return false;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            gameGeneration++;
            hintGeneration++;
            try
            {
                clockTimer?.Stop();
                clockTimer?.Dispose();
            }
            catch { }

            try
            {
                engine.Dispose();
            }
            catch { }

            foreach (var img in pieceImages.Values)
            {
                try { img.Dispose(); } catch { }
            }
            pieceImages.Clear();

            base.OnFormClosed(e);
        }
    }
}