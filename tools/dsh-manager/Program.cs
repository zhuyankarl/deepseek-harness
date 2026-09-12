using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Management;

/// <summary>
/// DSH Manager: a Windows desktop launcher for the DeepSeek Harness `dsh web` server.
/// Single-file WinForms app (no third-party dependencies); built with the in-box
/// .NET Framework csc.exe, see build.cmd.
/// </summary>
public class DshManager : Form
{
    AppConfig cfg;
    TextBox txtRepo, txtLog, txtPort;
    Button btnUpdate, btnStart, btnStop;
    Label lblStatus;
    volatile bool busy;

    /// <summary>Node command lines that identify a running dsh web server.</summary>
    static readonly Regex NodeDsh = new Regex(@"pnpm\s+dsh\s+web", RegexOptions.IgnoreCase);
    static readonly Regex NodeTsxWeb = new Regex(@"src[\\/]bin\.ts", RegexOptions.IgnoreCase);
    static readonly Regex WebArg = new Regex(@"[""\s]web[""\s]");
    /// <summary>cmd.exe wrapper that owns the service log redirection.</summary>
    static readonly Regex CmdWrapper = new Regex(@"dsh\s+web", RegexOptions.IgnoreCase);

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--selftest" || args[0] == "selftest"))
        {
            return SelfTest.Run();
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new DshManager());
        return 0;
    }

    public DshManager()
    {
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        cfg = AppConfig.Load(exeDir);
        string repo = (cfg.Repo.Length > 0 && IsRepoValid(cfg.Repo)) ? cfg.Repo : DetectRepo(exeDir);
        if (cfg.Repo != repo && repo.Length > 0) { cfg.Repo = repo; cfg.Save(); }

        Text = "DSH Manager (DeepSeek Harness)";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(640, 440);
        Font = new Font("Microsoft YaHei UI", 9F);

        var pnlTop = new Panel { Dock = DockStyle.Top, Height = 136, Padding = new Padding(12, 10, 12, 6) };
        var lblRepo = new Label { Text = "仓库路径:", AutoSize = true, Location = new Point(12, 17) };
        txtRepo = new TextBox { Text = repo, Location = new Point(80, 13), Width = 430, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var btnBrowse = new Button { Text = "…", Location = new Point(520, 12), Size = new Size(30, 24), Cursor = Cursors.Hand };

        var lblPort = new Label { Text = "端口:", AutoSize = true, Location = new Point(12, 51) };
        txtPort = new TextBox { Text = cfg.Port.ToString(), Location = new Point(52, 47), Width = 52 };
        lblStatus = new Label
        {
            Text = "就绪",
            Location = new Point(118, 54),
            AutoSize = true,
            ForeColor = Color.FromArgb(91, 107, 133)
        };

        btnUpdate = new Button
        {
            Text = "更新并启动",
            Location = new Point(12, 80),
            Size = new Size(150, 44),
            BackColor = Color.FromArgb(15, 43, 82),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        };
        btnStart = new Button
        {
            Text = "启动服务",
            Location = new Point(178, 80),
            Size = new Size(120, 44),
            BackColor = Color.FromArgb(15, 157, 110),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        };
        btnStop = new Button
        {
            Text = "关闭服务",
            Location = new Point(314, 80),
            Size = new Size(120, 44),
            BackColor = Color.FromArgb(176, 58, 58),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
        };

        pnlTop.Controls.AddRange(new Control[] { lblRepo, txtRepo, btnBrowse, lblPort, txtPort, lblStatus, btnUpdate, btnStart, btnStop });

        txtLog = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(250, 251, 253),
            ForeColor = Color.FromArgb(23, 35, 58),
            Font = new Font("Consolas", 9F),
            BorderStyle = BorderStyle.FixedSingle
        };

        Controls.Add(txtLog);
        Controls.Add(pnlTop);

        btnBrowse.Click += (s, e) => BrowseRepo();
        btnUpdate.Click += (s, e) =>
        {
            if (busy) return;
            string r = txtRepo.Text.Trim();
            cfg.Port = ReadPort();
            new Thread(() => UpdateFlow(r)) { IsBackground = true }.Start();
        };
        btnStart.Click += (s, e) =>
        {
            if (busy) return;
            string r = txtRepo.Text.Trim();
            cfg.Port = ReadPort();
            new Thread(() => StartFlow(r)) { IsBackground = true }.Start();
        };
        btnStop.Click += (s, e) => { if (busy) return; new Thread(StopFlow) { IsBackground = true }.Start(); };
        FormClosing += OnClosing;
        Activated += (s, e) => RefreshRuntimeStatus();

        Log("配置文件: " + cfg.Path);
        Log("日志目录: " + LogDir());
        Log("仓库: " + (repo.Length > 0 ? repo : "未检测到，请点击 … 选择"));
        Log("端口: " + cfg.Port);
        Log(IsDshRunning() ? "检测到 dsh web 服务正在运行" : "当前没有 dsh web 服务在运行");
    }

    /* ---------- 配置 ---------- */

    string LogDir()
    {
        if (cfg.LogDir.Length == 0)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dsh-manager", "logs");
        }
        return cfg.LogDir;
    }

    int ReadPort()
    {
        int p;
        if (int.TryParse(txtPort.Text.Trim(), out p) && p >= 1 && p <= 65535) return p;
        return cfg.Port;
    }

    string BaseUrl() { return "http://127.0.0.1:" + cfg.Port + "/"; }

    void BrowseRepo()
    {
        using (var dlg = new FolderBrowserDialog())
        {
            dlg.Description = "选择 deepseek-harness 仓库目录";
            dlg.SelectedPath = Directory.Exists(txtRepo.Text) ? txtRepo.Text : "";
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                txtRepo.Text = dlg.SelectedPath;
                cfg.Repo = dlg.SelectedPath;
                cfg.Save();
                Log("仓库已设为: " + dlg.SelectedPath);
            }
        }
    }

    void OnClosing(object sender, FormClosingEventArgs e)
    {
        if (busy && MessageBox.Show("任务正在执行，确定退出吗？", "确认",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        { e.Cancel = true; return; }
        cfg.Repo = txtRepo.Text.Trim();
        cfg.Port = ReadPort();
        cfg.Save();
    }

    /* 窗口激活时快速复查服务状态，自动纠正误报的“启动异常” */
    void RefreshRuntimeStatus()
    {
        if (busy) return;
        try
        {
            if (!IsDshRunning()) return;
            var req = (HttpWebRequest)WebRequest.Create(BaseUrl());
            req.Timeout = 2000;
            req.Method = "GET";
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if ((int)resp.StatusCode < 400 && lblStatus.Text != "服务运行中")
                {
                    Log("复查：服务已就绪");
                    SetStatus("服务运行中", Color.FromArgb(21, 115, 71));
                }
            }
        }
        catch { }
    }

    /* ---------- 仓库探测 ---------- */

    static bool IsRepoValid(string dir)
    {
        return Directory.Exists(dir) && File.Exists(Path.Combine(dir, "package.json"));
    }

    internal static string DetectRepo(string exeDir)
    {
        string dir = exeDir;
        while (true)
        {
            string pj = Path.Combine(dir, "package.json");
            if (File.Exists(pj))
            {
                try
                {
                    if (File.ReadAllText(pj).IndexOf("@deepseek-ai/dsh-root", StringComparison.Ordinal) >= 0) return dir;
                }
                catch { }
            }
            DirectoryInfo parent = new DirectoryInfo(dir).Parent;
            if (parent == null) break;
            dir = parent.FullName;
        }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates = new string[]
        {
            Path.Combine(home, "source", "repos", "deepseek-harness"),
            Path.Combine(home, "deepseek-harness"),
            Path.Combine(home, "code", "deepseek-harness")
        };
        foreach (string c in candidates)
        {
            if (IsRepoValid(c)) return c;
        }
        return "";
    }

    static string ReadVersion(string repo)
    {
        try
        {
            string json = File.ReadAllText(Path.Combine(repo, "package.json"));
            Match m = Regex.Match(json, @"""version""\s*:\s*""([^""]+)""");
            if (m.Success) return m.Groups[1].Value;
        }
        catch { }
        return "";
    }

    /* ---------- 日志与状态 ---------- */

    void Log(string s)
    {
        if (txtLog.InvokeRequired) { txtLog.BeginInvoke(new Action<string>(Log), s); return; }
        txtLog.AppendText(s + Environment.NewLine);
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.ScrollToCaret();
        try
        {
            string logDir = LogDir();
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "dsh-manager.log"),
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + s + Environment.NewLine,
                Encoding.UTF8);
        }
        catch
        {
            // The on-screen log remains available when the persistent log cannot be written.
        }
    }

    void SetStatus(string s, Color? c = null)
    {
        if (lblStatus.InvokeRequired) { lblStatus.BeginInvoke(new Action<string, Color?>(SetStatus), s, c); return; }
        lblStatus.Text = s;
        lblStatus.ForeColor = c ?? Color.FromArgb(91, 107, 133);
    }

    void SetBusy(bool b)
    {
        if (InvokeRequired) { BeginInvoke(new Action<bool>(SetBusy), b); return; }
        busy = b;
        btnUpdate.Enabled = !b;
        btnStart.Enabled = !b;
        btnStop.Enabled = !b;
        if (b) SetStatus("执行中...");
    }

    /* ---------- 命令执行 ---------- */

    class RunResult { public int Code; public string Output = ""; }

    RunResult RunCmd(string cmd, string workDir)
    {
        Log("> " + cmd);
        var sb = new StringBuilder();
        var psi = new ProcessStartInfo("cmd.exe", "/c " + cmd)
        {
            WorkingDirectory = workDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.GetEncoding(65001),
            StandardErrorEncoding = Encoding.GetEncoding(65001)
        };
        using (var p = Process.Start(psi))
        {
            p.OutputDataReceived += (s, e) => { if (e.Data != null) { lock (sb) sb.AppendLine(e.Data); Log(e.Data); } };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) { lock (sb) sb.AppendLine(e.Data); Log(e.Data); } };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return new RunResult { Code = p.ExitCode, Output = sb.ToString() };
        }
    }

    /// <summary>Run a pnpm command, preferring corepack and falling back to a bare pnpm.</summary>
    RunResult RunPnpm(string args, string repo)
    {
        var r = RunCmd("corepack pnpm " + args, repo);
        if (r.Code == 0) return r;
        if (r.Output.IndexOf("not recognized", StringComparison.OrdinalIgnoreCase) >= 0
            || r.Output.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
            || r.Output.IndexOf("unknown command", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Log("corepack 不可用，回退到 pnpm");
            return RunCmd("pnpm " + args, repo);
        }
        return r;
    }

    /* ---------- 进程管理 ---------- */

    internal static bool MatchNodeDsh(string cl)
    {
        return NodeDsh.IsMatch(cl) || (NodeTsxWeb.IsMatch(cl) && WebArg.IsMatch(cl));
    }

    bool IsDshRunning()
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'node.exe'"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    if (MatchNodeDsh((mo["CommandLine"] as string) ?? "")) return true;
                }
            }
        }
        catch { }
        return false;
    }

    void KillDsh()
    {
        int killed = 0;
        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'node.exe'"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    if (!MatchNodeDsh((mo["CommandLine"] as string) ?? "")) continue;
                    KillOne(mo, ref killed);
                }
            }
            // cmd.exe wrappers own the service log redirection; stop them too so
            // log files are released for cleanup.
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'cmd.exe'"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    if (!CmdWrapper.IsMatch((mo["CommandLine"] as string) ?? "")) continue;
                    KillOne(mo, ref killed);
                }
            }
        }
        catch (Exception ex) { Log("进程枚举异常: " + ex.Message); }
        Log(killed == 0 ? "未发现运行中的 dsh web 进程" : "共停止 " + killed + " 个进程");
        Thread.Sleep(2000);
    }

    void KillOne(ManagementObject mo, ref int killed)
    {
        try
        {
            var pid = Convert.ToInt32((uint)mo["ProcessId"]);
            var pr = Process.GetProcessById(pid);
            pr.Kill();
            pr.WaitForExit(3000);
            killed++;
            Log("已停止 PID " + pid);
        }
        catch { }
    }

    void StartService(string repo)
    {
        string logDir = LogDir();
        Directory.CreateDirectory(logDir);
        string outLog = Path.Combine(logDir, "dsh-web-out.log");
        string errLog = Path.Combine(logDir, "dsh-web-err.log");
        // Prefer corepack; fall back to a bare pnpm. --no-open lets this manager own the browser step.
        string webArgs = "dsh web --no-open --port " + cfg.Port;
        string cmd = "(corepack pnpm " + webArgs + " || pnpm " + webArgs + ") >> \"" + outLog + "\" 2>> \"" + errLog + "\"";
        var psi = new ProcessStartInfo("cmd.exe", "/c " + cmd)
        {
            WorkingDirectory = repo,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Minimized
        };
        Process.Start(psi);
        Log("服务进程已启动，日志: " + outLog);
    }

    /// <summary>Poll the web server until it answers (up to timeoutSeconds seconds).</summary>
    bool WaitForServerReady(int timeoutSeconds)
    {
        Log("等待服务就绪...");
        string url = BaseUrl();
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < timeoutSeconds)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 2000;
                req.Method = "GET";
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if ((int)resp.StatusCode < 400) return true;
                }
            }
            catch { }
            Thread.Sleep(1000);
        }
        return false;
    }

    void OpenBrowser()
    {
        try
        {
            Process.Start(BaseUrl());
            Log("已在默认浏览器打开: " + BaseUrl());
        }
        catch (Exception ex) { Log("打开浏览器失败: " + ex.Message); }
    }

    /* ---------- 流程 ---------- */

    /// <summary>Stop any running instance, start the service and report readiness.</summary>
    void RestartService(string repo)
    {
        SetStatus("重启服务...");
        Log("停止现有服务...");
        KillDsh();
        Log("启动服务...");
        StartAndWait(repo);
    }

    /// <summary>Start the service (assumes it is not running) and wait until it answers.</summary>
    void StartAndWait(string repo)
    {
        StartService(repo);
        bool ready = WaitForServerReady(60);
        if (!ready && IsDshRunning())
        {
            Log("进程已启动但页面尚未就绪，继续等待 90 秒...");
            SetStatus("服务启动中...", Color.FromArgb(176, 116, 0));
            ready = WaitForServerReady(90);
        }
        if (ready || IsDshRunning())
        {
            if (ready) { Log("服务已就绪: " + BaseUrl()); SetStatus("服务运行中", Color.FromArgb(21, 115, 71)); OpenBrowser(); }
            else { Log("启动超时：进程存活但页面未就绪，请查看日志目录"); SetStatus("启动异常", Color.Firebrick); }
        }
        else
        {
            Log("未检测到服务进程，请查看日志目录");
            SetStatus("启动异常", Color.Firebrick);
        }
    }

    void UpdateFlow(string repo)
    {
        SetBusy(true);
        try
        {
            if (!IsRepoValid(repo)) { Log("仓库路径无效: " + repo); SetStatus("路径错误", Color.Firebrick); return; }
            Log("===== 更新并启动 =====");
            Log("仓库: " + repo + " 当前版本: " + ReadVersion(repo));

            var fr = RunCmd("git fetch origin master", repo);
            if (fr.Code != 0)
            {
                // Offline fallback: the repo usually already holds a working build,
                // so keep this button usable without network instead of aborting.
                Log("git fetch 失败（可能无网络），跳过更新，用本地版本启动");
                SetStatus("跳过更新...", Color.FromArgb(176, 116, 0));
                RestartService(repo);
                return;
            }

            var cr = RunCmd("git rev-list --count HEAD..origin/master", repo);
            int behind;
            if (!int.TryParse(cr.Output.Trim(), out behind)) behind = 0;
            Log("落后上游提交数: " + behind);

            if (behind > 0)
            {
                SetStatus("拉取更新...");
                // 用 FETCH_HEAD 合并而不是 git pull：不依赖当前分支的 upstream 配置，
                // 在 feat 分支、无 tracking 信息时也能更新，且复用上一步已 fetch 的提交。
                if (RunCmd("git merge --ff-only FETCH_HEAD", repo).Code != 0)
                {
                    Log("快进失败，尝试普通 merge");
                    if (RunCmd("git merge --no-edit FETCH_HEAD", repo).Code != 0)
                    { Log("git merge 失败，中止"); SetStatus("merge 失败", Color.Firebrick); return; }
                }
                Log("拉取完成，当前版本: " + ReadVersion(repo));

                SetStatus("安装依赖...");
                if (RunPnpm("install", repo).Code != 0)
                { Log("依赖安装失败，中止"); SetStatus("install 失败", Color.Firebrick); return; }

                SetStatus("构建 DSH...");
                if (RunPnpm("run build", repo).Code != 0)
                { Log("完整构建失败，中止（保留现有服务）"); SetStatus("build 失败", Color.Firebrick); return; }

                Log("构建完成");
            }
            else
            {
                Log("已是最新版本，跳过构建");
            }

            RestartService(repo);
        }
        catch (Exception ex)
        {
            Log("异常: " + ex);
            SetStatus("异常", Color.Firebrick);
        }
        finally { SetBusy(false); }
    }

    void StartFlow(string repo)
    {
        SetBusy(true);
        try
        {
            if (!IsRepoValid(repo)) { Log("仓库路径无效: " + repo); SetStatus("路径错误", Color.Firebrick); return; }
            Log("===== 启动服务 =====");
            if (IsDshRunning())
            {
                Log("服务已在运行: " + BaseUrl());
                SetStatus("服务运行中", Color.FromArgb(21, 115, 71));
                OpenBrowser();
                return;
            }
            StartAndWait(repo);
        }
        catch (Exception ex)
        {
            Log("异常: " + ex);
            SetStatus("异常", Color.Firebrick);
        }
        finally { SetBusy(false); }
    }

    void StopFlow()
    {
        SetBusy(true);
        try
        {
            Log("===== 关闭服务 =====");
            if (!IsDshRunning())
            {
                Log("服务未在运行，无需关闭");
                SetStatus("服务未运行", Color.FromArgb(91, 107, 133));
                return;
            }
            KillDsh();
            Thread.Sleep(1500);
            if (IsDshRunning())
            {
                Log("仍有残留进程，请查看日志目录");
                SetStatus("关闭异常", Color.Firebrick);
            }
            else
            {
                Log("服务已关闭");
                SetStatus("服务已停止", Color.Firebrick);
            }
        }
        catch (Exception ex)
        {
            Log("异常: " + ex);
            SetStatus("异常", Color.Firebrick);
        }
        finally { SetBusy(false); }
    }
}

