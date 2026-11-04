using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ChachaCapture
{
    internal sealed class OcrForm : Form
    {
        private static OcrForm current;
        private readonly Bitmap image;
        private readonly TextBox result;
        private readonly Label status;
        private readonly ComboBox mode;
        private readonly CheckBox enlarge;
        private readonly Button retry, copy, close;
        private CancellationTokenSource cancellation;
        private bool busy, closing;
        internal Func<Bitmap, string, bool, CancellationToken, Task<string>> Recognizer = OcrService.RecognizeAsync;

        internal static void ShowImage(IWin32Window owner, Bitmap source)
        {
            if (current != null && !current.IsDisposed) { current.Activate(); return; }
            using (OcrForm form = new OcrForm(source))
            {
                current = form;
                try { form.ShowDialog(owner); }
                finally { current = null; }
            }
        }

        internal OcrForm(Bitmap source)
        {
            image = (Bitmap)source.Clone();
            Text = "Chacha · 텍스트 추출";
            Font = new Font("Malgun Gothic", 10F);
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Ui.Background; ForeColor = Ui.Text;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(780, 580); MinimumSize = new Size(620, 470);
            ShowInTaskbar = true; TopMost = true; KeyPreview = true;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.Controls.Add(new Label { Text = "한글·영문·숫자·기호를 읽습니다. 결과를 확인하고 수정한 뒤 복사하세요.", Dock = DockStyle.Fill, AutoSize = false }, 0, 0);
            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            options.Controls.Add(new Label { Text = "읽기 방식", AutoSize = true, Margin = new Padding(0, 7, 8, 0) });
            mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145, AccessibleName = "텍스트 읽기 방식", BackColor = Color.White, ForeColor = Color.Black };
            mode.Items.AddRange(new object[] { "자동 · 여러 영역", "한 문단", "한 줄" }); mode.SelectedIndex = 1;
            enlarge = new CheckBox { Text = "작은 글자 확대", Checked = true, AutoSize = true, Margin = new Padding(14, 5, 0, 0) };
            options.Controls.Add(mode); options.Controls.Add(enlarge); layout.Controls.Add(options, 0, 1);
            layout.Controls.Add(new PictureBox { Image = image, Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(45, 49, 55), AccessibleName = "읽을 원본 이미지", Margin = new Padding(0, 0, 0, 10) }, 0, 2);
            result = new TextBox { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, AcceptsReturn = true, AcceptsTab = true, Dock = DockStyle.Fill, AccessibleName = "추출한 텍스트", BackColor = Color.White, ForeColor = Color.Black, MaxLength = 0 };
            layout.Controls.Add(result, 0, 3);
            status = new Label { Dock = DockStyle.Fill, AutoSize = false, Text = "텍스트 인식을 준비합니다.", Padding = new Padding(0, 7, 0, 0) }; layout.Controls.Add(status, 0, 4);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            close = Button("닫기", delegate { Close(); }); copy = Button("전체 텍스트 복사", delegate { CopyResult(); }); retry = Button("다시 읽기", async delegate { await ReadAsync(); });
            buttons.Controls.AddRange(new Control[] { close, copy, retry }); layout.Controls.Add(buttons, 0, 5); Controls.Add(layout);
            result.TextChanged += delegate { copy.Enabled = !busy && !String.IsNullOrWhiteSpace(result.Text); };
            Shown += async delegate { await ReadAsync(); };
        }

        private static Button Button(string text, EventHandler action)
        {
            Button button = new Button { Text = text, AutoSize = true, Height = 34, MinimumSize = new Size(96, 34), Padding = new Padding(8, 0, 8, 0), UseVisualStyleBackColor = true, ForeColor = Color.Black };
            button.Click += action; return button;
        }

        internal async Task ReadAsync()
        {
            if (busy || closing) return;
            busy = true; retry.Enabled = mode.Enabled = enlarge.Enabled = copy.Enabled = false; result.ReadOnly = true; close.Text = "취소";
            status.Text = "텍스트를 읽고 있습니다. 이미지는 이 PC에서 처리합니다.";
            cancellation = new CancellationTokenSource();
            try
            {
                string text;
                // GDI+ bitmaps cannot be painted and encoded on different threads at once.
                using (Bitmap request = (Bitmap)image.Clone())
                    text = await Recognizer(request, mode.SelectedIndex == 1 ? "block" : mode.SelectedIndex == 2 ? "line" : "auto", enlarge.Checked, cancellation.Token);
                if (!closing)
                {
                    result.Text = text;
                    status.Text = String.IsNullOrWhiteSpace(text) ? "텍스트를 찾지 못했습니다. 영역을 좁히거나 읽기 방식을 바꿔 다시 시도해 주세요." : "텍스트를 읽었습니다. 작은 글자와 특수기호는 원본과 비교해 주세요.";
                    result.Focus(); result.SelectionStart = 0; result.SelectionLength = 0;
                }
            }
            catch (OperationCanceledException) { if (!closing) status.Text = "텍스트 읽기를 취소했습니다."; }
            catch (Exception error) { if (!closing) status.Text = error.Message; }
            finally
            {
                busy = false; cancellation.Dispose(); cancellation = null;
                if (!IsDisposed)
                {
                    retry.Enabled = mode.Enabled = enlarge.Enabled = true; copy.Enabled = !String.IsNullOrWhiteSpace(result.Text); result.ReadOnly = false; close.Text = "닫기";
                    if (closing) Close();
                }
            }
        }

        private void CopyResult()
        {
            if (busy || String.IsNullOrWhiteSpace(result.Text)) return;
            try { Clipboard.SetDataObject(result.Text, true, 5, 70); status.Text = "텍스트를 복사했습니다."; }
            catch (ExternalException) { status.Text = "클립보드가 사용 중입니다. 잠시 후 다시 복사해 주세요. 텍스트는 이 창에 남아 있습니다."; }
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy) { closing = true; cancellation.Cancel(); e.Cancel = true; status.Text = "텍스트 읽기를 중단하고 있습니다."; }
            base.OnFormClosing(e);
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == (Keys.Control | Keys.A) && result.Focused) { result.SelectAll(); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { closing = true; if (cancellation != null) cancellation.Cancel(); image.Dispose(); Font.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
