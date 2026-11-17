using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace XadrezApp
{
    public partial class MainForm
    {
        private readonly GameArchive archive = new();
        private Guid archiveId;
        private DateTimeOffset gameStartedAt;
        private string[]? savedGameDetails;
        private IReadOnlyList<PuzzleStep> puzzleSteps = Array.Empty<PuzzleStep>();
        private int puzzlePly;

        private void SaveGameToHistory()
        {
            if (game.Rules.SanMoves.Count == 0 || game.ActivePuzzle != null || activeTutorial != null)
            {
                MessageBox.Show(this, "Jogue pelo menos um lance de uma partida normal para salvá-la.");
                return;
            }

            var rules = game.Rules.Clone();
            var evaluations = analysis.MoveEvaluations.ToArray();
            Guid id = archiveId;
            DateTimeOffset startedAt = gameStartedAt;
            var values = EditFields("Salvar partida no histórico", new[] { "Brancas", "Pretas", "Evento" },
                savedGameDetails ?? new[] { "Jogador", analysis.IsUsingStockfish ? "Stockfish" : "Engine interna", "Partida clássica" },
                entries => entries.Any(string.IsNullOrWhiteSpace) ? "Preencha os nomes e o evento." : null);
            if (values == null) return;

            try
            {
                archive.Save(GameArchive.Capture(id, rules, startedAt, values[0], values[1], values[2], evaluations));
                savedGameDetails = values;
                tutorStatusLabel.Text = "Partida salva no histórico.";
            }
            catch (IOException ex) { ShowLibraryError(ex); }
            catch (UnauthorizedAccessException ex) { ShowLibraryError(ex); }
        }

        private void BrowseHistory()
        {
            IReadOnlyList<ArchivedGame> games;
            try { games = archive.ReadAll(); }
            catch (IOException ex) { ShowLibraryError(ex); return; }
            catch (UnauthorizedAccessException ex) { ShowLibraryError(ex); return; }

            using var dialog = CreateBrowser("Histórico de partidas", out var filters, out var list, out var details, out var buttons);
            var query = AddSearchBox(filters, "Jogadores, evento, data ou resultado");
            var openings = AddFilter(filters, "Todas as aberturas", games.Select(x => x.Opening));
            ArchivedGame? Selected() => (list.SelectedItem as Choice<ArchivedGame>)?.Value;

            void Refresh() => FillList(list, GameArchive.Filter(games, query.Text, FilterValue(openings)),
                entry => $"{entry.StartedAt:dd/MM/yyyy HH:mm}  {entry.White} × {entry.Black}  {entry.Result}");

            var pgn = AddButton(buttons, "Exportar PGN comentado", () =>
            {
                if (Selected() is ArchivedGame item) SaveExport(dialog, item.Pgn, "pgn", "PGN");
            });
            var html = AddButton(buttons, "Exportar relatório HTML", () =>
            {
                if (Selected() is ArchivedGame item) SaveExport(dialog, item.ToHtml(), "html", "HTML");
            });
            list.SelectedIndexChanged += (_, _) =>
            {
                var item = Selected();
                pgn.Enabled = html.Enabled = item != null;
                details.Text = item == null ? "Nenhuma partida encontrada." :
                    $"{item.Event}  ·  {item.Opening}\r\n{item.Analysis.Length} lance(s) avaliados pela IA\r\n\r\n{item.Pgn}";
            };
            query.TextChanged += (_, _) => Refresh();
            openings.SelectedIndexChanged += (_, _) => Refresh();
            Refresh();
            pgn.Enabled = html.Enabled = Selected() != null;
            if (Selected() == null) details.Text = "Nenhuma partida encontrada.";
            dialog.ShowDialog(this);
        }

        private void SaveExport(IWin32Window owner, string content, string extension, string label)
        {
            using var picker = new SaveFileDialog { FileName = $"partida.{extension}", Filter = $"{label} (*.{extension})|*.{extension}" };
            if (picker.ShowDialog(owner) != DialogResult.OK) return;

            try { File.WriteAllText(picker.FileName, content); }
            catch (IOException ex) { ShowLibraryError(ex, owner); }
            catch (UnauthorizedAccessException ex) { ShowLibraryError(ex, owner); }
        }

        private void BrowsePuzzles()
        {
            IReadOnlyList<TacticalPuzzle> puzzles;
            try { puzzles = StudyCatalog.BuiltInPuzzles.Concat(StudyCatalog.ReadCustomPuzzles()).ToArray(); }
            catch (IOException ex) { ShowLibraryError(ex); return; }
            catch (UnauthorizedAccessException ex) { ShowLibraryError(ex); return; }

            using var dialog = CreateBrowser("Biblioteca de puzzles", out var filters, out var list, out var details, out var buttons);
            var query = AddSearchBox(filters, "Título ou tema");
            var theme = AddFilter(filters, "Todos os temas", puzzles.Select(x => x.Theme));
            var level = AddFilter(filters, "Todos os níveis", puzzles.Select(x => x.Level));
            var phase = AddFilter(filters, "Todas as fases", puzzles.Select(x => x.Phase));
            var opening = AddFilter(filters, "Todas as aberturas", puzzles.Select(x => x.Opening));
            TacticalPuzzle? Selected() => (list.SelectedItem as Choice<TacticalPuzzle>)?.Value;

            void Refresh() => FillList(list, puzzles.Where(puzzle =>
                (FilterValue(theme) is not string t || t == puzzle.Theme) &&
                (FilterValue(level) is not string l || l == puzzle.Level) &&
                (FilterValue(phase) is not string p || p == puzzle.Phase) &&
                (FilterValue(opening) is not string o || o == puzzle.Opening) &&
                ($"{puzzle.Title} {puzzle.Theme}").Contains(query.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)),
                puzzle => puzzle.Title);

            var play = AddButton(buttons, "Jogar", () => { if (Selected() != null) dialog.DialogResult = DialogResult.OK; });
            var solution = AddButton(buttons, "Ver solução no tabuleiro", () => { if (Selected() != null) dialog.DialogResult = DialogResult.Retry; });
            list.SelectedIndexChanged += (_, _) =>
            {
                var puzzle = Selected();
                play.Enabled = solution.Enabled = puzzle != null;
                details.Text = puzzle == null ? "Nenhum puzzle encontrado." :
                    $"{puzzle.Theme} · {puzzle.Level} · {puzzle.Phase}\r\n{puzzle.Opening}\r\n\r\n{puzzle.Hint}";
            };
            query.TextChanged += (_, _) => Refresh();
            theme.SelectedIndexChanged += (_, _) => Refresh();
            level.SelectedIndexChanged += (_, _) => Refresh();
            phase.SelectedIndexChanged += (_, _) => Refresh();
            opening.SelectedIndexChanged += (_, _) => Refresh();
            Refresh();
            if (Selected() == null)
            {
                play.Enabled = solution.Enabled = false;
                details.Text = "Nenhum puzzle encontrado.";
            }
            var result = dialog.ShowDialog(this);
            if (Selected() is TacticalPuzzle choice)
            {
                if (result == DialogResult.OK) StartPuzzle(choice);
                else if (result == DialogResult.Retry) PreviewPuzzleSolution(choice);
            }
        }

        private void CreatePuzzle()
        {
            TacticalPuzzle? puzzle = null;
            var values = EditFields("Criar puzzle personalizado",
                new[] { "Título", "Tema", "Nível", "Fase", "Abertura (opcional)", "FEN", "Solução UCI (separada por espaços)", "Dica" },
                new[] { "", "", "Iniciante", "Meio-jogo", "", game.Rules.ToFen(), "", "" },
                entries =>
                {
                    if (entries[0].Length == 0 || entries[1].Length == 0)
                        return "Informe título e tema.";
                    if (entries[2] is not ("Iniciante" or "Intermediário" or "Avançado") ||
                        entries[3] is not ("Abertura" or "Meio-jogo" or "Final"))
                        return "Use Iniciante, Intermediário ou Avançado. Para a fase, use Abertura, Meio-jogo ou Final.";
                    var draft = new TacticalPuzzle
                    {
                        Title = entries[0], Theme = entries[1], Level = entries[2], Phase = entries[3],
                        Opening = entries[4], Fen = entries[5], SolutionUci = entries[6], Hint = entries[7]
                    };
                    if (!StudyCatalog.TryReadSolution(draft, out _, out string error)) return error;
                    puzzle = draft;
                    return null;
                });

            if (values == null || puzzle == null) return;
            try
            {
                StudyCatalog.SaveCustomPuzzle(puzzle);
                StartPuzzle(puzzle);
            }
            catch (IOException ex) { ShowLibraryError(ex); }
            catch (UnauthorizedAccessException ex) { ShowLibraryError(ex); }
        }

        private void PreviewPuzzleSolution(TacticalPuzzle puzzle)
        {
            if (!StudyCatalog.TryReadSolution(puzzle, out var steps, out string error))
            {
                MessageBox.Show(this, error, "Solução inválida");
                return;
            }

            using var dialog = new Form
            {
                Text = $"Solução: {puzzle.Title}", StartPosition = FormStartPosition.CenterParent,
                ClientSize = new Size(560, 620), MinimumSize = new Size(450, 460)
            };
            var rules = new ChessRules();
            rules.LoadFen(puzzle.Fen);
            var board = new BoardView(rules) { Dock = DockStyle.Fill };
            var status = new Label { Dock = DockStyle.Top, Height = 38, TextAlign = ContentAlignment.MiddleCenter };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
            int index = 0;
            Button? next = null;
            void UpdateStep(string caption)
            {
                status.Text = $"{index}/{steps.Count}  ·  {caption}";
                board.RefreshPositionFeedback();
                if (next != null) next.Enabled = index < steps.Count;
            }
            next = AddButton(buttons, "Próximo lance", () =>
            {
                var step = steps[index++];
                bool capture = rules.IsCaptureDestination(step.From, step.To);
                rules.ExecuteMove(step.From, step.To, step.Promotion);
                board.AnimateMove(step.From, step.To, capture, step.Promotion.HasValue);
                UpdateStep(step.San);
            });
            AddButton(buttons, "Reiniciar solução", () =>
            {
                index = 0;
                rules.LoadFen(puzzle.Fen);
                board.ResetAllOverlays();
                UpdateStep("Posição inicial");
            });
            dialog.Controls.Add(board);
            dialog.Controls.Add(status);
            dialog.Controls.Add(buttons);
            UpdateStep("Posição inicial");
            dialog.ShowDialog(this);
        }

        private void BrowseOpeningBook()
        {
            using var dialog = CreateBrowser("Livro de aberturas", out var filters, out var list, out var details, out _);
            var query = AddSearchBox(filters, "Nome ou código ECO");
            var book = StudyLibrary.GetOpeningBook();
            void Refresh() => FillList(list, book.Where(x => x.Name.Contains(query.Text.Trim(),
                StringComparison.CurrentCultureIgnoreCase)), x => x.Name);
            list.SelectedIndexChanged += (_, _) =>
            {
                var item = (list.SelectedItem as Choice<OpeningLesson>)?.Value;
                details.Text = item == null ? "Nenhuma abertura encontrada." :
                    $"{item.Name}\r\n\r\nLances (SAN): {item.Moves}\r\n\r\n{item.Description}";
            };
            query.TextChanged += (_, _) => Refresh();
            Refresh();
            dialog.ShowDialog(this);
        }

        private void ShowLibraryError(Exception error, IWin32Window? owner = null) =>
            MessageBox.Show(owner ?? this, error.Message, "Erro de armazenamento", MessageBoxButtons.OK, MessageBoxIcon.Error);

        private string[]? EditFields(string title, string[] labels, string[] initial, Func<string[], string?> validate)
        {
            using var dialog = new Form { Text = title, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(760, 48 + labels.Length * 35) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var inputs = new TextBox[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                layout.Controls.Add(new Label { Text = labels[i], AutoSize = true, Anchor = AnchorStyles.Left }, 0, i);
                inputs[i] = new TextBox { Text = initial[i], Dock = DockStyle.Fill, AccessibleName = labels[i] };
                layout.Controls.Add(inputs[i], 1, i);
            }
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
            layout.Controls.Add(buttons, 1, labels.Length);
            string[]? result = null;
            dialog.AcceptButton = AddButton(buttons, "Salvar", () =>
            {
                var values = inputs.Select(input => input.Text.Trim()).ToArray();
                if (validate(values) is string error)
                {
                    MessageBox.Show(dialog, error, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                result = values;
                dialog.DialogResult = DialogResult.OK;
            });
            var cancel = AddButton(buttons, "Cancelar", () => dialog.DialogResult = DialogResult.Cancel);
            dialog.CancelButton = cancel;
            dialog.Controls.Add(layout);
            return dialog.ShowDialog(this) == DialogResult.OK ? result : null;
        }

        private static Form CreateBrowser(string title, out FlowLayoutPanel filters, out ListBox list,
            out TextBox details, out FlowLayoutPanel buttons)
        {
            var dialog = new Form
            {
                Text = title, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(850, 515),
                MinimumSize = new Size(620, 380)
            };
            filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(4) };
            buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(4) };
            list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, AccessibleName = title };
            details = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                AccessibleName = "Detalhes"
            };
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(850, 400), SplitterDistance = 410 };
            split.Panel1.Controls.Add(list);
            split.Panel2.Controls.Add(details);
            dialog.Controls.Add(split);
            dialog.Controls.Add(buttons);
            dialog.Controls.Add(filters);
            return dialog;
        }

        private static Button AddButton(Control panel, string label, Action action)
        {
            var button = new Button { Text = label, AutoSize = true };
            button.Click += (_, _) => action();
            panel.Controls.Add(button);
            return button;
        }

        private static TextBox AddSearchBox(Control panel, string label)
        {
            var box = new TextBox { PlaceholderText = label, AccessibleName = label, Width = 220 };
            panel.Controls.Add(box);
            return box;
        }

        private static ComboBox AddFilter(Control panel, string label, IEnumerable<string> options)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 175, AccessibleName = label };
            box.Items.Add(label);
            foreach (string option in options.Where(s => s.Length > 0).Distinct().OrderBy(s => s))
                box.Items.Add(option);
            box.SelectedIndex = 0;
            panel.Controls.Add(box);
            return box;
        }

        private static string? FilterValue(ComboBox box) => box.SelectedIndex > 0 ? (string?)box.SelectedItem : null;

        private sealed record Choice<T>(T Value, string Label)
        {
            public override string ToString() => Label;
        }

        private static void FillList<T>(ListBox list, IEnumerable<T> values, Func<T, string> label)
        {
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                list.Items.AddRange(values.Select(x => (object)new Choice<T>(x, label(x))).ToArray());
                if (list.Items.Count > 0) list.SelectedIndex = 0;
            }
            finally { list.EndUpdate(); }
        }
    }
}
