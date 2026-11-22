using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: AssemblyVersion("0.0.0.0")]
namespace DiskLayoutGui
{
	internal static class Program
	{
		[STAThread]
		private static void Main()
		{
			Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
			{
				Report(e.Exception);
			};
			AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
			{
				Report(e.ExceptionObject as Exception);
			};
			try
			{
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
			}
			catch
			{
			}
			Application.Run(new MainForm());
		}

		private static void Report(Exception ex)
		{
			try
			{
				File.AppendAllText(Path.Combine(MainForm.DataDir, "crash.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ((ex == null) ? "(null)" : ex.ToString()) + "\r\n\r\n");
			}
			catch
			{
			}
			try
			{
				MessageBox.Show("程序出错：\r\n" + ((ex == null) ? "未知错误" : ex.Message) + "\r\n\r\n详细信息已写入程序目录下的 data\\crash.log", "出错");
			}
			catch
			{
			}
		}
	}
	internal sealed class Segment
	{
		public int Index;

		public DateTime Start;

		public DateTime End;

		public List<string> Files = new List<string>();

		public long Bytes;

		public string CsvPath;

		public double Gain = -1.0;

		public double TimeA = -1.0;

		public double TimeB = -1.0;
	}
	internal sealed class MainForm : Form
	{
		private struct MRow
		{
			public string File;

			public long OldLcn;

			public long NewLcn;

			public long Clusters;
		}

		private const string DefragTask = "\\Microsoft\\Windows\\Defrag\\ScheduledDefrag";

		private const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 5636096u;

		private const uint IOCTL_STORAGE_QUERY_PROPERTY = 2954240u;

		private const uint GENERIC_READ = 2147483648u;

		private const uint FILE_SHARE_READ = 1u;

		private const uint FILE_SHARE_WRITE = 2u;

		private const uint OPEN_EXISTING = 3u;

		private const uint FILE_FLAG_NO_BUFFERING = 536870912u;

		private const uint FSCTL_GET_RETRIEVAL_POINTERS = 589939u;

		private const uint FSCTL_MOVE_FILE = 589940u;

		private const uint FSCTL_GET_VOLUME_BITMAP = 589935u;

		private const int ERROR_MORE_DATA = 234;

		private TextBox txtDir;

		private Button btnBrowse;

		private Button btnStart;

		private Button btnStop;

		private Button btnAnalyze;

		private Button btnApply;

		private Button btnDefragOff;

		private Button btnDefragOn;

		private Button btnMark;

		private Button btnGlobal;

		private Button btnHistory;

		private Button btnScan;

		private Button btnCold;

		private Button btnDrift;

		private Button btnRollback;

		private Button btnMap;

		private Button btnPack;

		private Button btnCancel;

		private volatile bool _cancel;

		private Button btnAppend;

		private Button btnHelp;

		private Button btnDiag;

		private ToolTip tipDiag;

		private CheckBox chkMulti;

		private ToolTip tip;

		private Label lblStatus;

		private Label lblHint;

		private Label lblPct;

		private Label lblDir;

		private Label lblDefrag;

		private ListView lv;

		private ProgressBar pb;

		private TextBox txtLog;

		private string _runDir;

		private string _csv;

		private readonly List<Segment> _segs = new List<Segment>();

		private bool _recording;

		private bool _busy;

		private string _session;

		private string _etlPath;

		private DateTime _markTime = DateTime.MinValue;

		private bool _appendMode;

		private bool _disclaimerAccepted;

		private bool _alwaysAdmin;

		private bool _closingForRestart;

		private List<KeyValuePair<DateTime, string[]>> _events;

		private string _scope = "";

		private bool _bypassScope;

		private bool _built;

		private static readonly Dictionary<string, string> DevMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		private static string SettingsPath
		{
			get
			{
				return Path.Combine(DataDir, "settings.txt");
			}
		}

		internal static string DataDir
		{
			get
			{
				try
				{
					string directoryName = Path.GetDirectoryName(Application.ExecutablePath);
					string text = Path.Combine(directoryName, "data");
					Directory.CreateDirectory(text);
					string path = Path.Combine(text, ".wtest");
					File.WriteAllText(path, "1");
					File.Delete(path);
					return text;
				}
				catch
				{
					string text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiskLayout");
					Directory.CreateDirectory(text2);
					return text2;
				}
			}
		}

		private static string HistoryPath
		{
			get
			{
				return Path.Combine(DataDir, "history.csv");
			}
		}

		private static string RegistryPath
		{
			get
			{
				return Path.Combine(DataDir, "applied.csv");
			}
		}

		private static string WinDir
		{
			get
			{
				string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
				if (!string.IsNullOrEmpty(folderPath))
				{
					return folderPath.ToLowerInvariant();
				}
				return "c:\\windows";
			}
		}

		public MainForm()
		{
			// 先隐藏，等启动检查（提权/开卷）走完再显示 ——
			// 否则"先闪一个空窗口、再换提权实例"会看到屏幕闪一下
			base.Opacity = 0;
			base.ShowInTaskbar = false;
			Text = "DiskLayout —— 按真实读取顺序重排文件";
			base.ClientSize = new Size(900, 700);
			MinimumSize = new Size(940, 680);
			base.StartPosition = FormStartPosition.CenterScreen;
			Font = new Font("Microsoft YaHei UI", 9f);
			lblDir = new Label();
			lblDir.Text = "目标目录：";
			lblDir.SetBounds(12, 14, 86, 22);
			base.Controls.Add(lblDir);
			txtDir = new TextBox();
			txtDir.SetBounds(102, 11, 578, 24);
			txtDir.Text = "";
			TextBox textBox = txtDir;
			EventHandler value = delegate
			{
				_scope = (txtDir.Text ?? "").Trim();
			};
			textBox.TextChanged += value;
			base.Controls.Add(txtDir);
			btnHistory = new Button();
			btnHistory.Text = "历史记录";
			btnHistory.SetBounds(600, 10, 90, 26);
			btnHistory.Click += delegate
			{
				ShowHistory();
			};
			base.Controls.Add(btnHistory);
			btnDiag = new Button();
			btnDiag.Text = "诊断";
			btnDiag.SetBounds(492, 10, 60, 26);
			tipDiag = new ToolTip();
			tipDiag.SetToolTip(btnDiag, "把系统信息、设置、本次日志导出成一个 txt，方便自己排查。");
			btnDiag.Click += delegate
			{
				ExportDiagnose();
			};
			base.Controls.Add(btnDiag);
			btnHelp = new Button();
			btnHelp.Text = "说明";
			btnHelp.SetBounds(694, 10, 50, 26);
			btnHelp.Click += delegate
			{
				try
				{
					string text = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "使用说明.txt");
					if (File.Exists(text))
					{
						Process.Start("notepad.exe", "\"" + text + "\"");
					}
					else
					{
						MessageBox.Show("找不到 使用说明.txt（应该和 exe 放在同一目录）。", "说明");
					}
				}
				catch (Exception ex)
				{
					MessageBox.Show(ex.Message);
				}
			};
			base.Controls.Add(btnHelp);
			btnBrowse = new Button();
			btnBrowse.Text = "浏览...";
			btnBrowse.SetBounds(748, 10, 146, 26);
			btnBrowse.Click += delegate
			{
				BrowseDir();
			};
			base.Controls.Add(btnBrowse);
			btnStart = new Button();
			btnStart.Text = "● 开始录制";
			btnStart.SetBounds(84, 45, 118, 34);
			btnStart.Click += delegate
			{
				StartRecord();
			};
			base.Controls.Add(btnStart);
			btnStop = new Button();
			btnStop.Text = "■ 停止录制";
			btnStop.SetBounds(208, 45, 118, 34);
			btnStop.Enabled = false;
			btnStop.Click += delegate
			{
				StopRecord();
			};
			base.Controls.Add(btnStop);
			btnMark = new Button();
			btnMark.Text = "✂ 在此切分";
			btnMark.SetBounds(332, 45, 118, 34);
			btnMark.Enabled = false;
			btnMark.Click += delegate
			{
				MarkLoadStart();
			};
			base.Controls.Add(btnMark);
			btnCold = new Button();
			btnCold.Text = "挪内圈";
			btnCold.SetBounds(640, 364, 72, 32);
			btnCold.Click += delegate
			{
				ColdEvict();
			};
			base.Controls.Add(btnCold);
			btnAppend = new Button();
			btnAppend.Text = "＋ 接着录一段";
			btnAppend.SetBounds(456, 45, 108, 34);
			btnAppend.Click += delegate
			{
				StartRecord(true);
			};
			base.Controls.Add(btnAppend);
			lblStatus = new Label();
			lblStatus.Text = "就绪";
			lblStatus.SetBounds(574, 52, 310, 22);
			lblStatus.ForeColor = Color.DimGray;
			base.Controls.Add(lblStatus);
			lblHint = new Label();
			lblHint.Text = "流程：开始录制 → 启动程序 → 主界面停 5 秒 →「在此切分」→ 打开要优化的内容 → 停止录制";
			lblHint.SetBounds(84, 84, 556, 22);
			lblHint.ForeColor = Color.SteelBlue;
			base.Controls.Add(lblHint);
			chkMulti = new CheckBox();
			chkMulti.Text = "自动分段（停顿 ≥3 秒切开）";
			chkMulti.SetBounds(650, 82, 234, 24);
			chkMulti.CheckedChanged += delegate
			{
				ReSegment();
			};
			base.Controls.Add(chkMulti);
			tip = new ToolTip();
			tip.SetToolTip(chkMulti, "一次录制里，程序每停顿 3 秒以上就自动切成新的一段。\n适合：一次录完整场（启动 → 大厅 → 几关），中途不用点按钮。");
			tip.SetToolTip(btnAppend, "保留已经录好的段，再录一次新的接在后面。\n每次录制 = 一段，切分完全由你控制。\n适合：想精确控制每段边界的时候。");
			tip.SetToolTip(btnMark, "在当前时刻插一条分界线：这条线之前算一段，之后算一段。\n用途：把「启动加载」和「进关卡加载」分开，程序不用停。\n不点也行 —— 勾「自动分段」它会自己按停顿切。");
			tip.SetToolTip(btnStart, "开始一次新的录制。会清空当前列表里的段。\n想保留已录的段，用「＋ 接着录一段」。\n\n⚠ 录制前最好重启一次：系统缓存里如果已经有这些文件，\n   部分读取会直接从内存返回、不被记录，结果会偏小。");
			lv = new ListView();
			lv.SetBounds(84, 114, 800, 240);
			lv.View = View.Details;
			lv.FullRowSelect = true;
			lv.GridLines = true;
			lv.MultiSelect = true;
			lv.Columns.Add("段", 40);
			lv.Columns.Add("开始", 80);
			lv.Columns.Add("录制时长", 80);
			lv.Columns.Add("文件数", 80);
			lv.Columns.Add("数据量", 100);
			lv.Columns.Add("可提升", 80);
			lv.Columns.Add("预计可省", 70);
			lv.Columns.Add("说明", 270);
			base.Controls.Add(lv);
			btnAnalyze = new Button();
			btnAnalyze.Text = "分析";
			btnAnalyze.SetBounds(84, 364, 60, 32);
			btnAnalyze.Enabled = false;
			btnAnalyze.Click += delegate
			{
				AnalyzeSelected();
			};
			base.Controls.Add(btnAnalyze);
			btnApply = new Button();
			btnApply.Text = "重排";
			btnApply.SetBounds(152, 364, 60, 32);
			btnApply.Enabled = false;
			btnApply.Click += delegate
			{
				ApplySelected();
			};
			base.Controls.Add(btnApply);
			lblPct = new Label();
			lblPct.Text = "";
			lblPct.SetBounds(654, 404, 136, 22);
			lblPct.TextAlign = ContentAlignment.MiddleRight;
			base.Controls.Add(lblPct);
			btnGlobal = new Button();
			btnGlobal.Text = "全局优化";
			btnGlobal.SetBounds(220, 364, 84, 32);
			btnGlobal.Enabled = false;
			btnGlobal.Click += delegate
			{
				PlanGlobal();
			};
			base.Controls.Add(btnGlobal);
			btnScan = new Button();
			btnScan.Text = "快速扫描";
			btnScan.SetBounds(312, 364, 84, 32);
			btnScan.Click += delegate
			{
				ScanScope();
			};
			base.Controls.Add(btnScan);
			btnDrift = new Button();
			btnDrift.Text = "漂移检测";
			btnDrift.SetBounds(404, 364, 84, 32);
			btnDrift.Click += delegate
			{
				CheckDrift();
			};
			base.Controls.Add(btnDrift);
			btnRollback = new Button();
			btnRollback.Text = "回滚";
			btnRollback.SetBounds(496, 364, 56, 32);
			btnRollback.Click += delegate
			{
				Rollback();
			};
			base.Controls.Add(btnRollback);

			btnMap = new Button();
			btnMap.Text = "分布图";
			btnMap.SetBounds(796, 364, 88, 32);
			btnMap.Click += delegate
			{
				string vol = null;
				try
				{
					if (!string.IsNullOrEmpty(_scope))
					{
						vol = Path.GetPathRoot(Path.GetFullPath(_scope));
					}
				}
				catch
				{
				}
				if (string.IsNullOrEmpty(vol))
				{
					MessageBox.Show("先用「浏览...」选一个目录，工具才知道要画哪块盘。", "先选目录");
					return;
				}
				using (MapForm mapForm = new MapForm(vol))
				{
					mapForm.ShowDialog(this);
				}
			};
			base.Controls.Add(btnMap);

			btnPack = new Button();
			btnPack.Text = "填前部";
			btnPack.SetBounds(560, 364, 72, 32);
			btnPack.Click += delegate
			{
				PackFront();
			};
			base.Controls.Add(btnPack);
			pb = new ProgressBar();
			pb.SetBounds(84, 402, 700, 18);
			pb.Minimum = 0;
			pb.Maximum = 100;
			base.Controls.Add(pb);
			btnCancel = new Button();
			btnCancel.Text = "✕ 取消";
			btnCancel.SetBounds(792, 398, 92, 26);
			btnCancel.Enabled = false;
			btnCancel.Click += delegate
			{
				_cancel = true;
				Log("已请求取消，正在停下…");
				btnCancel.Enabled = false;
			};
			base.Controls.Add(btnCancel);
			Label label = new Label();
			label.Name = "lblDefragTag";
			label.Text = "Windows 自动整理：";
			label.SetBounds(84, 438, 150, 22);
			base.Controls.Add(label);
			btnDefragOff = new Button();
			btnDefragOff.Text = "禁用";
			btnDefragOff.SetBounds(238, 432, 90, 28);
			btnDefragOff.Click += delegate
			{
				SetDefrag(false);
			};
			base.Controls.Add(btnDefragOff);
			btnDefragOn = new Button();
			btnDefragOn.Text = "恢复";
			btnDefragOn.SetBounds(334, 432, 90, 28);
			btnDefragOn.Click += delegate
			{
				SetDefrag(true);
			};
			base.Controls.Add(btnDefragOn);
			lblDefrag = new Label();
			lblDefrag.SetBounds(434, 438, 446, 22);
			lblDefrag.Text = "查询中…";
			base.Controls.Add(lblDefrag);
			txtLog = new TextBox();
			txtLog.SetBounds(84, 474, 800, 206);
			txtLog.Multiline = true;
			txtLog.ScrollBars = ScrollBars.Vertical;
			txtLog.ReadOnly = true;
			txtLog.BackColor = Color.White;
			base.Controls.Add(txtLog);
			base.FormClosing += delegate
			{
				SaveSettings();
				OnClose();
			};
			_built = true;
			base.Resize += delegate
			{
				DoLayout();
			};
			DoLayout();
			RefreshDefragState();
			LoadSettings();
			// 如果这次是"提权重启"拉起来的，就别再重启第二次（防止来回弹）
			bool elevRetried = false;
			try
			{
				string[] argv = Environment.GetCommandLineArgs();
				for (int ai = 1; ai < argv.Length; ai++)
				{
					if (string.Equals(argv[ai], "--elev-retry", StringComparison.OrdinalIgnoreCase)) elevRetried = true;
				}
			}
			catch { }
			// 以"实际能不能开卷"为准；提权标记只用来解释原因
			bool elevated = ElevationFlag();
			bool adminOk = IsAdmin();
			int perr = -1;
			bool volOk = VolumeProbeOk(out perr);
			try
			{
				FileInfo fi = new FileInfo(Application.ExecutablePath);
				LogFile("=== 启动 ===");
				LogFile("程序   " + Application.ExecutablePath + "　" + fi.Length + " 字节　" + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
				LogFile("用户   " + Environment.UserDomainName + "\\" + Environment.UserName);
				LogFile("权限   提权=" + elevated + "　管理员组=" + InAdminGroup() + "　开卷=" + volOk + (volOk ? "" : ("(错误码 " + perr + ")")));
			}
			catch { }

			if (!volOk && !elevated)
			{
				// 真正的原因是"没提权"，不是目录问题 —— 先提权
				Log("⚠ 当前不是管理员权限，录制和搬移都会失败。");
				lblStatus.Text = "需要管理员权限";
				lblStatus.ForeColor = Color.Firebrick;

				if (elevRetried)
				{
					// 用 runas 重启过一次还是不成功 —— 别再重启，把话说清楚
					Log("⚠ 已尝试以管理员身份重启，但这个会话仍不是管理员。");
					Log("    请手动：右键 exe →「以管理员身份运行」。");
					MessageBox.Show("已经尝试过以管理员身份重启，但没有成功。\n\n请手动右键 exe →「以管理员身份运行」。",
						"提权没有生效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				}
				bool go = !elevRetried && _alwaysAdmin;   // 勾过"以后都默认"就不再问
				bool always = _alwaysAdmin;
				if (!go)
				{
					using (AdminAskForm ask = new AdminAskForm(false))
					{
						if (ask.ShowDialog(this) == DialogResult.OK)
						{
							go = true;
							always = ask.Always;
						}
					}
				}
				if (go)
				{
					if (always && !_alwaysAdmin)
					{
						_alwaysAdmin = true;
						SaveSettings();
					}
					try
					{
						ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath);
						psi.UseShellExecute = true;
						psi.Verb = "runas";
						psi.Arguments = "--elev-retry";
						Process.Start(psi);
					}
					catch
					{
						MessageBox.Show("提权失败，可能被 UAC 取消了。\n请手动右键 exe →「以管理员身份运行」。", "提权失败");
					}
					// 不能在构造函数里直接 Close()，改到显示之后安全关闭
					_closingForRestart = true;
					base.Shown += delegate { BeginInvoke((Action)delegate { Close(); }); };
				}
			}
			else if (!volOk)
			{
				// 是管理员，但开卷失败 → 只可能是"位置"或"安全软件"
				bool inDownloads = Application.ExecutablePath.ToLowerInvariant().Contains("\\downloads\\");
				Log("⚠ 打不开磁盘卷（错误码 " + perr + "），搬移功能不可用。");
				Log("    程序位置：" + Application.ExecutablePath);
				if (inDownloads)
				{
					Log("    原因：程序在「下载」文件夹里，系统会拒绝 RAW 卷访问。");
					Log("    请把整个文件夹移到 C:\\ 或 D:\\ 下再运行。");
				}
				else
				{
					Log("    可能被安全软件（360 / 火绒 / 管家）拦截了裸卷访问，加白名单或临时退出试试。");
				}
				Log("    当前能看到的卷：" + VisibleVolumes());
			}
			else
			{
				Log("已就绪（管理员权限）。选一个程序/游戏的目录，或直接开始录制。");
				try
				{
					List<string> hddsNow = FindHddVolumes();
					Log(hddsNow.Count > 0
						? ("检测到机械盘：" + string.Join("  ", hddsNow.ToArray()) + "　（挪内圈时，别的盘的文件会真的搬到这里）")
						: "没有检测到机械盘（物理盘查询失败或本机全是固态）。");
				}
				catch { }
				Log("提示：点「挪内圈」选文件或文件夹，就会内迁到内圈（冷数据）。");
				try
				{
					Log("关于缓存：录制前建议重启一次，让系统缓存清空。");
					Log("          否则部分读取可能直接从内存返回、不被记录，结果会变成「只优化了一部分」。");
					bool? fastBoot = FastStartupEnabled();
					if (fastBoot == true)
					{
						Log("          你这台机器开着「快速启动」：关机再开不清缓存，要选「重启」。");
					}
					else if (fastBoot == false)
					{
						Log("          你这台机器没开「快速启动」：关机或重启都能清缓存。");
					}
					TimeSpan up = TimeSpan.FromMilliseconds((double)(uint)Environment.TickCount);
					if (up.TotalHours >= 3.0)
					{
						Log(string.Format("          当前已开机 {0:N1} 小时，缓存可能已经很热了。", up.TotalHours));
					}
				}
				catch
				{
				}
			}
			base.Shown += delegate
			{
				if (_closingForRestart) return;
				// 兜底：走到这一步说明窗口要正常显示，把透明度恢复
				base.Opacity = 1;
				base.ShowInTaskbar = true;
				ShowDisclaimerIfNeeded();
			};
			if (!_closingForRestart)
			{
				base.Opacity = 1;
				base.ShowInTaskbar = true;
			}
		}

		/// <summary>首次启动时弹出风险提示；用户勾选“不再提示”后不再出现</summary>
		private void ShowDisclaimerIfNeeded()
		{
			if (_disclaimerAccepted)
			{
				return;
			}
			using (DisclaimerForm disclaimerForm = new DisclaimerForm())
			{
				if (disclaimerForm.ShowDialog(this) != DialogResult.OK)
				{
					Application.Exit();
					return;
				}
				if (disclaimerForm.DontShowAgain)
				{
					_disclaimerAccepted = true;
					SaveSettings();
					Log("已记录：以后不再显示风险提示。");
				}
			}
		}

		private void RefreshDefragState()
		{
			string output;
			if (RunTool("schtasks", "/Query /TN \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /FO CSV /NH", out output) != 0)
			{
				lblDefrag.Text = "查不到计划任务（需管理员，或系统已移除）";
				lblDefrag.ForeColor = Color.DimGray;
				Button button = btnDefragOff;
				bool enabled = (btnDefragOn.Enabled = IsAdmin());
				button.Enabled = enabled;
				return;
			}
			string text = output.ToLowerInvariant();
			bool flag2 = text.Contains("disabled") || output.Contains("禁用");
			if (flag2)
			{
				lblDefrag.Text = "已禁用 ✅ 不会打乱布局";
				lblDefrag.ForeColor = Color.SeaGreen;
			}
			else
			{
				lblDefrag.Text = "启用中 ⚠\ufe0f 可能打乱布局";
				lblDefrag.ForeColor = Color.Firebrick;
			}
			btnDefragOff.Enabled = IsAdmin() && !flag2;
			btnDefragOn.Enabled = IsAdmin() && flag2;
		}

		private void SetDefrag(bool enable)
		{
			if (!IsAdmin())
			{
				MessageBox.Show("需要管理员权限。", "权限不足");
			}
			else if (enable || MessageBox.Show("禁用后 Windows 不会再自动整理这块盘，也就不会打乱排好的顺序。\n\n注意：SSD 的自动 TRIM 也会一起停掉（同一计划任务）。\n以后想恢复，点旁边的“恢复”。\n\n继续吗？", "禁用 Windows 自动整理", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes)
			{
				string output;
				if (RunTool("schtasks", "/Change /TN \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /" + (enable ? "ENABLE" : "DISABLE"), out output) != 0)
				{
					Log("操作失败: " + output.Trim());
					MessageBox.Show("操作失败，可能是权限不足。", "失败");
				}
				else
				{
					Log(enable ? "已恢复 Windows 自动整理" : "已禁用 Windows 自动整理");
				}
				RefreshDefragState();
			}
		}

		private void LoadSettings()
		{
			try
			{
				string settingsPath = SettingsPath;
				if (!File.Exists(settingsPath)) return;
				string[] lines = File.ReadAllLines(settingsPath);
				bool parsed = false;
				foreach (string line in lines)
				{
					int i = line.IndexOf('=');
					if (i <= 0) continue;
					parsed = true;
					string k = line.Substring(0, i).Trim();
					string v = line.Substring(i + 1).Trim();
					if (k == "scope")
					{
						if (v.Length > 0 && Directory.Exists(v)) { txtDir.Text = v; _scope = v; }
					}
					else if (k == "disclaimer")
					{
						_disclaimerAccepted = (v == "1");
					}
					else if (k == "alwaysAdmin")
					{
						_alwaysAdmin = (v == "1");
					}
				}
				if (!parsed)
				{
					// 兼容旧格式：整个文件就是一行路径
					string text = (lines.Length > 0 ? lines[0].Trim() : "");
					if (text.Length > 0 && Directory.Exists(text)) { txtDir.Text = text; _scope = text; }
				}
			}
			catch
			{
			}
		}

		private void SaveSettings()
		{
			try
			{
				File.WriteAllText(SettingsPath,
					"scope=" + (_scope ?? "") + "\r\n" +
					"disclaimer=" + (_disclaimerAccepted ? "1" : "0") + "\r\n" +
					"alwaysAdmin=" + (_alwaysAdmin ? "1" : "0") + "\r\n",
					new UTF8Encoding(false));
			}
			catch
			{
			}
		}


		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern uint GetFinalPathNameByHandleW(SafeFileHandle h, StringBuilder path, uint size, uint flags);

		private static string ResolvePath(string p)
		{
			SafeFileHandle safeFileHandle = CreateFileW(p, 0u, 7u, IntPtr.Zero, 3u, 33554432u, IntPtr.Zero);
			if (safeFileHandle.IsInvalid)
			{
				return p;
			}
			try
			{
				StringBuilder stringBuilder = new StringBuilder(1024);
				uint finalPathNameByHandleW = GetFinalPathNameByHandleW(safeFileHandle, stringBuilder, (uint)stringBuilder.Capacity, 0u);
				if (finalPathNameByHandleW == 0 || finalPathNameByHandleW >= stringBuilder.Capacity)
				{
					return p;
				}
				string text = stringBuilder.ToString();
				if (text.StartsWith("\\\\?\\"))
				{
					text = text.Substring(4);
				}
				if (text.StartsWith("\\??\\"))
				{
					text = text.Substring(4);
				}
				return text;
			}
			finally
			{
				safeFileHandle.Dispose();
			}
		}

		private static string Q(string s)
		{
			return "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
		}

		private static string[] SplitCsv(string line)
		{
			List<string> list = new List<string>();
			StringBuilder stringBuilder = new StringBuilder();
			bool flag = false;
			for (int i = 0; i < line.Length; i++)
			{
				char c = line[i];
				if (flag)
				{
					if (c == '"')
					{
						if (i + 1 < line.Length && line[i + 1] == '"')
						{
							stringBuilder.Append('"');
							i++;
						}
						else
						{
							flag = false;
						}
					}
					else
					{
						stringBuilder.Append(c);
					}
					continue;
				}
				switch (c)
				{
				case '"':
					flag = true;
					break;
				case ',':
					list.Add(stringBuilder.ToString());
					stringBuilder.Length = 0;
					break;
				default:
					stringBuilder.Append(c);
					break;
				}
			}
			list.Add(stringBuilder.ToString());
			return list.ToArray();
		}

		private static List<string[]> LoadHistory()
		{
			List<string[]> list = new List<string[]>();
			try
			{
				string historyPath = HistoryPath;
				if (!File.Exists(historyPath))
				{
					return list;
				}
				string[] array = File.ReadAllLines(historyPath);
				foreach (string text in array)
				{
					if (text.Length != 0)
					{
						string[] array2 = SplitCsv(text);
						if (array2.Length >= 6)
						{
							list.Add(array2);
						}
					}
				}
			}
			catch
			{
			}
			return list;
		}

		private static void SaveHistory(List<string[]> list)
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (string[] item in list)
			{
				stringBuilder.AppendLine(string.Join(",", Q(item[0]), Q(item[1]), Q(item[2]), Q(item[3]), item[4], item[5],
					item.Length >= 7 ? item[6] : "-1"));
			}
			File.WriteAllText(HistoryPath, stringBuilder.ToString(), new UTF8Encoding(false));
		}

		private void AppendHistory(string rundir, int segs, double best)
		{
			try
			{
				List<string[]> list = LoadHistory();
				string text = "";
				try
				{
					text = Path.GetFileName(_scope.TrimEnd('\\'));
				}
				catch
				{
				}
				long totalBytes = 0;
				foreach (Segment sg in _segs) { totalBytes += sg.Bytes; }
				list.Add(new string[7]
				{
					DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
					text,
					_scope ?? "",
					rundir,
					segs.ToString(),
					best.ToString("F1", CultureInfo.InvariantCulture),
					totalBytes.ToString()
				});
				SaveHistory(list);
			}
			catch
			{
			}
		}

		private static void TouchHistory(string rundir, double best)
		{
			try
			{
				List<string[]> list = LoadHistory();
				bool flag = false;
				foreach (string[] item in list)
				{
					if (string.Equals(item[3], rundir, StringComparison.OrdinalIgnoreCase))
					{
						double result;
						if (!double.TryParse(item[5], NumberStyles.Float, CultureInfo.InvariantCulture, out result))
						{
							result = 0.0;
						}
						if (best > result)
						{
							item[5] = best.ToString("F1", CultureInfo.InvariantCulture);
							flag = true;
						}
					}
				}
				if (flag)
				{
					SaveHistory(list);
				}
			}
			catch
			{
			}
		}

		private static void RenameHistory(string rundir, string newName)
		{
			try
			{
				List<string[]> list = LoadHistory();
				foreach (string[] item in list)
				{
					if (string.Equals(item[3], rundir, StringComparison.OrdinalIgnoreCase))
					{
						item[1] = newName;
					}
				}
				SaveHistory(list);
			}
			catch
			{
			}
		}

		private static void DeleteHistory(string rundir)
		{
			try
			{
				List<string[]> list = LoadHistory();
				list.RemoveAll((string[] f) => string.Equals(f[3], rundir, StringComparison.OrdinalIgnoreCase));
				SaveHistory(list);
			}
			catch
			{
			}
		}


		private void ShowHistory()
		{
			using (HistoryForm historyForm = new HistoryForm(LoadHistory()))
			{
				historyForm.ShowDialog(this);
				// 只改了名字、没点"加载/删除"也要存盘，否则关掉再开又变回去
				if (historyForm.NameChanged)
				{
					SaveHistory(historyForm.Rows);
					Log("已保存历史记录的名字。");
				}
				if (historyForm.SelectedRow != null)
				{
					string rundir = historyForm.SelectedRow[3];
					RenameHistory(rundir, historyForm.SelectedRow[1]);
					if (historyForm.Action == "delete")
					{
						DeleteHistory(rundir);
						Log("已删除记录: " + historyForm.SelectedRow[1]);
					}
					else if (historyForm.Action == "compare" && historyForm.SelectedRow2 != null)
					{
						CompareRuns(historyForm.SelectedRow, historyForm.SelectedRow2);
					}
					else if (historyForm.Action == "load")
					{
						string _rd = rundir; string _nm = historyForm.SelectedRow[1];
						SetStatus("正在加载记录…");
						RunAsync(delegate { LoadRun(_rd, _nm); });
					}
				}
			}
		}

		private static List<Segment> LoadSegmentsFromDir(string rundir)
		{
			List<Segment> list = new List<Segment>();
			string[] files;
			try
			{
				files = Directory.GetFiles(rundir, "*.seg*.csv");
			}
			catch
			{
				return list;
			}
			Array.Sort(files, StringComparer.OrdinalIgnoreCase);
			int num = 0;
			string[] array = files;
			foreach (string text in array)
			{
				Segment segment = new Segment();
				num = (segment.Index = num + 1);
				segment.CsvPath = text;
				HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				try
				{
					string[] array2 = File.ReadAllLines(text);
					foreach (string text2 in array2)
					{
						if (text2.Length == 0 || text2.StartsWith("time,", StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}
						string[] array3 = SplitCsv(text2);
						if (array3.Length < 5)
						{
							continue;
						}
						string text3 = Normalize(array3[2]);
						if (hashSet.Add(text3))
						{
							segment.Files.Add(text3);
							try
							{
								segment.Bytes += new FileInfo(text3).Length;
							}
							catch
							{
							}
						}
					}
				}
				catch
				{
				}
				list.Add(segment);
			}
			return list;
		}

		private void CompareRuns(string[] a, string[] b)
		{
			List<Segment> list = LoadSegmentsFromDir(a[3]);
			List<Segment> list2 = LoadSegmentsFromDir(b[3]);
			if (list.Count == 0 || list2.Count == 0)
			{
				MessageBox.Show("某个记录里没有分段文件。");
				return;
			}
			Segment segment = list[0];
			Segment segment2 = list2[0];
			foreach (Segment item in list)
			{
				if (item.Files.Count > segment.Files.Count)
				{
					segment = item;
				}
			}
			foreach (Segment item2 in list2)
			{
				if (item2.Files.Count > segment2.Files.Count)
				{
					segment2 = item2;
				}
			}
			Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < segment2.Files.Count; i++)
			{
				if (!dictionary.ContainsKey(segment2.Files[i]))
				{
					dictionary[segment2.Files[i]] = i;
				}
			}
			List<int> list3 = new List<int>();
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			int num = 0;
			foreach (string file in segment.Files)
			{
				int value;
				if (hashSet.Add(file) && dictionary.TryGetValue(file, out value))
				{
					num++;
					list3.Add(value);
				}
			}
			int num2 = dictionary.Count + hashSet.Count - num;
			double num3 = ((num2 > 0) ? ((double)num * 100.0 / (double)num2) : 0.0);
			int num4 = Math.Max(1, list3.Count / 3000);
			List<int> list4 = new List<int>();
			for (int j = 0; j < list3.Count; j += num4)
			{
				list4.Add(list3[j]);
			}
			long num5 = 0L;
			long num6 = 0L;
			for (int k = 0; k < list4.Count; k++)
			{
				for (int l = k + 1; l < list4.Count; l++)
				{
					num6++;
					if (list4[k] < list4[l])
					{
						num5++;
					}
				}
			}
			double num7 = ((num6 > 0) ? ((double)num5 * 100.0 / (double)num6) : 0.0);
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("两次录制对比");
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("  A  " + a[0] + "  " + a[1] + "   第" + segment.Index + "段 " + segment.Files.Count + " 个文件");
			stringBuilder.AppendLine("  B  " + b[0] + "  " + b[1] + "   第" + segment2.Index + "段 " + segment2.Files.Count + " 个文件");
			stringBuilder.AppendLine();
			stringBuilder.AppendLine(string.Format("  共同文件   {0:N0} 个", num));
			stringBuilder.AppendLine(string.Format("  文件重叠率 {0:N1}%", num3));
			stringBuilder.AppendLine(string.Format("  顺序一致性 {0:N1}%", num7));
			stringBuilder.AppendLine();
			if (num3 < 50.0)
			{
				stringBuilder.AppendLine("  结论：两次读的东西差太多，静态布局基本没用。随时可能变。");
			}
			else if (num7 >= 80.0)
			{
				stringBuilder.AppendLine("  结论：顺序很稳定，优化能长期生效。");
			}
			else if (num7 >= 55.0)
			{
				stringBuilder.AppendLine("  结论：大致稳定。优化有效，但收益会比单次测量低一些。");
			}
			else
			{
				stringBuilder.AppendLine("  结论：顺序不稳定。优化只能覆盖一部分，效果会打折。");
			}
			Log(stringBuilder.ToString().TrimEnd());
			MessageBox.Show(stringBuilder.ToString(), "两次录制对比", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		}

		private void LoadRun(string rundir, string app)
		{
			try
			{
				string[] files = Directory.GetFiles(rundir, "*.seg*.csv");
				if (files.Length == 0)
				{
					MessageBox.Show("这个记录里没有分段文件。", "无法加载");
					return;
				}
				Array.Sort(files, StringComparer.OrdinalIgnoreCase);
				_runDir = rundir;
				_csv = Path.Combine(rundir, "trace.csv");
				_segs.Clear();
				List<KeyValuePair<DateTime, string[]>> list = new List<KeyValuePair<DateTime, string[]>>();
				Dictionary<int, string[]> cached = LoadResults(rundir);
				string[] array = files;
				foreach (string text in array)
				{
					Segment segment = new Segment();
					segment.Index = _segs.Count + 1;
					segment.CsvPath = text;
					string[] hit;
					bool hasHit = cached.TryGetValue(segment.Index, out hit);
					long knownBytes = 0;
					if (hasHit) long.TryParse(hit[2], out knownBytes);
					HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					DateTime dateTime = DateTime.MaxValue;
					DateTime dateTime2 = DateTime.MinValue;
					string[] array2 = File.ReadAllLines(text);
					foreach (string text2 in array2)
					{
						if (text2.Length == 0 || text2.StartsWith("time,", StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}
						string[] array3 = SplitCsv(text2);
						DateTime result;
						if (array3.Length < 5 || !DateTime.TryParse(array3[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result))
						{
							continue;
						}
						if (result < dateTime)
						{
							dateTime = result;
						}
						if (result > dateTime2)
						{
							dateTime2 = result;
						}
						list.Add(new KeyValuePair<DateTime, string[]>(result, array3));
						string text3 = Normalize(array3[2]);
						if (hashSet.Add(text3))
						{
							segment.Files.Add(text3);
							// 有 results.csv 就不再逐个文件查大小（那会让磁头到处跳）
							if (!hasHit)
							{
								try
								{
									segment.Bytes += new FileInfo(text3).Length;
								}
								catch
								{
								}
							}
						}
					}
					segment.Start = ((dateTime == DateTime.MaxValue) ? DateTime.Now : dateTime);
					segment.End = ((dateTime2 == DateTime.MinValue) ? segment.Start : dateTime2);
					if (hasHit)
					{
						segment.Bytes = knownBytes;
						double g, ta, tb;
						if (double.TryParse(hit[3], NumberStyles.Float, CultureInfo.InvariantCulture, out g)) segment.Gain = g;
						if (double.TryParse(hit[4], NumberStyles.Float, CultureInfo.InvariantCulture, out ta)) segment.TimeA = ta;
						if (double.TryParse(hit[5], NumberStyles.Float, CultureInfo.InvariantCulture, out tb)) segment.TimeB = tb;
					}
					if (segment.Files.Count > 0)
					{
						_segs.Add(segment);
					}
				}
				list.Sort((KeyValuePair<DateTime, string[]> x, KeyValuePair<DateTime, string[]> y) => x.Key.CompareTo(y.Key));
				_events = list;
				// 旧记录第一次加载后补写 results.csv，之后加载就不用再逐个文件查大小
				if (cached.Count == 0) SaveResults(rundir, _segs);
				// LoadRun 现在跑在后台线程，界面更新必须切回 UI 线程
				BeginInvoke((Action)delegate
				{
					FillList();
					SetButtons(true);
				});
				Log(string.Format("已加载历史记录「{0}」，共 {1} 段。{2}", app, _segs.Count,
					cached.Count > 0 ? "已恢复上次的分析结果，不用重新分析。" : "可以直接分析或重排，不用重新录制。"));
				SetStatus("已加载历史记录");
			}
			catch (Exception ex)
			{
				Log("加载失败: " + ex.Message);
			}
		}

		private static string SegmentKey(string runDir, int index)
		{
			string text = (string.IsNullOrEmpty(runDir) ? "run" : Path.GetFileName(runDir));
			return text + "#" + index;
		}

		/// <summary>读上次存下的分析结果：避免加载历史时重新算，也避免逐个文件查大小</summary>
		private static Dictionary<int, string[]> LoadResults(string rundir)
		{
			var d = new Dictionary<int, string[]>();
			try
			{
				if (string.IsNullOrEmpty(rundir)) return d;
				string p = Path.Combine(rundir, "results.csv");
				if (!File.Exists(p)) return d;
				foreach (string line in File.ReadAllLines(p))
				{
					if (line.Length == 0 || line.StartsWith("seg,")) continue;
					string[] parts = SplitCsv(line);
					if (parts.Length < 6) continue;
					int idx;
					if (int.TryParse(parts[0], out idx)) d[idx] = parts;
				}
			}
			catch
			{
			}
			return d;
		}

		private void SaveResults(string rundir, List<Segment> segs)
		{
			try
			{
				if (string.IsNullOrEmpty(rundir)) return;
				var sb = new StringBuilder();
				sb.AppendLine("seg,files,bytes,gain,timeA,timeB");
				foreach (Segment s in segs)
				{
					sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3:F3},{4:F3},{5:F3}",
						s.Index, s.Files.Count, s.Bytes, s.Gain, s.TimeA, s.TimeB));
				}
				File.WriteAllText(Path.Combine(rundir, "results.csv"), sb.ToString(), new UTF8Encoding(false));
			}
			catch
			{
			}
		}

		private static Dictionary<string, HashSet<string>> LoadRegistry()
		{
			Dictionary<string, HashSet<string>> dictionary = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
			string registryPath = RegistryPath;
			if (!File.Exists(registryPath))
			{
				return dictionary;
			}
			string[] array = File.ReadAllLines(registryPath);
			foreach (string text in array)
			{
				if (text.Length == 0)
				{
					continue;
				}
				int num = text.IndexOf(',');
				if (num > 0)
				{
					string key = text.Substring(0, num);
					string item = text.Substring(num + 1).Trim().Trim('"')
						.Replace("\"\"", "\"");
					HashSet<string> value;
					if (!dictionary.TryGetValue(key, out value))
					{
						value = (dictionary[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
					}
					value.Add(item);
				}
			}
			return dictionary;
		}

		private static void AppendRegistry(string key, IEnumerable<string> files)
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (string file in files)
			{
				stringBuilder.AppendLine(key + ",\"" + file.Replace("\"", "\"\"") + "\"");
			}
			File.AppendAllText(RegistryPath, stringBuilder.ToString(), new UTF8Encoding(false));
		}

		private static List<KeyValuePair<string, int>> FindOverlap(IEnumerable<string> files)
		{
			Dictionary<string, HashSet<string>> dictionary = LoadRegistry();
			HashSet<string> hashSet = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
			List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>();
			foreach (KeyValuePair<string, HashSet<string>> item in dictionary)
			{
				if (item.Key.Length == 0)
				{
					continue;
				}
				int num = 0;
				foreach (string item2 in hashSet)
				{
					if (item.Value.Contains(item2))
					{
						num++;
					}
				}
				if (num > 0)
				{
					list.Add(new KeyValuePair<string, int>(item.Key, num));
				}
			}
			list.Sort((KeyValuePair<string, int> a, KeyValuePair<string, int> b) => b.Value.CompareTo(a.Value));
			return list;
		}

		private void BrowseDir()
		{
			using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
			{
				folderBrowserDialog.Description = "选择程序 / 游戏的总目录（只搬这下面的文件）";
				try
				{
					if (!string.IsNullOrEmpty(_scope) && Directory.Exists(_scope))
					{
						folderBrowserDialog.SelectedPath = _scope;
					}
					else
					{
						string text = null;
						DriveInfo[] drives = DriveInfo.GetDrives();
						foreach (DriveInfo driveInfo in drives)
						{
							try
							{
								if (driveInfo.DriveType != DriveType.Fixed || !driveInfo.IsReady || IsHddVolume(driveInfo.RootDirectory.FullName) != true)
								{
									continue;
								}
								text = driveInfo.RootDirectory.FullName;
								break;
							}
							catch
							{
							}
						}
						if (text == null)
						{
							DriveInfo driveInfo2 = null;
							DriveInfo[] drives2 = DriveInfo.GetDrives();
							foreach (DriveInfo driveInfo3 in drives2)
							{
								try
								{
									if (driveInfo3.DriveType == DriveType.Fixed && driveInfo3.IsReady && (driveInfo2 == null || driveInfo3.AvailableFreeSpace > driveInfo2.AvailableFreeSpace))
									{
										driveInfo2 = driveInfo3;
									}
								}
								catch
								{
								}
							}
							if (driveInfo2 != null)
							{
								text = driveInfo2.RootDirectory.FullName;
							}
						}
						if (text != null)
						{
							folderBrowserDialog.SelectedPath = text;
						}
					}
				}
				catch
				{
				}
				if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
				{
					txtDir.Text = folderBrowserDialog.SelectedPath;
					SaveSettings();
				}
			}
		}

		private void StartRecord()
		{
			StartRecord(false);
		}

		private void StartRecord(bool append)
		{
			if (_busy)
			{
				return;
			}
			if (!IsAdmin())
			{
				MessageBox.Show("需要管理员权限。请关闭后右键“以管理员身份运行”。", "权限不足");
				return;
			}
			if (string.IsNullOrEmpty(_scope) || !Directory.Exists(_scope))
			{
				MessageBox.Show("请先用「浏览...」选择这个程序的总文件夹。\n\n它的作用有两个：\n  1. 决定哪些文件允许被重排（其他的一律跳过）\n  2. 决定录制产物放在哪\n\n例：D:\\Games\\某个游戏", "先选目录");
				return;
			}
			if (append && _segs.Count == 0)
			{
				MessageBox.Show("还没有任何段。\n\n先点「开始录制」录第一段，之后才能追加。", "追加一段");
				return;
			}
			try
			{
				string volRoot = Path.GetPathRoot(Path.GetFullPath(_scope));
				bool? isHdd = IsHddVolume(volRoot);
				if (isHdd.HasValue && !isHdd.Value)
				{
					if (MessageBox.Show("这块盘（" + volRoot + "）看起来是【固态硬盘】。\n\n本工具只对机械盘有意义：SSD 没有寻道，重排它几乎不会有提升，\n而且反复写对 SSD 寿命也没好处。\n\n仍然继续吗？", "这是固态盘", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
					{
						return;
					}
				}
			}
			catch
			{
			}
			_appendMode = append;
			try
			{
				// 录制产物一律放到 exe 旁边的 data\runs，不写进被优化的那个盘
				string path = Path.Combine(DataDir, "runs");
				_runDir = Path.Combine(path, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
				Directory.CreateDirectory(_runDir);
				_session = "DiskLayout_" + Process.GetCurrentProcess().Id;
				_etlPath = Path.Combine(Path.GetTempPath(), _session + ".etl");
				string etlPath = _etlPath;
				string output;
				int num = RunTool("logman", "start " + _session + " -p \"{EDD08927-9CC4-4E65-B970-C2560FB5C289}\" 0xFFFFFFFFFFFFFFFF 0x5 -o \"" + etlPath + "\" -ets", out output);
				if (num != 0)
				{
					Log("启动跟踪失败（退出码 " + num + "）: " + output.Trim());
					SetStatus("录制启动失败");
				}
				else
				{
					_recording = true;
					_markTime = DateTime.MinValue;
					SaveSettings();
					SetButtons(true);
					lblStatus.Text = "录制中…";
					lblStatus.ForeColor = Color.SeaGreen;
					if (append)
					{
						Log(string.Format("● 追加录制第 {0} 段。玩下一个场景，完事点「停止录制」。", _segs.Count + 1));
					}
					else
					{
						Log("● 开始录制。下面两行是【操作说明】，不是进度，一次显示完：");
						Log("   ① 启动程序 → 到主界面，停 5 秒别动");
						Log("   ② 点「在此切分」→ 进关卡 → 回来点「停止录制」");
					}
				}
			}
			catch (Exception ex)
			{
				Log("错误: " + ex.Message);
			}
		}

		private void MarkLoadStart()
		{
			if (_recording)
			{
				_markTime = DateTime.Now;
				Log("已标记：从这里往后算「加载段」（" + _markTime.ToString("HH:mm:ss") + "）");
				btnMark.Enabled = false;
			}
		}

		private void StopRecord()
		{
			if (_busy || !_recording)
			{
				return;
			}
			_recording = false;
			btnStop.Enabled = false;
			RunAsync(delegate
			{
				try
				{
					SetStatus("正在停止跟踪…");
					string output;
					RunTool("logman", "stop " + _session + " -ets", out output);
					Thread.Sleep(800);
					string text = (string.IsNullOrEmpty(_etlPath) ? Path.Combine(_runDir, "trace.etl") : _etlPath);
					string text2 = Path.Combine(Path.GetTempPath(), _session + ".xml");
					_csv = Path.Combine(_runDir, "trace.csv");
					SetStatus("正在导出 XML（可能较慢）…");
					RunTool("tracerpt", "\"" + text + "\" -o \"" + text2 + "\" -of XML -y", out output);
					SetStatus("正在解析轨迹…");
					Log("临时目录: " + _runDir);
					ParseAndSegment(text2, _csv, _appendMode);
					SaveResults(_runDir, _segs);
					if (_segs.Count > 0)
					{
						try
						{
							File.Delete(text);
							Log("已删除临时文件 trace.etl");
						}
						catch
						{
						}
						try
						{
							File.Delete(text2);
							Log("已删除临时文件 trace.xml");
						}
						catch
						{
						}
					}
					else
					{
						Log("没有解析到任何读取事件，保留 ETL/XML 以便排查。");
						Log("  " + text);
						Log("  " + text2);
					}
					BeginInvoke((Action)delegate
					{
						if ((txtDir.Text ?? "").Trim() != _scope)
						{
							txtDir.Text = _scope;
						}
					});
					SetStatus("完成，请选一段做分析");
					Log("解析完成，共 " + _segs.Count + " 段。");
					Log("  切分规则：勾了「自动分段」→ 按 ≥3 秒停顿切多段；没勾 → 只有「在此切分」那一刀（切 2 段）；都没用 → 1 段。");
					AppendHistory(_runDir, _segs.Count, 0.0);
				}
				catch (Exception ex)
				{
					Log("错误: " + ex.Message);
					SetStatus("失败");
				}
			});
		}

		private void AnalyzeSelected()
		{
			if (_busy)
			{
				return;
			}
			List<Segment> targets = new List<Segment>();
			foreach (int selectedIndex in lv.SelectedIndices)
			{
				if (selectedIndex >= 0 && selectedIndex < _segs.Count)
				{
					targets.Add(_segs[selectedIndex]);
				}
			}
			if (targets.Count == 0)
			{
				targets.AddRange(_segs);
			}
			if (targets.Count == 0)
			{
				MessageBox.Show("还没有分段，先录一次。");
				return;
			}
			RunAsync(delegate
			{
				try
				{
					for (int i = 0; i < targets.Count; i++)
					{
						if (_cancel)
						{
							Log(string.Format("已在第 {0}/{1} 段停止分析。", i, targets.Count));
							break;
						}
						Segment s = targets[i];
						if (s.Files.Count < 10)
						{
							s.Gain = 0.0;
							Log(string.Format("第 {0} 段：文件太少（{1} 个），跳过", s.Index, s.Files.Count));
						}
						else
						{
							SetStatus(string.Format("正在实测 第 {0}/{1} 段…", i + 1, targets.Count));
							double a;
							double b;
							Measure(s.Files, out a, out b, delegate(double mp, string msg)
							{
								BeginInvoke((Action)delegate
								{
									pb.Value = (int)Math.Min(100.0, Math.Max(0.0, mp * 100.0));
									lblPct.Text = msg;
								});
							});
							s.TimeA = a;
							s.TimeB = b;
							s.Gain = ((a > 0.0) ? Math.Max(0.0, (a - b) / a * 100.0) : 0.0);
							SaveResults(_runDir, _segs);
							double g = s.Gain;
							Invoke((Action)delegate
							{
								FillList();
								TouchHistory(_runDir, g);
								double num2 = (double)s.Bytes / 1048576.0;
								double num3 = ((b > 0.0) ? (num2 / b) : 0.0);
								string text = ((num3 >= 110.0) ? "磁盘已满负荷（接近顺序上限）→ 布局没有空间" : ((!(num3 >= 70.0)) ? "远低于顺序上限 → 瓶颈可能在 CPU/解压/网络，磁盘不是瓶颈" : "磁盘接近满负荷 → 空间有限"));
								Log(string.Format("第 {0} 段：按录制顺序 {1:N2}s，按物理顺序 {2:N2}s → 可提升 {3:N1}%", s.Index, a, b, g));
								Log(string.Format("        数据 {0:N1} MB ÷ 物理顺序 {1:N2}s = {2:N1} MB/s", num2, b, num3));
								Log("        判断：" + text);
								Log(string.Format("        注意：上面的“实测”是停止录制后单独量的，没有 ETW 开销；"
									+ "列表里的“录制时长”{0:N1}s 是带着录制开销量的，两者不能直接比。",
									(s.End - s.Start).TotalSeconds));
							});
						}
					}
					Invoke((Action)delegate
					{
						FillList();
						Log("分析完成。");
						SetStatus("分析完成");
						btnApply.Enabled = true;
					});
				}
				catch (Exception ex)
				{
					Log("错误: " + ex.Message);
					SetStatus("失败");
				}
			});
		}

		private void ApplySelected()
		{
			Segment s = Selected();
			if (s == null || _busy)
			{
				return;
			}
			if (s.Files.Count < 5)
			{
				MessageBox.Show("这一段的文件太少，不值得搬。", "跳过");
				return;
			}
			Log("目标目录: " + ((_scope.Length > 0) ? _scope : "(未设置，只会排除 Windows)"));
			List<KeyValuePair<string, int>> list = FindOverlap(s.Files);
			if (list.Count > 0)
			{
				int num = 0;
				StringBuilder stringBuilder = new StringBuilder();
				stringBuilder.AppendLine("检测到共享文件冲突：");
				stringBuilder.AppendLine();
				stringBuilder.AppendLine("这一段的文件里，有下列文件已经被之前的优化搬动过：");
				for (int i = 0; i < list.Count && i < 6; i++)
				{
					stringBuilder.AppendLine("    " + list[i].Key + "   共享 " + list[i].Value.ToString("N0") + " 个");
					num += list[i].Value;
				}
				if (list.Count > 6)
				{
					stringBuilder.AppendLine("    …… 还有 " + (list.Count - 6) + " 段");
				}
				stringBuilder.AppendLine();
				stringBuilder.AppendLine(string.Format("继续重排会把这些文件搬到这里，那几段的布局会有一部分失效（共 {0:N0} 个文件受影响）。", num));
				stringBuilder.AppendLine();
				stringBuilder.AppendLine("仍然继续吗？");
				if (MessageBox.Show(stringBuilder.ToString(), "共享文件冲突", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
				{
					return;
				}
			}
			if (s.Gain >= 0.0 && s.Gain < 10.0)
			{
				if (MessageBox.Show("这一段的预计提升只有 " + s.Gain.ToString("N1") + "%，确定还要重排吗？", "确认", MessageBoxButtons.YesNo) != DialogResult.Yes)
				{
					return;
				}
			}
			else if (MessageBox.Show(string.Format("即将把第 {0} 段的 {1:N0} 个文件（{2:N1} MB）按录制顺序重排。\n\n只改物理位置，不改内容、名字、时间戳。\n过程中请勿断电。\n\n继续吗？", s.Index, s.Files.Count, (double)s.Bytes / 1048576.0), "确认重排", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
			{
				return;
			}
			RunAsync(delegate
			{
				try
				{
					SetStatus("正在重排…");
					Apply("第" + s.Index + "段", s.Files, s.CsvPath + ".manifest.csv", SegmentKey(_runDir, s.Index), delegate(double pct, int done, int total, long mb, long totalMb)
					{
						BeginInvoke((Action)delegate
						{
							ShowMoveProgress(pct, mb, totalMb);
						});
					});
					Log(string.Format("第 {0} 段重排完成。", s.Index));
					SetStatus("重排完成，建议再分析一次确认");
				}
				catch (Exception ex)
				{
					Log("错误: " + ex.Message);
					SetStatus("失败");
				}
			});
		}

		private Segment Selected()
		{
			if (lv.SelectedIndices.Count == 0)
			{
				MessageBox.Show("请先在列表里选一段。");
				return null;
			}
			if (lv.SelectedIndices.Count > 1)
			{
				MessageBox.Show("重排请只选一段。\n\n想把多段一起排，用「全局优化（多段）」。", "只能选一段");
				return null;
			}
			int num = lv.SelectedIndices[0];
			if (num >= _segs.Count)
			{
				return null;
			}
			return _segs[num];
		}

		private void RunAsync(Action work)
		{
			_busy = true;
			_cancel = false;
			SetButtons(false);
			try
			{
				btnCancel.Enabled = true;
			}
			catch
			{
			}
			Thread thread = new Thread((ThreadStart)delegate
			{
				try
				{
					work();
				}
				catch (Exception ex)
				{
					if (_cancel)
					{
						Log("已取消。");
					}
					else
					{
						Log("错误: " + ex.Message);
					}
				}
				finally
				{
					_busy = false;
					try
					{
						BeginInvoke((Action)delegate
						{
							SetButtons(true);
							btnCancel.Enabled = false;
							pb.Value = 0;
							lblPct.Text = "";
						});
					}
					catch
					{
					}
				}
			});
			thread.IsBackground = true;
			thread.Start();
		}

		private DateTime _opStart = DateTime.MinValue;

		/// <summary>把搬移进度写进界面，并估算剩余时间。
		/// 速度为"已搬字节 ÷ 已用时间"；开头 5 秒内不给估算（那时抖动大）。</summary>
		private void ShowMoveProgress(double pct, long mbDone, long mbTotal)
		{
			if (_opStart == DateTime.MinValue) _opStart = DateTime.Now;
			double sec = (DateTime.Now - _opStart).TotalSeconds;
			string extra = "";
			if (sec >= 5.0 && mbDone > 0 && mbTotal > mbDone)
			{
				double speed = (double)mbDone / sec;                 // MB/s
				double remain = ((double)mbTotal - mbDone) / Math.Max(0.1, speed);
				extra = string.Format("　{0:N0} MB/s　剩 {1}", speed, FmtRemain(remain));
			}
			pb.Value = (int)Math.Min(100.0, Math.Max(0.0, pct));
			lblPct.Text = pct.ToString("N1") + "%" + extra;
		}

		/// <summary>剩余时间：超过 90 分钟就只给小时的粗略值，避免假精确。</summary>
		private static string FmtRemain(double seconds)
		{
			if (seconds < 60) return Math.Max(1, (int)seconds) + " 秒";
			double minutes = seconds / 60.0;
			if (minutes < 90) return ((int)Math.Round(minutes)) + " 分钟";
			return ((int)Math.Round(minutes / 60.0)) + " 小时以上";
		}
		private void SetButtons(bool enabled)
		{
			if (base.InvokeRequired)
			{
				BeginInvoke(new Action<bool>(SetButtons), enabled);
				return;
			}
			btnStart.Enabled = enabled && !_recording;
			btnStop.Enabled = enabled && _recording;
			btnMark.Enabled = enabled && _recording && _markTime == DateTime.MinValue;
			// 正在录制时"接着录一段"没有意义，而且容易点错 —— 置灰，停止后恢复
			btnAppend.Enabled = enabled && !_recording;
			btnAnalyze.Enabled = enabled && _segs.Count > 0;
			btnApply.Enabled = enabled && _segs.Count > 0;
			btnGlobal.Enabled = enabled && _segs.Count > 0 && _events != null;
			btnBrowse.Enabled = enabled;
		}

		private void SetStatus(string s)
		{
			if (base.InvokeRequired)
			{
				BeginInvoke(new Action<string>(SetStatus), s);
			}
			else
			{
				lblStatus.Text = s;
			}
		}

		private void Log(string s)
		{
			if (base.InvokeRequired)
			{
				BeginInvoke(new Action<string>(Log), s);
				return;
			}
			txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + "\r\n");
		}

		private void OnClose()
		{
			if (_recording && _session != null)
			{
				try
				{
					string output;
					RunTool("logman", "stop " + _session + " -ets", out output);
				}
				catch
				{
				}
			}
		}

		private void ParseAndSegment(string xmlPath, string csvPath)
		{
			ParseAndSegment(xmlPath, csvPath, false);
		}

		private void ParseAndSegment(string xmlPath, string csvPath, bool append)
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			List<string[]> list = new List<string[]>();
			Dictionary<string, int> dictionary2 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			string text = null;
			DateTime minValue = DateTime.MinValue;
			int num = 0;
			Dictionary<string, string> dictionary3 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			XmlReaderSettings xmlReaderSettings = new XmlReaderSettings();
			xmlReaderSettings.DtdProcessing = DtdProcessing.Ignore;
			xmlReaderSettings.IgnoreWhitespace = true;
			xmlReaderSettings.IgnoreComments = true;
			using (XmlReader xmlReader = XmlReader.Create(xmlPath, xmlReaderSettings))
			{
				xmlReader.MoveToContent();
				int num2 = 0;
				while (!xmlReader.EOF)
				{
					if (xmlReader.NodeType == XmlNodeType.Element && xmlReader.LocalName == "Event")
					{
						num2++;
						if (num2 % 20000 == 0)
						{
							SetStatus("解析中… 已处理 " + num2.ToString("N0") + " 条事件");
						}
						text = null;
						minValue = DateTime.MinValue;
						num = 0;
						dictionary3.Clear();
						using (XmlReader xmlReader2 = xmlReader.ReadSubtree())
						{
							xmlReader2.Read();
							while (!xmlReader2.EOF)
							{
								bool flag = false;
								if (xmlReader2.NodeType == XmlNodeType.Element)
								{
									switch (xmlReader2.LocalName)
									{
									case "TimeCreated":
									{
										string attribute2 = xmlReader2.GetAttribute("SystemTime");
										if (attribute2 != null)
										{
											DateTime.TryParse(attribute2, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out minValue);
										}
										break;
									}
									case "Execution":
									{
										string attribute3 = xmlReader2.GetAttribute("ProcessID");
										if (attribute3 != null)
										{
											int.TryParse(attribute3, out num);
										}
										break;
									}
									case "Data":
									{
										string attribute = xmlReader2.GetAttribute("Name");
										string text3 = xmlReader2.ReadElementContentAsString();
										if (attribute != null)
										{
											dictionary3[attribute] = text3.Trim();
										}
										flag = true;
										break;
									}
									case "Task":
									{
										string text2 = xmlReader2.ReadElementContentAsString().Trim();
										if (text2.Length > 0 && !char.IsDigit(text2[0]))
										{
											text = text2;
										}
										else if (text == null)
										{
											text = text2;
										}
										flag = true;
										break;
									}
									}
								}
								if (!flag && !xmlReader2.Read())
								{
									break;
								}
							}
						}
						string text4 = text ?? "?";
						int value;
						dictionary2.TryGetValue(text4, out value);
						dictionary2[text4] = value + 1;
						string value2;
						dictionary3.TryGetValue("FileObject", out value2);
						switch (text4)
						{
						case "Create":
						{
							string value5;
							if (value2 != null && dictionary3.TryGetValue("FileName", out value5) && value5.Length > 0)
							{
								dictionary[value2] = value5;
							}
							break;
						}
						case "Close":
							if (value2 != null)
							{
								dictionary.Remove(value2);
							}
							break;
						case "Read":
						{
							string value3 = null;
							if (value2 != null)
							{
								dictionary.TryGetValue(value2, out value3);
							}
							long num3 = 0L;
							long num4 = 0L;
							string value4;
							if (dictionary3.TryGetValue("ByteOffset", out value4))
							{
								num3 = Hex(value4);
							}
							if (dictionary3.TryGetValue("IOSize", out value4))
							{
								num4 = Hex(value4);
							}
							list.Add(new string[5]
							{
								minValue.ToString("O", CultureInfo.InvariantCulture),
								num.ToString(CultureInfo.InvariantCulture),
								value3 ?? "",
								num3.ToString(CultureInfo.InvariantCulture),
								num4.ToString(CultureInfo.InvariantCulture)
							});
							break;
						}
						}
					}
					xmlReader.Read();
				}
			}
			Dictionary<int, HashSet<string>> dictionary4 = new Dictionary<int, HashSet<string>>();
			foreach (string[] item in list)
			{
				int result;
				if (!string.IsNullOrEmpty(item[2]) && int.TryParse(item[1], out result) && result != 0 && result != 4)
				{
					HashSet<string> value6;
					if (!dictionary4.TryGetValue(result, out value6))
					{
						value6 = (dictionary4[result] = new HashSet<string>(StringComparer.OrdinalIgnoreCase));
					}
					value6.Add(item[2]);
				}
			}
			int num5 = -1;
			int num6 = -1;
			foreach (KeyValuePair<int, HashSet<string>> item2 in dictionary4)
			{
				if (item2.Value.Count > num6)
				{
					num6 = item2.Value.Count;
					num5 = item2.Key;
				}
			}
			if (num5 < 0)
			{
				Log("轨迹里没有可用进程。");
				return;
			}
			List<KeyValuePair<DateTime, string[]>> list2 = new List<KeyValuePair<DateTime, string[]>>();
			foreach (string[] item3 in list)
			{
				int result2;
				DateTime result3;
				if (int.TryParse(item3[1], out result2) && result2 == num5 && DateTime.TryParse(item3[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out result3) && !string.IsNullOrEmpty(item3[2]))
				{
					list2.Add(new KeyValuePair<DateTime, string[]>(result3, item3));
				}
			}
			list2.Sort((KeyValuePair<DateTime, string[]> x, KeyValuePair<DateTime, string[]> y) => x.Key.CompareTo(y.Key));
			if (append && _events != null)
			{
				_events.AddRange(list2);
			}
			else
			{
				_events = list2;
			}
			if (list2.Count == 0)
			{
				Log("主进程没有任何读取事件（录制期间可能没有读盘，或进程被过滤掉了）。");
				return;
			}			using (StreamWriter streamWriter = new StreamWriter(csvPath, false, new UTF8Encoding(true)))
			{
				streamWriter.WriteLine("time,pid,file,offset,size");
				foreach (KeyValuePair<DateTime, string[]> item4 in list2)
				{
					streamWriter.WriteLine(string.Join(",", item4.Value[0], item4.Value[1], "\"" + item4.Value[2].Replace("\"", "\"\"") + "\"", item4.Value[3], item4.Value[4]));
				}
			}
			if (!append)
			{
				_segs.Clear();
			}
			List<List<KeyValuePair<DateTime, string[]>>> list3 = (chkMulti.Checked ? SplitMulti(list2) : SplitTwo(list2));
			if (chkMulti.Checked)
			{
				list3.RemoveAll((List<KeyValuePair<DateTime, string[]>> g) => g.Count < 5);
			}
			_segs.AddRange(MakeSegments(list3, csvPath, true, _segs.Count));
			for (int k = 0; k < _segs.Count; k++)
			{
				_segs[k].Index = k + 1;
			}
			BeginInvoke(new Action(FillList));
		}
		private List<List<KeyValuePair<DateTime, string[]>>> SplitTwo(List<KeyValuePair<DateTime, string[]>> ev)
		{
			int num = -1;
			if (_markTime != DateTime.MinValue)
			{
				double num2 = double.MaxValue;
				for (int i = 1; i < ev.Count; i++)
				{
					double num3 = Math.Abs((ev[i].Key - _markTime).TotalSeconds);
					if (num3 < num2)
					{
						num2 = num3;
						num = i;
					}
				}
				Log(string.Format("按手动标记切分（与最近读取相差 {0:N1} 秒）", num2));
			}
			else
			{
				// 没有手动标记：保持一段。
				// （以前这里会在"最大间隔"处偷偷切一刀，导致"不勾自动分段"其实也会分段，
				//   很容易让人以为勾选框没生效。现在只有明确勾选或点标记才会分段。）
				num = -1;
				Log("没有切分：整场录制保持 1 段。");
				Log("  想要多段，三选一：勾「自动分段」/ 录制时点「✂ 在此切分」/ 用「＋ 接着录一段」。");
			}
			List<List<KeyValuePair<DateTime, string[]>>> list = new List<List<KeyValuePair<DateTime, string[]>>>();
			if (num > 0 && num < ev.Count)
			{
				int num5 = num;
				int num6 = ev.Count - num;
				if (num5 < 10 || num6 < 10)
				{
					Log(string.Format("其中一段太小（{0} / {1} 条读取），不切分，保持一段。", num5, num6));
					num = -1;
				}
			}
			if (num > 0 && num < ev.Count)
			{
				list.Add(ev.GetRange(0, num));
				list.Add(ev.GetRange(num, ev.Count - num));
			}
			else
			{
				list.Add(ev);
			}
			return list;
		}

		private List<List<KeyValuePair<DateTime, string[]>>> SplitMulti(List<KeyValuePair<DateTime, string[]>> ev)
		{
			List<List<KeyValuePair<DateTime, string[]>>> list = new List<List<KeyValuePair<DateTime, string[]>>>();
			List<KeyValuePair<DateTime, string[]>> list2 = new List<KeyValuePair<DateTime, string[]>>();
			DateTime dateTime = DateTime.MinValue;
			foreach (KeyValuePair<DateTime, string[]> item in ev)
			{
				bool flag = list2.Count > 0 && (item.Key - dateTime).TotalSeconds > 3.0;
				bool flag2 = _markTime != DateTime.MinValue && list2.Count > 0 && dateTime < _markTime && item.Key >= _markTime;
				if (flag || flag2)
				{
					list.Add(list2);
					list2 = new List<KeyValuePair<DateTime, string[]>>();
				}
				list2.Add(item);
				dateTime = item.Key;
			}
			if (list2.Count > 0)
			{
				list.Add(list2);
			}
			return list;
		}

		private void ReSegment()
		{
			if (_busy)
			{
				return;
			}
			if (_events == null || _events.Count == 0)
			{
				Log("还没有轨迹，先录一次。");
				return;
			}
			_segs.Clear();
			List<List<KeyValuePair<DateTime, string[]>>> list = (chkMulti.Checked ? SplitMulti(_events) : SplitTwo(_events));
			if (chkMulti.Checked)
			{
				list.RemoveAll((List<KeyValuePair<DateTime, string[]>> g) => g.Count < 5);
			}
			_segs.AddRange(MakeSegments(list, _csv, true));
			Log(string.Format("重新切分：{0} 段（{1}）", _segs.Count, chkMulti.Checked ? "多段：间隔 >= 3 秒 + 手动标记" : "两段规则"));
			FillList();
			SetButtons(true);
		}

		private List<Segment> MakeSegments(List<List<KeyValuePair<DateTime, string[]>>> groups, string csvPath, bool writeCsv)
		{
			return MakeSegments(groups, csvPath, writeCsv, 0);
		}

		/// <summary>baseIndex = 已有几段。追加录制时必须接着编号，
		/// 否则会从 seg1 重新写，把之前录的段覆盖掉 —— 历史记录里就只剩一段。</summary>
		private List<Segment> MakeSegments(List<List<KeyValuePair<DateTime, string[]>>> groups, string csvPath, bool writeCsv, int baseIndex)
		{
			List<Segment> list = new List<Segment>();
			int num = baseIndex;
			foreach (List<KeyValuePair<DateTime, string[]>> group in groups)
			{
				if (group.Count == 0)
				{
					continue;
				}
				Segment segment = new Segment();
				num = (segment.Index = num + 1);
				segment.Start = group[0].Key;
				segment.End = group[group.Count - 1].Key;
				HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (KeyValuePair<DateTime, string[]> item in group)
				{
					string text = Normalize(item.Value[2]);
					if (hashSet.Add(text))
					{
						segment.Files.Add(text);
						try
						{
							segment.Bytes += new FileInfo(text).Length;
						}
						catch
						{
						}
					}
				}
				segment.CsvPath = csvPath + ".seg" + num + ".csv";
				if (writeCsv)
				{
					using (StreamWriter streamWriter = new StreamWriter(segment.CsvPath, false, new UTF8Encoding(true)))
					{
						streamWriter.WriteLine("time,pid,file,offset,size");
						foreach (KeyValuePair<DateTime, string[]> item2 in group)
						{
							streamWriter.WriteLine(string.Join(",", item2.Value[0], item2.Value[1], "\"" + item2.Value[2].Replace("\"", "\"\"") + "\"", item2.Value[3], item2.Value[4]));
						}
					}
				}
				list.Add(segment);
			}
			return list;
		}

		private void PlanGlobal()
		{
			if (_busy)
			{
				return;
			}
			if (_events == null || _events.Count == 0)
			{
				MessageBox.Show("请先录制并分析一次。");
				return;
			}
			// 如果列表里选中了 2 段以上，就只用选中的段做全局规划
			// （这样可以把"启动段"排除在外，只合并关卡段）
			List<Segment> list2;
			if (lv.SelectedIndices.Count >= 2)
			{
				list2 = new List<Segment>();
				foreach (int selectedIndex in lv.SelectedIndices)
				{
					if (selectedIndex >= 0 && selectedIndex < _segs.Count)
					{
						list2.Add(_segs[selectedIndex]);
					}
				}
				Log(string.Format("全局优化：只处理列表里选中的 {0} 段", list2.Count));
			}
			else
			{
				List<List<KeyValuePair<DateTime, string[]>>> list = SplitMulti(_events);
				list.RemoveAll((List<KeyValuePair<DateTime, string[]>> g) => g.Count < 5);
				Log(string.Format("全局切分：{0} 段（间隔 >= 3 秒处切开{1}）", list.Count, (_markTime != DateTime.MinValue) ? "，并沿用「手动标记」那条分界线" : ""));
				if (list.Count < 2)
				{
					MessageBox.Show("只切出 " + list.Count + " 段，不需要全局优化。\n\n如果想要多段，请在各个场景之间多停几秒再录。");
					return;
				}
				list2 = MakeSegments(list, _csv, false);
			}
			if (list2.Count < 2)
			{
				MessageBox.Show("需要至少 2 段才能做全局优化。\n\n提示：按住 Ctrl 在列表里选 2 段以上，就只合并这几段。");
				return;
			}
			string text;
			try
			{
				text = (string.IsNullOrEmpty(_scope) ? null : (Path.GetFullPath(_scope).TrimEnd('\\') + "\\"));
			}
			catch
			{
				text = null;
			}
			Dictionary<string, List<int>> dictionary = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
			for (int num = 0; num < list2.Count; num++)
			{
				foreach (string file in list2[num].Files)
				{
					List<int> value;
					if (!dictionary.TryGetValue(file, out value))
					{
						value = (dictionary[file] = new List<int>());
					}
					if (!value.Contains(num))
					{
						value.Add(num);
					}
				}
			}
			List<Dictionary<string, int>> pos = new List<Dictionary<string, int>>();
			foreach (Segment item in list2)
			{
				Dictionary<string, int> dictionary2 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
				int num2 = 0;
				foreach (string file2 in item.Files)
				{
					if (!dictionary2.ContainsKey(file2))
					{
						dictionary2[file2] = num2++;
					}
				}
				pos.Add(dictionary2);
			}
			Dictionary<string, List<string>> dictionary3 = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, List<int>> item2 in dictionary)
			{
				string key = item2.Key;
				if ((text != null && !key.StartsWith(text, StringComparison.OrdinalIgnoreCase)) || key.ToLowerInvariant().StartsWith(WinDir + "\\"))
				{
					continue;
				}
				StringBuilder stringBuilder = new StringBuilder();
				foreach (int item3 in item2.Value)
				{
					if (stringBuilder.Length > 0)
					{
						stringBuilder.Append('+');
					}
					stringBuilder.Append(item3 + 1);
				}
				string key2 = stringBuilder.ToString();
				List<string> value2;
				if (!dictionary3.TryGetValue(key2, out value2))
				{
					value2 = (dictionary3[key2] = new List<string>());
				}
				value2.Add(key);
			}
			List<KeyValuePair<string, List<string>>> list5 = new List<KeyValuePair<string, List<string>>>();
			Dictionary<string, double> sigRank = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
			foreach (KeyValuePair<string, List<string>> item4 in dictionary3)
			{
				string key3 = item4.Key;
				List<string> value3 = item4.Value;
				string[] array = key3.Split('+');
				int first = int.Parse(array[0]) - 1;
				value3.Sort(delegate(string x, string y)
				{
					int value5;
					if (!pos[first].TryGetValue(x, out value5))
					{
						value5 = int.MaxValue;
					}
					int value6;
					if (!pos[first].TryGetValue(y, out value6))
					{
						value6 = int.MaxValue;
					}
					return value5.CompareTo(value6);
				});
				double num3 = 0.0;
				string[] array2 = array;
				foreach (string s in array2)
				{
					int index = int.Parse(s) - 1;
					int num5 = Math.Max(1, pos[index].Count);
					double num6 = 0.0;
					foreach (string item5 in value3)
					{
						int value4;
						if (pos[index].TryGetValue(item5, out value4))
						{
							num6 += (double)value4 / (double)num5;
						}
					}
					num3 += num6 / (double)Math.Max(1, value3.Count);
				}
				sigRank[key3] = num3 / (double)array.Length;
				list5.Add(new KeyValuePair<string, List<string>>(key3, value3));
			}
			list5.Sort((KeyValuePair<string, List<string>> a, KeyValuePair<string, List<string>> b) => sigRank[a.Key].CompareTo(sigRank[b.Key]));
			OptimizeBlockOrder(list5, list2);
			List<string> global = new List<string>();
			foreach (KeyValuePair<string, List<string>> item6 in list5)
			{
				global.AddRange(item6.Value);
			}
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string item7 in global)
			{
				hashSet.Add(item7);
			}
			long num7 = 0L;
			foreach (string item8 in global)
			{
				try
				{
					num7 += new FileInfo(item8).Length;
				}
				catch
				{
				}
			}
			List<string> list6 = new List<string>();
			if (text != null)
			{
				try
				{
					foreach (string item9 in SafeFiles(_scope, 200000))
					{
						if (!hashSet.Contains(item9) && !item9.ToLowerInvariant().StartsWith(WinDir + "\\"))
						{
							list6.Add(item9);
						}
					}
					list6.Sort(StringComparer.OrdinalIgnoreCase);
				}
				catch
				{
				}
			}
			long num8 = 0L;
			foreach (string item10 in list6)
			{
				try
				{
					num8 += new FileInfo(item10).Length;
				}
				catch
				{
				}
			}
			StringBuilder stringBuilder2 = new StringBuilder();
			stringBuilder2.AppendLine("全局布局计划");
			stringBuilder2.AppendLine();
			stringBuilder2.AppendLine(string.Format("    共 {0} 段，{1} 个文件，{2:N1} MB", list2.Count, global.Count, (double)num7 / 1048576.0));
			stringBuilder2.AppendLine(string.Format("    分成 {0} 个块（按「被哪几段读到」分组）", list5.Count));
			stringBuilder2.AppendLine();
			for (int num9 = 0; num9 < list5.Count && num9 < 12; num9++)
			{
				stringBuilder2.AppendLine(string.Format("    块{0,-2} 段{1} 读   {2:N0} 个文件", num9 + 1, list5[num9].Key, list5[num9].Value.Count));
			}
			if (list5.Count > 12)
			{
				stringBuilder2.AppendLine("    ……");
			}
			stringBuilder2.AppendLine();
			stringBuilder2.AppendLine("排列方式：块按「各段内的平均位置」排序，尽量让每段顺着读。");
			stringBuilder2.AppendLine("每段读到自己的那几个块，块数越少越接近顺序读。");
			stringBuilder2.AppendLine();
			if (list6.Count > 0)
			{
				stringBuilder2.AppendLine(string.Format("另有 {0:N0} 个文件没被任何段读到（{1:N1} MB），下一步可以选择是否也接在后面。", list6.Count, (double)num8 / 1048576.0));
			}
			if (text == null)
			{
				stringBuilder2.AppendLine("⚠\ufe0f 没选目标目录：只会排除 Windows，同卷其他文件也可能被动。");
			}
			stringBuilder2.AppendLine("注意：要搬动全部这些文件，需要一块足够大的连续空闲区。");
			stringBuilder2.AppendLine();
			stringBuilder2.AppendLine("开始重排吗？");
			if (MessageBox.Show(stringBuilder2.ToString(), "全局多段优化", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) != DialogResult.Yes)
			{
				return;
			}
			if (list6.Count > 0)
			{
				string text2 = string.Format("要不要把「没读到的 {0:N0} 个文件（{1:N1} MB）」也接到后面？\n\n是：整个目录变成一块连续区域，没录到的场景也能略微受益（约 4–8%），但要搬更多数据。\n否：只搬录到的那些，快很多，但游戏的数据会分成两块。", list6.Count, (double)num8 / 1048576.0);
				if (MessageBox.Show(text2, "是否整包搬过去", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
				{
					global.AddRange(list6);
					num7 += num8;
					Log(string.Format("已并入未读到的 {0:N0} 个文件", list6.Count));
				}
			}
			string mf = Path.Combine(_runDir, "global.manifest.csv");
			string key4 = SegmentKey(_runDir, 0) + "-GLOBAL";
			RunAsync(delegate
			{
				try
				{
					SetStatus("正在全局重排…");
					Apply("全局", global, mf, key4, delegate(double pct, int done, int total, long mb, long totalMb)
					{
						BeginInvoke((Action)delegate
						{
							ShowMoveProgress(pct, mb, totalMb);
						});
					});
					Log("全局重排完成。之后每一段都会只用 2 段读取。");
					SetStatus("全局重排完成");
				}
				catch (Exception ex)
				{
					Log("错误: " + ex.Message);
					SetStatus("失败");
				}
			});
		}

		/// <summary>判断系统是否开启「快速启动」（HiberbootEnabled）。开=1，关=0，读不到=null</summary>
		private static bool? FastStartupEnabled()
		{
			try
			{
				using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power"))
				{
					if (k == null)
					{
						return null;
					}
					object v = k.GetValue("HiberbootEnabled");
					if (v == null)
					{
						return null;
					}
					return Convert.ToInt32(v) != 0;
				}
			}
			catch
			{
				return null;
			}
		}
		private static bool? IsHddVolume(string root)
		{
			try
			{
				int num = VolumeDiskNumber(root);
				if (num < 0)
				{
					return null;
				}
				SafeFileHandle safeFileHandle = CreateFileW("\\\\.\\PhysicalDrive" + num, 2147483648u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
				if (safeFileHandle.IsInvalid)
				{
					return null;
				}
				try
				{
					IntPtr intPtr = Marshal.AllocHGlobal(12);
					IntPtr intPtr2 = Marshal.AllocHGlobal(32);
					try
					{
						Marshal.WriteInt32(intPtr, 0, 7);
						Marshal.WriteInt32(intPtr, 4, 0);
						Marshal.WriteInt32(intPtr, 8, 0);
						int br;
						if (DeviceIoControl(safeFileHandle, 2954240u, intPtr, 12, intPtr2, 32, out br, IntPtr.Zero))
						{
							return Marshal.ReadByte(intPtr2, 8) != 0;
						}
					}
					finally
					{
						Marshal.FreeHGlobal(intPtr);
						Marshal.FreeHGlobal(intPtr2);
					}
					string text = DeviceModel(safeFileHandle);
					if (string.IsNullOrEmpty(text))
					{
						return null;
					}
					string text2 = text.ToUpperInvariant();
					if (text2.Contains("SSD") || text2.Contains("NVME") || text2.Contains("SOLID STATE"))
					{
						return false;
					}
					if (text2.StartsWith("WDC WD") || text2.StartsWith("ST") || text2.StartsWith("HGST") || text2.StartsWith("TOSHIBA") || text2.StartsWith("SAMSUNG HD") || text2.StartsWith("HITACHI"))
					{
						return true;
					}
					return null;
				}
				finally
				{
					safeFileHandle.Dispose();
				}
			}
			catch
			{
				return null;
			}
		}

		private static int VolumeDiskNumber(string root)
		{
			SafeFileHandle safeFileHandle = CreateFileW("\\\\.\\" + root.TrimEnd('\\'), 2147483648u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (safeFileHandle.IsInvalid)
			{
				return -1;
			}
			IntPtr intPtr = Marshal.AllocHGlobal(1024);
			try
			{
				int br;
				if (!DeviceIoControl(safeFileHandle, 5636096u, IntPtr.Zero, 0, intPtr, 1024, out br, IntPtr.Zero))
				{
					return -1;
				}
				if (Marshal.ReadInt32(intPtr, 0) <= 0)
				{
					return -1;
				}
				return Marshal.ReadInt32(intPtr, 8);
			}
			finally
			{
				Marshal.FreeHGlobal(intPtr);
				safeFileHandle.Dispose();
			}
		}

		private static string DeviceModel(SafeFileHandle ph)
		{
			IntPtr intPtr = Marshal.AllocHGlobal(12);
			IntPtr intPtr2 = Marshal.AllocHGlobal(1024);
			try
			{
				Marshal.WriteInt32(intPtr, 0, 0);
				Marshal.WriteInt32(intPtr, 4, 0);
				Marshal.WriteInt32(intPtr, 8, 0);
				int br;
				if (!DeviceIoControl(ph, 2954240u, intPtr, 12, intPtr2, 1024, out br, IntPtr.Zero))
				{
					return null;
				}
				int num = Marshal.ReadInt32(intPtr2, 16);
				if (num <= 0 || num >= br)
				{
					return null;
				}
				StringBuilder stringBuilder = new StringBuilder();
				for (int i = num; i < br; i++)
				{
					byte b = Marshal.ReadByte(intPtr2, i);
					if (b == 0)
					{
						break;
					}
					stringBuilder.Append((char)b);
				}
				return stringBuilder.ToString().Trim();
			}
			finally
			{
				Marshal.FreeHGlobal(intPtr);
				Marshal.FreeHGlobal(intPtr2);
			}
		}

		private void ScanScope()
		{
			if (_busy)
			{
				return;
			}
			if (string.IsNullOrEmpty(_scope) || !Directory.Exists(_scope))
			{
				MessageBox.Show("先选一个程序 / 游戏的目录。", "先选目录");
				return;
			}
			RunAsync(delegate
			{
				try
				{
					SetStatus("正在扫描…");
					List<string> list = SafeFiles(_scope, 200000);
					long totalBytes = 0L;
					long totalExtents = 0L;
					long totalBreaks = 0L;
					int fileCount = 0;
					int multiCount = 0;
					long minLcn = long.MaxValue;
					long maxLcn = long.MinValue;
					int step = 0;
					// 每个元素：{ 路径, 字节, 段数 }，只收 3 段以上的
					List<string[]> frag = new List<string[]>();
					foreach (string item in list)
					{
						step++;
						// 每 200 个文件报一次进度（太密会刷爆消息队列）
						if (step % 200 == 0 || step == list.Count)
						{
							int doneN = step;
							int totalN = list.Count;
							SetStatus(string.Format("扫描中… {0:N0}/{1:N0}", doneN, totalN));
							BeginInvoke((Action)delegate
							{
								pb.Value = (int)Math.Min(100, (long)doneN * 100 / Math.Max(1, totalN));
								lblPct.Text = string.Format("扫描 {0:N0}/{1:N0}", doneN, totalN);
							});
						}
						long len = 0L;
						try
						{
							len = new FileInfo(item).Length;
						}
						catch
						{
							continue;
						}
						long firstLcn;
						long clusters;
						int ext;
						int brk;
						if (len > 0 && ScanFile(item, out firstLcn, out clusters, out ext, out brk))
						{
							fileCount++;
							totalBytes += len;
							totalExtents += ext;
							totalBreaks += brk;
							if (ext > 1)
							{
								multiCount++;
							}
							if (brk >= 2)
							{
								frag.Add(new string[3] { item, len.ToString(), brk.ToString() });
							}
							if (firstLcn >= 0)
							{
								if (firstLcn < minLcn) minLcn = firstLcn;
								long end = firstLcn + clusters - 1;
								if (end > maxLcn) maxLcn = end;
							}
						}
					}
					if (fileCount == 0)
					{
						Log("扫描不到任何文件。");
						SetStatus("扫描完成");
						return;
					}

					long clusterSize = 4096L;
					try
					{
						uint spc, bps, fr, tot;
						if (GetDiskFreeSpaceW(Path.GetPathRoot(_scope), out spc, out bps, out fr, out tot))
						{
							clusterSize = (long)spc * (long)bps;
						}
					}
					catch
					{
					}

					double avgBytes = (double)totalBytes / (double)fileCount;
					double totalMb = (double)totalBytes / 1048576.0;
					double spanMb = ((minLcn == long.MaxValue) ? 0.0 : ((double)((maxLcn - minLcn + 1) * clusterSize) / 1048576.0));
					double spanRatio = ((totalMb > 0.0) ? (spanMb / totalMb) : 0.0);
					double avgExt = (double)totalBreaks / (double)fileCount;

					// 理论上限：把全部跳转消掉的理想值
					//   跳转次数 ≈ 总段数 − 1（按物理顺序读时的结构性跳转）
					//   传输时间 = 总字节 ÷ 100 MB/s（顺序速度按 100 估）
					double seeks = Math.Max(0.0, (double)totalBreaks);
					double seekSec = seeks * 0.0065;
					double transferSec = totalMb / 100.0;
					double ceiling = (seekSec + transferSec > 0.0) ? (seekSec / (seekSec + transferSec)) * 100.0 : 0.0;

					string between = (avgBytes < 1048576.0) ? "寻道占大头（有机会）" : ((avgBytes < 3145728.0) ? "中等（还有空间）" : "寻道被摊薄（机会小）");
					string inside = (avgExt < 1.5) ? "基本不碎" : ((avgExt < 3.0) ? "轻度碎片" : ((avgExt < 8.0) ? "中度碎片（值得整理）" : "重度碎片（很值得整理）"));

					StringBuilder sb = new StringBuilder();
					sb.AppendLine("快速扫描（只读，没动任何数据）");
					sb.AppendLine();
					sb.AppendLine("  目录      " + _scope);
					sb.AppendLine(string.Format("  文件      {0:N0} 个", fileCount));
					sb.AppendLine(string.Format("  总字节    {0:N1} MB", totalMb));
					sb.AppendLine(string.Format("  平均大小  {0:N0} KB", avgBytes / 1024.0));
					sb.AppendLine(string.Format("  跨度      {0:N1} MB（跨度/数据 = {1:N1}x）", spanMb, spanRatio));
					sb.AppendLine(string.Format("  总段数    {0:N0}（多段文件 {1:N0} 个）", totalExtents, multiCount));
					sb.AppendLine(string.Format("  真实断点  {0:N0} 处（首尾相接的段不算跳转；2 处以上 {1:N0} 个文件）", totalBreaks, frag.Count));
					sb.AppendLine();
					sb.AppendLine("  两个**独立**的浪费来源：");
					sb.AppendLine(string.Format("    文件之间   平均 {0:N0} KB → {1}", avgBytes / 1024.0, between));
					sb.AppendLine(string.Format("    文件内部   平均 {0:N1} 段/文件 → {1}", avgExt, inside));
					sb.AppendLine();
					sb.AppendLine(string.Format("  理论上限   约 {0:N0}%", ceiling));
					sb.AppendLine(string.Format("    算法：跳转 {0:N0} 次 × 6.5ms ÷ (传输 {1:N1}s + 跳转 {2:N1}s)", seeks, transferSec, seekSec));
					sb.AppendLine("    这是「把所有跳转都消掉」的理想值；实际通常只有 1/3 ~ 1/2。");
					sb.AppendLine("    传输速度按 100 MB/s 估；外圈快、内圈慢，实际会浮动。");
					sb.AppendLine();

					bool interGood = avgBytes < 3145728.0;
					bool intraGood = avgExt >= 3.0;
					string verdict;
					if (interGood && intraGood)
					{
						verdict = "✅ 两头都有空间：录一次值得试，而且值得整理碎片";
					}
					else if (intraGood)
					{
						verdict = "✅ 文件内部碎得厉害 → 整理碎片会有明显收益（不用录也能做）";
					}
					else if (interGood)
					{
						verdict = "⚠ 只能靠「按顺序重排」拿收益 → 必须录一次才知道多少";
					}
					else
					{
						verdict = "❌ 两头都窄：文件不小、也不碎，基本没戏";
					}
					sb.AppendLine("  结论：" + verdict);
					sb.AppendLine();
					sb.AppendLine("扫描只是筛掉明显没戏的，真实收益只有录一次才知道。");
sb.AppendLine("但「文件内部碎片」这一项是确定的：段数越多，整理后必快。");
					if (frag.Count > 0)
					{
						sb.AppendLine();
						sb.AppendLine(string.Format("（关掉这个窗口后会问你要不要整理这 {0:N0} 个碎文件 —— 那就是「整理碎片」的入口）", frag.Count));
					}

					if (frag.Count > 0)
					{
						sb.AppendLine();
						sb.AppendLine(string.Format("  最碎的 {0} 个：", Math.Min(frag.Count, 8)));
						frag.Sort(delegate(string[] a, string[] b)
						{
							int ea = int.Parse(a[2]);
							int eb = int.Parse(b[2]);
							if (ea != eb) return eb.CompareTo(ea);
							return long.Parse(b[1]).CompareTo(long.Parse(a[1]));
						});
						int show = Math.Min(frag.Count, 8);
						for (int i = 0; i < show; i++)
						{
							sb.AppendLine(string.Format("      {0,-40} {1,8:N1} MB   {2,4} 段",
								ShortName(frag[i][0], 38), long.Parse(frag[i][1]) / 1048576.0, int.Parse(frag[i][2])));
						}
						if (frag.Count > show)
						{
							sb.AppendLine(string.Format("      …… 还有 {0:N0} 个", frag.Count - show));
						}
					}

					string report = sb.ToString().TrimEnd();
					Log(report);
					SetStatus("扫描完成");

					// 只整理最碎的 500 个，避免一次搬太久
					List<string> fixList = new List<string>();
					long fixBytes = 0L;
					int cap = Math.Min(frag.Count, 500);
					for (int i = 0; i < cap; i++)
					{
						fixList.Add(frag[i][0]);
						fixBytes += long.Parse(frag[i][1]);
					}

					BeginInvoke((Action)delegate
					{
						MessageBox.Show(report, "扫描结果", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
						if (fixList.Count == 0)
						{
							return;
						}
						string q = string.Format(
							"发现 {0:N0} 个文件被切成 3 段以上，{1:N1} MB。\n\n" +
							"要把它们整理成连续的一整块吗？\n\n" +
							"  · 只搬数据块，路径 / 名字 / 时间戳都不变\n" +
							"  · 每个文件会变成 1 段，读它时不再中途跳转\n" +
							"  · 需要一块足够大的连续空闲区，放不下的会跳过\n\n" +
							"继续吗？",
							fixList.Count, (double)fixBytes / 1048576.0);
						if (MessageBox.Show(q, "整理碎片", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
						{
							return;
						}
						string mf = Path.Combine(DataDir, "defrag_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".manifest.csv");
						string key = "DEFRAG#" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
						RunAsync(delegate
						{
							try
							{
								SetStatus("正在整理碎片…");
								ApplyPacked("碎片整理", fixList, mf, key, delegate(double pct, int done2, int totalN, long mbNow, long mbTotal)
								{
									BeginInvoke((Action)delegate
									{
										ShowMoveProgress(pct, 0, 1);
									});
								});
								Log("碎片整理完成。再扫描一次可以看到段数下降。");
								SetStatus("碎片整理完成");
							}
							catch (Exception ex)
							{
								Log("碎片整理失败: " + ex.Message);
								SetStatus("失败");
							}
						});
					});
				}
				catch (Exception ex)
				{
					Log("扫描出错: " + ex.Message);
				}
			});
		}

		private static double MaxFileMb(List<string> files)
		{
			double max = 0.0;
			foreach (string f in files)
			{
				try
				{
					double mb = (double)new FileInfo(f).Length / 1048576.0;
					if (mb > max) max = mb;
				}
				catch
				{
				}
			}
			return max;
		}
		/// <summary>导出诊断信息：系统、权限、卷、设置、历史、日志</summary>
		private void ExportDiagnose()
		{
			try
			{
				StringBuilder sb = new StringBuilder();
				sb.AppendLine("DiskLayout 诊断信息");
				sb.AppendLine("================================");
				sb.AppendLine("时间        " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
				sb.AppendLine("程序        " + Application.ExecutablePath);
				string osName = "";
				try
				{
					using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
					{
						if (k != null)
						{
							object pn = k.GetValue("ProductName");
							object dv = k.GetValue("DisplayVersion");
							object bi = k.GetValue("CurrentBuildNumber");
							if (pn != null) osName = pn.ToString();
							if (dv != null) osName += " " + dv.ToString();
							if (bi != null) osName += " (Build " + bi.ToString() + ")";
						}
					}
				}
				catch
				{
				}
				if (osName.Length == 0) osName = Environment.OSVersion.VersionString;
				sb.AppendLine("系统        " + osName + "（" + (Environment.Is64BitOperatingSystem ? "64 位" : "32 位") + "）");
				sb.AppendLine("管理员      " + (IsAdmin() ? "是" : "否"));
				bool? fb = FastStartupEnabled();
				sb.AppendLine("快速启动    " + (fb.HasValue ? (fb.Value ? "开（关机不清缓存）" : "关（关机即清缓存）") : "读不到"));
				sb.AppendLine("已开机      " + TimeSpan.FromMilliseconds((double)(uint)Environment.TickCount).TotalHours.ToString("N1") + " 小时");
				sb.AppendLine("数据目录    " + DataDir);
				sb.AppendLine("目标目录    " + (_scope ?? "(未选)"));
				sb.AppendLine();

sb.AppendLine("卷访问自检（读 / 写）；" + (IsAdmin() ? "本进程=管理员" : "本进程=普通权限"));
				try
				{
					foreach (DriveInfo d in DriveInfo.GetDrives())
					{
						try { if (d.IsReady) sb.AppendLine("  " + VolumeAccessProbe(d.Name)); } catch { }
					}
				}
				catch { }
				sb.AppendLine();
				sb.AppendLine("卷：");
				try
				{
					foreach (DriveInfo d in DriveInfo.GetDrives())
					{
						try
						{
							if (!d.IsReady) { sb.AppendLine("  " + d.Name + "  " + d.DriveType + "  （未就绪）"); continue; }
							bool? hdd = IsHddVolume(d.Name);
							sb.AppendLine(string.Format("  {0} {1,-6} {2,-8} 可用 {3:N1} GB / 共 {4:N1} GB  介质={5}",
								d.Name, d.DriveType.ToString(), d.DriveFormat,
								(double)d.AvailableFreeSpace / 1073741824.0,
								(double)d.TotalSize / 1073741824.0,
								hdd.HasValue ? (hdd.Value ? "机械盘" : "固态盘") : "未知"));
						}
						catch { }
					}
				}
				catch { }
				sb.AppendLine();

				sb.AppendLine("设置（data\\settings.txt）：");
				try
				{
					string sp = SettingsPath;
					if (File.Exists(sp))
					{
						foreach (string line in File.ReadAllLines(sp)) sb.AppendLine("  " + line);
					}
					else sb.AppendLine("  (还没有)");
				}
				catch (Exception ex) { sb.AppendLine("  读不到: " + ex.Message); }
				sb.AppendLine();

				sb.AppendLine("历史记录（最近 10 条）：");
				try
				{
					List<string[]> hist = LoadHistory();
					int n = Math.Min(10, hist.Count);
					for (int i = 0; i < n; i++)
					{
						string[] r = hist[i];
						sb.AppendLine("  " + string.Join(" | ", r));
					}
					if (hist.Count == 0) sb.AppendLine("  (没有)");
				}
				catch (Exception ex) { sb.AppendLine("  读不到: " + ex.Message); }
				sb.AppendLine();

				sb.AppendLine("录制目录（最近 10 个）：");
				try
				{
					string runs = Path.Combine(DataDir, "runs");
					if (Directory.Exists(runs))
					{
						string[] dirs = Directory.GetDirectories(runs);
						Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
						int from = Math.Max(0, dirs.Length - 10);
						for (int i = dirs.Length - 1; i >= from; i--)
						{
							sb.AppendLine("  " + Path.GetFileName(dirs[i]) + "  （" + Directory.GetFiles(dirs[i]).Length + " 个文件）");
						}
					}
					else sb.AppendLine("  (还没有)");
				}
				catch { }
				sb.AppendLine();

				sb.AppendLine("当前界面上的段：");
				for (int i = 0; i < _segs.Count; i++)
				{
					Segment s = _segs[i];
					sb.AppendLine(string.Format("  第 {0} 段  {1:N0} 个文件  {2:N1} MB  收益 {3:N1}%",
						s.Index, s.Files.Count, (double)s.Bytes / 1048576.0, s.Gain));
				}
				if (_segs.Count == 0) sb.AppendLine("  (没有)");
				sb.AppendLine();

				sb.AppendLine("===== 本次运行的完整日志 =====");
				sb.AppendLine(txtLog.Text.TrimEnd());

				string outPath = Path.Combine(DataDir, "diagnose_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
				File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(true));
				Log("诊断信息已导出: " + outPath);
				if (MessageBox.Show("已导出到：\n" + outPath + "\n\n现在打开看吗？", "诊断", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
				{
					Process.Start("notepad.exe", "\"" + outPath + "\"");
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("导出失败: " + ex.Message, "诊断");
			}
		}
		private static string ShortName(string path, int max)
		{
			if (path == null) return "";
			if (path.Length <= max) return path;
			return "…" + path.Substring(path.Length - max + 1);
		}

		private static bool ScanFile(string path, out long firstLcn, out long totalClusters, out int extentCount, out int realBreaks)
		{
			firstLcn = -1L;
			totalClusters = 0L;
			extentCount = 0;
			realBreaks = 0;
			SafeFileHandle safeFileHandle = CreateFileW(path, 2147483648u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (safeFileHandle.IsInvalid)
			{
				return false;
			}
			IntPtr intPtr = Marshal.AllocHGlobal(8);
			Marshal.WriteInt64(intPtr, 0, 0L);
			int num = 4096;
			IntPtr intPtr2 = Marshal.AllocHGlobal(num);
			try
			{
				bool flag = false;
				for (int i = 0; i < 8; i++)
				{
					int br;
					if (DeviceIoControl(safeFileHandle, 589939u, intPtr, 8, intPtr2, num, out br, IntPtr.Zero))
					{
						flag = true;
						break;
					}
					if (Marshal.GetLastWin32Error() == 234 && br > num)
					{
						Marshal.FreeHGlobal(intPtr2);
						num = br;
						intPtr2 = Marshal.AllocHGlobal(num);
						continue;
					}
					return false;
				}
				if (!flag)
				{
					return false;
				}
				int num2 = Marshal.ReadInt32(intPtr2, 0);
				if (num2 <= 0)
				{
					return false;
				}
				extentCount = num2;
				// 段数 ≠ 真实跳转次数：下一段的 LCN 如果正好接在上一段后面，
				// 读的时候其实是连续的、不用跳。这种"假碎片"要单独排除。
				long startVcn = Marshal.ReadInt64(intPtr2, 8);
				long prevNext = startVcn;
				long prevLcn = -1L;
				long prevLen = 0L;
				long num3 = 0L;
				int brk = 0;
				for (int j = 0; j < num2; j++)
				{
					int num4 = 16 + j * 16;
					long num5 = Marshal.ReadInt64(intPtr2, num4);
					long num6 = Marshal.ReadInt64(intPtr2, num4 + 8);
					long len = num5 - prevNext;
					if (num6 >= 0)
					{
						if (prevLcn < 0)
						{
							firstLcn = num6;
						}
						else if (num6 != prevLcn + prevLen)
						{
							brk++;
						}
						prevLcn = num6;
						prevLen = len;
					}
					prevNext = num5;
					num3 = num5;
				}
				totalClusters = num3;
				realBreaks = brk;
				return true;
			}
			finally
			{
				Marshal.FreeHGlobal(intPtr);
				Marshal.FreeHGlobal(intPtr2);
				safeFileHandle.Dispose();
			}
		}

		private void ColdEvict()
		{
			if (_busy)
			{
				return;
			}
			if (!IsAdmin())
			{
				MessageBox.Show("需要管理员权限。请右键“以管理员身份运行”。", "权限不足");
				return;
			}
			if (string.IsNullOrEmpty(_scope) || !Directory.Exists(_scope))
			{
				MessageBox.Show("先用「浏览...」选中要内迁的目录（比如放视频/压缩包的那个盘或文件夹）。", "先选目录");
				return;
			}
			// 直接从目标目录开始浏览；换目录在浏览窗口里点「选择文件夹...」就行，
			// 不必先弹一次目录选择框（那样会有两道门，语义重复）
			string startDir = _scope;
			if (string.IsNullOrEmpty(startDir) || !Directory.Exists(startDir))
			{
				startDir = Path.GetPathRoot(Application.ExecutablePath);
			}
			List<string> selected;
			using (ColdPickForm coldPickForm = new ColdPickForm(startDir))
			{
				if (coldPickForm.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}
				selected = coldPickForm.Selected;
			}
			if (selected.Count == 0)
			{
				MessageBox.Show("没有选中任何文件。");
			}
			else
			{
				// 按所在的盘分组：别的盘上的文件要真的搬到机械盘，而不是在它自己那块盘里挪
				Dictionary<string, List<string>> byVol = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
				foreach (string one in selected)
				{
					string root = null;
					try { root = Path.GetPathRoot(Path.GetFullPath(one)); } catch { }
					if (string.IsNullOrEmpty(root)) continue;
					if (!byVol.ContainsKey(root)) byVol[root] = new List<string>();
					byVol[root].Add(one);
				}
				foreach (KeyValuePair<string, List<string>> kv in byVol)
				{
					ColdMove(kv.Value, kv.Key);
				}
			}
		}

		private string PickManifest()
		{
			List<string> list = new List<string>();
			try
			{
				if (!string.IsNullOrEmpty(_runDir) && Directory.Exists(_runDir))
				{
					list.AddRange(Directory.GetFiles(_runDir, "*.manifest.csv", SearchOption.TopDirectoryOnly));
				}
			}
			catch
			{
			}
			try
			{
				if (Directory.Exists(DataDir))
				{
					list.AddRange(Directory.GetFiles(DataDir, "*.manifest.csv", SearchOption.TopDirectoryOnly));
				}
			}
			catch
			{
			}
			if (list.Count == 0)
			{
				MessageBox.Show("没有找到搬家清单（*.manifest.csv）。\n\n清单是「重排」或「冷数据内迁」时自动生成的，\n没搬过就没有。", "没有清单");
				return null;
			}
			list.Sort(StringComparer.OrdinalIgnoreCase);
			if (list.Count == 1)
			{
				return list[0];
			}
			using (ListPickForm listPickForm = new ListPickForm("选择搬家清单", list))
			{
				if (listPickForm.ShowDialog(this) != DialogResult.OK)
				{
					return null;
				}
				return listPickForm.Selected;
			}
		}

		private static List<MRow> ParseManifest(string path)
		{
			List<MRow> list = new List<MRow>();
			string[] array = File.ReadAllLines(path);
			foreach (string text in array)
			{
				if (text.Length != 0 && !text.StartsWith("file,", StringComparison.OrdinalIgnoreCase))
				{
					string[] array2 = SplitCsv(text);
					long result;
					long result2;
					long result3;
					if (array2.Length >= 4 && long.TryParse(array2[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && long.TryParse(array2[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out result2) && long.TryParse(array2[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out result3))
					{
						list.Add(new MRow
						{
							File = array2[0],
							OldLcn = result,
							NewLcn = result2,
							Clusters = result3
						});
					}
				}
			}
			return list;
		}

		private void CheckDrift()
		{
			string mf = PickManifest();
			if (mf == null)
			{
				return;
			}
			List<MRow> rows = ParseManifest(mf);
			if (rows.Count == 0)
			{
				MessageBox.Show("清单是空的。");
				return;
			}
			RunAsync(delegate
			{
				try
				{
					SetStatus("正在检查漂移…");
					int num = 0;
					int num2 = 0;
					int num3 = 0;
					int num4 = 0;
					List<string> list = new List<string>();
					List<string> list2 = new List<string>();
					List<string> list3 = new List<string>();
					foreach (MRow item in rows)
					{
						long lcn;
						long clusters;
						if (!File.Exists(item.File))
						{
							num3++;
							if (list2.Count < 6)
							{
								list2.Add(item.File);
							}
						}
						else if (!TryExtent(item.File, out lcn, out clusters))
						{
							num4++;
							if (list3.Count < 6)
							{
								list3.Add(item.File);
							}
						}
						else if (lcn == item.NewLcn)
						{
							num++;
						}
						else
						{
							num2++;
							if (list.Count < 5)
							{
								list.Add(string.Format("    {0}  期望 LCN {1:N0}，实际 {2:N0}", Path.GetFileName(item.File), item.NewLcn, lcn));
							}
						}
					}
					double num5 = ((rows.Count > 0) ? ((double)num2 * 100.0 / (double)rows.Count) : 0.0);
					StringBuilder stringBuilder = new StringBuilder();
					stringBuilder.AppendLine("漂移检测");
					stringBuilder.AppendLine();
					stringBuilder.AppendLine("  清单    " + mf);
					stringBuilder.AppendLine(string.Format("  文件    {0:N0} 个", rows.Count));
					stringBuilder.AppendLine(string.Format("  位置正确 {0:N0} 个", num));
					stringBuilder.AppendLine(string.Format("  位置变了 {0:N0} 个（{1:N1}%）", num2, num5));
					if (num3 > 0)
					{
						stringBuilder.AppendLine(string.Format("  文件不存在 {0:N0} 个（被更新删除/替换，或临时文件）", num3));
					}
					if (num4 > 0)
					{
						stringBuilder.AppendLine(string.Format("  打不开   {0:N0} 个（被其他程序占用，关掉程序再测）", num4));
					}
					if (list2.Count > 0)
					{
						stringBuilder.AppendLine();
						stringBuilder.AppendLine("  不存在的例子：");
						foreach (string item2 in list2)
						{
							stringBuilder.AppendLine("    " + item2);
						}
					}
					if (list3.Count > 0)
					{
						stringBuilder.AppendLine();
						stringBuilder.AppendLine("  打不开的例子：");
						foreach (string item3 in list3)
						{
							stringBuilder.AppendLine("    " + item3);
						}
					}
					if (list.Count > 0)
					{
						stringBuilder.AppendLine();
						stringBuilder.AppendLine("  变化举例：");
						foreach (string item4 in list)
						{
							stringBuilder.AppendLine(item4);
						}
					}
					stringBuilder.AppendLine();
					if (num5 < 5.0)
					{
						stringBuilder.AppendLine("  结论：基本没变，不需要处理。");
					}
					else if (num5 < 30.0)
					{
						stringBuilder.AppendLine("  结论：有部分漂移，可以再跑一次重排。");
					}
					else
					{
						stringBuilder.AppendLine("  结论：漂移明显（可能是程序更新了），建议重新录制并重排。");
					}
					Log(stringBuilder.ToString().TrimEnd());
					SetStatus("漂移检测完成");
					MessageBox.Show(stringBuilder.ToString(), "漂移检测", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				}
				catch (Exception ex)
				{
					Log("漂移检测失败: " + ex.Message);
					SetStatus("失败");
				}
			});
		}

		private void Rollback()
		{
			if (_busy)
			{
				return;
			}
			if (!IsAdmin())
			{
				MessageBox.Show("需要管理员权限。请右键“以管理员身份运行”。", "权限不足");
				return;
			}
			string text = PickManifest();
			if (text == null)
			{
				return;
			}
			List<MRow> rows = ParseManifest(text);
			if (rows.Count == 0)
			{
				MessageBox.Show("清单是空的。");
			}
			else
			{
				if (MessageBox.Show(string.Format("把清单里的 {0:N0} 个文件搬回它们【原来的位置】。\n\n这是「重排」的逆操作。\n\n继续吗？", rows.Count), "回滚", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
				{
					return;
				}
				RunAsync(delegate
				{
					try
					{
						SetStatus("正在回滚…");
						string pathRoot = Path.GetPathRoot(rows[0].File);
			SafeFileHandle safeFileHandle = CreateFileW("\\\\.\\" + pathRoot.TrimEnd('\\'), 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (safeFileHandle.IsInvalid)
			{
				throw new Exception("打开卷失败（需要管理员）");
			}
						int num = 0;
						int num2 = 0;
						int num3 = 0;
						try
						{
							for (int i = 0; i < rows.Count; i++)
							{
								MRow mRow = rows[i];
								if (i % 20 == 0)
								{
									SetStatus(string.Format("回滚中 {0}/{1}…", i + 1, rows.Count));
								}
								long lcn;
								long clusters;
								if (!File.Exists(mRow.File))
								{
									num2++;
								}
								else if (!TryExtent(mRow.File, out lcn, out clusters))
								{
									num2++;
								}
								else if (lcn == mRow.OldLcn)
								{
									num2++;
								}
								else
								{
									long count = ((clusters > 0) ? clusters : mRow.Clusters);
									if (MoveFile(safeFileHandle, mRow.File, 0L, mRow.OldLcn, count))
									{
										num++;
									}
									else
									{
										num3++;
									}
								}
							}
						}
						finally
						{
							safeFileHandle.Dispose();
						}
						Log(string.Format("回滚完成：搬回 {0:N0} 个，跳过 {1:N0} 个（已在原位/不存在），失败 {2:N0} 个", num, num2, num3));
						SetStatus("回滚完成");
						MessageBox.Show(string.Format("回滚完成。\n\n搬回 {0:N0} 个\n跳过 {1:N0} 个\n失败 {2:N0} 个\n\n失败的原因通常是原位置已被别的文件占用。", num, num2, num3), "回滚");
					}
					catch (Exception ex)
					{
						Log("回滚失败: " + ex.Message);
						SetStatus("失败");
					}
				});
			}
		}


		/// <summary>找所有可用的机械盘（用于把别处的大文件搬过来）</summary>
		private static List<string> FindHddVolumes()
		{
			List<string> list = new List<string>();
			try
			{
				foreach (DriveInfo d in DriveInfo.GetDrives())
				{
					try
					{
						if (d.DriveType != DriveType.Fixed || !d.IsReady)
						{
							continue;
						}
						bool? hdd = IsHddVolume(d.Name);
						if (hdd == true)
						{
							list.Add(d.Name);
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
			return list;
		}

		/// <summary>
		/// 跨盘搬运：复制 → 校验大小 → 删除源文件。
		/// 任一步失败就保留源文件，绝不出现"两边都没有"。
		/// </summary>
		private List<string> CopyAcrossVolumes(List<string> files, string targetRoot, Action<double, int, int, long, long> progress)
		{
			Directory.CreateDirectory(targetRoot);
			long total = 0;
			foreach (string f in files)
			{
				try { total += new FileInfo(f).Length; } catch { }
			}
			long totalMb = total / 1048576;
			long doneMb = 0;
			List<string> moved = new List<string>();
			for (int i = 0; i < files.Count; i++)
			{
				if (_cancel)
				{
					Log(string.Format("已取消：搬完 {0:N0} 个后停下，剩下的没动。", moved.Count));
					break;
				}
				string src = files[i];
				string dst = UniquePath(Path.Combine(targetRoot, Path.GetFileName(src)));
				try
				{
					long len = new FileInfo(src).Length;
					File.Copy(src, dst, false);
					long copied = new FileInfo(dst).Length;
					if (copied != len)
					{
						// 大小对不上：删掉副本，源文件保留
						try { File.Delete(dst); } catch { }
						Log("复制校验失败，已保留源文件: " + src);
						continue;
					}
					File.Delete(src);
					moved.Add(dst);
					doneMb += len / 1048576;
					Log(string.Format("已搬到 {0}  ({1:N1} MB)", dst, len / 1048576.0));
					if (progress != null)
					{
						progress((double)doneMb * 100.0 / Math.Max(1.0, (double)totalMb), i + 1, files.Count, doneMb, totalMb);
					}
				}
				catch (Exception ex)
				{
					Log("搬不了 " + src + " : " + ex.Message);
				}
			}
			return moved;
		}

		/// <summary>把选中的冷数据挪到内圈：本来就在机械盘上 → 只挪数据块；
		/// 在固态盘/其他盘上 → 复制到机械盘的 _ColdData，校验大小，删源，再排到内圈。</summary>
		private void ColdMove(List<string> files, string vol)
		{
			long totalBytes2 = 0;
			foreach (string one in files) { try { totalBytes2 += new FileInfo(one).Length; } catch { } }
			double totalMb2 = (double)totalBytes2 / 1048576.0;
			bool? srcHdd = IsHddVolume(vol);
			bool needCopy = (srcHdd.HasValue && !srcHdd.Value);
			if (!srcHdd.HasValue)
			{
				// 判断不出来（比如没提权、或是虚拟盘/移动盘）：问一句，别擅自决定
				DialogResult ask = MessageBox.Show(
					string.Format("判断不出 {0} 是机械盘还是固态盘。\n\n要把它当冷数据、真的搬到机械盘吗？\n选「否」= 只在这块盘内部挪到内圈（固态盘上没意义）。", vol),
					"这块盘的类型不确定", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
				if (ask != DialogResult.Yes)
				{
					DoEvict(files, true);
					return;
				}
				needCopy = true;
			}
			if (!needCopy)
			{
				// 本来就在机械盘上：只挪数据块，不改路径
				DoEvict(files, true);
				return;
			}

			// ---------- 在固态盘上：可以真的搬到机械盘 ----------
			List<string> hdds = FindHddVolumes();
			if (hdds.Count == 0)
			{
				MessageBox.Show("选中的文件在 " + vol + "（固态盘）。\n\n没有找到可用的机械盘，所以只能在本盘内挪数据块（对 SSD 没意义）。", "没有机械盘");
				if (MessageBox.Show("仍然要把它们在本盘内挪到\"内圈\"吗？", "继续吗", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
				{
					DoEvict(files, true);
				}
				return;
			}

			// 默认选好空闲最多的机械盘，用户不满意可以在弹窗里改
			string targetVol;
			using (TargetDiskForm tf = new TargetDiskForm(vol, hdds, files.Count, totalMb2))
			{
				if (tf.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(tf.Target))
				{
					return;
				}
				targetVol = tf.Target;
			}
			string targetRoot = Path.Combine(targetVol, "_ColdData");

			RunAsync(delegate
			{
				try
				{
					SetStatus("正在复制到机械盘…");
					List<string> moved = CopyAcrossVolumes(files, targetRoot, delegate(double pct, int done2, int totalN, long mb, long totalMb)
					{
						BeginInvoke((Action)delegate
						{
							ShowMoveProgress(pct, mb, totalMb);
						});
					});
					if (moved.Count == 0)
					{
						Log("没有文件成功搬过去。");
						SetStatus("失败");
						return;
					}
					Log(string.Format("复制完成：{0:N0} 个，开始排内圈…", moved.Count));
					_bypassScope = true;
					try
					{
						EvictCore(moved);
					}
					finally
					{
						_bypassScope = false;
					}
				}
				catch (Exception ex)
				{
					Log("搬到机械盘失败: " + ex.Message);
					SetStatus("失败");
				}
			});
		}


		private static string UniquePath(string p)
		{
			if (!File.Exists(p) && !Directory.Exists(p))
			{
				return p;
			}
			string directoryName = Path.GetDirectoryName(p);
			string fileName = Path.GetFileName(p);
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
			string extension = Path.GetExtension(fileName);
			for (int i = 2; i < 9999; i++)
			{
				string text = Path.Combine(directoryName, fileNameWithoutExtension + " (" + i + ")" + extension);
				if (!File.Exists(text) && !Directory.Exists(text))
				{
					return text;
				}
			}
			return p + "_" + DateTime.Now.Ticks;
		}

		private void DoEvict(List<string> files, bool fromDrop)
		{
			if (_busy)
			{
				return;
			}
			if (!IsAdmin())
			{
				MessageBox.Show("需要管理员权限。请右键“以管理员身份运行”。", "权限不足");
				return;
			}
			long num = 0L;
			foreach (string file in files)
			{
				try
				{
					num += new FileInfo(file).Length;
				}
				catch
				{
				}
			}
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine(string.Format("把这 {0:N0} 个文件（{1:N1} MB）挪到【内圈】（卷的末尾）。", files.Count, (double)num / 1048576.0));
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("要挪的文件：");
			int num6 = Math.Min(files.Count, 10);
			for (int j = 0; j < num6; j++)
			{
				stringBuilder.AppendLine("    " + Path.GetFileName(files[j]));
			}
			if (files.Count > num6)
			{
				stringBuilder.AppendLine(string.Format("    …… 还有 {0:N0} 个", files.Count - num6));
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("目的：把磁头很少去的区域用起来，腾出外圈给常读的东西。");
			stringBuilder.AppendLine("这些文件以后读会慢一些（外圈快、内圈慢）。");
			stringBuilder.AppendLine();
			stringBuilder.Append("适合：视频、压缩包、备份这类不常读的大文件。继续吗？");
			string text = stringBuilder.ToString();
			if (MessageBox.Show(text, "挪到内圈", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
			{
				return;
			}
			// 挪到内圈是"你明确选中的文件"，不需要再按目标目录过滤
			_bypassScope = true;
			RunAsync(delegate
			{
				try
				{
					EvictCore(files);
				}
				catch (Exception ex)
				{
					Log("内迁失败: " + ex.Message);
					SetStatus("失败");
				}
				finally
				{
					_bypassScope = false;
				}
			});
		}

		/// <summary>填到前部：把整个目录挪到"当前位置之前"的第一块连续空闲区（不拼洞、不后退）</summary>
		private void PackFront()
		{
			if (_busy) return;
			if (!IsAdmin())
			{
				MessageBox.Show("需要管理员权限。请右键“以管理员身份运行”。", "权限不足");
				return;
			}
			if (string.IsNullOrEmpty(_scope) || !Directory.Exists(_scope))
			{
				MessageBox.Show("先用「浏览...」选中要整理的文件夹。", "先选目录");
				return;
			}
			try
			{
				string volRoot = Path.GetPathRoot(Path.GetFullPath(_scope));
				bool? isHdd = IsHddVolume(volRoot);
				if (isHdd.HasValue && !isHdd.Value)
				{
					if (MessageBox.Show("这块盘看起来是【固态硬盘】，填到前部对它没有意义。\n\n仍然继续吗？", "这是固态盘", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
					{
						return;
					}
				}
			}
			catch { }

			List<string> all;
			Cursor = Cursors.WaitCursor;
			try { all = SafeFiles(_scope, 500000); }
			finally { Cursor = Cursors.Default; }
			if (all.Count == 0) { MessageBox.Show("这个目录里没有文件。"); return; }

			long total = 0;
			foreach (string f in all) { try { total += new FileInfo(f).Length; } catch { } }

			string q = string.Format(
				"把「{0}」里的 {1:N0} 个文件（{2:N1} MB）整体挪到【当前位置之前的第一块连续空闲区】。\n\n" +
				"和「重排」的区别：\n" +
				"  重排        → 按录制顺序排成连续的一长条，位置由工具挑（可能在后面）\n" +
				"  填到前部    → 不管顺序，只用一整块连续区，而且一定在当前位置之前\n\n" +
				"结果：整个文件夹在盘上是一整条连续区域，读取时 0 次额外跳转。\n" +
				"前面没有装得下的整块空位时，这次就不做（不会拆成小块硬塞）。\n\n" +
				"⚠️ 会搬动较多数据，可能几十分钟。中途不要断电。\n\n" +
				"继续吗？", Path.GetFileName(_scope.TrimEnd('\\')), all.Count, total / 1048576.0);
			if (MessageBox.Show(q, "填到前部", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

			string mf = Path.Combine(DataDir, "pack_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".manifest.csv");
			string key = "PACK#" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
			_bypassScope = true;
			RunAsync(delegate
			{
				try
				{
					SetStatus("正在填到前部…");
					ApplyPacked("填到前部", all, mf, key, delegate(double pct, int done, int totalN, long mb, long totalMb)
					{
						BeginInvoke((Action)delegate
						{
							ShowMoveProgress(pct, 0, 1);
						});
					}, true);
					Log("填到前部完成。");
					SetStatus("填到前部完成");
				}
				catch (Exception ex)
				{
					Log("填到前部失败: " + ex.Message);
					LogFile("填到前部失败：" + ex.Message);
					SetStatus("失败");
					string msg = ex.Message;
					try
					{
						BeginInvoke((Action)delegate
						{
							MessageBox.Show(msg + "\n\n说明：这个功能只把文件夹【往盘的外圈方向】挪，"
								+ "而且只用一整块连续空闲区。\n前面没有这么大的整块空位时，它不会拆成小块硬塞，"
								+ "也不会挪到后面去。\n\n可以：腾出前面的空间后再试，或者改用「重排」。",
								"填到前部", MessageBoxButtons.OK, MessageBoxIcon.Information);
						});
					}
					catch
					{
					}
				}
				finally
				{
					_bypassScope = false;
				}
			});
		}

		/// <summary>把整个目录搬进一块连续空闲区。默认允许挑位置；
		/// onlyForward=true 时只用「当前位置之前」的整块，不拼空洞、也不往后放。</summary>
		/// <summary>只在"当前整段之前"找一整块连续空闲区，不拼空洞、也不放到后面。</summary>
		private void ApplyPacked(string label, List<string> srcFiles, string manifestPath, string registryKey,
			Action<double, int, int, long, long> progress)
		{
			ApplyPacked(label, srcFiles, manifestPath, registryKey, progress, false);
		}

		private void ApplyPacked(string label, List<string> srcFiles, string manifestPath, string registryKey,
			Action<double, int, int, long, long> progress, bool onlyForward)
		{
			string[] real = new string[srcFiles.Count];
			for (int i = 0; i < srcFiles.Count; i++)
			{
				real[i] = ResolvePath(srcFiles[i]);
			}
			string scope = null;
			try { if (!string.IsNullOrEmpty(_scope)) scope = Path.GetFullPath(_scope).TrimEnd('\\') + "\\"; }
			catch { }

			var files = new List<string>();
			int skipped = 0;
			for (int i = 0; i < real.Length; i++)
			{
				string full = real[i];
				if (string.IsNullOrEmpty(full)) { skipped++; continue; }
				if (full.ToLowerInvariant().StartsWith(WinDir + "\\")) { skipped++; continue; }
				if (!File.Exists(full)) { skipped++; continue; }
				if (scope != null)
				{
					bool inScope = full.StartsWith(scope, StringComparison.OrdinalIgnoreCase)
					            || srcFiles[i].StartsWith(scope, StringComparison.OrdinalIgnoreCase);
					if (!inScope) { skipped++; continue; }
				}
				files.Add(full);
			}
			if (skipped > 0) Log("安全过滤：跳过 " + skipped + " 个文件");
			if (files.Count == 0) throw new Exception("没有可搬的文件。");

			string root = Path.GetPathRoot(files[0]);
			var use = new List<string>();
			foreach (string f in files)
			{
				if (string.Equals(Path.GetPathRoot(f), root, StringComparison.OrdinalIgnoreCase)) use.Add(f);
			}
			if (use.Count == 0) throw new Exception("没有可搬的文件。");

			uint spc, bps, fr, tot;
			if (!GetDiskFreeSpaceW(root, out spc, out bps, out fr, out tot)) throw new Exception("读不到卷信息");
			long clusterSize = (long)spc * bps;

			var cpf = new long[use.Count];
			long need = 0;
			for (int i = 0; i < use.Count; i++)
			{
				long len = 0;
				try { len = new FileInfo(use[i]).Length; } catch { }
				cpf[i] = (len + clusterSize - 1) / clusterSize;
				if (cpf[i] <= 0) cpf[i] = 1;
				need += cpf[i];
			}

			SafeFileHandle vh = CreateFileW(@"\\.\" + root.TrimEnd('\\'), GENERIC_READ | 0x40000000,
				FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
			if (vh.IsInvalid) throw new Exception("打开卷失败（需要管理员）");
			try
			{
				var runs = new List<long[]>();
				if (onlyForward)
				{
					// 「当前位置」用【按数据量加权的中心】，不能用最小 LCN ——
					// 一个文件夹往往横跨整块盘（实测有 0.03%~99.87% 的），
					// 只要有一个文件在最前面，最小 LCN 就永远是 0%，永远挪不动。
					long curMin = long.MaxValue;
					double wSum = 0.0, wLen = 0.0;
					foreach (string one in use)
					{
						long l0, c0;
						if (TryExtent(one, out l0, out c0) && l0 >= 0 && c0 > 0)
						{
							if (l0 < curMin) curMin = l0;
							wSum += (l0 + c0 / 2.0) * (double)c0;
							wLen += (double)c0;
						}
					}
					double curPct = (wLen > 0.0) ? (wSum / wLen * 100.0 / Math.Max(1.0, (double)tot)) : 0.0;
					long anchor = (wLen > 0.0) ? (long)(wSum / wLen) : tot;
					Log(string.Format("当前位置：按数据量加权约 {0:N1}% 处（最靠前的一个文件在 {1:N1}%）",
						curPct, (curMin == long.MaxValue ? 0.0 : (double)curMin * 100.0 / Math.Max(1.0, (double)tot))));
					if (curPct < 3.0)
					{
						throw new Exception(string.Format(
							"这个文件夹已经基本在磁盘最前面了（按数据量算约 {0:N1}% 处），前面没有更靠外的空间，没必要再挪。",
							curPct));
					}
					long start = FindFreeRunBefore(vh, tot, need, anchor);
					if (start < 0)
					{
						throw new Exception(string.Format(
							"「{0:N1}% 处」之前没有装得下的连续空闲区：需要 {1:N1} MB。\n前面空位太碎，或者总量太大。",
							curPct, (double)need * (double)clusterSize / 1048576.0));
					}
					runs.Add(new long[] { start, need });
					Log(string.Format("整块连续区：LCN {0:N0} 起（约 {1:N1}% 处，在当前位置之前），0 次额外跳转",
						start, (double)start * 100.0 / Math.Max(1.0, (double)tot)));
				}
				else
				{
					runs = FindFreeRuns(vh, tot, need, 32, 256);
					if (runs.Count == 0) throw new Exception("找不到空闲区。");
				}

				long totalFree = 0;
				foreach (long[] r in runs) totalFree += r[1];
				double freeBytes2 = (double)(totalFree * clusterSize);
				double needBytes2 = (double)(need * clusterSize);
				if (freeBytes2 < 1073741824.0 || needBytes2 < 1073741824.0)
				{
					Log(string.Format("找到 {0} 个空闲区，共 {1:N1} MB（需要 {2:N1} MB）",
						runs.Count, freeBytes2 / 1048576.0, needBytes2 / 1048576.0));
				}
				else
				{
					Log(string.Format("找到 {0} 个空闲区，共 {1:N1} GB（需要 {2:N1} GB）",
						runs.Count, freeBytes2 / 1073741824.0, needBytes2 / 1073741824.0));
				}

				// ---------- 预演：先算出哪些文件放不下，让用户决定 ----------
				// 不这么做的话，会"悄悄跳过几个、搬了剩下的"，最后布局是半乱的。
				{
					int riPre = 0;
					long offPre = 0;
					var wontFit = new List<string>();
					for (int i = 0; i < use.Count; i++)
					{
						long nPre = cpf[i];
						int sRi = riPre;
						long sOff = offPre;
						while (riPre < runs.Count && runs[riPre][1] - offPre < nPre)
						{
							riPre++;
							offPre = 0;
						}
						if (riPre >= runs.Count)
						{
							riPre = sRi;
							offPre = sOff;
							wontFit.Add(use[i]);
						}
						else
						{
							offPre += nPre;
						}
					}
					if (wontFit.Count > 0)
					{
						if (wontFit.Count >= use.Count)
						{
							throw new Exception(string.Format(
								"一个都放不下：最大的连续空闲区放不进任何一个文件（共 {0} 个，最大 {1:N1} MB）。\n\n" +
								"先把外圈腾一点地方，或者用「内迁」搬到卷末尾。",
								use.Count, MaxFileMb(use)));
						}
						StringBuilder sbq = new StringBuilder();
						sbq.AppendLine(string.Format("有 {0:N0} 个文件放不下（没有足够大的连续空位）：", wontFit.Count));
						int showN = Math.Min(6, wontFit.Count);
						for (int i = 0; i < showN; i++)
						{
							double mbOne = 0.0;
							try { mbOne = new FileInfo(wontFit[i]).Length / 1048576.0; } catch { }
							sbq.AppendLine(string.Format("    {0}   {1:N1} MB", Path.GetFileName(wontFit[i]), mbOne));
						}
						if (wontFit.Count > showN)
						{
							sbq.AppendLine(string.Format("    …… 还有 {0:N0} 个", wontFit.Count - showN));
						}
						sbq.AppendLine();
						sbq.AppendLine("继续 → 只搬放得下的，这些保持原位（布局会变成「搬了一半」）");
						sbq.AppendLine("取消 → 一个都不动，先去腾地方");
						sbq.AppendLine();
						sbq.Append("怎么处理？");
						string askText = sbq.ToString();
						DialogResult ans = DialogResult.No;
						if (InvokeRequired)
						{
							Invoke((Action)delegate
							{
								ans = MessageBox.Show(askText, "有文件放不下", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
							});
						}
						else
						{
							ans = MessageBox.Show(askText, "有文件放不下", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
						}
						if (ans != DialogResult.Yes)
						{
							throw new Exception("已取消（有文件放不下，没有搬任何东西）。");
						}
						Log(string.Format("用户选择继续：{0:N0} 个文件会保持原位。", wontFit.Count));
					}
				}

				var manifest = new StringBuilder();
				manifest.AppendLine("file,old_lcn,new_lcn,clusters");
				int ri = 0;
				long off = 0;
				int done = 0, failed = 0;
				int tooBig = 0;
				long doneMb = 0;
				long totalMb = need * clusterSize / 1048576;
				for (int i = 0; i < use.Count; i++)
				{
					long n = cpf[i];
					int savedRi = ri;
					long savedOff = off;
					while (ri < runs.Count && runs[ri][1] - off < n)
					{
						ri++;
						off = 0;
					}
					if (ri >= runs.Count)
					{
						// 找不到足够大的连续区（通常文件本身太大）：跳过它，继续试后面的小文件
						ri = savedRi;
						off = savedOff;
						tooBig++;
						if (tooBig <= 5)
						{
							Log(string.Format("放不下（需要 {0:N1} MB 连续区），跳过: {1}", n * clusterSize / 1048576.0, Path.GetFileName(use[i])));
						}
						continue;
					}
					long target = runs[ri][0] + off;
					long oldLcn = -1, oldCnt;
					TryExtent(use[i], out oldLcn, out oldCnt);
					bool moved = MoveFile(vh, use[i], 0, target, n);
					if (moved) done++;
					else failed++;
					manifest.AppendLine(string.Format("{0},{1},{2},{3}",
						"\"" + use[i].Replace("\"", "\"\"") + "\"", oldLcn, target, n));
					off += n;
					doneMb += n * clusterSize / 1048576;
					if (progress != null) progress((double)doneMb * 100.0 / Math.Max(1.0, totalMb), i + 1, use.Count, doneMb, totalMb);
					if (_cancel)
					{
						Log(string.Format("已取消：搬完 {0:N0} 个后停下，剩下的没动。", done));
						break;
					}
				}

				try { File.WriteAllText(manifestPath, manifest.ToString(), new UTF8Encoding(false)); }
				catch (Exception ex) { Log("清单写入失败: " + ex.Message); }
				try { AppendRegistry(registryKey, use); }
				catch { }

				Log(string.Format("填到前部完成：搬了 {0:N0} 个，失败 {1:N0} 个，用了 {2:N0} 个空闲区", done, failed, ri + 1));
			}
			finally
			{
				vh.Dispose();
			}
		}

		/// <summary>从 LCN 0 往上收集所有空闲区（每个至少 8 簇，避免全是碎渣），累计够 need 就停</summary>
		/// <summary>按"卷的百分比位置"估顺序读速度（MB/s）。
		/// 来自实测：0%=122、50%=105、90%=74、99%=62，用一条直线近似。</summary>
		/// <summary>按"卷的百分比位置"估顺序读速度（MB/s）。
		/// 用实测点插值，不用直线 —— 实测曲线"前段平、后段陡"，
		/// 直线在 50% 处会低估约 12 MB/s，影响"整块 vs 拼洞"的判断。
		/// 实测：0%=122.2、10%=121.1、25%=116.3、50%=104.8、75%=85.5、90%=74.4、99%=62.3</summary>
		private static double SpeedAtPct(double pct)
		{
			double[] at = { 0.0, 10.0, 25.0, 50.0, 75.0, 90.0, 99.0 };
			double[] mv = { 122.2, 121.1, 116.3, 104.8, 85.5, 74.4, 62.3 };
			if (pct <= at[0]) return mv[0];
			if (pct >= at[at.Length - 1]) return mv[mv.Length - 1];
			for (int i = 1; i < at.Length; i++)
			{
				if (pct <= at[i])
				{
					double k = (pct - at[i - 1]) / (at[i] - at[i - 1]);
					return mv[i - 1] + (mv[i] - mv[i - 1]) * k;
				}
			}
			return mv[mv.Length - 1];
		}
		/// <summary>在 LCN 0 到 beforeLcn 之间，找第一块装得下 need 的连续空闲区。
		/// 要求整块都落在 beforeLcn 之前（这样挪完之后一定比原来更靠外圈）。找不到返回 -1。</summary>
		private static long FindFreeRunBefore(SafeFileHandle vh, long totalClusters, long need, long beforeLcn)
		{
			if (need <= 0 || beforeLcn <= need) return -1L;
			int outSize = (int)(totalClusters / 8) + 64;
			IntPtr buf = Marshal.AllocHGlobal(outSize);
			try
			{
				IntPtr ib = Marshal.AllocHGlobal(8);
				Marshal.WriteInt64(ib, 0, 0);
				int bytes;
				bool ok = DeviceIoControl(vh, FSCTL_GET_VOLUME_BITMAP, ib, 8, buf, outSize, out bytes, IntPtr.Zero);
				Marshal.FreeHGlobal(ib);
				if (!ok) return -1L;
				long bitmapBytes = Marshal.ReadInt64(buf, 8);
				if (bitmapBytes <= 0 || bitmapBytes > outSize - 16) bitmapBytes = outSize - 16;
				var bits = new byte[bitmapBytes];
				Marshal.Copy(new IntPtr(buf.ToInt64() + 16), bits, 0, (int)bitmapBytes);
				long limit = Math.Min(Math.Min(totalClusters, beforeLcn), bitmapBytes * 8);
				long run = 0;
				long start = 0;
				for (long c = 0; c < limit; c++)
				{
					if (((bits[c >> 3] >> (int)(c & 7)) & 1) == 0)
					{
						if (run == 0) start = c;
						run++;
						if (run >= need) return start;
					}
					else
					{
						run = 0;
					}
				}
				return -1L;
			}
			finally
			{
				Marshal.FreeHGlobal(buf);
			}
		}
		private static List<long[]> FindFreeRuns(SafeFileHandle vh, long totalClusters, long need, int maxRuns, long minRunClusters)
		{
			var list = new List<long[]>();
			int outSize = (int)(totalClusters / 8) + 64;
			IntPtr buf = Marshal.AllocHGlobal(outSize);
			try
			{
				IntPtr ib = Marshal.AllocHGlobal(8);
				Marshal.WriteInt64(ib, 0, 0);
				int bytes;
				bool ok = DeviceIoControl(vh, FSCTL_GET_VOLUME_BITMAP, ib, 8, buf, outSize, out bytes, IntPtr.Zero);
				Marshal.FreeHGlobal(ib);
				if (!ok) return list;
				long bitmapBytes = Marshal.ReadInt64(buf, 8);
				if (bitmapBytes <= 0 || bitmapBytes > outSize - 16) bitmapBytes = outSize - 16;
				var bits = new byte[bitmapBytes];
				Marshal.Copy(new IntPtr(buf.ToInt64() + 16), bits, 0, (int)bitmapBytes);
				long clusters = Math.Min(totalClusters, bitmapBytes * 8);

				long run = 0;
				long start = 0;
				long got = 0;
				for (long c = 0; c < clusters; c++)
				{
					if (((bits[c >> 3] >> (int)(c & 7)) & 1) == 0)
					{
						if (run == 0) start = c;
						run++;
					}
					else
					{
						// 只收"够大的洞"：太小的洞用上，多一次跳转，不值得
						if (run >= minRunClusters && list.Count < maxRuns)
						{
							list.Add(new long[2] { start, run });
							got += run;
						}
						run = 0;
					}
					if (got >= need) break;
					if (list.Count >= maxRuns) break;
				}
				if (run >= minRunClusters && got < need && list.Count < maxRuns) list.Add(new long[2] { start, run });
				return list;
			}
			finally
			{
				Marshal.FreeHGlobal(buf);
			}
		}

		private void EvictCore(List<string> files)
		{
			string manifestPath = Path.Combine(DataDir, "cold_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".manifest.csv");
			string registryKey = "COLD#" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
			SetStatus("正在内迁到内圈…");
			Apply("冷数据内迁", files, manifestPath, registryKey, delegate(double pct, int done, int totalN, long mb, long totalMb)
			{
				BeginInvoke((Action)delegate
				{
					ShowMoveProgress(pct, mb, totalMb);
				});
			}, true, 1);
			Log("冷数据内迁完成，外圈空间已腾出。");
			SetStatus("内迁完成");
		}

		private void DoLayout()
		{
			if (!_built)
			{
				return;
			}
			int num = base.ClientSize.Width;
			int num2 = base.ClientSize.Height;
			int num3 = Math.Max(0, num2 - 700);
			// 右上角按钮从右往左排：浏览 → 说明 → 历史记录 → 诊断
			// （以前"诊断"算到 num-408，正好压在路径框上，看起来像个空白按钮）
			btnBrowse.Width = 96;
			btnBrowse.Left = num - 12 - btnBrowse.Width;
			btnHelp.Left = btnBrowse.Left - 6 - 50;
			btnHistory.Left = btnHelp.Left - 6 - 90;
			btnDiag.Left = btnHistory.Left - 6 - 60;
			txtDir.Width = Math.Max(160, btnDiag.Left - 6 - 102);
			lv.Width = Math.Max(200, num - 96);
			lv.Height = 240 + num3;
			int num4 = 364 + num3;
			btnAnalyze.Top = num4;
			btnApply.Top = num4;
			btnGlobal.Top = num4;
			btnScan.Top = num4;
			btnDrift.Top = num4;
			btnRollback.Top = num4;
			btnMap.Top = num4;
			btnPack.Top = num4;
			btnCold.Top = num4;
			// 进度百分比挪到进度条同一行，右侧对齐
			lblPct.Top = 404 + num3;
			lblPct.Left = num - 246;
			lblPct.Width = 136;
			pb.Top = 402 + num3;
			pb.Width = Math.Max(200, num - 334);
			btnCancel.Left = num - 104;
			btnCancel.Top = 398 + num3;
			foreach (Control control in base.Controls)
			{
				if (control.Name == "lblDefragTag")
				{
					control.Top = 438 + num3;
				}
			}
			btnDefragOff.Top = 432 + num3;
			btnDefragOn.Top = 432 + num3;
			lblDefrag.Top = 438 + num3;
			lblDefrag.Width = Math.Max(120, num - 434 - 12);
			txtLog.Top = 474 + num3;
			txtLog.Width = Math.Max(200, num - 96);
			txtLog.Height = 206 + num3;
		}

		internal static List<string> SafeFiles(string root, int cap)
		{
			List<string> list = new List<string>();
			if (string.IsNullOrEmpty(root))
			{
				return list;
			}
			Stack<string> stack = new Stack<string>();
			stack.Push(root);
			while (stack.Count > 0 && list.Count < cap)
			{
				string path = stack.Pop();
				try
				{
					string[] files = Directory.GetFiles(path);
					foreach (string item in files)
					{
						list.Add(item);
						if (list.Count >= cap)
						{
							break;
						}
					}
				}
				catch
				{
				}
				try
				{
					string[] directories = Directory.GetDirectories(path);
					foreach (string text in directories)
					{
						string fileName = Path.GetFileName(text);
						if (string.Equals(fileName, "$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) || string.Equals(fileName, "System Volume Information", StringComparison.OrdinalIgnoreCase))
						{
							continue;
						}
						try
						{
							FileAttributes attributes = File.GetAttributes(text);
							if ((attributes & FileAttributes.ReparsePoint) != 0)
							{
								continue;
							}
						}
						catch
						{
							continue;
						}
						stack.Push(text);
					}
				}
				catch
				{
				}
			}
			return list;
		}

		/// <summary>
		/// 块排序的局部搜索：试相邻两块交换，看所有段的总跳转次数是否减少。
		/// 一次跳转 ≈ 一次寻道（十几毫秒），所以少一次就是赚一次。
		/// </summary>
		private void OptimizeBlockOrder(List<KeyValuePair<string, List<string>>> blocks, List<Segment> segs)
		{
			if (blocks.Count < 3 || segs == null || segs.Count == 0)
			{
				return;
			}
			Dictionary<string, int> fileBlock = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < blocks.Count; i++)
			{
				foreach (string f in blocks[i].Value)
				{
					fileBlock[f] = i;
				}
			}
			List<int[]> seqs = new List<int[]>();
			foreach (Segment s in segs)
			{
				List<int> l = new List<int>();
				int last = -2;
				foreach (string f in s.Files)
				{
					int b;
					if (!fileBlock.TryGetValue(f, out b)) continue;
					if (b != last) { l.Add(b); last = b; }
				}
				if (l.Count > 0) seqs.Add(l.ToArray());
			}
			if (seqs.Count == 0)
			{
				return;
			}

			int[] order = new int[blocks.Count];
			for (int i = 0; i < order.Length; i++)
			{
				order[i] = i;
			}
			int best = CountRuns(order, seqs);
			int before = best;
			for (int pass = 0; pass < 4; pass++)
			{
				bool improved = false;
				for (int i = 0; i + 1 < order.Length; i++)
				{
					int t = order[i];
					order[i] = order[i + 1];
					order[i + 1] = t;
					int c = CountRuns(order, seqs);
					if (c < best)
					{
						best = c;
						improved = true;
					}
					else
					{
						t = order[i];
						order[i] = order[i + 1];
						order[i + 1] = t;
					}
				}
				if (!improved)
				{
					break;
				}
			}

			if (best < before)
			{
				List<KeyValuePair<string, List<string>>> reordered = new List<KeyValuePair<string, List<string>>>();
				foreach (int idx in order)
				{
					reordered.Add(blocks[idx]);
				}
				blocks.Clear();
				blocks.AddRange(reordered);
				Log(string.Format("块顺序优化：总跳转 {0} → {1} 次（少 {2} 次寻道）", before, best, before - best));
			}
			else
			{
				Log(string.Format("块顺序优化：已经是 {0} 次跳转，没有更好的排法", before));
			}
		}

		private static int CountRuns(int[] order, List<int[]> seqs)
		{
			int[] pos = new int[order.Length];
			for (int p = 0; p < order.Length; p++)
			{
				pos[order[p]] = p;
			}
			int total = 0;
			foreach (int[] seq in seqs)
			{
				int runs = 1;
				for (int i = 1; i < seq.Length; i++)
				{
					if (pos[seq[i]] != pos[seq[i - 1]] + 1)
					{
						runs++;
					}
				}
				total += runs;
			}
			return total;
		}

		private void FillList()
		{
			if (_segs.Count == 0)
			{
				return;
			}
			lv.BeginUpdate();
			lv.Items.Clear();
			foreach (Segment seg in _segs)
			{
				double totalSeconds = (seg.End - seg.Start).TotalSeconds;
				string text = ((seg.Gain < 0.0) ? "—" : (seg.Gain.ToString("N1") + "%"));
				string text2 = ((seg.Gain < 0.0 || seg.TimeA <= 0.0) ? "—" : ((seg.TimeA - seg.TimeB).ToString("N1") + " 秒"));
				string text3 = ((seg.Files.Count >= 10 && seg.Bytes >= 10485760) ? ((!(seg.Gain < 0.0)) ? ((!(seg.Gain >= 20.0)) ? ((!(seg.Gain >= 10.0)) ? "不值得" : "有空间") : "值得做") : "未分析") : "太小，忽略");
				ListViewItem value = new ListViewItem(new string[8]
				{
					seg.Index.ToString(),
					(seg.Start - _segs[0].Start).ToString("mm\\:ss"),
					totalSeconds.ToString("N1") + "s",
					seg.Files.Count.ToString("N0"),
					((double)seg.Bytes / 1048576.0).ToString("N1") + " MB",
					text,
					text2,
					text3
				});
				lv.Items.Add(value);
			}
			lv.EndUpdate();
		}

		private void Measure(List<string> files, out double a, out double b, Action<double, string> progress)
		{
			List<KeyValuePair<string, long>> list = new List<KeyValuePair<string, long>>();
			List<string> missing = new List<string>();
			int idx = 0;
			foreach (string file in files)
			{
				if (!File.Exists(file))
				{
					if (missing.Count < 200) missing.Add(file);
					continue;
				}
				idx++;
				if (progress != null && idx % 64 == 0)
				{
					progress(0.05 * idx / Math.Max(1, files.Count), string.Format("定位文件 {0:N0}/{1:N0}", idx, files.Count));
				}
				long lcn;
				long clusters;
				if (TryExtent(file, out lcn, out clusters) && lcn >= 0)
				{
					list.Add(new KeyValuePair<string, long>(file, lcn));
				}
			}
			if (missing.Count > 0)
			{
				Log(string.Format("注意：有 {0} 个文件已经不存在（游戏更新 / 临时文件 / 被清理），已从分析中排除。", missing.Count));
				int shown = Math.Min(3, missing.Count);
				for (int k = 0; k < shown; k++) Log("    例：" + missing[k]);
				if (missing.Count > shown) Log("    （其余 " + (missing.Count - shown) + " 个略）");
				Log("    数量多的话说明录制之后游戏更新过，建议重新录一次再分析。");
			}
			if (list.Count < 5)
			{
				a = 0.0;
				b = 0.0;
				return;
			}
			List<KeyValuePair<string, long>> list2 = new List<KeyValuePair<string, long>>(list);
			list2.Sort((KeyValuePair<string, long> x, KeyValuePair<string, long> y) => x.Value.CompareTo(y.Value));
			List<string> list3 = new List<string>();
			foreach (KeyValuePair<string, long> item in list)
			{
				list3.Add(item.Key);
			}
			List<string> list4 = new List<string>();
			foreach (KeyValuePair<string, long> item2 in list2)
			{
				list4.Add(item2.Key);
			}
			a = ReadAll(list3, progress, 0.05, 0.475, "按录制顺序");
			if (_cancel) { b = 0.0; return; }
			b = ReadAll(list4, progress, 0.525, 0.475, "按物理顺序");
		}

		private double ReadAll(List<string> files, Action<double, string> progress, double lo, double span, string label)
		{
			IntPtr hglobal = Marshal.AllocHGlobal(1056768);
			long num = hglobal.ToInt64();
			IntPtr buf = new IntPtr((num + 4095) & -4096);
			try
			{
				OnePass(files, buf, null);
				List<double> list = new List<double>();
				for (int i = 0; i < 2; i++)
				{
					if (_cancel)
					{
						throw new Exception("用户取消");
					}
					int passNo = i;
					Stopwatch stopwatch = Stopwatch.StartNew();
					OnePass(files, buf, delegate(int done, int total)
					{
						if (progress != null)
						{
							double pct = ((double)passNo + (double)done / Math.Max(1, total)) / 2.0;
							progress(lo + span * pct, string.Format("{0} 第 {1}/2 遍 {2:N0}/{3:N0}", label, passNo + 1, done, total));
						}
					});
					stopwatch.Stop();
					list.Add(stopwatch.Elapsed.TotalSeconds);
				}
				list.Sort();
				return list[list.Count / 2];
			}
			finally
			{
				Marshal.FreeHGlobal(hglobal);
			}
		}

		private void OnePass(List<string> files, IntPtr buf, Action<int, int> cb)
		{
			int done = 0;
			foreach (string file in files)
			{
				if (_cancel)
				{
					throw new Exception("用户取消");
				}
				done++;
				if (cb != null && (done % 32 == 0 || done == files.Count))
				{
					cb(done, files.Count);
				}
				SafeFileHandle safeFileHandle = CreateFileW(file, 2147483648u, 3u, IntPtr.Zero, 3u, 536870912u, IntPtr.Zero);
				if (safeFileHandle.IsInvalid)
				{
					continue;
				}
				try
				{
					long num;
					try
					{
						num = new FileInfo(file).Length;
					}
					catch
					{
						goto end_IL_003a;
					}
					while (num > 0)
					{
						long num2 = Math.Min(1048576L, num);
						num2 = (num2 + 4095) / 4096 * 4096;
						uint got;
						if (ReadFile(safeFileHandle, buf, (uint)num2, out got, IntPtr.Zero) && got != 0)
						{
							num -= got;
							continue;
						}
						break;
					}
					end_IL_003a:;
				}
				finally
				{
					safeFileHandle.Dispose();
				}
			}
		}

		private void Apply(string label, List<string> srcFiles, string manifestPath, string registryKey, Action<double, int, int, long, long> progress)
		{
			Apply(label, srcFiles, manifestPath, registryKey, progress, false, 5);
		}

		private void Apply(string label, List<string> srcFiles, string manifestPath, string registryKey, Action<double, int, int, long, long> progress, bool preferInner)
		{
			Apply(label, srcFiles, manifestPath, registryKey, progress, preferInner, 5);
		}

		/// <summary>minFiles：过滤后至少还剩几个才继续（冷数据内迁允许只搬 1 个）</summary>
		private void Apply(string label, List<string> srcFiles, string manifestPath, string registryKey, Action<double, int, int, long, long> progress, bool preferInner, int minFiles)
		{
			string[] array = new string[srcFiles.Count];
			int num = 0;
			for (int i = 0; i < srcFiles.Count; i++)
			{
				array[i] = ResolvePath(srcFiles[i]);
				if (!string.Equals(array[i], srcFiles[i], StringComparison.OrdinalIgnoreCase))
				{
					num++;
				}
			}
			if (num > 0)
			{
				Log(string.Format("检测到 {0:N0} 个文件经由链接访问，已解析到真实位置（例如 {1}）", num, array[0]));
			}
			string pathRoot = Path.GetPathRoot(array[0]);
			string text = null;
			try
			{
				if (!string.IsNullOrEmpty(_scope))
				{
					text = Path.GetFullPath(_scope).TrimEnd('\\') + "\\";
				}
			}
			catch
			{
			}
			List<string> list = new List<string>();
			int num2 = 0;
			for (int j = 0; j < array.Length; j++)
			{
				string text2 = array[j];
				if (string.IsNullOrEmpty(text2))
				{
					num2++;
				}
				else if (!string.Equals(Path.GetPathRoot(text2), pathRoot, StringComparison.OrdinalIgnoreCase))
				{
					num2++;
				}
				else if (text2.ToLowerInvariant().StartsWith(WinDir + "\\"))
				{
					num2++;
				}
				else if (text != null && !_bypassScope && !text2.StartsWith(text, StringComparison.OrdinalIgnoreCase) && !srcFiles[j].StartsWith(text, StringComparison.OrdinalIgnoreCase))
				{
					num2++;
				}
				else
				{
					list.Add(text2);
				}
			}
			if (num2 > 0)
			{
				Log("安全过滤：跳过 " + num2 + " 个文件（不在选定目录 / 跨卷 / Windows 目录）");
			}
			if (list.Count < minFiles)
			{
				throw new Exception("过滤后只剩 " + list.Count + " 个文件，不值得搬。");
			}
			if (text == null)
			{
				Log("⚠\ufe0f 没有选择目标目录：只会排除 Windows 目录，其他同卷文件都会被动。建议先选目录。");
			}
			uint spc;
			uint bps;
			uint fr;
			uint tot;
			if (!GetDiskFreeSpaceW(pathRoot, out spc, out bps, out fr, out tot))
			{
				throw new Exception("读不到卷信息");
			}
			long num3 = (long)spc * (long)bps;
			long[] array2 = new long[list.Count];
			long num4 = 0L;
			for (int k = 0; k < list.Count; k++)
			{
				long num5 = 0L;
				try
				{
					num5 = new FileInfo(list[k]).Length;
				}
				catch
				{
				}
				array2[k] = (num5 + num3 - 1) / num3;
				num4 += array2[k];
			}
			SafeFileHandle safeFileHandle = CreateFileW("\\\\.\\" + pathRoot.TrimEnd('\\'), 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (safeFileHandle.IsInvalid)
			{
				throw new Exception("打开卷失败（需要管理员）");
			}
			long num6 = FindFreeRun(safeFileHandle, tot, num4, preferInner);
			if (num6 < 0)
			{
				throw new Exception(string.Format("找不到 {0:N0} MB 连续空闲区", (double)(num4 * num3) / 1048576.0));
			}
			double num7 = ((tot != 0) ? ((double)num6 * 100.0 / (double)tot) : 0.0);
			Log(string.Format("目标区：LCN {0:N0} 起，约在卷的 {1:N1}% 处（越靠前越接近外圈，速度越快）", num6, num7));
			double num8 = -1.0;
			try
			{
				long num9 = 0L;
				long num10 = 0L;
				foreach (string item in list)
				{
					long lcn;
					long clusters;
					if (TryExtent(item, out lcn, out clusters) && lcn >= 0)
					{
						long num11 = clusters * num3;
						num9 += num11;
						num10 += lcn * num11;
					}
				}
				if (num9 > 0)
				{
					num8 = (double)num10 / (double)num9 * 100.0 / (double)tot;
				}
			}
			catch
			{
			}
			if (num8 >= 0.0 && num7 > num8 + 10.0)
			{
				string text3 = string.Format("注意：这次会把这些文件搬到更靠内圈的位置。\n\n    现在的位置：约 {0:N1}% 处\n    目标的位置：约 {1:N1}% 处\n\n机械盘外圈快、内圈慢（典型差 1.5–2 倍）。\n越靠后越慢，所以搬过去可能【整体变慢】，而不是变快。\n\n只有在「重新排序带来的收益」大于「换到慢区的损失」时才值得搬。\n\n仍然继续吗？", num8, num7);
				if (MessageBox.Show(text3, "目标区比现在更靠内圈", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
				{
					throw new Exception("已取消（目标区比当前位置更靠内圈）。");
				}
			}
			long num12 = 0L;
			long num13 = 0L;
			long arg = num4 * num3 / 1048576;
			int num14 = 0;
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("file,old_lcn,new_lcn,clusters");
			for (int l = 0; l < list.Count; l++)
			{
				long lcn2 = -1L;
				long clusters2;
				TryExtent(list[l], out lcn2, out clusters2);
				long num15 = num6 + num12;
				if (!MoveFile(safeFileHandle, list[l], 0L, num15, array2[l]))
				{
					num14++;
				}
				stringBuilder.AppendLine(string.Format("{0},{1},{2},{3}", "\"" + list[l].Replace("\"", "\"\"") + "\"", lcn2, num15, array2[l]));
				num12 += array2[l];
				num13 += array2[l] * num3 / 1048576;
				progress((double)(l + 1) * 100.0 / (double)list.Count, l + 1, list.Count, num13, arg);
			}
			try
			{
				File.WriteAllText(manifestPath, stringBuilder.ToString(), new UTF8Encoding(false));
				Log("搬家清单已写入: " + manifestPath);
			}
			catch (Exception ex)
			{
				Log("清单写入失败: " + ex.Message);
			}
			try
			{
				AppendRegistry(registryKey, list);
				Log("已记入优化历史（以后重排别的段会检查共享冲突）");
			}
			catch (Exception ex2)
			{
				Log("记录优化历史失败: " + ex2.Message);
			}
			if (num14 > 0)
			{
				Log("⚠\ufe0f 有 " + num14 + " 个文件搬移失败（可能被占用）");
			}
			int num16 = 0;
			num12 = 0L;
			for (int m = 0; m < list.Count; m++)
			{
				long lcn3;
				long clusters3;
				if (TryExtent(list[m], out lcn3, out clusters3) && lcn3 == num6 + num12)
				{
					num16++;
				}
				num12 += array2[m];
			}
			Log(string.Format("校验：{0:N0}/{1:N0} 个文件落在预期位置", num16, list.Count));
		}

		private static bool MoveFile(SafeFileHandle vh, string path, long vcn, long lcn, long count)
		{
			SafeFileHandle safeFileHandle = CreateFileW(path, 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (safeFileHandle.IsInvalid)
			{
				return false;
			}
			IntPtr intPtr = Marshal.AllocHGlobal(32);
			Marshal.WriteInt64(intPtr, 0, safeFileHandle.DangerousGetHandle().ToInt64());
			Marshal.WriteInt64(intPtr, 8, vcn);
			Marshal.WriteInt64(intPtr, 16, lcn);
			Marshal.WriteInt32(intPtr, 24, (int)count);
			Marshal.WriteInt32(intPtr, 28, 0);
			int br;
			bool result = DeviceIoControl(vh, 589940u, intPtr, 32, IntPtr.Zero, 0, out br, IntPtr.Zero);
			Marshal.FreeHGlobal(intPtr);
			safeFileHandle.Dispose();
			return result;
		}

		private static long FindFreeRun(SafeFileHandle vh, long totalClusters, long need)
		{
			return FindFreeRun(vh, totalClusters, need, false);
		}

		private static long FindFreeRun(SafeFileHandle vh, long totalClusters, long need, bool preferInner)
		{
			int num = (int)(totalClusters / 8) + 64;
			IntPtr intPtr = Marshal.AllocHGlobal(num);
			try
			{
				IntPtr intPtr2 = Marshal.AllocHGlobal(8);
				Marshal.WriteInt64(intPtr2, 0, 0L);
				int br;
				bool flag = DeviceIoControl(vh, 589935u, intPtr2, 8, intPtr, num, out br, IntPtr.Zero);
				Marshal.FreeHGlobal(intPtr2);
				if (!flag)
				{
					return -1L;
				}
				long num2 = Marshal.ReadInt64(intPtr, 8);
				if (num2 <= 0 || num2 > num - 16)
				{
					num2 = num - 16;
				}
				byte[] array = new byte[num2];
				Marshal.Copy(new IntPtr(intPtr.ToInt64() + 16), array, 0, (int)num2);
				long num3 = Math.Min(totalClusters, num2 * 8);
				long num4 = 0L;
				long result = -1L;
				if (!preferInner)
				{
					for (long num5 = 0L; num5 < num3; num5++)
					{
						if (((array[num5 >> 3] >> (int)(num5 & 7)) & 1) == 0)
						{
							if (num4 == 0)
							{
								result = num5;
							}
							num4++;
							if (num4 >= need)
							{
								return result;
							}
						}
						else
						{
							num4 = 0L;
						}
					}
				}
				else
				{
					// 从卷末尾往前找：找到连续 need 个空簇后，返回的必须是这一段的【起点】，
					// 不能是反向扫描时记住的末端（那样文件会从末端开始放，直接占到别人头上）
					for (long num6 = num3 - 1; num6 >= 0; num6--)
					{
						if (((array[num6 >> 3] >> (int)(num6 & 7)) & 1) == 0)
						{
							num4++;
							if (num4 >= need)
							{
								return num6;
							}
						}
						else
						{
							num4 = 0L;
						}
					}
				}
				return -1L;
			}
			finally
			{
				Marshal.FreeHGlobal(intPtr);
			}
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern SafeFileHandle CreateFileW(string n, uint a, uint s, IntPtr sa, uint d, uint f, IntPtr t);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool DeviceIoControl(SafeFileHandle h, uint c, IntPtr i, int isz, IntPtr o, int osz, out int br, IntPtr ov);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool ReadFile(SafeFileHandle h, IntPtr b, uint n, out uint got, IntPtr ov);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool GetDiskFreeSpaceW(string r, out uint spc, out uint bps, out uint fr, out uint tot);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern uint QueryDosDeviceW(string name, StringBuilder target, uint max);

		private static bool TryExtent(string path, out long lcn, out long clusters)
		{
			lcn = -1L;
			clusters = 0L;
			SafeFileHandle safeFileHandle = CreateFileW(path, 2147483648u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (safeFileHandle.IsInvalid)
			{
				return false;
			}
			IntPtr intPtr = Marshal.AllocHGlobal(8);
			Marshal.WriteInt64(intPtr, 0, 0L);
			int num = 4096;
			IntPtr intPtr2 = Marshal.AllocHGlobal(num);
			try
			{
				bool flag = false;
				for (int i = 0; i < 8; i++)
				{
					int br;
					if (DeviceIoControl(safeFileHandle, 589939u, intPtr, 8, intPtr2, num, out br, IntPtr.Zero))
					{
						flag = true;
						break;
					}
					if (Marshal.GetLastWin32Error() == 234 && br > num)
					{
						Marshal.FreeHGlobal(intPtr2);
						num = br;
						intPtr2 = Marshal.AllocHGlobal(num);
						continue;
					}
					return false;
				}
				if (!flag)
				{
					return false;
				}
				int num2 = Marshal.ReadInt32(intPtr2, 0);
				if (num2 <= 0)
				{
					return false;
				}
				// 结构：0=段数，8=起始 VCN，16 起每 16 字节一对 {NextVcn, Lcn}
				// 这里要的是「第一段起始 LCN」和「整个文件占多少簇」，
				// 所以必须把所有段累加 —— 只看第一段会让回滚只搬一部分。
				long startVcn = Marshal.ReadInt64(intPtr2, 8);
				long prevNext = startVcn;
				long total = 0L;
				long firstLcn = -1L;
				for (int i = 0; i < num2; i++)
				{
					int o = 16 + i * 16;
					long nextVcn = Marshal.ReadInt64(intPtr2, o);
					long lcnHere = Marshal.ReadInt64(intPtr2, o + 8);
					if (nextVcn > prevNext)
					{
						if (lcnHere >= 0)
						{
							if (firstLcn < 0) firstLcn = lcnHere;
							total += nextVcn - prevNext;
						}
						prevNext = nextVcn;
					}
				}
				lcn = firstLcn;
				clusters = total;
				return firstLcn >= 0;
			}
			finally
			{
				Marshal.FreeHGlobal(intPtr);
				Marshal.FreeHGlobal(intPtr2);
				safeFileHandle.Dispose();
			}
		}

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool GetVolumePathNameW(string lpszFileName, StringBuilder lpszVolumePathName, int cchBufferLength);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool GetVolumeNameForVolumeMountPointW(string lpszVolumeMountPoint, StringBuilder lpszVolumeName, int cchBufferLength);

		/// <summary>把一个普通路径解析成可以喂给 CreateFileW 的卷设备路径。
		/// 盘符根 → \\.\X: ；挂在文件夹上的卷 → \\?\Volume{guid}\。
		/// 网络路径 / 映射盘 / SUBST 会失败，此时给出明确原因。</summary>
		private static bool TryResolveVolumePath(string anyPath, out string devicePath, out string reason)
		{
			devicePath = null;
			reason = null;
			try
			{
				StringBuilder sb = new StringBuilder(1024);
				if (!GetVolumePathNameW(anyPath, sb, sb.Capacity))
				{
					reason = "GetVolumePathName 失败，错误码 " + Marshal.GetLastWin32Error()
						+ "（路径可能不在本机磁盘上，比如网络路径 / 映射盘 / SUBST）";
					return false;
				}
				string mount = sb.ToString();                 // 例如 "E:\" 或 "C:\Mount\Vol\"
				if (mount.Length >= 2 && mount[1] == ':' && mount.TrimEnd('\\').Length == 2)
				{
					devicePath = @"\\.\" + mount.TrimEnd('\\');   // \\.\E:
					return true;
				}
				// 不是盘符根：可能挂在文件夹上
				StringBuilder guid = new StringBuilder(1024);
				if (GetVolumeNameForVolumeMountPointW(mount, guid, guid.Capacity))
				{
					devicePath = guid.ToString();                 // \\?\Volume{...}\
					return true;
				}
				reason = "路径所在的卷没有盘符（挂在文件夹上），且取不到卷 GUID，错误码 " + Marshal.GetLastWin32Error();
				return false;
			}
			catch (Exception ex)
			{
				reason = ex.Message;
				return false;
			}
		}



		/// <summary>自检：对每个卷分别尝试"只读打开"和"读写打开"，把结果和错误码写出来。
		/// 用来区分是"权限没拿到"还是"安全软件拦了裸卷访问"。</summary>
		private static string VolumeAccessProbe(string root)
		{
			string dev;
			string why;
			if (!TryResolveVolumePath(root, out dev, out why)) return "解析失败：" + why;
			string r = "?";
			string w = "?";
			SafeFileHandle h1 = CreateFileW(dev, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
			if (h1.IsInvalid) r = "错" + Marshal.GetLastWin32Error();
			else { r = "OK"; h1.Dispose(); }
			SafeFileHandle h2 = CreateFileW(dev, GENERIC_READ | 0x40000000, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
			if (h2.IsInvalid) w = "错" + Marshal.GetLastWin32Error();
			else { w = "OK"; h2.Dispose(); }
			return string.Format("{0} → {1}　读:{2}　写:{3}", root, dev, r, w);
		}

		private static string VisibleVolumes()
		{
			StringBuilder sb = new StringBuilder();
			try
			{
				foreach (DriveInfo d in DriveInfo.GetDrives())
				{
					try
					{
						sb.Append(d.Name);
						sb.Append(d.IsReady ? ("(" + d.DriveType + " " + (d.TotalSize / 1073741824L) + "GB) ") : "(未就绪) ");
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
			return sb.Length == 0 ? "（一个都没有）" : sb.ToString().Trim();
		}
		private static string Normalize(string p)
		{
			if (string.IsNullOrEmpty(p))
			{
				return p;
			}
			if (DevMap.Count == 0)
			{
				StringBuilder stringBuilder = new StringBuilder(1024);
				for (char c = 'A'; c <= 'Z'; c = (char)(c + 1))
				{
					string text = c + ":";
					stringBuilder.Length = 0;
					if (QueryDosDeviceW(text, stringBuilder, (uint)stringBuilder.Capacity) != 0)
					{
						string text2 = stringBuilder.ToString();
						if (text2.StartsWith("\\Device\\", StringComparison.OrdinalIgnoreCase) && !DevMap.ContainsKey(text2))
						{
							DevMap[text2] = text;
						}
					}
				}
			}
			foreach (KeyValuePair<string, string> item in DevMap)
			{
				if (p.StartsWith(item.Key + "\\", StringComparison.OrdinalIgnoreCase))
				{
					return item.Value + "\\" + p.Substring(item.Key.Length + 1);
				}
			}
			return p;
		}

		private static long Hex(string s)
		{
			if (s == null)
			{
				return 0L;
			}
			s = s.Trim();
			if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			{
				s = s.Substring(2);
			}
			long result;
			if (!long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result))
			{
				return 0L;
			}
			return result;
		}

		[DllImport("advapi32.dll", SetLastError = true)]
		private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

		[DllImport("advapi32.dll", SetLastError = true)]
		private static extern bool GetTokenInformation(IntPtr token, int cls, ref int info, int len, out int retLen);

		[DllImport("kernel32.dll")]
		private static extern IntPtr GetCurrentProcess();

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool CloseHandle(IntPtr h);

		/// <summary>UAC 的“已提权”标记。关掉 UAC 的机器上它可能是 0，
		/// 所以它只能当“是管理员”的证据，不能当“不是管理员”的证据。</summary>
		/// <summary>写一行到 data\log.txt。界面上看不到的东西（权限、路径、错误码）都记在这里。</summary>
		private static void LogFile(string s)
		{
			try
			{
				string p = Path.Combine(DataDir, "log.txt");
				File.AppendAllText(p, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + s + "\r\n", new UTF8Encoding(false));
			}
			catch
			{
			}
		}
		private static bool ElevationFlag()
		{
			IntPtr token;
			if (!OpenProcessToken(GetCurrentProcess(), 8u, out token))
			{
				return false;
			}
			try
			{
				int info = 0;
				int retLen = 0;
				if (!GetTokenInformation(token, 20, ref info, 4, out retLen))
				{
					return false;
				}
				return info != 0;
			}
			finally
			{
				CloseHandle(token);
			}
		}

		/// <summary>是不是管理员：先看提权标记；标记为 0 时不能直接判“不是”，
		/// 因为 UAC 关闭（EnableLUA=0）的系统上进程有完整权限、标记仍是 0。
		/// 这时再看令牌里有没有启用的 Administrators 组。</summary>
		private static bool IsAdmin()
		{
			if (ElevationFlag())
			{
				return true;
			}
			if (InAdminGroup())
			{
				return true;
			}
			// 兜底：有些机器上两种判据都拿不到（UAC 关闭、令牌被裁剪），
			// 但我们真正要的是「能不能对卷下指令」，所以直接试着开一次本机卷。
			int perr;
			return VolumeProbeOk(out perr);
		}

		/// <summary>真的去开一次本机卷（读+写）。这个结果才是「能不能干活」的硬证据。</summary>
		private static bool VolumeProbeOk(out int err)
		{
			err = -1;
			try
			{
				string root = Path.GetPathRoot(Application.ExecutablePath);
				string dev;
				string why;
				if (root != null && TryResolveVolumePath(root, out dev, out why))
				{
					SafeFileHandle h = CreateFileW(dev, GENERIC_READ | 0x40000000,
						FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
					if (!h.IsInvalid)
					{
						h.Dispose();
						err = 0;
						return true;
					}
					err = Marshal.GetLastWin32Error();
				}
			}
			catch
			{
			}
			return false;
		}

		/// <summary>令牌里有没有「启用的 Administrators 组」（不看 UAC 标记）。</summary>
		private static bool InAdminGroup()
		{
			try
			{
				WindowsIdentity id = WindowsIdentity.GetCurrent();
				WindowsPrincipal pr = new WindowsPrincipal(id);
				return pr.IsInRole(WindowsBuiltInRole.Administrator);
			}
			catch
			{
				return false;
			}
		}

		private static int RunTool(string exe, string args, out string output)
		{
			ProcessStartInfo processStartInfo = new ProcessStartInfo(exe, args);
			processStartInfo.UseShellExecute = false;
			processStartInfo.RedirectStandardOutput = true;
			processStartInfo.RedirectStandardError = true;
			processStartInfo.CreateNoWindow = true;
			Process process = Process.Start(processStartInfo);
			string text = process.StandardOutput.ReadToEnd();
			string text2 = process.StandardError.ReadToEnd();
			process.WaitForExit();
			output = (text ?? "") + (text2 ?? "");
			return process.ExitCode;
		}
	}
	internal sealed class ListPickForm : Form
	{
		public string Selected;

		private ListBox lb;

		public ListPickForm(string title, List<string> items)
		{
			Text = title;
			base.ClientSize = new Size(700, 420);
			base.StartPosition = FormStartPosition.CenterParent;
			Font = new Font("Microsoft YaHei UI", 9f);
			lb = new ListBox();
			lb.SetBounds(12, 12, 676, 340);
			foreach (string item in items)
			{
				lb.Items.Add(item);
			}
			if (lb.Items.Count > 0)
			{
				lb.SelectedIndex = lb.Items.Count - 1;
			}
			lb.DoubleClick += delegate
			{
				Ok();
			};
			base.Controls.Add(lb);
			Button button = new Button();
			button.Text = "确定";
			button.SetBounds(478, 364, 100, 32);
			button.Click += delegate
			{
				Ok();
			};
			base.Controls.Add(button);
			Button button2 = new Button();
			button2.Text = "取消";
			button2.SetBounds(586, 364, 100, 32);
			button2.Click += delegate
			{
				base.DialogResult = DialogResult.Cancel;
				Close();
			};
			base.Controls.Add(button2);
		}

		private void Ok()
		{
			if (lb.SelectedIndex >= 0)
			{
				Selected = (string)lb.SelectedItem;
				base.DialogResult = DialogResult.OK;
				Close();
			}
		}
	}
	internal sealed class ColdPickForm : Form
	{
		public List<string> Selected = new List<string>();

		private ListView lv;
		private Label lblPath;
		private Label lblInfo;
		private Button btnOK;
		private string _dir;
		private bool _loading;

		public ColdPickForm(string root)
			: this(root, null, null)
		{
		}

		public ColdPickForm(string root, string title, string okText)
		{
			Text = (title == null ? "选择要挪到内圈的文件或文件夹" : title);
			try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
			base.ClientSize = new Size(840, 560);
			base.MinimumSize = new Size(720, 480);
			base.StartPosition = FormStartPosition.CenterParent;
			Font = new Font("Microsoft YaHei UI", 9f);
			lblPath = new Label();
			lblPath.SetBounds(12, 10, 586, 22);
			lblPath.ForeColor = Color.SteelBlue;
			base.Controls.Add(lblPath);
			Button button = new Button();
			button.Text = "上一级";
			button.SetBounds(606, 8, 80, 26);
			button.Click += delegate
			{
				GoUp();
			};
			base.Controls.Add(button);
			Button button2 = new Button();
			button2.Text = "选择文件夹...";
			button2.SetBounds(692, 8, 134, 26);
			button2.Click += delegate
			{
				using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
				{
					folderBrowserDialog.Description = "直接跳到某个文件夹";
					try
					{
						if (Directory.Exists(_dir))
						{
							folderBrowserDialog.SelectedPath = _dir;
						}
					}
					catch
					{
					}
					if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
					{
						Navigate(folderBrowserDialog.SelectedPath);
					}
				}
			};
			base.Controls.Add(button2);
			lv = new ListView();
			lv.SetBounds(12, 40, 814, 396);
			lv.View = View.Details;
			lv.CheckBoxes = true;
			lv.FullRowSelect = true;
			lv.GridLines = true;
			lv.Columns.Add("名称", 484);
			lv.Columns.Add("大小(MB)", 100);
			lv.Columns.Add("类型", 96);
			lv.DoubleClick += delegate
			{
				OpenSelected();
			};
			lv.ItemChecked += delegate
			{
				if (_loading)
				{
					return;
				}
				try
				{
					RefreshInfo();
				}
				catch
				{
				}
			};
			base.Controls.Add(lv);
			lblInfo = new Label();
			lblInfo.SetBounds(12, 442, 814, 22);
			base.Controls.Add(lblInfo);
			Button button3 = new Button();
			button3.Text = "全选本页";
			button3.SetBounds(12, 470, 100, 30);
			button3.Click += delegate
			{
				foreach (ListViewItem item in lv.Items)
				{
					if (item.Tag != null)
					{
						item.Checked = true;
					}
				}
			};
			base.Controls.Add(button3);
			Button button4 = new Button();
			button4.Text = "全不选";
			button4.SetBounds(118, 470, 90, 30);
			button4.Click += delegate
			{
				foreach (ListViewItem item in lv.Items)
				{
					item.Checked = false;
				}
			};
			base.Controls.Add(button4);
			Label label = new Label();
			label.Text = "双击文件夹进入；勾选文件夹 = 选中里面所有文件";
			label.SetBounds(214, 476, 360, 22);
			label.ForeColor = Color.DimGray;
			base.Controls.Add(label);
			btnOK = new Button();
			btnOK.Text = (okText == null ? "确定挪走" : okText);
			btnOK.SetBounds(596, 470, 110, 30);
			btnOK.Click += delegate
			{
				Accept();
			};
			base.Controls.Add(btnOK);
			Button button6 = new Button();
			button6.Text = "取消";
			button6.SetBounds(712, 470, 114, 30);
			button6.Click += delegate
			{
				base.DialogResult = DialogResult.Cancel;
				Close();
			};
			base.Controls.Add(button6);
			try
			{
				if (Directory.Exists(root))
				{
					Navigate(root);
				}
			}
			catch
			{
			}
		}

		private void GoUp()
		{
			try
			{
				DirectoryInfo parent = Directory.GetParent(_dir);
				if (parent != null)
				{
					Navigate(parent.FullName);
				}
			}
			catch
			{
			}
		}

		private void OpenSelected()
		{
			if (lv.SelectedItems.Count == 0)
			{
				return;
			}
			string text = lv.SelectedItems[0].Tag as string;
			if (text != null && Directory.Exists(text))
			{
				Navigate(text);
			}
		}

		private void Navigate(string dir)
		{
			_dir = dir;
			lblPath.Text = "当前：" + dir;
			_loading = true;
			lv.BeginUpdate();
			lv.Items.Clear();
			string[] array;
			string[] array2;
			try
			{
				array = Directory.GetDirectories(dir);
			}
			catch
			{
				array = new string[0];
			}
			try
			{
				array2 = Directory.GetFiles(dir);
			}
			catch
			{
				array2 = new string[0];
			}
			Array.Sort(array, StringComparer.OrdinalIgnoreCase);
			string[] array3 = array;
			for (int i = 0; i < array3.Length; i++)
			{
				string text = array3[i];
				string fileName = Path.GetFileName(text);
				if (string.Equals(fileName, "$RECYCLE.BIN", StringComparison.OrdinalIgnoreCase) || string.Equals(fileName, "System Volume Information", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				bool flag = false;
				try
				{
					flag = (File.GetAttributes(text) & FileAttributes.ReparsePoint) != 0;
				}
				catch
				{
				}
				ListViewItem listViewItem = new ListViewItem(new string[3]
				{
					fileName + (flag ? "  (链接)" : ""),
					"",
					"文件夹"
				});
				listViewItem.Tag = text;
				lv.Items.Add(listViewItem);
			}
			List<KeyValuePair<string, long>> list = new List<KeyValuePair<string, long>>();
			string[] array4 = array2;
			// 文件特别多时不再逐个查大小（几千次 FileInfo 会把窗口卡住），
			// 只按名字排、大小列留空 —— 这里主要是挑东西，不是比大小
			bool probeSize = (array4.Length <= 800);
			foreach (string text2 in array4)
			{
				long value = 0L;
				if (probeSize)
				{
					try
					{
						value = new FileInfo(text2).Length;
					}
					catch
					{
					}
				}
				list.Add(new KeyValuePair<string, long>(text2, value));
			}
			if (probeSize)
			{
				list.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b)
				{
					return b.Value.CompareTo(a.Value);
				});
			}
			else
			{
				list.Sort(delegate(KeyValuePair<string, long> a, KeyValuePair<string, long> b)
				{
					return string.Compare(Path.GetFileName(a.Key), Path.GetFileName(b.Key), StringComparison.OrdinalIgnoreCase);
				});
			}
			foreach (KeyValuePair<string, long> item in list)
			{
				ListViewItem listViewItem = new ListViewItem(new string[3]
				{
					Path.GetFileName(item.Key),
					probeSize ? ((double)item.Value / 1048576.0).ToString("N1") : "",
					"文件"
				});
				listViewItem.Tag = item.Key;
				lv.Items.Add(listViewItem);
			}
			lv.EndUpdate();
			_loading = false;
			RefreshInfo();
		}

		private void RefreshInfo()
		{
			if (lv == null || lblInfo == null)
			{
				return;
			}
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			long num5 = 0L;
			foreach (ListViewItem item in lv.Items)
			{
				if (item == null)
				{
					continue;
				}
				string text = item.Tag as string;
				if (text == null)
				{
					continue;
				}
				if (Directory.Exists(text))
				{
					num++;
					if (item.Checked)
					{
						num3++;
					}
				}
				else
				{
					num2++;
					if (item.Checked)
					{
						num4++;
						try
						{
							num5 += new FileInfo(text).Length;
						}
						catch
						{
						}
					}
				}
			}
			lblInfo.Text = string.Format("本页：{0} 个文件夹 / {1} 个文件；已勾选：文件夹 {2} 个、文件 {3} 个（{4:N1} MB）", num, num2, num3, num4, (double)num5 / 1048576.0);
		}

		private void Accept()
		{
			List<string> list = new List<string>();
			List<string> list2 = new List<string>();
			foreach (ListViewItem item in lv.Items)
			{
				if (item == null || !item.Checked)
				{
					continue;
				}
				string text = item.Tag as string;
				if (text != null)
				{
					if (Directory.Exists(text))
					{
						list.Add(text);
					}
					else
					{
						list2.Add(text);
					}
				}
			}
			if (list.Count == 0 && list2.Count == 0)
			{
				MessageBox.Show("还没有勾选任何东西。\n\n勾选文件夹 = 选中里面所有文件。", "没有选中");
				return;
			}
			Cursor = Cursors.WaitCursor;
			try
			{
				foreach (string item2 in list)
				{
					list2.AddRange(MainForm.SafeFiles(item2, 200000));
				}
			}
			finally
			{
				Cursor = Cursors.Default;
			}
			Selected.Clear();
			HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (string item3 in list2)
			{
				if (hashSet.Add(item3))
				{
					Selected.Add(item3);
				}
			}
			base.DialogResult = DialogResult.OK;
			Close();
		}
	}
	internal sealed class HistoryForm : Form
	{
		public string[] SelectedRow;

		public string[] SelectedRow2;

		public string Action;

		/// <summary>改名后的完整列表：关窗后由主窗口写回 history.csv，否则改名一关就丢</summary>
		public List<string[]> Rows
		{
			get
			{
				return _rows;
			}
		}

		/// <summary>有没有改过名字（避免没改也重写文件）</summary>
		public bool NameChanged;

		private ListView lv;

		private TextBox txtName;

		private List<string[]> _rows;

		public HistoryForm(List<string[]> rows)
		{
			_rows = rows;
			Text = "历史记录（可多选两条做对比）";
			try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
			base.ClientSize = new Size(760, 420);
			MinimumSize = new Size(680, 380);
			base.StartPosition = FormStartPosition.CenterParent;
			Font = new Font("Microsoft YaHei UI", 9f);
			lv = new ListView();
			lv.SetBounds(12, 12, 736, 300);
			lv.View = View.Details;
			lv.FullRowSelect = true;
			lv.GridLines = true;
			lv.MultiSelect = true;
			lv.Columns.Add("时间", 130);
			lv.Columns.Add("应用", 150);
			lv.Columns.Add("段数", 50);
			lv.Columns.Add("数据量", 90);
			lv.Columns.Add("最佳", 60);
			lv.Columns.Add("目录", 300);
			foreach (string[] row in rows)
			{
				string size = "—";
				long b;
				if (row.Length >= 7 && long.TryParse(row[6], out b) && b > 0)
				{
					size = (b >= 1073741824L)
						? ((double)b / 1073741824.0).ToString("N1") + " GB"
						: ((double)b / 1048576.0).ToString("N0") + " MB";
				}
				ListViewItem value = new ListViewItem(new string[6]
				{
					row[0],
					row[1],
					row[4],
					size,
					row[5] + "%",
					row[2]
				});
				lv.Items.Add(value);
			}
			lv.SelectedIndexChanged += delegate
			{
				if (lv.SelectedIndices.Count != 0)
				{
					int num = lv.SelectedIndices[0];
					txtName.Text = ((num < _rows.Count) ? _rows[num][1] : "");
				}
			};
			base.Controls.Add(lv);
			Label label = new Label();
			label.Text = "应用名：";
			label.SetBounds(12, 326, 70, 22);
			base.Controls.Add(label);
			txtName = new TextBox();
			txtName.SetBounds(84, 323, 300, 24);
			base.Controls.Add(txtName);
			Button button = new Button();
			button.Text = "保存名称";
			button.SetBounds(392, 322, 90, 26);
			button.Click += delegate
			{
				if (lv.SelectedIndices.Count != 0)
				{
					int index = lv.SelectedIndices[0];
					_rows[index][1] = txtName.Text.Trim();
					lv.Items[index].SubItems[1].Text = _rows[index][1];
					NameChanged = true;
				}
			};
			base.Controls.Add(button);
			Button button2 = new Button();
			button2.Text = "加载选中";
			button2.SetBounds(12, 364, 110, 32);
			button2.Click += delegate
			{
				if (lv.SelectedIndices.Count == 0)
				{
					MessageBox.Show("先选一条记录。");
				}
				else
				{
					SelectedRow = _rows[lv.SelectedIndices[0]];
					Action = "load";
					base.DialogResult = DialogResult.OK;
					Close();
				}
			};
			base.Controls.Add(button2);
			Button button3 = new Button();
			button3.Text = "删除记录";
			button3.SetBounds(132, 364, 110, 32);
			button3.Click += delegate
			{
				if (lv.SelectedIndices.Count == 0)
				{
					MessageBox.Show("先选一条记录。");
				}
				else if (MessageBox.Show("只删除这条记录，不会删除磁盘上的轨迹文件。继续？", "删除", MessageBoxButtons.YesNo) == DialogResult.Yes)
				{
					SelectedRow = _rows[lv.SelectedIndices[0]];
					Action = "delete";
					base.DialogResult = DialogResult.OK;
					Close();
				}
			};
			base.Controls.Add(button3);
			Button button4 = new Button();
			button4.Text = "对比两条";
			button4.SetBounds(252, 364, 110, 32);
			button4.Click += delegate
			{
				if (lv.SelectedIndices.Count != 2)
				{
					MessageBox.Show("按住 Ctrl 选【正好两条】记录，再点这个按钮。", "选两条");
				}
				else
				{
					SelectedRow = _rows[lv.SelectedIndices[0]];
					SelectedRow2 = _rows[lv.SelectedIndices[1]];
					Action = "compare";
					base.DialogResult = DialogResult.OK;
					Close();
				}
			};
			base.Controls.Add(button4);
			Button button5 = new Button();
			button5.Text = "关闭";
			button5.SetBounds(650, 364, 90, 32);
			button5.Click += delegate
			{
				base.DialogResult = DialogResult.Cancel;
				Close();
			};
			base.Controls.Add(button5);
			if (rows.Count == 0)
			{
				Label label2 = new Label();
				label2.Text = "还没有任何录制记录。跑一次「开始录制 → 停止录制」就会自动建档。";
				label2.SetBounds(12, 150, 720, 60);
				label2.ForeColor = Color.DimGray;
				base.Controls.Add(label2);
				label2.BringToFront();
			}
		}
	}

	// ================= 硬盘空间分布图 =================
/// <summary>问用户要不要以管理员身份重启，附带"以后都默认"的勾选。</summary>
	internal sealed class AdminAskForm : Form
	{
		public bool Always;

		public AdminAskForm(bool canAlways)
		{
			Text = "需要管理员权限";
			try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
			base.ClientSize = new Size(460, 190);
			base.FormBorderStyle = FormBorderStyle.FixedDialog;
			base.MaximizeBox = false;
			base.MinimizeBox = false;
			base.StartPosition = FormStartPosition.CenterScreen;
			Font = new Font("Microsoft YaHei UI", 9f);

			Label lbl = new Label();
			lbl.SetBounds(16, 16, 428, 72);
			lbl.Text = "这个程序需要管理员权限才能对磁盘下指令。\n\n现在就以管理员身份重新启动吗？";
			base.Controls.Add(lbl);

			CheckBox chk = new CheckBox();
			chk.Text = "以后都默认以管理员身份启动（不再问）";
			chk.SetBounds(16, 92, 428, 24);
			chk.Checked = canAlways;
			base.Controls.Add(chk);

			Button ok = new Button();
			ok.Text = "以管理员身份重启";
			ok.SetBounds(180, 132, 150, 30);
			ok.Click += delegate { Always = chk.Checked; base.DialogResult = DialogResult.OK; Close(); };
			base.Controls.Add(ok);

			Button no = new Button();
			no.Text = "这次不用";
			no.SetBounds(340, 132, 100, 30);
			no.Click += delegate { base.DialogResult = DialogResult.Cancel; Close(); };
			base.Controls.Add(no);

			base.AcceptButton = ok;
			base.CancelButton = no;
		}
	}

	/// <summary>搬到机械盘之前的一个确认窗：默认已选好"空闲最多的机械盘"，不满意可以改。</summary>
	internal sealed class TargetDiskForm : Form
	{
		public string Target;

		private List<string> _vols = new List<string>();
		private ComboBox _cb;

		public TargetDiskForm(string sourceVol, List<string> hdds, int fileCount, double totalMb)
		{
			Text = "搬到机械盘";
			try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
			base.ClientSize = new Size(520, 300);
			base.FormBorderStyle = FormBorderStyle.FixedDialog;
			base.MaximizeBox = false;
			base.MinimizeBox = false;
			base.StartPosition = FormStartPosition.CenterParent;
			Font = new Font("Microsoft YaHei UI", 9f);

			Label lbl = new Label();
			lbl.SetBounds(16, 14, 488, 92);
			lbl.Text = string.Format(
				"这 {0:N0} 个文件（{1:N1} MB）在 {2}（固态盘）。\n\n" +
				"搬过去会做四件事：复制 → 校验大小 → 删掉源文件 → 在机械盘上排到内圈。",
				fileCount, totalMb, sourceVol);
			base.Controls.Add(lbl);

			Label lbl2 = new Label();
			lbl2.SetBounds(16, 112, 120, 22);
			lbl2.Text = "搬到哪个机械盘：";
			base.Controls.Add(lbl2);

			_cb = new ComboBox();
			_cb.SetBounds(140, 110, 364, 24);
			_cb.DropDownStyle = ComboBoxStyle.DropDownList;
			int best = 0;
			long bestFree = -1;
			foreach (string v in hdds)
			{
				long free = 0;
				try { free = new DriveInfo(v).AvailableFreeSpace; } catch { }
				string item = string.Format("{0}　空闲 {1:N1} GB", v, (double)free / 1073741824.0);
				if (free > bestFree) { bestFree = free; best = _vols.Count; }
				_vols.Add(v);
				_cb.Items.Add(item);
			}
			if (_vols.Count > 0) _cb.SelectedIndex = best;   // 默认：空闲最多的那块
			base.Controls.Add(_cb);

			Label warn = new Label();
			warn.SetBounds(16, 144, 488, 60);
			warn.ForeColor = Color.Firebrick;
			warn.Text = "注意：这【会】改变文件路径，原位置不再有文件。\n" +
				"   别搬要运行的程序、游戏目录；视频、压缩包、备份这类可以。";
			base.Controls.Add(warn);

			Label note = new Label();
			note.SetBounds(16, 206, 488, 22);
			note.ForeColor = Color.DimGray;
			note.Text = "默认已经选好空闲最多的那块，不满意可以在这里改。";
			base.Controls.Add(note);

			Button ok = new Button();
			ok.Text = "开始搬";
			ok.SetBounds(286, 248, 100, 30);
			ok.Click += delegate
			{
				if (_cb.SelectedIndex < 0) return;
				Target = _vols[_cb.SelectedIndex];
				base.DialogResult = DialogResult.OK;
				Close();
			};
			base.Controls.Add(ok);

			Button no = new Button();
			no.Text = "取消";
			no.SetBounds(394, 248, 100, 30);
			no.Click += delegate { base.DialogResult = DialogResult.Cancel; Close(); };
			base.Controls.Add(no);

			base.AcceptButton = ok;
			base.CancelButton = no;
		}
	}
	internal sealed class MapForm : Form
	{
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern SafeFileHandle CreateFileW(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr ib, int isz, IntPtr ob, int osz, out int br, IntPtr ov);
		[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern bool GetDiskFreeSpaceW(string r, out uint spc, out uint bps, out uint fr, out uint tot);

		private const uint GENERIC_READ = 0x80000000;
		private const uint FILE_SHARE_READ = 1;
		private const uint FILE_SHARE_WRITE = 2;
		private const uint OPEN_EXISTING = 3;
		private const uint FSCTL_GET_VOLUME_BITMAP = 0x0009006F;
		private const uint FSCTL_GET_RETRIEVAL_POINTERS = 0x00090073;
		private const int ERROR_MORE_DATA = 234;

		private string _vol;
		private PictureBox pic;
		private Label lblInfo;
private Label lblClick;

		private Button btnPick;

		private ToolTip Tip = new ToolTip();


		private Button btnRef;

		private Button btnClose;

		private Label lblHelp;
		private System.Windows.Forms.Timer _resizeTimer;

		private byte[] _bits;
		private long _clusters;
		private long _freeClusters;
		private long _bestStart = -1;
		private long _bestLen;
		private long _clusterSize;
		private List<long[]> _extents = new List<long[]>();
		private string _selName = "(未选文件夹)";

		public MapForm(string volRoot)
		{
			_vol = volRoot;
			Text = "硬盘空间分布图 — " + volRoot;
			try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
			base.ClientSize = new Size(1204, 700);
			base.MinimumSize = new Size(760, 480);
			base.StartPosition = FormStartPosition.CenterParent;
			Font = new Font("Microsoft YaHei UI", 9f);

			lblInfo = new Label();
			lblInfo.SetBounds(12, 10, 860, 22);
			lblInfo.ForeColor = Color.DimGray;
			base.Controls.Add(lblInfo);

			btnPick = new Button();
			btnPick.Text = "标位置...";
			btnPick.SetBounds(812, 8, 104, 26);
			btnPick.Click += delegate { PickAny(); };
			Tip.SetToolTip(btnPick, "同一个窗口里：勾文件夹 = 标它里面所有文件；一直双击进到最里层，就能勾单个文件（比如一个大的 .pak）");
			base.Controls.Add(btnPick);


			btnRef = new Button();
			btnRef.Text = "刷新";
			btnRef.SetBounds(1018, 8, 66, 26);
			btnRef.Click += delegate { StartLoad(); };
			base.Controls.Add(btnRef);

			btnClose = new Button();
			btnClose.Text = "关闭";
			btnClose.SetBounds(1088, 8, 84, 26);
			btnClose.Click += delegate { Close(); };
			base.Controls.Add(btnClose);

			pic = new PictureBox();
			pic.SetBounds(12, 40, 1160, 558);
			pic.Anchor = (AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
			pic.BorderStyle = BorderStyle.FixedSingle;
			pic.SizeMode = PictureBoxSizeMode.Normal;
			pic.MouseClick += delegate(object s, MouseEventArgs e) { OnMapClick(e); };
			base.Controls.Add(pic);

			lblClick = new Label();
			lblClick.SetBounds(12, 660, 1160, 22);
			lblClick.Anchor = (AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
			base.Controls.Add(lblClick);

			lblHelp = new Label();
			lblHelp.SetBounds(12, 606, 1160, 50);
			lblHelp.Anchor = (AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
			lblHelp.ForeColor = Color.DimGray;
			base.Controls.Add(lblHelp);

			_resizeTimer = new System.Windows.Forms.Timer();
			_resizeTimer.Interval = 250;
			_resizeTimer.Tick += delegate
			{
				_resizeTimer.Stop();
				DoResize();
				Render();
			};
			base.Resize += delegate
			{
				_resizeTimer.Stop();
				_resizeTimer.Start();
			};
			base.Shown += delegate
			{
				DoResize();
			};

			uint spc, bps, fr, tot;
			if (GetDiskFreeSpaceW(volRoot, out spc, out bps, out fr, out tot))
			{
				_clusterSize = (long)spc * bps;
			}
			// 读位图 + 统计要遍历上亿个簇，放在后台线程，否则窗口会"未响应"几秒
			StartLoad();
		}

		private void StartLoad()
		{
			UseWaitCursor = true;
			Thread th = new Thread((ThreadStart)delegate
			{
				bool ok = LoadVolume();
				try
				{
					BeginInvoke((Action)delegate
					{
						UseWaitCursor = false;
						if (ok) Render();
					});
				}
				catch
				{
				}
			});
			th.IsBackground = true;
			th.Start();
		}

		/// <summary>窗口尺寸变化时重新摆放图片和说明文字（以前只锚定、不重排，缩小窗口文字会被切掉）</summary>
		private void DoResize()
		{
			int w = base.ClientSize.Width;
			int h = base.ClientSize.Height;
			int bottomH = 104;                     // 底部两行文字占的高度
			// 右上角按钮从右往左排，信息标签让出位置 —— 以前固定坐标会互相盖住
			int right = w - 12;
			int gap = 6;
			btnClose.SetBounds(right - 78, 8, 78, 26); right -= 78 + gap;
			btnRef.SetBounds(right - 62, 8, 62, 26); right -= 62 + gap;
			btnPick.SetBounds(right - 104, 8, 104, 26); right -= 104 + gap;
			lblInfo.SetBounds(12, 10, Math.Max(80, right - 12), 22);
			pic.SetBounds(12, 40, Math.Max(80, w - 24), Math.Max(80, h - 40 - bottomH));
			lblHelp.SetBounds(12, h - bottomH, Math.Max(80, w - 24), 56);
			lblClick.SetBounds(12, h - 46, Math.Max(80, w - 24), 22);
		}

		/// <summary>读卷位图（只读一次，缩放时复用）</summary>
		private bool LoadVolume()
		{
			_bits = null;
			SafeFileHandle vh = CreateFileW(@"\\.\" + _vol.TrimEnd('\\'), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
			if (vh.IsInvalid)
			{
				MessageBox.Show("打开卷失败（错误码 " + Marshal.GetLastWin32Error() + "）。\n\n请右键 exe → 以管理员身份运行。", "分布图");
				return false;
			}
			try
			{
				uint spc, bps, fr, tot;
				if (!GetDiskFreeSpaceW(_vol, out spc, out bps, out fr, out tot))
				{
					MessageBox.Show("读不到卷信息。", "分布图");
					return false;
				}
				int outSize = (int)((long)tot / 8) + 64;
				IntPtr buf = Marshal.AllocHGlobal(outSize);
				try
				{
					IntPtr ib = Marshal.AllocHGlobal(8);
					Marshal.WriteInt64(ib, 0, 0);
					int bytes;
					bool ok = DeviceIoControl(vh, FSCTL_GET_VOLUME_BITMAP, ib, 8, buf, outSize, out bytes, IntPtr.Zero);
					Marshal.FreeHGlobal(ib);
					if (!ok)
					{
						MessageBox.Show("读取卷位图失败。", "分布图");
						return false;
					}
					long bitmapBytes = Marshal.ReadInt64(buf, 8);
					if (bitmapBytes <= 0 || bitmapBytes > outSize - 16) bitmapBytes = outSize - 16;
					_bits = new byte[bitmapBytes];
					Marshal.Copy(new IntPtr(buf.ToInt64() + 16), _bits, 0, (int)bitmapBytes);
					_clusters = Math.Min((long)tot, bitmapBytes * 8);

					_freeClusters = 0;
					_bestStart = -1;
					_bestLen = 0;
					long run = 0;
					long runStart = 0;
					for (long c = 0; c < _clusters; c++)
					{
						if (((_bits[c >> 3] >> (int)(c & 7)) & 1) != 0)
						{
							run = 0;
						}
						else
						{
							_freeClusters++;
							if (run == 0) runStart = c;
							run++;
							if (run > _bestLen) { _bestLen = run; _bestStart = runStart; }
						}
					}
					return true;
				}
				finally
				{
					Marshal.FreeHGlobal(buf);
				}
			}
			finally
			{
				vh.Dispose();
			}
		}

		private void PickFolder()
		{
			using (FolderBrowserDialog d = new FolderBrowserDialog())
			{
				d.Description = "选一个文件夹，图上会用橙色标出它的位置";
				if (d.ShowDialog(this) != DialogResult.OK) return;
				Cursor = Cursors.WaitCursor;
				try
				{
					_extents = new List<long[]>();
					_selName = d.SelectedPath;
					foreach (string f in MainForm.SafeFiles(d.SelectedPath, 200000))
					{
						GetExtents(f, _extents);
					}
				}
				catch { }
				finally { Cursor = Cursors.Default; }
				Render();
			}
		}

		/// <summary>统一的选择入口：一个窗口里既能勾文件夹，也能一直进到最里层勾单个文件。</summary>
		private void PickAny()
		{
			string start;
			try { start = Path.GetPathRoot(Path.GetFullPath(_vol)); }
			catch { start = _vol; }
			using (ColdPickForm pick = new ColdPickForm(start, "标出哪些文件的位置（可勾文件夹，也可进到文件）", "确定标记"))
			{
				if (pick.ShowDialog(this) != DialogResult.OK) return;
				List<string> files = pick.Selected;
				if (files.Count == 0) return;
				Cursor = Cursors.WaitCursor;
				try
				{
					_extents = new List<long[]>();
					_selName = (files.Count == 1)
						? Path.GetFileName(files[0])
						: (Path.GetFileName(files[0]) + " 等 " + files.Count + " 个文件");
					foreach (string one in files)
					{
						GetExtents(one, _extents);
					}
				}
				catch { }
				finally { Cursor = Cursors.Default; }
				Render();
			}
		}
		/// <summary>直接选单个（或多个）文件来标注，不用先找它所在的文件夹</summary>
		private void PickFiles()
		{
			using (OpenFileDialog d = new OpenFileDialog())
			{
				d.Title = "选一个或多个文件，图上会用橙色标出它们的位置";
				d.Multiselect = true;
				d.CheckFileExists = true;
				if (d.ShowDialog(this) != DialogResult.OK) return;
				Cursor = Cursors.WaitCursor;
				try
				{
					_extents = new List<long[]>();
					string[] files = d.FileNames;
					_selName = (files.Length == 1)
						? Path.GetFileName(files[0])
						: (Path.GetFileName(files[0]) + " 等 " + files.Length + " 个文件");
					foreach (string f in files)
					{
						GetExtents(f, _extents);
					}
				}
				catch { }
				finally { Cursor = Cursors.Default; }
				Render();
			}
		}

		private static bool GetExtents(string path, List<long[]> into)
		{
			SafeFileHandle h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
			if (h.IsInvalid) return false;
			IntPtr ib = Marshal.AllocHGlobal(8);
			Marshal.WriteInt64(ib, 0, 0);
			int outSize = 4096;
			IntPtr ob = Marshal.AllocHGlobal(outSize);
			try
			{
				int bytes;
				bool ok = false;
				for (int t = 0; t < 8; t++)
				{
					if (DeviceIoControl(h, FSCTL_GET_RETRIEVAL_POINTERS, ib, 8, ob, outSize, out bytes, IntPtr.Zero)) { ok = true; break; }
					if (Marshal.GetLastWin32Error() == ERROR_MORE_DATA && bytes > outSize)
					{
						Marshal.FreeHGlobal(ob);
						outSize = bytes;
						ob = Marshal.AllocHGlobal(outSize);
						continue;
					}
					return false;
				}
				if (!ok) return false;
				int cnt = Marshal.ReadInt32(ob, 0);
				if (cnt <= 0) return false;
				// NTFS 返回的结构：
				//   0   DWORD   段数
				//   8   int64   起始 VCN
				//   16  每对 16 字节：{ NextVcn(int64), Lcn(int64) }
				// 之前按 8 字节跳着读，读到的是错位的垃圾 → 高亮时有时全标、有时不标
				long startVcn = Marshal.ReadInt64(ob, 8);
				long prevNext = startVcn;
				for (int i = 0; i < cnt; i++)
				{
					int o = 16 + i * 16;
					long nextVcn = Marshal.ReadInt64(ob, o);
					long lcn = Marshal.ReadInt64(ob, o + 8);
					// Lcn = -1 表示这段没有分配（稀疏），跳过
					if (lcn >= 0 && nextVcn > prevNext)
					{
						into.Add(new long[2] { lcn, nextVcn - prevNext });
					}
					prevNext = nextVcn;
				}
				return true;
			}
			finally
			{
				Marshal.FreeHGlobal(ib);
				Marshal.FreeHGlobal(ob);
				h.Dispose();
			}
		}

		private void Render()
		{
			if (_bits == null || _clusters <= 0) return;
			int W = pic.ClientSize.Width;
			int H = pic.ClientSize.Height;
			if (W < 40 || H < 40) return;

			Cursor = Cursors.WaitCursor;
			try
			{
				int cells = W * H;
				int[] pix = new int[cells];
				bool hasSel = (_extents != null && _extents.Count > 0);
				// 有选中时把底图压暗一档，否则几百 MB 的文件在蓝底上几乎看不出来
				int usedColor = hasSel ? unchecked((int)0xFF1B3550) : unchecked((int)0xFF33608F);
				int freeColor = hasSel ? unchecked((int)0xFF141414) : unchecked((int)0xFF232323);
				int selColor = unchecked((int)0xFFFFD24A);

				// 每格覆盖多少簇
				double perCell = (double)_clusters / cells;

				for (int i = 0; i < cells; i++)
				{
					long a = (long)(i * perCell);
					long b = (long)((i + 1) * perCell);
					if (b <= a) b = a + 1;
					bool used = false;
					for (long c = a; c < b && c < _clusters; c++)
					{
						if (((_bits[c >> 3] >> (int)(c & 7)) & 1) != 0) { used = true; break; }
					}
					pix[i] = used ? usedColor : freeColor;
				}

				// 高亮：每个区间至少画 2x2 像素，保证小文件也能看见
				foreach (long[] e in _extents)
				{
					long l = e[0];
					long n = Math.Max(1, e[1]);
					long ca = (long)(l / perCell);
					long cb = (long)((l + n) / perCell);
					if (cb <= ca) cb = ca + 1;
					int x0 = (int)(ca % W);
					int y0 = (int)(ca / W);
					int x1 = (int)(cb % W);
					int y1 = (int)(cb / W);
					for (int y = y0; y <= y1 && y < H; y++)
					{
						int xs = (y == y0) ? x0 : 0;
						int xe = (y == y1) ? x1 : W - 1;
						for (int x = xs; x <= xe && x < W; x++)
						{
							if (x < 0) continue;
							// 3x3：几百 MB 的文件在 0.6MB/像素下只有几十个像素，2x2 太细
							for (int dy = 0; dy <= 1; dy++)
							{
								int yy = y + dy;
								if (yy >= H) continue;
								for (int dx = -1; dx <= 1; dx++)
								{
									int xx = x + dx;
									if (xx < 0 || xx >= W) continue;
									pix[yy * W + xx] = selColor;
								}
							}
						}
					}
				}

				Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
				BitmapData data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
				Marshal.Copy(pix, 0, data.Scan0, cells);
				bmp.UnlockBits(data);

				// 网格 + 纵向刻度
				using (Graphics g = Graphics.FromImage(bmp))
				{
					using (Pen pen = new Pen(Color.FromArgb(55, 255, 255, 255), 1f))
					{
						// 只画横线：纵轴 = 从外圈到内圈的位置百分比
						// 竖线没有意义（横向不是独立维度），不再画
						for (int i = 1; i < 10; i++)
						{
							int y = H * i / 10;
							g.DrawLine(pen, 0, y, W, y);
						}
					}
					using (Font f = new Font("Consolas", 8f))
					using (SolidBrush br = new SolidBrush(Color.FromArgb(210, 230, 230, 230)))
					using (SolidBrush bg = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
					{
						// 纵轴：0% 顶部（外圈）→ 100% 底部（内圈）
						for (int i = 0; i <= 10; i++)
						{
							int y = H * i / 10 - 6;
							if (y < 0) y = 0;
							if (y > H - 14) y = H - 14;
							g.FillRectangle(bg, 1, y, 30, 13);
							g.DrawString((i * 10).ToString() + "%", f, br, 2, y);
						}
						// 横轴不是独立维度：整张图是同一条 LCN 序列折行画的
						using (Font f2 = new Font("Microsoft YaHei UI", 8f))
						{
							string hint = "← 同一行从左到右，到行尾换下一行继续 →";
							SizeF sz = g.MeasureString(hint, f2);
							g.FillRectangle(bg, W - sz.Width - 12, 1, sz.Width + 8, 16);
							g.DrawString(hint, f2, br, W - sz.Width - 8, 2);
						}
					}
				}

				if (pic.Image != null) pic.Image.Dispose();
				pic.Image = bmp;

				double gb = 1073741824.0;
				double mbPerCell = perCell * _clusterSize / 1048576.0;
				lblInfo.Text = string.Format("{0} ｜ 总 {1:N0} GB ｜ 已用 {2:N0} GB ｜ 空闲 {3:N0} GB ｜ 最大连续空区 {4:N1} GB（{5:N1}% 处）｜ 1 像素 ≈ {6:N2} MB",
					_vol,
					(double)(_clusters * _clusterSize) / gb,
					(double)((_clusters - _freeClusters) * _clusterSize) / gb,
					(double)(_freeClusters * _clusterSize) / gb,
					(double)(_bestLen * _clusterSize) / gb,
					_bestStart < 0 ? 0.0 : _bestStart * 100.0 / _clusters,
					mbPerCell);

				lblHelp.Text =
					"怎么读这张图：蓝色 = 已用，深灰 = 空闲，橙色 = 你扫描的文件夹。\n" +
					"整张图是一条磁盘位置的序列 —— 左上是【最外圈】（最快），一行一行往下，右下是【最内圈】（最慢）。\n" +
					"纵向百分比 = 从外圈算起的位置；横向不表示速度，只是\"这一行还没读完\"。点图上任意位置可以看具体坐标。";
				long selClusters = 0;
				foreach (long[] e2 in _extents) selClusters += Math.Max(1, e2[1]);
				double selBytes = (double)selClusters * _clusterSize;
				string selSize = (selBytes >= 1073741824.0)
					? (selBytes / 1073741824.0).ToString("N2") + " GB"
					: (selBytes / 1048576.0).ToString("N0") + " MB";
				lblClick.Text = string.Format("蓝=已用　深灰=空闲　橙={0}　{1}　{2:N0} 段　纵向每格 = 10%",
					_selName, selSize, _extents.Count);
			}
			finally
			{
				Cursor = Cursors.Default;
			}
		}

		private void OnMapClick(MouseEventArgs e)
		{
			if (_clusters <= 0) return;
			int W = pic.ClientSize.Width;
			int H = pic.ClientSize.Height;
			if (e.X < 0 || e.X >= W || e.Y < 0 || e.Y >= H) return;
			long cell = (long)e.Y * W + e.X;
			long cluster = (long)((double)cell * _clusters / (W * H));
			double pct = cluster * 100.0 / _clusters;
			double gb = (double)(cluster * _clusterSize) / 1073741824.0;
			double inRow = W > 0 ? e.X * 100.0 / W : 0;
			lblClick.Text = string.Format(
				"第 {0:N0}/{1:N0} 行（从外圈算 {2:N1}%），行内 {3:N0}%　→　LCN {4:N0}（约 {5:N1} GB 偏移）　｜　整张图只有一条序列：一行从左到右，到行尾换下一行继续",
				e.Y + 1, H, pct, inRow, cluster, gb);
		}
	}
	// ================= 首次启动的风险提示 =================
	internal sealed class DisclaimerForm : Form
	{
		public bool DontShowAgain;

		public DisclaimerForm()
		{
			Text = "使用前请阅读";
			base.ClientSize = new Size(780, 636);
			base.FormBorderStyle = FormBorderStyle.FixedDialog;
			base.MaximizeBox = false;
			base.MinimizeBox = false;
			base.StartPosition = FormStartPosition.CenterParent;
			Font = new Font("Microsoft YaHei UI", 9f);

			Label title = new Label();
			title.Text = "这个工具会直接改写磁盘上的数据布局";
			title.SetBounds(22, 16, 730, 30);
			title.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
			title.ForeColor = Color.FromArgb(170, 30, 30);
			base.Controls.Add(title);

			// 红色警示块
			Panel warn = new Panel();
			warn.SetBounds(22, 54, 736, 132);
			warn.BackColor = Color.FromArgb(255, 240, 240);
			warn.BorderStyle = BorderStyle.FixedSingle;
			base.Controls.Add(warn);

			Label warnTitle = new Label();
			warnTitle.Text = "⚠  可能造成文件丢失、文件损坏，甚至硬盘损坏";
			warnTitle.SetBounds(12, 10, 710, 26);
			warnTitle.Font = new Font("Microsoft YaHei UI", 11.5f, FontStyle.Bold);
			warnTitle.ForeColor = Color.FromArgb(180, 20, 20);
			warn.Controls.Add(warnTitle);

			Label warnBody = new Label();
			warnBody.SetBounds(12, 40, 710, 84);
			warnBody.ForeColor = Color.FromArgb(120, 20, 20);
			warnBody.Text =
				"这不是系统层的优化，而是真的去搬动磁盘上的数据块。\r\n" +
				"任何对磁盘写入的操作都存在固有风险，可能的结果包括但不限于：\r\n" +
				"    文件丢失、文件损坏、分区表或引导区损坏、硬盘出现物理故障、系统无法启动。\r\n" +
				"请务必先备份重要数据。继续使用即表示你自愿承担全部后果。";
			warn.Controls.Add(warnBody);

			Label body = new Label();
			body.SetBounds(22, 194, 736, 306);
			body.Text =
				"另外请确认你了解：\r\n\r\n" +
				"  1. 需要管理员权限（要直接对卷下指令）\r\n\r\n" +
				"  2. 会搬动大量数据，可能耗时几十分钟。中途不要断电、\r\n" +
				"     不要强制关机——中断可能留下不完整的布局，极端情况下会损坏文件。\r\n\r\n" +
				"  3. 搬移调用的是 Windows 自带的文件搬移接口，设计上失败会回滚。\r\n" +
				"     但“设计上会回滚”不等于“保证安全”：遇到坏道、断电、驱动异常、\r\n" +
				"     杀毒软件干扰等情况，仍可能出问题。本工具不能保证任何情况下都绝对安全。\r\n\r\n" +
				"  4. 只对「机械硬盘 + 散文件多 + 读取占大头」的场景有效，\r\n" +
				"     不是万能加速器，也不替代 SSD。固态硬盘上使用没有意义。\r\n\r\n" +
				"  5. 联机游戏、频繁更新的软件不建议使用（反作弊敏感 + 更新会打乱布局）。";
			base.Controls.Add(body);

			// 免责声明单独一行、加粗深红，放在按钮正上方
			Label deny = new Label();
			deny.SetBounds(22, 496, 736, 44);
			deny.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
			deny.ForeColor = Color.FromArgb(170, 20, 20);
			deny.Text =
				"作者不对任何数据丢失、文件损坏、硬件损伤或由此产生的其他损失负任何责任。\r\n" +
				"使用本工具产生的一切后果，由使用者自行承担。";
			base.Controls.Add(deny);

			CheckBox chk = new CheckBox();
			chk.Text = "我已阅读并理解上述风险，以后不再提示";
			chk.SetBounds(22, 548, 500, 26);
			base.Controls.Add(chk);

			Button ok = new Button();
			ok.Text = "同意并自行承担风险";
			ok.SetBounds(432, 584, 196, 38);
			ok.DialogResult = DialogResult.OK;
			ok.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
			base.Controls.Add(ok);

			Button cancel = new Button();
			cancel.Text = "退出";
			cancel.SetBounds(638, 584, 120, 38);
			cancel.DialogResult = DialogResult.Cancel;
			base.Controls.Add(cancel);

			base.AcceptButton = ok;
			base.CancelButton = cancel;
			base.Shown += delegate
			{
				DontShowAgain = chk.Checked;
			};
			ok.Click += delegate
			{
				DontShowAgain = chk.Checked;
			};
		}
	}

}
