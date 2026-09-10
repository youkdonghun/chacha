using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// A separate process owns a real Windows global shortcut. Drive this and Chacha
// with the desktop UI tool to verify recording, conflict reporting and unhooking.
internal sealed class InputFixture : Form
{
    private readonly Label status;
    private readonly Label count;
    private readonly string shortcut;
    private readonly uint modifiers;
    private bool registered;
    private int activations;

    private InputFixture(bool withModifiers)
    {
        shortcut = withModifiers ? "Ctrl+Shift+F10" : "F10";
        modifiers = withModifiers ? 6U : 0U;
        Text = "Chacha QA · 외부 단축키 / 캡처 기준 화면";
        Name = "ChachaInputFixture";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(740, 470);
        MinimumSize = MaximumSize = Size;
        MaximizeBox = false;
        BackColor = Color.FromArgb(245, 247, 252);
        Font = new Font("Malgun Gothic", 11F);

        Label title = new Label { Text = "독립 프로그램의 실제 전역 단축키", AutoSize = false,
            Bounds = new Rectangle(24, 20, 690, 35), Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(25, 35, 60) };
        Controls.Add(title);
        status = new Label { AutoSize = false, Bounds = new Rectangle(24, 65, 690, 35),
            ForeColor = Color.FromArgb(25, 35, 60), AccessibleName = "단축키 등록 상태" };
        Controls.Add(status);
        count = new Label { Text = "활성화: 0회", AutoSize = false, Bounds = new Rectangle(24, 105, 690, 50),
            Font = new Font("Malgun Gothic", 22F, FontStyle.Bold), ForeColor = Color.FromArgb(25, 35, 60),
            AccessibleName = "외부 앱 단축키 활성화 횟수" };
        Controls.Add(count);
        AddBlock(new Rectangle(24, 177, 216, 158), Color.FromArgb(36, 113, 226), "BLUE 36 · 113 · 226");
        AddBlock(new Rectangle(262, 177, 216, 158), Color.FromArgb(37, 154, 99), "GREEN 37 · 154 · 99");
        AddBlock(new Rectangle(500, 177, 216, 158), Color.FromArgb(221, 77, 75), "RED 221 · 77 · 75");
        Controls.Add(new Label { Text = "Chacha 입력란에서 " + shortcut + " 녹화 → 이 창의 횟수는 그대로\n" +
            "입력란에서 벗어난 뒤 " + shortcut + " → 이 창의 횟수 증가\n" +
            "위 색상 블록은 캡처 색상·흰 화면 확인에 사용합니다.", AutoSize = false,
            Bounds = new Rectangle(24, 353, 690, 90), ForeColor = Color.FromArgb(65, 77, 96) });
    }

    private void AddBlock(Rectangle bounds, Color color, string caption)
    {
        Panel panel = new Panel { Bounds = bounds, BackColor = color };
        panel.Controls.Add(new Label { Text = caption, Dock = DockStyle.Bottom, Height = 38,
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold), BackColor = color });
        Controls.Add(panel);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        registered = RegisterHotKey(Handle, 1, modifiers | 0x4000, (uint)Keys.F10);
        int error = registered ? 0 : Marshal.GetLastWin32Error();
        status.Text = registered ? shortcut + " 등록됨 · 이 프로세스가 소유합니다" :
            shortcut + " 등록 실패 · Windows 오류 " + error + " · fixture를 종료하고 다른 조합으로 실행하세요";
        status.ForeColor = registered ? Color.FromArgb(28, 126, 77) : Color.FromArgb(190, 52, 47);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x312 && message.WParam.ToInt32() == 1)
        {
            activations++;
            count.Text = "활성화: " + activations + "회";
            count.AccessibleDescription = shortcut + " 전역 단축키를 " + activations + "회 받았습니다.";
            return;
        }
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (registered) { UnregisterHotKey(Handle, 1); registered = false; }
        base.Dispose(disposing);
    }

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool modified = args.Length != 0 && args[0].Equals("--ctrl-shift", StringComparison.OrdinalIgnoreCase);
        InputFixture fixture = new InputFixture(modified);
        fixture.Show();
        fixture.WindowState = FormWindowState.Normal;
        fixture.Show();
        Application.Run(fixture);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
