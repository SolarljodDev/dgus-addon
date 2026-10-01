using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Windows.Forms;
using BizDraw;

namespace DgusPlus
{
    class UploadForm : Form
    {
        readonly ComboBox port = new ComboBox();
        readonly ComboBox baud = new ComboBox();
        readonly CheckBox all = new CheckBox();
        bool syncingAll;
        readonly CheckedListBox files = new CheckedListBox();
        readonly ProgressBar bar = new ProgressBar();
        readonly TextBox log = new TextBox();
        readonly Button start = new Button();
        readonly Button stop = new Button();
        readonly Timer timer = new Timer();
        int logShown;

        public static void ShowFor(IWin32Window owner)
        {
            if (string.IsNullOrEmpty(Globel.ProjectPath))
            {
                MessageBox.Show(owner, Loc.T("Open a project first."), "DGUS+");
                return;
            }
            using (UploadForm f = new UploadForm()) f.ShowDialog(owner);
        }

        UploadForm()
        {
            Text = Loc.T("Upload to display");
            Font = Theme.UiFont;
            ClientSize = new Size(620, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;

            AddLabel(Loc.T("Port"), 12, 14);
            port.SetBounds(90, 10, 120, 24);
            port.DropDownStyle = ComboBoxStyle.DropDown;
            port.Items.AddRange(SerialPort.GetPortNames());
            port.Text = Plus.Cfg.DisplayPort;
            if (port.Text.Length == 0 && port.Items.Count > 0) port.SelectedIndex = 0;

            AddLabel(Loc.T("Baud rate"), 225, 14);
            baud.SetBounds(290, 10, 90, 24);
            baud.Items.AddRange(new object[] { "115200", "230400", "460800", "921600" });
            baud.Text = Plus.Cfg.DisplayBaud.ToString();   // последняя использованная; у нового пользователя 115200

            all.Text = Loc.T("Select all");
            all.AutoSize = true;
            all.Location = new Point(12, 44);
            all.CheckedChanged += delegate
            {
                if (syncingAll) return;
                for (int i = 0; i < files.Items.Count; i++) files.SetItemChecked(i, all.Checked);
            };
            files.SetBounds(12, 68, 596, 200);
            files.CheckOnClick = true;
            foreach (string p in Directory.GetFiles(ProjectFiles.DwinSet))
            {
                string n = Path.GetFileName(p);
                if (!DisplayUpload.IsUploadable(n)) continue;
                files.Items.Add(new Item(p), false);
            }
            // Галка «Выбрать все» следует за отдельными пунктами (ItemCheck срабатывает до смены — считаем после).
            files.ItemCheck += delegate
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    syncingAll = true;
                    all.Checked = files.Items.Count > 0 && files.CheckedItems.Count == files.Items.Count;
                    syncingAll = false;
                });
            };

            bar.SetBounds(12, 280, 596, 18);
            log.SetBounds(12, 306, 596, 204);
            log.Multiline = true;
            log.ReadOnly = true;
            log.ScrollBars = ScrollBars.Vertical;

            start.Text = Loc.T("Upload");
            start.SetBounds(412, 520, 96, 30);
            start.Click += delegate { Plus.Guard(StartUpload); };
            stop.Text = Loc.T("Stop");
            stop.SetBounds(512, 520, 96, 30);
            stop.Click += delegate
            {
                DisplayUpload.Job cur = DisplayUpload.Current;
                if (cur != null && cur.State == "running") cur.Cancel = true;
                else Close();
            };

            Controls.AddRange(new Control[] { port, baud, all, files, bar, log, start, stop });
            timer.Interval = 300;
            timer.Tick += delegate { Refresh2(); };
            timer.Start();
            Refresh2();
        }

        void AddLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Location = new Point(x, y);
            Controls.Add(l);
        }

        class Item
        {
            public readonly string Path;
            public Item(string p) { Path = p; }
            public override string ToString()
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(Path);
                return fi.Name + "   (" + Math.Max(1, fi.Length / 1024) + " KB, " + fi.LastWriteTime.ToString("dd.MM HH:mm") + ")";
            }
        }

        void StartUpload()
        {
            List<string> pick = new List<string>();
            foreach (object o in files.CheckedItems) pick.Add(((Item)o).Path);
            if (pick.Count == 0) { MessageBox.Show(this, Loc.T("Tick the files to upload."), "DGUS+"); return; }
            int b;
            if (!int.TryParse(baud.Text, out b)) b = 115200;
            Plus.Cfg.DisplayPort = port.Text.Trim();
            Plus.Cfg.DisplayBaud = b;
            Plus.Cfg.Save();
            logShown = 0;
            log.Clear();
            DisplayUpload.Start(Plus.Cfg.DisplayPort, b, pick);
        }

        void Refresh2()
        {
            DisplayUpload.Job j = DisplayUpload.Current;
            bool running = j != null && j.State == "running";
            start.Enabled = !running;
            stop.Text = running ? Loc.T("Stop") : Loc.T("Close");
            if (j == null) return;
            bar.Value = j.BytesTotal == 0 ? 0 : (int)Math.Min(100, 100 * j.BytesDone / j.BytesTotal);
            lock (j.Log)
            {
                for (; logShown < j.Log.Count; logShown++) log.AppendText(j.Log[logShown] + Environment.NewLine);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            DisplayUpload.Job j = DisplayUpload.Current;
            if (j != null && j.State == "running" &&
                MessageBox.Show(this, Loc.T("An upload is running. Stop it?"), "DGUS+", MessageBoxButtons.YesNo) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            if (j != null && j.State == "running") j.Cancel = true;
            timer.Stop();
            base.OnFormClosing(e);
        }
    }
}
