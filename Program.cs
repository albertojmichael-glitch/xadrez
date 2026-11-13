using System;
using System.Drawing;
using System.Windows.Forms;

namespace XadrezApp
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
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
        }
    }

    public static class PromotionDialog
    {
        public static char AskPiece(bool isWhite, IWin32Window? owner = null, bool english = false)
        {
            using var form = new Form
            {
                Text = english ? "👑 Pawn Promotion" : "👑 Promoção de Peão",
                ClientSize = new Size(360, 115),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                ControlBox = false,
                BackColor = Color.FromArgb(236, 233, 216),
                Font = new Font("Tahoma", 8.5f, FontStyle.Bold)
            };

            var lbl = new Label
            {
                Text = english ? "Choose the piece to promote your pawn:" : "Escolha a peça para promover o seu peão:",
                Location = new Point(14, 12),
                AutoSize = true
            };
            form.Controls.Add(lbl);

            char selected = 'Q';
            (string label, char code)[] options = english
                ? new[] { ("♛ Queen", 'Q'), ("♜ Rook", 'R'), ("♝ Bishop", 'B'), ("♞ Knight", 'N') }
                : new[] { ("♛ Rainha", 'Q'), ("♜ Torre", 'R'), ("♝ Bispo", 'B'), ("♞ Cavalo", 'N') };

            int x = 12;
            foreach (var opt in options)
            {
                var btn = new Button
                {
                    Text = opt.label,
                    Location = new Point(x, 42),
                    Size = new Size(80, 48),
                    FlatStyle = FlatStyle.System,
                    Cursor = Cursors.Hand,
                    AccessibleName = opt.label
                };
                char c = opt.code;
                btn.Click += (s, e) =>
                {
                    selected = c;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };
                form.Controls.Add(btn);
                x += 85;
            }

            if (owner != null)
                form.ShowDialog(owner);
            else
                form.ShowDialog();

            return isWhite ? char.ToUpper(selected) : char.ToLower(selected);
        }
    }
}