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

        public MainForm()
        {
            Text = "MainForm - Lucas Chess & Classic Study Suite";
            ClientSize = new Size(960, 760);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(236, 233, 216);
            Font = new Font("Tahoma", 8.25f, FontStyle.Regular);

            boardView = new BoardView(game.Rules)
            {
                Location = new Point(12, 102)
            };
            boardView.SquareLeftClicked += OnBoardSquareLeftClicked;

            clock = new ClockController(
                () => game.Rules.WhiteTurn,
                () => game.GameOver,
                () => game.ActivePuzzle != null);
            clock.ClockUpdated += UpdateClockLabels;
            clock.TimeExpired += OnClockTimeExpired;

            InitClassicUI();
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
                if (game.ActivePuzzle != null)
                    game.CurrentPuzzleIndex = (game.CurrentPuzzleIndex + 1) % StudyLibrary.Puzzles.Count;
                StartPuzzle(StudyLibrary.Puzzles[game.CurrentPuzzleIndex]);
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
                bool gameInProgress = game.Rules.SanMoves.Count > 0 && !game.GameOver && game.ActivePuzzle == null;
                clock.ConfigureClockMode(clockCombo.SelectedItem?.ToString() ?? "Fischer 5+3", resetRemainingTime: !gameInProgress);
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

            Controls.Add(boardView);

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
            btnAbortSearch.Click += (s, e) => analysis.StopSearch();

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

        private void UpdateClockLabels()
        {
            var (w, b) = clock.GetFormattedLabels();
            whiteClockLabel.Text = w;
            blackClockLabel.Text = b;
        }

        private void OnClockTimeExpired(bool whiteExpired)
        {
            if (whiteExpired)
            {
                game.Rules.GameResult = "0-1";
                EndCurrentGame("Tempo esgotado! Vitória das Pretas no relógio.");
                MessageBox.Show("Seu tempo acabou! Vitória das Pretas por tempo.", "Relógio Esgotado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                game.Rules.GameResult = "1-0";
                EndCurrentGame("Tempo do bot esgotado! Vitória sua no relógio.");
                MessageBox.Show("O tempo das Pretas acabou! Você venceu por tempo.", "Relógio Esgotado", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

            openingBookLabel.Text = "Abertura: Posição Inicial (Aguardando 1º lance)";
            string sfStatus = analysis.IsUsingStockfish ? "Stockfish Oficial Conectado" : "Engine Tática Posicional Interna Ativa";
            engineLogBox.Text = $"[{sfStatus}] 15 Puzzles e Análise Pós-Lance prontos." + Environment.NewLine;
            tutorStatusLabel.Text = "Sua vez (Brancas)";

            RefreshLegalMovesTable();
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
            openingBookLabel.Text = $"MODO PUZZLE: {puzzle.Title} [{puzzle.Theme}]";
            tutorStatusLabel.Text = $"Dica: {puzzle.Hint}";
            UpdateClockLabels();
            RefreshLegalMovesTable();
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
            var txt = new TextBox { Text = game.Rules.ToFen(), Location = new Point(12, 18), Width = 495 };
            var btn = new Button { Text = "Carregar FEN", Location = new Point(400, 55), Size = new Size(105, 32) };
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

            openingBookLabel.Text = "Abertura: Posição carregada via FEN";
            engineLogBox.AppendText($"[FEN] Posição carregada: {game.Rules.ToFen()}" + Environment.NewLine);
            UpdateClockLabels();
            RefreshLegalMovesTable();
            boardView.Invalidate();

            if (CheckAndAnnounceEndGame()) return;

            if (game.Rules.WhiteTurn)
                tutorStatusLabel.Text = "Posição carregada. Sua vez (Brancas)";
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
            if (!game.TryTakeback(analysis, clock, boardView)) return;

            if (moveHistoryList.Items.Count > 0)
                moveHistoryList.Items.RemoveAt(moveHistoryList.Items.Count - 1);

            openingBookLabel.Text = $"Abertura: {StudyLibrary.DetectOpening(game.Rules.SanMoves)}";
            tutorStatusLabel.Text = "Lance desfeito (Takeback). Sua vez!";
            RefreshLegalMovesTable();
            boardView.Invalidate();
        }

        private void ResignGame()
        {
            if (game.GameOver) return;
            game.Rules.GameResult = "0-1";
            EndCurrentGame("Você desistiu. Vitória das Pretas.");
            MessageBox.Show("Você desistiu da partida. Vitória das Pretas!", "Fim de Jogo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OfferDraw()
        {
            if (game.GameOver) return;
            if (game.EvaluateDrawOffer())
            {
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

            if (game.IsBotThinking || !game.Rules.WhiteTurn)
            {
                if (boardView.PremoveSelection.HasValue && boardView.PremoveSelection.Value != clicked)
                {
                    boardView.QueuedPremove = (boardView.PremoveSelection.Value, clicked);
                    boardView.PremoveSelection = null;
                    tutorStatusLabel.Text = $"Jogada automática (Premove) agendada: {ChessRules.SquareToAlgebraic(boardView.QueuedPremove.Value.from)}->{ChessRules.SquareToAlgebraic(boardView.QueuedPremove.Value.to)}";
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
            if (p != '.' && char.IsUpper(p))
            {
                boardView.SelectedSquare = clicked;
                boardView.LegalMoves = game.Rules.GetLegalMovesForSquare(col, row, true);
            }
            else
            {
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
                tutorStatusLabel.Text = $"{context}: {ex.Message}";
            }
            catch { /* formulário já descartado */ }
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
                promoPiece = PromotionDialog.AskPiece(true, this);

            if (generation != game.GameGeneration || game.GameOver) return;

            game.CancelRunningHint(analysis);
            game.History.Push(preMoveSnapshot);

            int moveNum = game.Rules.FullMoveNumber;
            string whiteSan = game.Rules.ExecuteMove(from, to, promoPiece);
            bool isBookMove = StudyLibrary.IsBookMove(game.Rules.SanMoves);
            clock.ApplyClockIncrement(true);

            boardView.ClearSelectionAndHint();
            openingBookLabel.Text = $"Abertura: {StudyLibrary.DetectOpening(game.Rules.SanMoves)}";
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
                rowItem.SubItems[4].Text = "Xeque-Mate!";
                rowItem.SubItems[4].ForeColor = Color.FromArgb(20, 150, 80);
                analysis.RecordCheckmateMove(moveNum, whiteSan);
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
                ? "Analisando a precisão do seu lance e calculando resposta do Bot..."
                : "Bot (Pretas) calculando o lance...";

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
                }

                var (botFrom, botTo, botPromo, usedFallback) = game.EnsureValidBotMove(
                    evalSummary.BotAnalysis.BestMove,
                    evalSummary.BotAnalysis.Promotion);

                if (usedFallback)
                    engineLogBox.AppendText("[Aviso] A engine não devolveu um lance legal; usando um lance alternativo." + Environment.NewLine);

                string blackSan = game.Rules.ExecuteMove(botFrom, botTo, botPromo ?? 'q');
                clock.ApplyClockIncrement(false);
                rowItem.SubItems[2].Text = blackSan;
                rowItem.SubItems[3].Text = evalSummary.EvalAfterAnalysis.EvalText;

                nodesTextBox.Text = (evalSummary.EvalAfterAnalysis.Nodes + evalSummary.BotAnalysis.Nodes).ToString();
                if (!string.IsNullOrWhiteSpace(evalSummary.EvalAfterAnalysis.LogLine))
                    engineLogBox.AppendText(evalSummary.EvalAfterAnalysis.LogLine + Environment.NewLine);

                game.IsBotThinking = false;
                openingBookLabel.Text = $"Abertura: {StudyLibrary.DetectOpening(game.Rules.SanMoves)}";
                RefreshLegalMovesTable();
                boardView.Invalidate();

                if (CheckAndAnnounceEndGame()) return;

                if (playerMove != null && evalSummary.BestWhiteBefore != null)
                {
                    double avgAcc = analysis.GetAverageAccuracy();
                    string bestSuggestion = (!evalSummary.PlayedBest && !playerMove.IsBookMove && evalSummary.BestWhiteBefore.BestMove.HasValue)
                        ? $" | Melhor: {evalSummary.BestAlternativeStr}"
                        : "";
                    tutorStatusLabel.Text = $"{evalSummary.Classification.description} {evalSummary.Classification.badge} ({evalSummary.CoachNote}){bestSuggestion} | Aval: {evalSummary.EvalAfterAnalysis.EvalText} | Precisão: {avgAcc:0.0}%";
                }
                else
                {
                    tutorStatusLabel.Text = $"Bot jogou {blackSan}. Sua vez (Brancas) | Aval: {evalSummary.EvalAfterAnalysis.EvalText}";
                }

                botTurnCompleted = true;
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
                    tutorStatusLabel.Text = "Premove cancelado (lance ilegal na nova posição).";
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
                game.GameOver = true;
                RefreshLegalMovesTable();
                boardView.Invalidate();
                tutorStatusLabel.Text = $"PARABÉNS! Você acertou o {solvedPuzzle.Title}!";

                int currentIdx = StudyLibrary.Puzzles.IndexOf(solvedPuzzle);
                var nextPuzzle = StudyLibrary.Puzzles[(currentIdx + 1) % StudyLibrary.Puzzles.Count];
                var ans = MessageBox.Show($"Excelente visão tática! Lance correto ({playedUci}).\n\nDeseja ir para o próximo desafio ({nextPuzzle.Title})?", "Puzzle Resolvido!", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (ans == DialogResult.Yes && game.ActivePuzzle == solvedPuzzle)
                    StartPuzzle(nextPuzzle);
            }
            else
            {
                tutorStatusLabel.Text = $"Lance incorreto ({playedUci}). Dica: {game.ActivePuzzle.Hint}";
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
            tutorStatusLabel.Text = "Calculando o melhor lance tático/posicional para você...";

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
                    boardView.Invalidate();
                }
                nodesTextBox.Text = res.Nodes.ToString();
                if (!string.IsNullOrWhiteSpace(res.LogLine))
                    engineLogBox.AppendText(res.LogLine + Environment.NewLine);

                if (!showHintArrow)
                {
                    string report = analysis.BuildPositionReport(analysisRules, res);
                    engineLogBox.AppendText(report + Environment.NewLine);
                }

                string promoSuffix = res.Promotion.HasValue ? $"={char.ToUpper(res.Promotion.Value)}" : "";
                string mvStr = $"{ChessRules.SquareToAlgebraic(mv.from)} -> {ChessRules.SquareToAlgebraic(mv.to)}{promoSuffix}";
                string topCandidates = res.TopMovesSummary.Count > 1 ? $" | Candidatos: {string.Join(" / ", res.TopMovesSummary)}" : "";
                tutorStatusLabel.Text = $"Melhor lance IA: {mvStr} (Aval: {res.EvalText}){topCandidates}";
            }
        }

        private void ShowGameAnalysisReport()
        {
            string summary = analysis.BuildGameAccuracySummaryText(game.Rules.SanMoves);
            engineLogBox.AppendText(summary + Environment.NewLine);
            MessageBox.Show(summary, "Relatório de Precisão da Partida (IA)", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private bool CheckAndAnnounceEndGame()
        {
            if (game.Rules.CheckEndGame(out string title, out string detail))
            {
                if (analysis.MoveEvaluations.Count > 0)
                {
                    double avgAcc = analysis.GetAverageAccuracy();
                    engineLogBox.AppendText(analysis.BuildGameAccuracySummaryText(game.Rules.SanMoves) + Environment.NewLine);
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
            game.InvalidateGenerations();
            clock.Dispose();
            analysis.Dispose();
            boardView.Dispose();
            base.OnFormClosed(e);
        }
    }
}