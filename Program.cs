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

    // Janela Clássica para escolha de peça na Promoção do Peão
    public static class PromotionDialog
    {
        public static char AskPiece(bool isWhite, IWin32Window? owner = null)
        {
            using var form = new Form
            {
                Text = "Promoção de Peão",
                ClientSize = new Size(340, 110),
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
                Text = "Escolha a peça para promover o seu peão:",
                Location = new Point(15, 12),
                AutoSize = true
            };
            form.Controls.Add(lbl);

            char selected = 'Q';
            (string label, char code)[] options =
            {
                ("Rainha (Q)", 'Q'),
                ("Torre (R)", 'R'),
                ("Bispo (B)", 'B'),
                ("Cavalo (N)", 'N')
            };

            int x = 12;
            foreach (var opt in options)
            {
                var btn = new Button
                {
                    Text = opt.label,
                    Location = new Point(x, 42),
                    Size = new Size(76, 45),
                    FlatStyle = FlatStyle.System,
                    Cursor = Cursors.Hand
                };
                char c = opt.code;
                btn.Click += (s, e) =>
                {
                    selected = c;
                    form.DialogResult = DialogResult.OK;
                    form.Close();
                };
                form.Controls.Add(btn);
                x += 80;
            }

            if (owner != null)
                form.ShowDialog(owner);
            else
                form.ShowDialog();

            return isWhite ? char.ToUpper(selected) : char.ToLower(selected);
        }
    }
}