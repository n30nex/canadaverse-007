using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Canadaverse Launcher")]
[assembly: System.Reflection.AssemblyDescription("Independent local game launcher and staging room directory")]
[assembly: System.Reflection.AssemblyCompany("Canadaverse contributors")]
[assembly: System.Reflection.AssemblyVersion("0.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.2.0.0")]

namespace Canadaverse {
    static class Program {
        [STAThread]
        static void Main(string[] args) {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Canadaverse007");
            string portable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "portable");
            if (Directory.Exists(portable)) state = portable;
            string page = "play";
            for (int i = 0; i < args.Length; i++) {
                if (args[i] == "--state-dir" && i + 1 < args.Length) state = Path.GetFullPath(args[++i]);
                else if (args[i] == "--page" && i + 1 < args.Length) page = args[++i];
                else { MessageBox.Show("Supported options: --state-dir PATH, --page play|rooms|setup", "Canadaverse Launcher"); return; }
            }
            try { Application.Run(new LauncherForm(state, page)); }
            catch (Exception error) { MessageBox.Show(error.Message, "Canadaverse Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    sealed class SignalArt : Control {
        public SignalArt() { DoubleBuffered = true; TabStop = false; BackColor = Color.FromArgb(19, 27, 36); }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var faint = new Pen(Color.FromArgb(35, 52, 64))) {
                for (int x = 20; x < Width; x += 24) g.DrawLine(faint, x, 0, x, Height);
                for (int y = 12; y < Height; y += 24) g.DrawLine(faint, 0, y, Width, y);
            }
            var center = new Point(Width / 2, Height / 2);
            using (var pen = new Pen(Color.FromArgb(116, 173, 157), 2)) {
                for (int radius = 28; radius <= 100; radius += 24)
                    g.DrawArc(pen, center.X - radius, center.Y - radius, radius * 2, radius * 2, -70, 270);
                g.DrawLine(pen, center.X, center.Y, center.X + 75, center.Y - 75);
            }
            using (var gold = new SolidBrush(Color.FromArgb(231, 193, 109))) {
                g.FillEllipse(gold, center.X - 6, center.Y - 6, 12, 12);
                g.FillEllipse(gold, center.X + 69, center.Y - 81, 12, 12);
            }
        }
    }

    public sealed class LauncherForm : Form {
        static readonly Color Bg = Color.FromArgb(12, 16, 22), PanelColor = Color.FromArgb(19, 27, 36),
            Line = Color.FromArgb(38, 51, 63), Ink = Color.FromArgb(237, 244, 245),
            Muted = Color.FromArgb(155, 174, 187), Gold = Color.FromArgb(231, 193, 109), Green = Color.FromArgb(127, 212, 181);
        readonly string state;
        Settings settings;
        readonly Panel content = new Panel();
        readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        readonly Dictionary<string, Button> nav = new Dictionary<string, Button>();
        readonly Label status = new Label(), engineStatus = new Label(), directoryStatus = new Label(), hostStatus = new Label();
        readonly TextBox engine = new TextBox(), rom = new TextBox(), master = new TextBox(), host = new TextBox(), roomName = new TextBox();
        readonly CheckBox development = new CheckBox(), matching = new CheckBox();
        readonly ComboBox roomMap = new ComboBox(), capacity = new ComboBox();
        readonly ListView roomList = new ListView();
        Button playButton, createButton, closeButton, refreshButton;
        readonly Timer timer = new Timer();
        DirectoryClient leaseClient;
        string roomId, hostToken;
        bool busy, refreshing, heartbeatBusy, closing, closed;
        DateTime leaseUntil;
        Process game;

