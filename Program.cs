using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace XadrezApp
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--verify")
            {
                return RunSelfVerification();
            }

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        private static int RunSelfVerification()
        {
            try
            {
                var rules = new ChessRules();
                if (rules.GetAllLegalMoves(true).Count != 20)
                    throw new Exception("Initial moves count != 20");

                string pgn = "[Event \"Test\"]\n[White \"Ana\"]\n[Black \"Bot\"]\n[Result \"1-0\"]\n1. e4 {c} e5 2. Nf3 (2. Bc4) Nc6 3. Bb5 a6 1-0";
                if (!rules.TryLoadPgn(pgn, out var snaps, out var hdrs, out string err))
                    throw new Exception("TryLoadPgn failed: " + err);
                if (snaps.Count != 7 || hdrs["White"] != "Ana" || rules.GameResult != "1-0")
                    throw new Exception("TryLoadPgn state mismatch");

                string pvSan = ChessRules.FormatUciPvToSan(new ChessRules(), "e2e4 e7e5 g1f3 b8c6");
                if (pvSan != "1. e4 e5 2. Nf3 Nc6")
                    throw new Exception("FormatUciPvToSan mismatch: " + pvSan);

                var sacRules = new ChessRules();
                sacRules.LoadFen("r1bq1rk1/ppp2ppp/2np1n2/4p3/2B1P3/2NP1N2/PPP2PPP/R1BQK2R w KQ - 0 6");
                if (!EngineService.IsMaterialSacrifice(sacRules, new Point(2, 4), new Point(7, 1), null, 0.4, 1.5))
                    throw new Exception("Greek gift should be detected as sacrifice");

                var normalCapRules = new ChessRules();
                normalCapRules.LoadFen("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2");
                if (EngineService.IsMaterialSacrifice(normalCapRules, new Point(4, 4), new Point(3, 3), null, 0.3, 0.5))
                    throw new Exception("Normal pawn capture should NOT be sacrifice");

                var mateIn3 = EngineService.ClassifyMove(2.0, 99.0, true, mateInMoves: 3, isImmediateCheckmate: false);
                if (mateIn3.description != "Mate em 3")
                    throw new Exception("Expected 'Mate em 3', got: " + mateIn3.description);

                using var form = new MainForm();
                form.Size = new Size(1200, 820);
                Console.WriteLine("SELF_VERIFICATION_OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SELF_VERIFICATION_FAILED: " + ex);
                return 1;
            }
        }
    }

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
    }

    // Preferências do usuário salvas entre sessões (dificuldade, tema, relógio e orientação/cor)
    public sealed class UserSettings
    {
        public int DifficultyIndex { get; set; } = 0;
        public string Theme { get; set; } = "Madeira";
        public string ClockMode { get; set; } = "Fischer 5+3";
        public bool PlayAsWhite { get; set; } = true;
        public bool BoardFlipped { get; set; } = false;

        private static string GetSettingsFilePath()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "XadrezApp");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "user_settings.json");
            }
            catch
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_settings.json");
            }
        }

        public static UserSettings Load()
        {
            try
            {
                string path = GetSettingsFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<UserSettings>(json);
                    if (loaded != null)
                        return loaded;
                }
            }
            catch
            {
                // Ignora falhas de leitura de configuração e usa padrões seguros
            }
            return new UserSettings();
        }

        public void Save()
        {
            try
            {
                string path = GetSettingsFilePath();
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch
            {
                // Ignora falhas de escrita em disco restrito
            }
        }
    }

    // Diálogo de escolha de peça na Promoção do Peão com imagens das peças
    public static class PromotionDialog
    {
        public static char AskPiece(
            bool isWhite,
            IWin32Window? owner = null,
            IReadOnlyDictionary<string, Image>? pieceImages = null)
        {
            using var form = new Form
            {
                Text = "Promoção de Peão",
                ClientSize = new Size(400, 148),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = false,
                BackColor = Color.FromArgb(242, 240, 230),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            var lbl = new Label
            {
                Text = "Escolha a peça para promover o seu peão:",
                Location = new Point(16, 12),
                AutoSize = true
            };
            form.Controls.Add(lbl);

            char selected = 'Q';
            (string label, char code, string symbol)[] options =
            {
                ("Dama (Q)", 'Q', isWhite ? "♕" : "♛"),
                ("Torre (R)", 'R', isWhite ? "♖" : "♜"),
                ("Bispo (B)", 'B', isWhite ? "♗" : "♝"),
                ("Cavalo (N)", 'N', isWhite ? "♘" : "♞")
            };

            var createdThumbnails = new List<Bitmap>();
            int x = 14;
            foreach (var opt in options)
            {
                string imgKey = isWhite ? $"{char.ToUpper(opt.code)}1" : $"{char.ToLower(opt.code)}1";
                Bitmap? thumb = null;
                if (pieceImages != null && pieceImages.TryGetValue(imgKey, out Image? rawImg) && rawImg != null)
                {
                    thumb = new Bitmap(48, 48);
                    using (Graphics g = Graphics.FromImage(thumb))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(rawImg, 2, 2, 44, 44);
                    }
                    createdThumbnails.Add(thumb);
                }

                var btn = new Button
                {
                    Text = thumb != null ? opt.label : $"{opt.symbol}\n{opt.label}",
                    Image = thumb,
                    ImageAlign = ContentAlignment.TopCenter,
                    TextAlign = ContentAlignment.BottomCenter,
                    TextImageRelation = TextImageRelation.ImageAboveText,
                    Location = new Point(x, 38),
                    Size = new Size(88, 94),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(252, 250, 244),
                    Cursor = Cursors.Hand,
                    Padding = new Padding(2, 6, 2, 6)
                };
                btn.FlatAppearance.BorderColor = Color.FromArgb(180, 165, 140);
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(230, 242, 255);

                char c = opt.code;
                btn.Click += (s, e) =>
                {
                    selected = c;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };
                form.Controls.Add(btn);
                x += 94;
            }

            try
            {
                if (owner != null)
                    form.ShowDialog(owner);
                else
                    form.ShowDialog();
            }
            finally
            {
                foreach (var bmp in createdThumbnails)
                {
                    try { bmp.Dispose(); } catch { }
                }
            }

            return isWhite ? char.ToUpper(selected) : char.ToLower(selected);
        }
    }
}