/// <summary>INI-style configuration stored next to the executable.</summary>
class AppConfig
{
    public string Repo = "";
    public int Port = 3080;
    public string LogDir = "";
    public string Path;

    public static AppConfig Load(string exeDir)
    {
        var cfg = new AppConfig();
        string exeSide = System.IO.Path.Combine(exeDir, "dsh-manager.ini");
        string appdata = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "dsh-manager", "dsh-manager.ini");
        // Portable override first; otherwise a per-user config outside the exe folder.
        cfg.Path = File.Exists(exeSide) ? exeSide : appdata;
        try
        {
            if (File.Exists(cfg.Path))
            {
                foreach (string raw in File.ReadAllLines(cfg.Path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim().Trim('"');
                    if (key == "repo") cfg.Repo = val;
                    else if (key == "port") { int p; if (int.TryParse(val, out p)) cfg.Port = p; }
                    else if (key == "logdir") cfg.LogDir = val;
                }
            }
        }
        catch { }
        return cfg;
    }

    public void Save()
    {
        try
        {
            string dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine("# dsh-manager configuration");
            sb.AppendLine("repo=" + Repo);
            sb.AppendLine("port=" + Port.ToString());
            sb.AppendLine("logdir=" + LogDir);
            File.WriteAllText(Path, sb.ToString(), Encoding.UTF8);
        }
        catch { }
    }
}