        public LauncherForm(string stateDirectory, string initialPage) {
            state = stateDirectory; settings = Core.Load(state);
            Text = "Canadaverse Launcher"; BackColor = Bg; ForeColor = Ink; Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            ClientSize = new Size(1050, 740); MinimumSize = new Size(980, 720); StartPosition = FormStartPosition.CenterScreen;
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 205, BackColor = Color.FromArgb(15, 21, 29) };
            var brand = Label("C", 22, 24, 42, 42, 24, Gold); sidebar.Controls.Add(brand);
            sidebar.Controls.Add(Label("CANADAVERSE", 22, 77, 180, 30, 12, Ink, true));
            sidebar.Controls.Add(Label("GOLDENEYE / 007", 22, 105, 180, 24, 9, Muted));
            string[] keys = { "play", "rooms", "setup" }, names = { "01     Play", "02     Rooms", "03     Setup" };
            for (int i = 0; i < keys.Length; i++) {
                string key = keys[i]; var b = Button(names[i], 14, 172 + 55 * i, 176, 44, false);
                b.TextAlign = ContentAlignment.MiddleLeft; b.Padding = new Padding(12, 0, 0, 0); b.Click += (s, e) => ShowPage(key);
                nav[key] = b; sidebar.Controls.Add(b);
            }
            var footer = new Panel { Dock = DockStyle.Bottom, Height = 140, BackColor = sidebar.BackColor };
            footer.Controls.Add(Label("INDEPENDENT. OPEN.", 22, 5, 180, 24, 9, Green));
            footer.Controls.Add(Label("Your ROM stays on your PC.", 22, 38, 170, 42, 9, Muted));
            footer.Controls.Add(Label(Core.Version, 22, 95, 180, 24, 9, Muted)); sidebar.Controls.Add(footer);
            content.Dock = DockStyle.Fill; content.Padding = new Padding(32, 24, 28, 20);
            status.Dock = DockStyle.Bottom; status.Height = 54; status.Padding = new Padding(24, 10, 20, 4);
            status.ForeColor = Muted; status.BackColor = Color.FromArgb(17, 24, 32); status.Font = new Font("Segoe UI", 9);
            status.Text = "Ready. Choose Setup to connect your engine and ROM.";
            Controls.Add(content); Controls.Add(status); Controls.Add(sidebar);
            BuildPlay(); BuildRooms(); BuildSetup(); ShowPage(pages.ContainsKey(initialPage) ? initialPage : "play");
            UpdateEngineStatus(); timer.Interval = 15000; timer.Tick += async (s, e) => { await Heartbeat(); if (pages["rooms"].Visible) await RefreshRooms(); PollGame(); }; timer.Start();
            FormClosing += OnClosing;
        }
        static Label Label(string text, int x, int y, int width, int height, float size, Color color, bool bold = false) {
            return new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), ForeColor = color,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), BackColor = Color.Transparent };
        }
        static Button Button(string text, int x, int y, int width, int height, bool primary) {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, height), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Gold : Color.FromArgb(28, 42, 54), ForeColor = primary ? Bg : Ink, Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 10, primary ? FontStyle.Bold : FontStyle.Regular), UseVisualStyleBackColor = false };
            b.FlatAppearance.BorderColor = primary ? Gold : Line; b.FlatAppearance.BorderSize = 1; return b;
        }
        Panel Page(string key, string eyebrow, string title, string subtitle) {
            var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Bg, Visible = false };
            page.Controls.Add(Label(eyebrow, 0, 0, 720, 24, 9, Gold, true));
            page.Controls.Add(Label(title, 0, 34, 720, 54, 30, Ink));
            page.Controls.Add(Label(subtitle, 0, 96, 720, 42, 10, Muted));
            pages[key] = page; content.Controls.Add(page); return page;
        }
        Panel Card(Panel parent, int y, int height) {
            var panel = new Panel { Location = new Point(0, y), Size = new Size(745, height), BackColor = PanelColor,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            parent.Controls.Add(panel); return panel;
        }
        void BuildPlay() {
            var page = Page("play", "LOCAL PLAY / READY WHEN YOU ARE", "Back in the field.", "A modern launchpad for your GoldenEye backup.");
            var hero = Card(page, 154, 260);
            hero.Controls.Add(Label("GOLDENEYE 007", 24, 25, 440, 38, 23, Ink, true));
            hero.Controls.Add(Label("Project Midas 1.0.2b · Preview", 24, 74, 420, 28, 11, Gold));
            hero.Controls.Add(Label("Widescreen. Modern controls. Your original game.\nAll game files and saves stay on your computer.", 24, 118, 430, 54, 10, Muted));
            playButton = Button("Play GoldenEye   →", 24, 196, 210, 42, true);
            playButton.Click += async (s, e) => await Run(async () => {
                if (Process.GetProcessesByName("ProjectMidas").Length > 0) throw new InvalidOperationException("GoldenEye is already running.");
                game = await Task.Run(() => Core.Launch(settings, state)); Say("GoldenEye started. Close the game normally when you’re finished.");
            }); hero.Controls.Add(playButton);
            var verify = Button("Verify files", 248, 196, 130, 42, false); verify.Click += async (s, e) => await Verify(); hero.Controls.Add(verify);
            var art = new SignalArt { Location = new Point(485, 22), Size = new Size(235, 220), Anchor = AnchorStyles.Top | AnchorStyles.Right }; hero.Controls.Add(art);
            var installed = Card(page, 432, 85);
            installed.Controls.Add(Label("YOUR INSTALLATION", 20, 14, 650, 22, 9, Green, true));
            engineStatus.SetBounds(20, 42, 690, 30); engineStatus.ForeColor = Muted; engineStatus.Font = new Font("Segoe UI", 9); installed.Controls.Add(engineStatus);
            var note = Card(page, 535, 104);
            note.Controls.Add(Label("Online play is still being built.", 20, 15, 690, 25, 12, Gold));
            note.Controls.Add(Label("The Rooms page connects to the master directory. Staging rooms do not yet\nconnect game clients. Local single-player and split-screen are available in the engine.", 20, 49, 690, 48, 9, Muted));
        }
        void TextField(Panel page, string title, TextBox field, int y, string value, bool browse, string filter) {
            page.Controls.Add(Label(title, 0, y, 720, 22, 10, Ink));
            field.SetBounds(0, y + 28, browse ? 610 : 745, 31); field.BackColor = PanelColor; field.ForeColor = Ink;
            field.BorderStyle = BorderStyle.FixedSingle; field.Font = new Font("Segoe UI", 11); field.Text = value ?? "";
            field.AccessibleName = title; field.MaxLength = 4096; page.Controls.Add(field);
            if (browse) {
                var b = Button("Browse…", 623, y + 25, 122, 35, false); b.Click += (s, e) => {
                    using (var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true })
                        if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.FileName;
                }; page.Controls.Add(b);
            }
        }
        void BuildSetup() {
            var page = Page("setup", "YOUR PC / YOUR FILES", "Make it yours.", "Select an existing engine, or install our verified preview from upstream.");
            TextField(page, "Project Midas engine", engine, 145, settings.Engine, true, "Project Midas|ProjectMidas.exe");
            TextField(page, "Your USA ROM or ZIP backup", rom, 223, settings.Rom, true, "ROM backup|*.z64;*.zip");
            TextField(page, "Master server", master, 301, settings.Master, false, "");
            TextField(page, "Public host name", host, 379, settings.Host, false, ""); host.MaxLength = 24;
            development.SetBounds(0, 455, 730, 28); development.Text = "Allow a local source build (only executables you built or trust)";
            development.Checked = settings.Development; development.ForeColor = Muted; development.Font = new Font("Segoe UI", 9); page.Controls.Add(development);
            var save = Button("Save setup", 0, 500, 160, 42, true); save.Click += async (s, e) => await Run(async () => {
                Core.MasterUri(master.Text.Trim());
                if (host.Text.Trim().Length == 0) throw new InvalidDataException("Enter a public host name.");
                if (roomId != null && master.Text.Trim().TrimEnd('/') != settings.Master.TrimEnd('/')) await CloseRoom();
                settings = new Settings { Engine = engine.Text.Trim(), Rom = rom.Text.Trim(), Master = master.Text.Trim().TrimEnd('/'), Host = host.Text.Trim(), Development = development.Checked };
                Core.Save(state, settings); UpdateEngineStatus(); Say("Setup saved. Use Verify files before your first launch.");
            }); page.Controls.Add(save);
            var install = Button("Install pinned engine", 174, 500, 208, 42, false); install.Click += async (s, e) => await Run(async () => {
                try { FinishInstall(await Core.InstallEngine(state, Say)); }
                catch (System.Net.Http.HttpRequestException error) { throw new InvalidDataException("Download unavailable. Get the Windows ZIP from the upstream release, then choose Install engine ZIP.", error); }
            }); page.Controls.Add(install);
            var archive = Button("Install engine ZIP", 396, 500, 180, 42, false); archive.Click += async (s, e) => {
                using (var dialog = new OpenFileDialog { Filter = "Official engine archive|*.zip", CheckFileExists = true }) {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    string path = dialog.FileName;
                    await Run(async () => FinishInstall(await Task.Run(() => Core.InstallEngineArchive(state, path))));
                }
            }; page.Controls.Add(archive);
            var source = Button("Source & releases", 590, 500, 155, 42, false); source.Click += (s, e) => Open("https://github.com/n30nex/canadaverse-007"); page.Controls.Add(source);
            page.Controls.Add(Label("Nothing is uploaded when verifying a ROM. The original ZIP stays untouched.\nThe launcher downloads no ROMs, mods or game assets from the master server.", 0, 565, 735, 54, 9, Muted));
        }
        void BuildRooms() {
            var page = Page("rooms", "MASTER DIRECTORY / COMMUNITY", "Find your people.", "Live staging listings. Game connections will arrive with a future netcode release.");
            var notice = Card(page, 143, 65); notice.Controls.Add(Label("STAGING ONLY  ·  This engine cannot host or join an online match yet.", 18, 21, 715, 28, 10, Gold));
            directoryStatus.SetBounds(0, 226, 540, 30); directoryStatus.ForeColor = Muted; directoryStatus.Text = "Connect to the master server to load rooms."; page.Controls.Add(directoryStatus);
            refreshButton = Button("Refresh", 625, 219, 120, 36, false); refreshButton.Click += async (s, e) => await RefreshRooms(); page.Controls.Add(refreshButton);
            matching.SetBounds(0, 258, 730, 28); matching.Text = "Show only my engine build"; matching.ForeColor = Muted; matching.Font = new Font("Segoe UI", 9);
            matching.CheckedChanged += async (s, e) => await RefreshRooms(); page.Controls.Add(matching);
            roomList.SetBounds(0, 295, 745, 190); roomList.View = View.Details; roomList.FullRowSelect = true; roomList.MultiSelect = false;
            roomList.HideSelection = false; roomList.BackColor = PanelColor; roomList.ForeColor = Ink; roomList.BorderStyle = BorderStyle.FixedSingle;
            roomList.Columns.Add("Room / host", 280); roomList.Columns.Add("Map", 100); roomList.Columns.Add("Presence", 80); roomList.Columns.Add("Build", 120); roomList.Columns.Add("Status", 135);
            roomList.AccessibleName = "Live room listings"; page.Controls.Add(roomList);
            var create = Card(page, 502, 134);
            create.Controls.Add(Label("CREATE A STAGING ROOM", 16, 12, 650, 24, 9, Green, true));
            roomName.SetBounds(16, 45, 265, 28); roomName.Text = "GoldenEye hangout"; roomName.MaxLength = 60; roomName.AccessibleName = "Room name";
            roomName.BackColor = Bg; roomName.ForeColor = Ink; create.Controls.Add(roomName);
            roomMap.SetBounds(292, 45, 140, 28); roomMap.DropDownStyle = ComboBoxStyle.DropDownList;
            roomMap.Items.AddRange(new object[] { "Facility", "Complex", "Temple", "Library", "Caverns", "Archives", "Bunker", "Basement", "Stack" }); roomMap.SelectedIndex = 0; roomMap.AccessibleName = "Room map"; create.Controls.Add(roomMap);
            capacity.SetBounds(443, 45, 89, 28); capacity.DropDownStyle = ComboBoxStyle.DropDownList; capacity.Items.AddRange(new object[] { "2 slots", "3 slots", "4 slots" }); capacity.SelectedIndex = 2; capacity.AccessibleName = "Room capacity"; create.Controls.Add(capacity);
            createButton = Button("Create room", 544, 41, 180, 36, true); createButton.Click += async (s, e) => await Run(CreateRoom); create.Controls.Add(createButton);
            hostStatus.SetBounds(16, 91, 490, 30); hostStatus.Text = "Only your room name, host name and build metadata are public."; hostStatus.ForeColor = Muted; hostStatus.Font = new Font("Segoe UI", 8); create.Controls.Add(hostStatus);
            closeButton = Button("Close room", 544, 87, 180, 32, false); closeButton.Enabled = false; closeButton.Click += async (s, e) => await Run(CloseRoom); create.Controls.Add(closeButton);
        }
        void CreateGraphics(string executable) {
            string path = Path.Combine(Path.GetDirectoryName(executable), "midas.ini");
            if (File.Exists(path)) return;
            var bounds = Screen.FromControl(this).Bounds;
            File.WriteAllText(path, "[input]\nsmart_mouse_lock=1\np1_settings_for=kbm\n[display]\nwindow_width=" + bounds.Width + "\nwindow_height=" + bounds.Height + "\nfullscreen=fullscreen\nwidescreen=1\nhfov_wide=90\nmaxfps=60\nvsync=1\nmsaa=4\nfiltering=trilinear\nanisotropy=16\nfilter_2d=0\nroom_cache=2\nunlock_all=0\nhead_roll=1\n");
        }
        void FinishInstall(string path) {
            engine.Text = path; settings.Engine = path; settings.Development = false; development.Checked = false;
            CreateGraphics(path); Core.Save(state, settings); UpdateEngineStatus(); Say("Engine installed and verified. Select your ROM and save setup.");
        }
        async void ShowPage(string key) {
            foreach (var pair in pages) pair.Value.Visible = pair.Key == key;
            foreach (var pair in nav) { pair.Value.BackColor = pair.Key == key ? Color.FromArgb(41, 43, 37) : Color.FromArgb(15, 21, 29); pair.Value.ForeColor = pair.Key == key ? Gold : Muted; }
            pages[key].BringToFront(); if (key == "rooms") await RefreshRooms();
        }
        void UpdateEngineStatus() {
            engineStatus.Text = File.Exists(settings.Engine) ? "Engine selected · " + (settings.Development ? "Local source build" : "Pinned preview") + " · " + (File.Exists(settings.Rom) ? "ROM selected" : "Choose your ROM in Setup") : "No engine selected. Open Setup to get started.";
        }
        void Say(string message) { if (!IsDisposed) { status.ForeColor = Muted; status.Text = message; } }
        async Task Run(Func<Task> action) {
            if (busy) return; busy = true; UseWaitCursor = true;
            try { await action(); }
            catch (Exception error) { status.ForeColor = Gold; status.Text = error.Message; }
            finally { if (!IsDisposed) UseWaitCursor = false; busy = false; }
        }
        async Task Verify() { await Run(async () => { Say("Verifying engine and ROM…"); await Task.Run(() => { Core.InspectEngine(settings.Engine, settings.Development); Core.ReadRom(settings.Rom); }); Say("Verified: supported USA ROM and selected engine. Ready for local play."); }); }
        void PollGame() { if (game != null && game.HasExited) { Say("GoldenEye closed. Exit code: " + game.ExitCode); game.Dispose(); game = null; } }
        async Task RefreshRooms() {
            if (refreshing || closed) return; refreshing = true; refreshButton.Enabled = false;
            try {
                string suffix = "";
                if (matching.Checked) suffix = "?build=" + await Task.Run(() => Core.InspectEngine(settings.Engine, settings.Development));
                using (var client = new DirectoryClient(settings.Master)) {
                    var response = await client.Request("GET", "/v1/rooms" + suffix);
                    var rows = Core.RoomRows(response);
                    roomList.Items.Clear();
                    foreach (var obj in rows) {
                        var row = (Dictionary<string, object>)obj; var build = (Dictionary<string, object>)row["build"];
                        string digest = Convert.ToString(build["engine_sha256"]);
                        var item = new ListViewItem(Convert.ToString(row["name"]) + " / " + Convert.ToString(row["host"]));
                        item.SubItems.Add(Convert.ToString(row["map"])); item.SubItems.Add(Convert.ToString(row["players"]) + " / " + Convert.ToString(row["capacity"]));
                        item.SubItems.Add(digest.Length >= 12 ? digest.Substring(0, 12) : "Unknown"); item.SubItems.Add("Staging only"); roomList.Items.Add(item);
                    }
                    directoryStatus.ForeColor = Green;
                    directoryStatus.Text = rows.Length == 0 ? "Connected · No rooms right now. Create the first staging room." : "Connected · " + rows.Length + " room(s) · Updated " + DateTime.Now.ToShortTimeString();
                }
            } catch (Exception error) { roomList.Items.Clear(); directoryStatus.ForeColor = Gold; directoryStatus.Text = "Directory unavailable. Local play still works."; Say(error.Message); }
            finally { refreshing = false; if (!IsDisposed) refreshButton.Enabled = true; }
        }
        async Task CreateRoom() {
            if (roomId != null) throw new InvalidOperationException("Close your current room before creating another.");
            Say("Verifying files and registering your staging room…"); string name = roomName.Text, map = Convert.ToString(roomMap.SelectedItem); int slots = capacity.SelectedIndex + 2;
            var body = await Task.Run(() => Core.RoomBody(settings, name, map, slots));
            var client = new DirectoryClient(settings.Master);
            try {
                var response = await client.Request("POST", "/v1/rooms", body); var room = (Dictionary<string, object>)response["room"];
                roomId = Convert.ToString(room["id"]); hostToken = Convert.ToString(response["host_token"]); leaseClient = client;
                leaseUntil = DateTime.UtcNow.AddSeconds(Convert.ToInt32(response["lease_seconds"]));
                createButton.Enabled = false; closeButton.Enabled = true; hostStatus.Text = "Your staging room is listed. Kept alive while this launcher is open.";
                Say("Room registered. Online gameplay is not available in this engine."); await RefreshRooms();
            } catch { client.Dispose(); throw; }
        }
        async Task Heartbeat() {
            if (roomId == null || heartbeatBusy || closing) return; heartbeatBusy = true;
            string renewing = roomId;
            try {
                await leaseClient.Request("POST", "/v1/rooms/" + roomId + "/heartbeat", new { status = "staging" }, hostToken);
                if (renewing != roomId || closed) return;
                leaseUntil = DateTime.UtcNow.AddSeconds(45); hostStatus.Text = "Your staging room is live · Lease renewed " + DateTime.Now.ToShortTimeString();
            } catch (Exception) {
                if (renewing != roomId || closed) return;
                if (DateTime.UtcNow >= leaseUntil) { ForgetRoom(); hostStatus.Text = "Room lease expired. Reconnect and create it again."; }
                else hostStatus.Text = "Heartbeat failed. Retrying before the room lease expires…";
            } finally { heartbeatBusy = false; }
        }
        void ForgetRoom() {
            roomId = null; hostToken = null; if (leaseClient != null) leaseClient.Dispose(); leaseClient = null;
            if (!IsDisposed) { createButton.Enabled = true; closeButton.Enabled = false; }
        }
        async Task CloseRoom() {
            if (roomId == null) return;
            try { await leaseClient.Request("DELETE", "/v1/rooms/" + roomId, null, hostToken); }
            finally { ForgetRoom(); hostStatus.Text = "Room closed, or its lease will expire within 45 seconds."; }
            await RefreshRooms(); Say("Staging room closed.");
        }
        async void OnClosing(object sender, FormClosingEventArgs e) {
            if (closed) return; e.Cancel = true; if (closing) return; closing = true; timer.Stop();
            try { await CloseRoom(); } catch { /* Server removes an orphaned lease automatically. */ }
            closed = true; Close();
        }
        static void Open(string url) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    }
}
