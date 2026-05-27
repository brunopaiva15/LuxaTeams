using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LuxaTeams
{
    public class Form1 : Form
    {
        // --- CONFIGURATION & ENGINE ---
        private static readonly HttpClient client = new HttpClient();
        private const string LuxaforUrl = "https://api.luxafor.com/webhook/v1/actions/solid_color";
        private static readonly string[] BusyActivities = { "InACall", "InAConferenceCall", "OnThePhone", "Presenting", "InAMeeting" };

        private static readonly HashSet<string> UselessFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Code Cache", "GPUCache", "DawnCache", "BlobStorage", "Crashpad", "GraphCache", "snapshots", "databases"
        };

        // SAFETY FIX: the \b prevents matching "prevAvailability" and "prevActivity"
        private static readonly Regex AvailRx = new Regex(@"\bavailab[il]+ity\s*[:""]+\s*([A-Za-z]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ActRx = new Regex(@"\bactivity\s*[:""]+\s*([A-Za-z]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private string _ebWeb;
        private bool _isRunning = false;
        private bool _reallyQuit = false;
        private string _lastColor = null;
        private bool _startHidden = false;

        // --- UI COMPONENTS ---
        private TextBox txtUserId;
        private NumericUpDown numInterval;
        private Button btnToggle;
        private CheckBox chkStartup;
        private TextBox txtLogs;
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;

        public Form1()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (args.Contains("/background", StringComparer.OrdinalIgnoreCase))
            {
                _startHidden = true;
            }

            this.Text = "LuxaTeams";
            this.Size = new Size(450, 430);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Icon = SystemIcons.Application;
            this.StartPosition = FormStartPosition.CenterScreen;

            BuildInterface();
            BuildTrayIcon();
            LoadSettings();

            _ebWeb = FindEbWebView();
            if (_ebWeb == null)
            {
                Log("Teams folder not found. Launch Teams first.");
            }
            else
            {
                Log("Teams cache & logs detected locally.");
                if (_startHidden)
                {
                    Log("Background auto-start enabled.");
                    StartMonitoring();
                }
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            if (_startHidden)
            {
                _startHidden = false;
                if (!this.IsHandleCreated) CreateHandle();
                base.SetVisibleCore(false);
                return;
            }
            base.SetVisibleCore(value);
        }

        private void BuildInterface()
        {
            Label lblId = new Label { Text = "Luxafor User ID:", Location = new Point(15, 20), Size = new Size(110, 20) };
            txtUserId = new TextBox { Location = new Point(130, 18), Size = new Size(280, 20) };
            txtUserId.TextChanged += (s, e) => SaveSettings();

            Label lblInterval = new Label { Text = "Interval (sec):", Location = new Point(15, 55), Size = new Size(110, 20) };
            numInterval = new NumericUpDown { Location = new Point(130, 53), Size = new Size(60, 20), Minimum = 1, Maximum = 60, Value = 5 };
            numInterval.ValueChanged += (s, e) => SaveSettings();

            chkStartup = new CheckBox { Text = "Start automatically with Windows", Location = new Point(15, 88), Size = new Size(300, 20) };
            chkStartup.CheckedChanged += ChkStartup_CheckedChanged;

            btnToggle = new Button { Text = "Start", Location = new Point(210, 50), Size = new Size(200, 26), BackColor = Color.LightGreen, FlatStyle = FlatStyle.Flat };
            btnToggle.Click += BtnToggle_Click;

            txtLogs = new TextBox { Location = new Point(15, 125), Size = new Size(395, 250), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 8.5f), BackColor = Color.Black, ForeColor = Color.LightGray };

            this.Controls.AddRange(new Control[] { lblId, txtUserId, lblInterval, numInterval, chkStartup, btnToggle, txtLogs });
        }

        private void BuildTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Open interface", null, (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; });
            trayMenu.Items.Add("Quit", null, (s, e) => { _reallyQuit = true; Application.Exit(); });

            trayIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                ContextMenuStrip = trayMenu,
                Text = "LuxaTeams - Teams presence",
                Visible = true
            };

            trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = FormWindowState.Normal; };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_reallyQuit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            base.OnFormClosing(e);
        }

        private async void BtnToggle_Click(object sender, EventArgs e)
        {
            if (!_isRunning)
            {
                StartMonitoring();
                await RunMonitoringLoopAsync();
            }
            else
            {
                StopMonitoring();
            }
        }

        private void StartMonitoring()
        {
            _isRunning = true;
            btnToggle.Text = "Stop";
            btnToggle.BackColor = Color.LightCoral;
            txtUserId.Enabled = false;
            numInterval.Enabled = true;
            Log("Monitoring active.");
        }

        private void StopMonitoring()
        {
            _isRunning = false;
            btnToggle.Text = "Start";
            btnToggle.BackColor = Color.LightGreen;
            txtUserId.Enabled = true;
            numInterval.Enabled = true;
            Log("Monitoring paused.");
        }

        private void LoadSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\LuxaTeams"))
                {
                    if (key != null)
                    {
                        txtUserId.Text = key.GetValue("UserId", "xxx").ToString();
                        numInterval.Value = Convert.ToInt32(key.GetValue("IntervalSeconds", 5));
                    }
                }

                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    chkStartup.Checked = (runKey?.GetValue("LuxaTeams") != null);
                }
            }
            catch (Exception ex) { Log($"[Settings Error] {ex.Message}"); }
        }

        private void SaveSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\LuxaTeams"))
                {
                    key.SetValue("UserId", txtUserId.Text);
                    key.SetValue("IntervalSeconds", (int)numInterval.Value);
                }
            }
            catch { }
        }

        private void ChkStartup_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;

                    if (chkStartup.Checked)
                    {
                        string runPath = $"\"{Application.ExecutablePath}\" /background";
                        key.SetValue("LuxaTeams", runPath);
                        Log("Registered for Windows startup.");
                    }
                    else
                    {
                        key.DeleteValue("LuxaTeams", false);
                        Log("Removed from Windows startup.");
                    }
                }
            }
            catch (Exception ex) { Log($"[Startup Error] {ex.Message}"); }
        }

        private async Task RunMonitoringLoopAsync()
        {
            while (_isRunning)
            {
                try
                {
                    if (_ebWeb == null) _ebWeb = FindEbWebView();

                    if (_ebWeb != null)
                    {
                        var p = GetSelfPresence();
                        if (p != null)
                        {
                            string color = ColorForPresence(p.Availability, p.Activity);

                            if (color != _lastColor)
                            {
                                _lastColor = color;
                                Log($"Detected status: {p.Availability}/{p.Activity} -> Color: {color.ToUpper()}");
                                await SendLuxaforColorAsync(color);
                            }
                        }
                    }
                }
                catch (Exception ex) { Log($"[Loop Error] {ex.Message}"); }

                await Task.Delay((int)numInterval.Value * 1000);
            }
        }

        private void Log(string message)
        {
            if (txtLogs.InvokeRequired)
            {
                txtLogs.Invoke(new Action(() => Log(message)));
                return;
            }
            txtLogs.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }

        private string ColorForPresence(string availability, string activity)
        {
            // Cross-check: if either field reports Busy/Call -> Red
            if (BusyActivities.Contains(activity, StringComparer.OrdinalIgnoreCase) ||
                BusyActivities.Contains(availability, StringComparer.OrdinalIgnoreCase)) return "red";

            if (IsAny(availability, "Busy", "DoNotDisturb") ||
                IsAny(activity, "Busy", "DoNotDisturb")) return "red";

            if (IsAny(availability, "Away", "BeRightBack") ||
                IsAny(activity, "Away", "BeRightBack")) return "yellow";

            return "green";
        }

        private bool IsAny(string value, params string[] options) => options.Any(o => string.Equals(value, o, StringComparison.OrdinalIgnoreCase));

        private string FindEbWebView()
        {
            string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (!Directory.Exists(packages)) return null;

            foreach (var pkg in Directory.GetDirectories(packages, "MSTeams_*"))
            {
                string candidate = Path.Combine(pkg, @"LocalCache\Microsoft\MSTeams\EBWebView");
                if (Directory.Exists(candidate)) return candidate;
            }
            return null;
        }

        private PresenceInfo GetSelfPresence()
        {
            var cutoffTime = DateTime.UtcNow.AddSeconds(-15);

            var candidates = EnumerateRecentFilesSmart(_ebWeb, cutoffTime)
                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                .Take(40);

            List<PresenceInfo> freshlyDetectedStates = new List<PresenceInfo>();

            foreach (var fi in candidates)
            {
                string txt;
                try
                {
                    byte[] bytes = ReadAllBytesShared(fi.FullName);
                    txt = Encoding.ASCII.GetString(bytes);
                }
                catch { continue; }

                if (!txt.Contains("availability") && !txt.Contains("availablity")) continue;

                var mAvail = AvailRx.Matches(txt);
                var mAct = ActRx.Matches(txt);

                if (mAvail.Count > 0 && mAct.Count > 0)
                {
                    string availability = mAvail[mAvail.Count - 1].Groups[1].Value;
                    string activity = mAct[mAct.Count - 1].Groups[1].Value;

                    if (string.Equals(activity, "undefined", StringComparison.OrdinalIgnoreCase))
                    {
                        activity = availability;
                    }

                    freshlyDetectedStates.Add(new PresenceInfo { Availability = availability, Activity = activity });
                }
            }

            if (freshlyDetectedStates.Count == 0) return null;

            foreach (var state in freshlyDetectedStates)
            {
                if (BusyActivities.Contains(state.Activity, StringComparer.OrdinalIgnoreCase) || IsAny(state.Availability, "Busy", "DoNotDisturb"))
                {
                    return state;
                }
            }

            return freshlyDetectedStates.First();
        }

        private IEnumerable<FileInfo> EnumerateRecentFilesSmart(string root, DateTime cutoff)
        {
            var stack = new Stack<string>();
            if (Directory.Exists(root)) stack.Push(root);

            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                string dirName = Path.GetFileName(dir);

                if (UselessFolders.Contains(dirName)) continue;

                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); } catch { subdirs = Array.Empty<string>(); }
                foreach (var d in subdirs) stack.Push(d);

                string[] files;
                try { files = Directory.GetFiles(dir); } catch { files = Array.Empty<string>(); }
                foreach (var f in files)
                {
                    FileInfo fi = null;
                    try { fi = new FileInfo(f); } catch { continue; }

                    if (fi.LastWriteTimeUtc > cutoff && fi.Length > 100 && fi.Length < 3 * 1024 * 1024)
                    {
                        yield return fi;
                    }
                }
            }
        }

        private byte[] ReadAllBytesShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private async Task SendLuxaforColorAsync(string color)
        {
            string rawId = txtUserId.Text;
            string cleanedUserId = new string(rawId.Where(char.IsLetterOrDigit).ToArray());

            if (string.IsNullOrEmpty(cleanedUserId) || cleanedUserId == "xxx") return;

            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }

            string jsonPayload = $"{{\"userId\": \"{cleanedUserId}\", \"actionFields\": {{\"color\": \"{color}\"}}}}";
            try
            {
                var content = new StringContent(jsonPayload, new UTF8Encoding(false), "application/json");
                HttpResponseMessage response = await client.PostAsync(LuxaforUrl, content);
                if (response.IsSuccessStatusCode)
                    Log($"[API] Synced: {color.ToUpper()}.");
                else
                    Log($"[API] HTTP error: {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                Log($"[API] Failed: {ex.Message}");
            }
        }

        private class PresenceInfo
        {
            public string Availability;
            public string Activity;
        }
    }
}