/// <summary>Non-interactive checks run with `DSHManager.exe --selftest`.</summary>
static class SelfTest
{
    public static int Run()
    {
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        var cfg = AppConfig.Load(exeDir);
        string logDir = cfg.LogDir.Length > 0 ? cfg.LogDir
            : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dsh-manager", "logs");
        Directory.CreateDirectory(logDir);
        string report = System.IO.Path.Combine(logDir, "selftest.log");
        var sb = new StringBuilder();
        int failed = 0;

        Action<string, bool, string> Check = delegate(string name, bool pass, string detail)
        {
            sb.AppendLine((pass ? "PASS " : "FAIL ") + name + " - " + detail);
            if (!pass) failed++;
        };

        sb.AppendLine("dsh-manager selftest " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        // config roundtrip through a dedicated temp directory
        string tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-manager-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var round = new AppConfig { Path = System.IO.Path.Combine(tmpDir, "dsh-manager.ini"), Repo = @"C:\repo", Port = 4000, LogDir = @"C:\logs" };
        round.Save();
        var back = AppConfig.Load(tmpDir);
        Check("config roundtrip", back.Repo == @"C:\repo" && back.Port == 4000 && back.LogDir == @"C:\logs",
            "repo=" + back.Repo + " port=" + back.Port + " logdir=" + back.LogDir);
        try { Directory.Delete(tmpDir, true); } catch { }

        // tool availability
        Check("git", FindOnPath("git.exe").Length > 0, FindOnPath("git.exe"));
        Check("node", FindOnPath("node.exe").Length > 0, FindOnPath("node.exe"));
        Check("corepack", FindOnPath("corepack.cmd").Length > 0 || FindOnPath("corepack").Length > 0, "corepack");
        Check("csc (in-box compiler)", FindCsc().Length > 0, FindCsc());

        // process-matching patterns
        Check("match: corepack wrapper",
            DshManager.MatchNodeDsh(@"C:\Program Files\nodejs\node.exe corepack pnpm dsh web"), "expected match");
        Check("match: tsx source launch",
            DshManager.MatchNodeDsh(@"node --import tsx/esm apps/cli/src/bin.ts ""web"""), "expected match");
        Check("match: unrelated node",
            !DshManager.MatchNodeDsh(@"node server.js web"), "expected no match");

        // repo detection
        string detected = DshManager.DetectRepo(exeDir);
        Check("repo detection", detected.Length > 0, detected.Length > 0 ? detected : "not found (set repo= in dsh-manager.ini)");

        // log dir writable
        try
        {
            string probe = System.IO.Path.Combine(logDir, ".probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            Check("log dir writable", true, logDir);
        }
        catch
        {
            Check("log dir writable", false, logDir);
        }

        // server reachability (informational; not a gate)
        try
        {
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + cfg.Port + "/");
            req.Timeout = 2000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                sb.AppendLine("INFO server reachable on port " + cfg.Port + " (status " + (int)resp.StatusCode + ")");
            }
        }
        catch
        {
            sb.AppendLine("INFO server not reachable on port " + cfg.Port + " (expected when stopped)");
        }

        sb.AppendLine(failed == 0 ? "RESULT all checks passed" : "RESULT " + failed + " check(s) failed");
        File.WriteAllText(report, sb.ToString(), Encoding.UTF8);
        return failed == 0 ? 0 : 1;
    }

    static string FindOnPath(string name)
    {
        try
        {
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                if (dir.Length == 0) continue;
                string full = System.IO.Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
        }
        catch { }
        return "";
    }

    static string FindCsc()
    {
        string windir = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        string[] candidates = new string[]
        {
            System.IO.Path.Combine(windir, "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe"),
            System.IO.Path.Combine(windir, "Microsoft.NET", "Framework", "v4.0.30319", "csc.exe")
        };
        foreach (string c in candidates) if (File.Exists(c)) return c;
        return "";
    }
}
