using System;
using System.Collections;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ChachaCapture
{
    // Render only generated images and hidden controls. No desktop pixels, clipboard or user input.
    internal static class EditorUiRegressionTests
    {
        private static void Assert(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static T Field<T>(object target, string name)
        { return (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }

        private static void CreateRenderHandles(Control control)
        {
            IntPtr handle = control.Handle;
            foreach (Control child in control.Controls) CreateRenderHandles(child);
            control.PerformLayout();
        }

        internal static void Run()
        {
            using (Bitmap pixels = new Bitmap(400, 230))
            using (Bitmap desktop = new Bitmap(1280, 900))
            {
                using (Graphics graphics = Graphics.FromImage(pixels)) graphics.Clear(Color.CornflowerBlue);
                using (EditorForm editor = new EditorForm(pixels, desktop, new Rectangle(0, 0, 1280, 900), new Rectangle(200, 200, 400, 230)))
                {
                    CreateRenderHandles(editor);
                    FlowLayoutPanel tools = Field<FlowLayoutPanel>(editor, "_toolRow");
                    IDictionary buttons = Field<IDictionary>(editor, "_toolButtons");
                    Assert(buttons.Count == 14, "Capture editing lost a drawing tool.");
                    foreach (DictionaryEntry entry in buttons)
                    {
                        Button button = (Button)entry.Value;
                        bool named = false;
                        foreach (char letter in button.Text) if (letter >= '\uac00' && letter <= '\ud7a3') named = true;
                        Assert(named, "An inline tool replaced its readable Korean name with a symbol: " + entry.Key);
                        Assert(button.Right <= tools.ClientSize.Width && button.Bottom <= tools.ClientSize.Height,
                            "The inline tool is clipped by its row: " + button.Text);
                        Assert(button.AccessibleName == button.Text + " 도구", "The tool has no accessible label.");
                        AssertTextFitsAndPaints(button);
                    }
                    AssertTextFitsAndPaints(Field<Button>(editor, "_fillButton"));
                    AssertTextFitsAndPaints(Field<Button>(editor, "_colorButton"));
                    Panel bar = Field<Panel>(editor, "_inlineBar");
                    foreach (Control row in bar.Controls)
                        Assert(row.Bottom <= bar.ClientSize.Height, "The editor properties or completion buttons are clipped.");
                    Assert(editor.CurrentScreenBounds == new Rectangle(200, 200, 400, 230), "Toolbar layout moved the captured pixels.");
                }

                using (EditorForm editor = new EditorForm(pixels))
                {
                    int floated = 0, committed = 0;
                    editor.PinRequested += delegate(Bitmap result)
                    {
                        using (result) Assert(result.GetPixel(20, 20).ToArgb() == Color.CornflowerBlue.ToArgb(), "Floating changed the source pixels.");
                        floated++;
                    };
                    editor.ImageCommitted += delegate(Bitmap result) { result.Dispose(); committed++; };
                    editor.FloatingHotkey = "Ctrl+Alt+F8";
                    Press(editor, Keys.F3); Assert(floated == 0, "The editor retained a hardcoded F3 shortcut.");
                    Assert(Press(editor, Keys.Control | Keys.Alt | Keys.F8) && floated == 1, "The configured floating shortcut was ignored.");
                    editor.FloatingHotkey = "";
                    Press(editor, Keys.Control | Keys.Alt | Keys.F8); Assert(floated == 1, "Clearing the floating shortcut left it active.");
                    Assert(Press(editor, Keys.Control | Keys.T) && floated == 2, "The local Ctrl+T action stopped working.");
                    editor.FloatSelection(); Assert(floated == 3 && committed == 0, "Explicit floating entered the copy/save commit pipeline.");
                }
                using (EditorForm editor = new EditorForm(pixels, desktop, new Rectangle(0, 0, 1280, 900), new Rectangle(200, 200, 400, 230)))
                {
                    int floated = 0, committed = 0;
                    editor.PinRequested += delegate(Bitmap result) { result.Dispose(); floated++; };
                    editor.ImageCommitted += delegate(Bitmap result) { result.Dispose(); committed++; };
                    editor.FloatSelection();
                    Assert(floated == 1 && committed == 0, "Inline floating triggered history or automatic saving.");
                }
                using (EditorForm editor = new EditorForm(pixels, new Rectangle(80, 90, 400, 230)))
                {
                    int committed = 0;
                    editor.PinRequested += delegate(Bitmap result) { result.Dispose(); };
                    editor.ImageCommitted += delegate(Bitmap result) { result.Dispose(); committed++; };
                    editor.FloatSelection();
                    Assert(committed == 1, "Finishing an existing pin lost its edited pixels.");
                }
            }
        }

        private static bool Press(EditorForm editor, Keys keys)
        {
            Message message = Message.Create(editor.Handle, 0x100, new IntPtr((int)(keys & Keys.KeyCode)), IntPtr.Zero);
            return (bool)typeof(EditorForm).GetMethod("ProcessCmdKey", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(editor, new object[] { message, keys });
        }

        private static void AssertTextFitsAndPaints(Button button)
        {
            string label = button.Text;
            using (Bitmap painted = new Bitmap(button.Width, button.Height))
            using (Bitmap empty = new Bitmap(button.Width, button.Height))
            {
                using (Graphics graphics = Graphics.FromImage(painted))
                {
                    Size text = TextRenderer.MeasureText(graphics, label, button.Font, new Size(Int32.MaxValue, Int32.MaxValue),
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    Assert(text.Width <= button.Width - 10 && text.Height <= button.Height - 6,
                        "The editor label cannot fit without truncation: " + label);
                }
                button.DrawToBitmap(painted, new Rectangle(Point.Empty, painted.Size));
                try { button.Text = ""; button.DrawToBitmap(empty, new Rectangle(Point.Empty, empty.Size)); }
                finally { button.Text = label; }
                int inkPixels = 0;
                for (int y = 3; y < painted.Height - 3; y++)
                    for (int x = 5; x < painted.Width - 5; x++)
                        if (painted.GetPixel(x, y).ToArgb() != empty.GetPixel(x, y).ToArgb()) inkPixels++;
                Assert(inkPixels > 5, "The editor button has a name but renders no visible text: " + label);
            }
        }
    }
}
