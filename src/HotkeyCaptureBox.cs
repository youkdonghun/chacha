using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ChachaCapture
{
    /// <summary>Records a key chord without allowing text input or dialog shortcuts.</summary>
    public sealed class HotkeyCaptureBox : TextBox
    {
        private string hotkey = String.Empty;
        private string focusOriginal = String.Empty;
        private bool previewingModifiers;
        private bool chordCompleted;
        private bool recordingFocus;
        private IntPtr keyboardHook;
        private KeyboardHookCallback keyboardCallback;
        private readonly bool[] heldKeys = new bool[256];
        private readonly bool[] capturedKeys = new bool[256];
        private int recordingSession;

        public event EventHandler HotkeyChanged;

        public HotkeyCaptureBox()
        {
            ReadOnly = true;
            ShortcutsEnabled = false;
            TabStop = true;
            ImeMode = ImeMode.Disable;
            Cursor = Cursors.Hand;
            AccessibleDescription = "클릭하거나 Tab 키로 선택한 뒤 단축키를 누르세요. Ctrl, Alt, Shift를 조합할 수 있습니다. Esc는 변경을 되돌리고 Tab은 다음 입력란으로 이동합니다.";
        }

        public string Hotkey
        {
            get { return hotkey; }
            set
            {
                string next = (value ?? String.Empty).Trim();
                uint modifiers, key;
                string canonical;
                if (HotkeyWindow.Parse(next, out modifiers, out key) &&
                    TryFormat((Keys)key | ((modifiers & 2) != 0 ? Keys.Control : Keys.None) |
                        ((modifiers & 1) != 0 ? Keys.Alt : Keys.None) |
                        ((modifiers & 4) != 0 ? Keys.Shift : Keys.None), out canonical)) next = canonical;
                bool changed = hotkey != next;
                hotkey = next;
                previewingModifiers = false;
                chordCompleted = false;
                ShowValue(hotkey);
                if (!recordingFocus) focusOriginal = hotkey;
                if (changed && HotkeyChanged != null) HotkeyChanged(this, EventArgs.Empty);
            }
        }

        public override string Text
        {
            get { return base.Text; }
            set { Hotkey = value; }
        }

        private void ShowValue(string value)
        {
            base.Text = value;
            SelectionStart = base.Text.Length;
            SelectionLength = 0;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            recordingSession++;
            recordingFocus = true;
            focusOriginal = hotkey;
            chordCompleted = false;
            previewingModifiers = false;
            base.OnGotFocus(e);
            ShowValue("키를 눌러 주세요…");
            StartKeyboardRecording();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            StopKeyboardRecording();
            recordingSession++;
            recordingFocus = false;
            previewingModifiers = false;
            chordCompleted = false;
            ShowValue(hotkey);
            base.OnLostFocus(e);
        }

        protected override void Dispose(bool disposing)
        {
            StopKeyboardRecording();
            base.Dispose(disposing);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            StopKeyboardRecording();
            recordingSession++;
            base.OnHandleDestroyed(e);
        }

        // A registered shortcut (for example another capture app's F1) can be consumed
        // before a TextBox receives WM_KEYDOWN. Observe it only while this recorder is
        // actually focused. This never registers a global shortcut or changes its owner.
        private void StartKeyboardRecording()
        {
            StopKeyboardRecording();
            if (!Focused || FindForm() == null) return;
            Array.Clear(heldKeys, 0, heldKeys.Length);
            Array.Clear(capturedKeys, 0, capturedKeys.Length);
            foreach (Keys key in new[] { Keys.LControlKey, Keys.RControlKey, Keys.LMenu, Keys.RMenu,
                Keys.LShiftKey, Keys.RShiftKey, Keys.LWin, Keys.RWin })
                heldKeys[(int)key] = (GetAsyncKeyState((int)key) & 0x8000) != 0;
            keyboardCallback = KeyboardInput;
            keyboardHook = SetWindowsHookEx(13, keyboardCallback, GetModuleHandle(null), 0);
        }

        private void StopKeyboardRecording()
        {
            if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
            keyboardHook = IntPtr.Zero;
            keyboardCallback = null;
        }

        private IntPtr KeyboardInput(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || !recordingFocus || IsDisposed || !IsHandleCreated || GetFocus() != Handle)
                return CallNextHookEx(keyboardHook, code, message, data);
            Form owner = FindForm();
            if (owner == null || GetForegroundWindow() != owner.Handle)
                return CallNextHookEx(keyboardHook, code, message, data);
            int kind = message.ToInt32();
            bool down = kind == 0x100 || kind == 0x104;
            if (!down && kind != 0x101 && kind != 0x105)
                return CallNextHookEx(keyboardHook, code, message, data);
            Keys key = (Keys)Marshal.ReadInt32(data);
            Keys keyData;
            bool consume = TranslateKeyboardInput(key, down, out keyData);
            if (consume || IsModifier(key))
            {
                // Keep the native callback short; validation/painting runs on the normal
                // UI queue, and a focus change invalidates any queued recording work.
                int session = recordingSession;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed || !recordingFocus || recordingSession != session || !Focused) return;
                        if (down) RecordKey(keyData);
                        else UpdateModifierPreview(keyData & Keys.Modifiers);
                    });
                }
                catch (InvalidOperationException) { }
            }
            return consume ? new IntPtr(1) : CallNextHookEx(keyboardHook, code, message, data);
        }

        internal bool TranslateKeyboardInput(Keys key, bool down, out Keys keyData)
        {
            int number = (int)key;
            keyData = key;
            if (number < 0 || number >= heldKeys.Length) return false;
            heldKeys[number] = down;
            Keys modifiers = Keys.None;
            if (heldKeys[(int)Keys.ControlKey] || heldKeys[(int)Keys.LControlKey] || heldKeys[(int)Keys.RControlKey]) modifiers |= Keys.Control;
            if (heldKeys[(int)Keys.Menu] || heldKeys[(int)Keys.LMenu] || heldKeys[(int)Keys.RMenu]) modifiers |= Keys.Alt;
            if (heldKeys[(int)Keys.ShiftKey] || heldKeys[(int)Keys.LShiftKey] || heldKeys[(int)Keys.RShiftKey]) modifiers |= Keys.Shift;
            keyData |= modifiers;
            if (IsModifier(key) || key == Keys.LWin || key == Keys.RWin) return false;
            bool captured = capturedKeys[number];
            if (!down) { capturedKeys[number] = false; return captured; }
            // Navigation and Windows system shortcuts retain their normal behavior.
            if (IsNavigationTab(keyData) || heldKeys[(int)Keys.LWin] || heldKeys[(int)Keys.RWin]) return false;
            string value;
            if (key != Keys.Escape && !TryFormat(keyData, out value)) return false;
            capturedKeys[number] = true;
            return true;
        }

        private static bool IsNavigationTab(Keys keyData)
        {
            return (keyData & Keys.KeyCode) == Keys.Tab && (keyData & (Keys.Control | Keys.Alt)) == Keys.None;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return !IsNavigationTab(keyData);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (IsNavigationTab(keyData)) return base.ProcessCmdKey(ref msg, keyData);
            RecordKey(keyData);
            return true;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (IsNavigationTab(keyData)) return base.ProcessDialogKey(keyData);
            RecordKey(keyData);
            return true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!IsNavigationTab(e.KeyData))
            {
                RecordKey(e.KeyData);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            e.Handled = true;
            base.OnKeyPress(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            Keys remaining = e.Modifiers;
            if (IsControl(e.KeyCode)) remaining &= ~Keys.Control;
            if (IsAlt(e.KeyCode)) remaining &= ~Keys.Alt;
            if (IsShift(e.KeyCode)) remaining &= ~Keys.Shift;
            UpdateModifierPreview(remaining);
            e.Handled = true;
            base.OnKeyUp(e);
        }

        private void UpdateModifierPreview(Keys remaining)
        {
            if (previewingModifiers && !chordCompleted)
            {
                ShowValue(remaining == Keys.None ? hotkey : ModifierPrefix(remaining) + "…");
                previewingModifiers = remaining != Keys.None;
            }
            if (remaining == Keys.None) chordCompleted = false;
        }

        private void RecordKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (keyData == Keys.Escape)
            {
                Hotkey = focusOriginal;
                return;
            }
            if (key == Keys.LWin || key == Keys.RWin || WindowsKeyDown())
            {
                previewingModifiers = false;
                chordCompleted = true;
                ShowValue("Windows 키 조합은 지원하지 않습니다");
                return;
            }
            if (IsModifier(key))
            {
                if (chordCompleted) return;
                Keys modifiers = keyData & Keys.Modifiers;
                if (IsControl(key)) modifiers |= Keys.Control;
                if (IsAlt(key)) modifiers |= Keys.Alt;
                if (IsShift(key)) modifiers |= Keys.Shift;
                previewingModifiers = true;
                ShowValue(ModifierPrefix(modifiers) + "…");
                return;
            }
            string formatted;
            if (!TryFormat(keyData, out formatted)) return;
            Hotkey = formatted;
            chordCompleted = true;
        }

        internal static bool TryFormat(Keys keyData, out string value)
        {
            value = null;
            Keys key = keyData & Keys.KeyCode;
            int number = (int)key;
            if (number < 8 || number > 255 || IsModifier(key) || key == Keys.LWin || key == Keys.RWin ||
                !Enum.IsDefined(typeof(Keys), key)) return false;
            string name;
            switch (key)
            {
                case Keys.Return: name = "Enter"; break;
                case Keys.Prior: name = "PageUp"; break;
                case Keys.Next: name = "PageDown"; break;
                case Keys.Back: name = "Back"; break;
                default: name = key.ToString(); break;
            }
            value = ModifierPrefix(keyData) + name;
            return true;
        }

        private static string ModifierPrefix(Keys keyData)
        {
            return ((keyData & Keys.Control) != 0 ? "Ctrl+" : "") +
                ((keyData & Keys.Alt) != 0 ? "Alt+" : "") +
                ((keyData & Keys.Shift) != 0 ? "Shift+" : "");
        }

        private static bool IsControl(Keys key) { return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey; }
        private static bool IsAlt(Keys key) { return key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu; }
        private static bool IsShift(Keys key) { return key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey; }
        private static bool IsModifier(Keys key) { return IsControl(key) || IsAlt(key) || IsShift(key); }

        private static bool WindowsKeyDown()
        {
            return (GetKeyState((int)Keys.LWin) & 0x8000) != 0 || (GetKeyState((int)Keys.RWin) & 0x8000) != 0;
        }

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);
        private delegate IntPtr KeyboardHookCallback(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int type, KeyboardHookCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
    }
}
