using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace XadrezApp
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
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

    // Preferências do usuário salvas entre sessões (dificuldade, tema, relógio, orientação/cor e som)
    public sealed class UserSettings
    {
        public int DifficultyIndex { get; set; } = 0;
        public string Theme { get; set; } = "Madeira";
        public string ClockMode { get; set; } = "Fischer 5+3";
        public bool PlayAsWhite { get; set; } = true;
        public bool BoardFlipped { get; set; } = false;
        public bool SoundEnabled { get; set; } = true;
        public string BotDelayMode { get; set; } = "Variável (0 a 1s)";

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

    // Diálogo de escolha de peça na Promoção do Peão com imagens, atalhos Q/R/B/N e cancelamento via Esc/X
    public static class PromotionDialog
    {
        public static char? AskPiece(
            bool isWhite,
            IWin32Window? owner = null,
            IReadOnlyDictionary<string, Image>? pieceImages = null,
            Point? screenLocation = null)
        {
            using var form = new Form
            {
                Text = "Promoção (Q, R, B, N | Esc cancela)",
                AutoScaleMode = AutoScaleMode.Dpi,
                ClientSize = new Size(400, 148),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = screenLocation.HasValue
                    ? FormStartPosition.Manual
                    : (owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen),
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = true,
                KeyPreview = true,
                BackColor = Color.FromArgb(242, 240, 230),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            if (screenLocation.HasValue)
            {
                form.Location = screenLocation.Value;
            }

            var lbl = new Label
            {
                Text = "Escolha a peça (atalhos Q, R, B, N ou Esc para cancelar):",
                Location = new Point(14, 12),
                AutoSize = true
            };
            form.Controls.Add(lbl);

            char? selected = null;
            (string label, char code, string symbol)[] options =
            {
                ("Dama [Q]", 'Q', isWhite ? "♕" : "♛"),
                ("Torre [R]", 'R', isWhite ? "♖" : "♜"),
                ("Bispo [B]", 'B', isWhite ? "♗" : "♝"),
                ("Cavalo [N]", 'N', isWhite ? "♘" : "♞")
            };

            void ChooseAndClose(char pieceCode)
            {
                selected = pieceCode;
                form.DialogResult = DialogResult.OK;
                form.Close();
            }

            form.KeyDown += (s, e) =>
            {
                switch (e.KeyCode)
                {
                    case Keys.Escape:
                        selected = null;
                        form.DialogResult = DialogResult.Cancel;
                        form.Close();
                        e.Handled = true;
                        break;
                    case Keys.Q:
                    case Keys.D:
                    case Keys.D1:
                    case Keys.NumPad1:
                        ChooseAndClose('Q');
                        e.Handled = true;
                        break;
                    case Keys.R:
                    case Keys.T:
                    case Keys.D2:
                    case Keys.NumPad2:
                        ChooseAndClose('R');
                        e.Handled = true;
                        break;
                    case Keys.B:
                    case Keys.D3:
                    case Keys.NumPad3:
                        ChooseAndClose('B');
                        e.Handled = true;
                        break;
                    case Keys.N:
                    case Keys.C:
                    case Keys.D4:
                    case Keys.NumPad4:
                        ChooseAndClose('N');
                        e.Handled = true;
                        break;
                }
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
                btn.Click += (s, e) => ChooseAndClose(c);
                form.Controls.Add(btn);
                x += 94;
            }

            DialogResult result;
            try
            {
                result = owner != null ? form.ShowDialog(owner) : form.ShowDialog();
            }
            finally
            {
                foreach (var bmp in createdThumbnails)
                {
                    try { bmp.Dispose(); } catch { }
                }
            }

            if (result != DialogResult.OK || !selected.HasValue)
                return null;

            return isWhite ? char.ToUpper(selected.Value) : char.ToLower(selected.Value);
        }
    }

    // Efeitos sonoros sintetizados em memória (WAV PCM) para lance, captura, xeque e fim de partida
    public static class ChessSoundEffects
    {
        private static readonly byte[] MoveWav = BuildToneWav(new[] { (245.0, 0.045, 0.42) });
        private static readonly byte[] CaptureWav = BuildToneWav(new[] { (330.0, 0.032, 0.50), (185.0, 0.045, 0.55) });
        private static readonly byte[] CheckWav = BuildToneWav(new[] { (587.33, 0.055, 0.48), (880.0, 0.085, 0.52) });
        private static readonly byte[] GameEndWav = BuildToneWav(new[] { (523.25, 0.070, 0.45), (659.25, 0.070, 0.45), (783.99, 0.130, 0.50) });

        public static void PlayMove() => PlayBytes(MoveWav);
        public static void PlayCapture() => PlayBytes(CaptureWav);
        public static void PlayCheck() => PlayBytes(CheckWav);
        public static void PlayGameEnd() => PlayBytes(GameEndWav);

        private static void PlayBytes(byte[] wavData)
        {
            try
            {
                var ms = new MemoryStream(wavData, writable: false);
                using var player = new SoundPlayer(ms);
                player.Play();
            }
            catch
            {
                // Ignora falhas de dispositivo de áudio em ambientes sem placa de som
            }
        }

        private static byte[] BuildToneWav((double freqHz, double durationSec, double volume)[] segments)
        {
            const int sampleRate = 22050;
            int totalSamples = 0;
            foreach (var seg in segments)
                totalSamples += Math.Max(1, (int)(seg.durationSec * sampleRate));

            int dataBytes = totalSamples * 2; // 16-bit mono PCM
            using var ms = new MemoryStream(44 + dataBytes);
            using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

            // Cabeçalho RIFF WAVE
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(36 + dataBytes);
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(16); // PCM chunk size
            bw.Write((short)1); // AudioFormat = 1 (PCM)
            bw.Write((short)1); // NumChannels = 1
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2); // ByteRate
            bw.Write((short)2); // BlockAlign
            bw.Write((short)16); // BitsPerSample
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataBytes);

            foreach (var (freqHz, durationSec, volume) in segments)
            {
                int count = Math.Max(1, (int)(durationSec * sampleRate));
                for (int i = 0; i < count; i++)
                {
                    double t = i / (double)sampleRate;
                    double progress = i / (double)count;
                    // Envelope com ataque rápido e decaimento exponencial suave (timbre percussivo de peça)
                    double attack = Math.Min(1.0, i / (sampleRate * 0.003));
                    double decay = Math.Exp(-4.8 * progress);
                    double env = attack * decay * volume;

                    double fundamental = Math.Sin(2.0 * Math.PI * freqHz * t);
                    double harmonic = 0.35 * Math.Sin(2.0 * Math.PI * (freqHz * 1.5) * t);
                    double sample = Math.Clamp((fundamental + harmonic) * env, -1.0, 1.0);
                    short pcm = (short)(sample * short.MaxValue);
                    bw.Write(pcm);
                }
            }

            bw.Flush();
            return ms.ToArray();
        }
    }
}
