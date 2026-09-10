using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace ChachaCapture
{
    public sealed class GroupArchive
    {
        public string Name;
        public List<PinRecord> Images = new List<PinRecord>();
    }
    public static class GroupFiles
    {
        public static void Export(string path, Storage store, ImageGroup group, IEnumerable<PinRecord> images)
        {
            GroupArchive manifest = new GroupArchive { Name = group.Name, Images = images.ToList() };
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                using (Stream target = archive.CreateEntry("manifest.xml").Open()) new XmlSerializer(typeof(GroupArchive)).Serialize(target, manifest);
                foreach (PinRecord image in manifest.Images)
                {
                    string source = store.PinPath(image.Id);
                    AddEntry(archive, image.Id + ".png", source);
                    if (image.HasAnimation && File.Exists(source + ".gif")) AddEntry(archive, image.Id + ".gif", source + ".gif");
                }
            }
        }
        private static void AddEntry(ZipArchive archive, string name, string path)
        {
            using (Stream input = File.OpenRead(path)) using (Stream target = archive.CreateEntry(name, CompressionLevel.Optimal).Open()) input.CopyTo(target);
        }
        public static GroupArchive Read(string path, Action<PinRecord, Bitmap, byte[]> receive)
        {
            using (ZipArchive archive = ZipFile.OpenRead(path))
            {
                ZipArchiveEntry entry = archive.GetEntry("manifest.xml");
                if (entry == null || entry.Length > 2 * 1024 * 1024) throw new InvalidDataException("그룹 정보가 없거나 너무 큽니다.");
                GroupArchive group;
                using (Stream input = entry.Open()) group = (GroupArchive)new XmlSerializer(typeof(GroupArchive)).Deserialize(input);
                if (group == null || group.Images == null || group.Images.Count > 100) throw new InvalidDataException("그룹 이미지 수는 최대 100개입니다.");
                long total = 0;
                System.Collections.Generic.HashSet<string> ids = new System.Collections.Generic.HashSet<string>();
                foreach (PinRecord image in group.Images)
                {
                    Guid id;
                    if (image == null || !Guid.TryParseExact(image.Id, "N", out id) || !ids.Add(image.Id)) throw new InvalidDataException("잘못되었거나 중복된 이미지 ID입니다.");
                    ZipArchiveEntry pixels = archive.GetEntry(image.Id + ".png");
                    if (pixels == null || pixels.Length > 64 * 1024 * 1024 || (total += pixels.Length) > 256 * 1024 * 1024) throw new InvalidDataException("그룹 이미지 파일이 없거나 너무 큽니다.");
                    byte[] animation = null;
                    ZipArchiveEntry gif = archive.GetEntry(image.Id + ".gif");
                    if (image.HasAnimation && gif == null) throw new InvalidDataException("그룹의 GIF 원본 파일이 없습니다.");
                    if (image.HasAnimation && gif != null)
                    {
                        if (gif.Length > 32 * 1024 * 1024 || (total += gif.Length) > 256 * 1024 * 1024) throw new InvalidDataException("GIF 파일이 너무 큽니다.");
                        using (MemoryStream memory = new MemoryStream()) { using (Stream source = gif.Open()) source.CopyTo(memory); animation = memory.ToArray(); }
                    }
                    using (Stream input = pixels.Open()) using (Image decoded = Image.FromStream(input))
                    {
                        if ((long)decoded.Width * decoded.Height > 50000000) throw new InvalidDataException("이미지 해상도가 너무 큽니다.");
                        using (Bitmap bitmap = new Bitmap(decoded)) receive(image, bitmap, animation);
                    }
                }
                return group;
            }
        }
    }

    public sealed class GroupsForm : Form
    {
        private readonly CaptureApplication app;
        private readonly ListBox groupList;
        private readonly ListView imageList;
        private readonly ImageList thumbnails;
        private readonly ComboBox destination;
        private bool refreshing;
        public GroupsForm(CaptureApplication controller)
        {
            app = controller; SuspendLayout();
            Text = "Chacha · 이미지 그룹"; Icon = Ui.CreateIcon(); Font = Ui.Font(10, FontStyle.Regular); BackColor = Ui.Background; ForeColor = Ui.Text;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(890, 590); MinimumSize = new Size(790, 520); StartPosition = FormStartPosition.CenterParent;
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(18) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 215)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102)); Controls.Add(root);
            Label title = Ui.Label("이미지 그룹", 19, Ui.Text); root.Controls.Add(title, 0, 0);
            Label help = Ui.Label("그룹별로 작업 이미지를 보관하고 한 번에 전환하세요.", 10, Ui.Muted); help.Dock = DockStyle.Fill; help.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(help, 1, 0);
            groupList = new ListBox { Dock = DockStyle.Fill, BackColor = Ui.Surface, ForeColor = Ui.Text, BorderStyle = BorderStyle.None, ItemHeight = 32, IntegralHeight = false, DisplayMember = "Name" };
            groupList.SelectedIndexChanged += delegate { if (!refreshing) RefreshImages(); }; root.Controls.Add(groupList, 0, 1);
            thumbnails = new ImageList { ImageSize = new Size(96, 72), ColorDepth = ColorDepth.Depth32Bit };
            // Force native allocation before adding short-lived bitmaps: ImageList otherwise defers copying until first display.
            thumbnails.Handle.ToInt64();
            imageList = new ListView { Dock = DockStyle.Fill, View = View.LargeIcon, CheckBoxes = true, MultiSelect = true, BackColor = Ui.Surface, ForeColor = Ui.Text, LargeImageList = thumbnails, BorderStyle = BorderStyle.None, HideSelection = false };
            imageList.ItemCheck += delegate(object sender, ItemCheckEventArgs e) { if (!refreshing && e.Index < imageList.Items.Count) { PinForm pin = imageList.Items[e.Index].Tag as PinForm; if (pin != null && !pin.IsDisposed) pin.IsSelected = e.NewValue == CheckState.Checked; } };
            imageList.DoubleClick += delegate { foreach (ListViewItem item in imageList.SelectedItems) app.RestorePin(item.Tag as PinForm); };
            root.Controls.Add(imageList, 1, 1);
            FlowLayoutPanel left = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
            AddButton(left, "새 그룹", 89, delegate { string name = Prompt("새 그룹 이름", ""); if (name != null) { app.CreateGroup(name); RefreshGroups(); } });
            AddButton(left, "이름 변경", 89, delegate { ImageGroup group = SelectedGroup; if (group == null) return; string name = Prompt("그룹 이름 변경", group.Name); if (name != null) { group.Name = name; app.Store.SaveGroups(); RefreshGroups(); } });
            AddButton(left, "그룹 해제", 195, delegate { ImageGroup group = SelectedGroup; if (group == null || group.Id == "default") return; app.RemoveGroup(group.Id); RefreshGroups(); }); root.Controls.Add(left, 0, 2);
            FlowLayoutPanel right = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
            AddButton(right, "그룹 전환", 106, delegate { if (SelectedGroup != null) app.SwitchGroup(SelectedGroup.Id); });
            AddButton(right, "내보내기", 96, delegate { Export(); }); AddButton(right, "가져오기", 96, delegate { Import(); });
            AddButton(right, "선택 모두", 100, delegate { foreach (ListViewItem item in imageList.Items) item.Checked = true; });
            destination = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 195, DisplayMember = "Name", BackColor = Ui.Surface, ForeColor = Ui.Text, Margin = new Padding(0, 0, 8, 0) }; right.Controls.Add(destination);
            AddButton(right, "선택 이미지 이동", 160, delegate { ImageGroup group = destination.SelectedItem as ImageGroup; if (group == null) return; app.MoveSelectedToGroup(group.Id); RefreshImages(); }); root.Controls.Add(right, 1, 2);
            RefreshGroups(); ResumeLayout(true);
        }
        private ImageGroup SelectedGroup { get { return groupList.SelectedItem as ImageGroup; } }
        protected override void Dispose(bool disposing) { if (disposing && thumbnails != null) thumbnails.Dispose(); base.Dispose(disposing); }
        private void AddButton(Control panel, string text, int width, EventHandler click) { Button button = Ui.Button(text, false, click); button.Width = width; button.Height = 35; panel.Controls.Add(button); }
        private void RefreshGroups()
        {
            string selected = SelectedGroup == null ? app.Store.Settings.ActiveGroup : SelectedGroup.Id;
            refreshing = true; groupList.Items.Clear(); destination.Items.Clear();
            foreach (ImageGroup group in app.Store.Groups) { groupList.Items.Add(group); destination.Items.Add(group); if (group.Id == selected) groupList.SelectedItem = group; }
            if (groupList.SelectedIndex < 0 && groupList.Items.Count > 0) groupList.SelectedIndex = 0;
            if (destination.Items.Count > 0) destination.SelectedIndex = 0;
            refreshing = false; RefreshImages();
        }
        private void RefreshImages()
        {
            refreshing = true; imageList.Items.Clear(); thumbnails.Images.Clear();
            ImageGroup group = SelectedGroup;
            if (group != null) foreach (PinForm pin in app.AllPins.Where(p => p.GroupId == group.Id))
            {
                using (Bitmap image = pin.ExportImage()) using (Bitmap thumb = new Bitmap(96, 72))
                {
                    using (Graphics g = Graphics.FromImage(thumb)) { g.Clear(Ui.Surface); float factor = Math.Min(96f / image.Width, 72f / image.Height); int w = Math.Max(1, (int)(image.Width * factor)), h = Math.Max(1, (int)(image.Height * factor)); g.DrawImage(image, (96 - w) / 2, (72 - h) / 2, w, h); }
                    thumbnails.Images.Add(thumb);
                    imageList.Items.Add(new ListViewItem(image.Width + " × " + image.Height + (pin.FrameCount > 1 ? " GIF" : ""), thumbnails.Images.Count - 1) { Tag = pin, Checked = pin.IsSelected });
                }
            }
            refreshing = false;
        }
        private void Export()
        {
            if (SelectedGroup == null) return;
            using (SaveFileDialog dialog = new SaveFileDialog { Filter = "Chacha 이미지 그룹|*.chacha", FileName = "image-group.chacha" }) if (dialog.ShowDialog(this) == DialogResult.OK) { try { app.ExportGroup(dialog.FileName, SelectedGroup.Id); } catch (Exception e) { Program.Report(e); } }
        }
        private void Import()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Chacha 이미지 그룹|*.chacha" }) if (dialog.ShowDialog(this) == DialogResult.OK) { try { app.ImportGroup(dialog.FileName); RefreshGroups(); } catch (Exception e) { Program.Report(e); } }
        }
        public static string Prompt(string title, string initial)
        {
            using (Form dialog = new Form { Text = title, ClientSize = new Size(390, 118), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, BackColor = Ui.Background, Font = Ui.Font(10, FontStyle.Regular) })
            {
                TextBox field = new TextBox { Text = initial, MaxLength = 60, Location = new Point(18, 18), Width = 354, BackColor = Ui.Surface, ForeColor = Ui.Text };
                Button ok = Ui.Button("확인", true, null); ok.SetBounds(272, 66, 100, 35); ok.DialogResult = DialogResult.OK; dialog.AcceptButton = ok;
                Button cancel = Ui.Button("취소", false, null); cancel.SetBounds(164, 66, 100, 35); cancel.DialogResult = DialogResult.Cancel; dialog.CancelButton = cancel;
                dialog.Controls.AddRange(new Control[] { field, ok, cancel }); return dialog.ShowDialog() == DialogResult.OK && !String.IsNullOrWhiteSpace(field.Text) ? field.Text.Trim() : null;
            }
        }
    }
}
