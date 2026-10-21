using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Resources;
using System.Windows.Forms;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace PclDownloadFixer
{
    public enum PatchState { Applied, AlreadyPatched, NotFound, Skipped }

    public class PatchResult
    {
        public string Target;
        public PatchState State;
        public string Detail;
    }

    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveCecil;
            if (args != null && args.Length >= 1)
            {
                try { Environment.ExitCode = RunCli(args[0]); }
                catch (Exception ex)
                {
                    try { Console.WriteLine("FAIL: " + ex.GetType().FullName + " | " + ex.Message); } catch { }
                    Environment.ExitCode = 2;
                }
                return;
            }
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, System.Threading.ThreadExceptionEventArgs e)
            {
                MessageBox.Show("程序出错：" + e.Exception.Message, "PCL 下载功能修复工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show("程序出错：" + (ex == null ? "(未知)" : ex.Message), "PCL 下载功能修复工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        // 命令行模式： PCL下载修复工具.exe "路径\Plain Craft Launcher 2.exe"
        static int RunCli(string path)
        {
            var fixer = new PclFixer(delegate (string s) { Console.WriteLine(s); });
            string err;
            bool ok = fixer.Run(path, out err);
            foreach (var r in fixer.Results)
            {
                string tag = r.State == PatchState.Applied ? "[已修复]"
                           : r.State == PatchState.AlreadyPatched ? "[本来就好]"
                           : r.State == PatchState.Skipped ? "[跳过]" : "[未找到]";
                Console.WriteLine(tag + " " + r.Target);
                if (!string.IsNullOrEmpty(r.Detail)) Console.WriteLine("        " + r.Detail);
            }
            Console.WriteLine(ok ? "OK" : "FAIL: " + err);
            return ok ? 0 : 1;
        }

        static System.Reflection.Assembly ResolveCecil(object sender, ResolveEventArgs args)
        {
            if (args.Name == null || !args.Name.StartsWith("Mono.Cecil")) return null;
            try
            {
                using (Stream s = typeof(Program).Assembly.GetManifestResourceStream("Mono.Cecil.dll"))
                {
                    if (s == null) return null;
                    byte[] buf = new byte[s.Length];
                    s.Read(buf, 0, buf.Length);
                    return System.Reflection.Assembly.Load(buf);
                }
            }
            catch { return null; }
        }
    }

    public class MainForm : Form
    {
        readonly TextBox txtPath = new TextBox();
        readonly Button btnBrowse = new Button();
        readonly Button btnStart = new Button();
        readonly TextBox txtLog = new TextBox();
        readonly Label lblHint = new Label();

        public MainForm()
        {
            Text = "PCL 下载功能修复工具";
            ClientSize = new Size(700, 470);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Font = new Font("Microsoft YaHei UI", 9f);

            var lbl1 = new Label();
            lbl1.Text = "PCL 程序：";
            lbl1.Location = new Point(14, 18);
            lbl1.AutoSize = true;

            txtPath.Location = new Point(92, 15);
            txtPath.Size = new Size(470, 24);
            txtPath.ReadOnly = true;

            btnBrowse.Text = "选择程序...";
            btnBrowse.Location = new Point(570, 13);
            btnBrowse.Size = new Size(112, 28);
            btnBrowse.Click += delegate { Browse(); };

            lblHint.Location = new Point(14, 52);
            lblHint.Size = new Size(546, 36);
            lblHint.ForeColor = Color.DimGray;
            lblHint.Text = "选择新版 PCL 后点“开始修复”。不依赖老版本；自动备份为 .bak，不改动其它内容。";

            btnStart.Text = "开始修复";
            btnStart.Location = new Point(570, 52);
            btnStart.Size = new Size(112, 36);
            btnStart.Enabled = false;
            btnStart.Click += delegate { Run(); };

            txtLog.Location = new Point(14, 96);
            txtLog.Size = new Size(668, 358);
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.White;
            txtLog.Font = new Font("Consolas", 9f);

            Controls.Add(lbl1);
            Controls.Add(txtPath);
            Controls.Add(btnBrowse);
            Controls.Add(lblHint);
            Controls.Add(btnStart);
            Controls.Add(txtLog);
        }

        void Browse()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择要修复的 PCL 程序";
                dlg.Filter = "PCL 程序 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                txtPath.Text = dlg.FileName;
                btnStart.Enabled = true;
                txtLog.Clear();
                Log("已选择：" + dlg.FileName);
                Log("点“开始修复”即可。");
            }
        }

        void Log(string s)
        {
            txtLog.AppendText(s + Environment.NewLine);
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.ScrollToCaret();
            Application.DoEvents();
        }

        void Run()
        {
            string path = txtPath.Text;
            if (!File.Exists(path)) { MessageBox.Show(this, "文件不存在。", "错误"); return; }
            btnBrowse.Enabled = false;
            btnStart.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                var fixer = new PclFixer(Log);
                string err;
                bool ok = fixer.Run(path, out err);
                Log("");
                foreach (var r in fixer.Results)
                {
                    string tag = r.State == PatchState.Applied ? "[已修复]"
                               : r.State == PatchState.AlreadyPatched ? "[本来就好]"
                               : r.State == PatchState.Skipped ? "[跳过]" : "[未找到]";
                    Log(tag + " " + r.Target);
                    if (!string.IsNullOrEmpty(r.Detail)) Log("        " + r.Detail);
                }
                Log("");
                Log(ok ? "=== 完成，可以双击运行了 ===" : "=== 失败：" + err + " ===");
            }
            catch (Exception ex)
            {
                Log("=== 异常：" + ex.Message + " ===");
            }
            finally
            {
                Cursor = Cursors.Default;
                btnBrowse.Enabled = true;
                btnStart.Enabled = true;
            }
        }
    }

    /// <summary>不依赖老版 PCL：全部补丁按语义现场生成 IL。</summary>
    public class PclFixer
    {
        const string SabotageMsg = "为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能，非常抱歉 qwq……";
        static readonly string[] Markers = { "mcbbs.packmeta", "mmc-pack.json", "modrinth.index.json", "manifest.json", "modpack.json", "modpack.zip", "modpack.mrpack" };
        static readonly string[] KindMarkers = { "manifest.json", "modpack.json", "mmc-pack.json", "mcbbs.packmeta", "modrinth.index.json", "modpack.zip", "modpack.mrpack" };
        static readonly string[] KindMethods = { "InstallPackCurseForge", "InstallPackHMCL", "InstallPackMMC", "InstallPackMCBBS", "InstallPackModrinth", "InstallPackLauncherPack", "InstallPackLauncherPack" };

        readonly Action<string> log;
        public List<PatchResult> Results = new List<PatchResult>();
        string tempDir;

        public PclFixer(Action<string> log) { this.log = log; }

        public bool Run(string exePath, out string error)
        {
            error = null;
            tempDir = PickWorkDir();
            if (tempDir == null) { error = "无法创建临时工作目录（请把工具放到可写目录下运行）"; return false; }
            string tmpOut = exePath + ".pclfix.tmp";

            try
            {
                var probe = AssemblyDefinition.ReadAssembly(exePath);
                string asmName = probe.Name.Name;
                ExtractEmbeddedDeps(probe.MainModule);
                probe.Dispose();

                var resolver = new DefaultAssemblyResolver();
                resolver.AddSearchDirectory(tempDir);
                resolver.AddSearchDirectory(Path.GetDirectoryName(exePath));
                var rp = new ReaderParameters();
                rp.AssemblyResolver = resolver;
                var asm = AssemblyDefinition.ReadAssembly(exePath, rp);
                var mod = asm.MainModule;

                PatchResult r1 = PatchDownloadPage(mod);
                Results.Add(r1);
                PatchResult r2 = PatchModpackDispatch(mod);
                Results.Add(r2);
                PatchResult r3 = PatchModpackPicker(mod);
                Results.Add(r3);

                bool anyFound = Results.Any(delegate (PatchResult r) { return r.State != PatchState.NotFound; });
                bool changed = Results.Any(delegate (PatchResult r) { return r.State == PatchState.Applied; });

                if (!anyFound)
                {
                    asm.Dispose();
                    resolver.Dispose();
                    error = "没有在这份 PCL 里找到“禁用下载”的改动点。\n它可能已经是修复版，或版本不受支持。";
                    return false;
                }
                if (!changed)
                {
                    asm.Dispose();
                    resolver.Dispose();
                    log("三处改动点都已是修复状态，无需处理。");
                    return true;
                }

                log("写盘校验中...");
                asm.Write(tmpOut);
                asm.Dispose();
                resolver.Dispose();

                if (!Verify(tmpOut))
                {
                    try { File.Delete(tmpOut); } catch { }
                    error = "写出的文件未通过回读校验，已放弃替换（原文件未改动）。";
                    return false;
                }

                string bak = exePath + ".bak";
                File.Copy(exePath, bak, true);
                log("已备份：" + Path.GetFileName(bak));
                File.Delete(exePath);
                File.Move(tmpOut, exePath);
                log("已写入：" + exePath);
                log("程序集 " + asmName + "，大小 " + new FileInfo(exePath).Length + " 字节");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                try { if (File.Exists(tmpOut)) File.Delete(tmpOut); } catch { }
                return false;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        static string PickWorkDir()
        {
            string name = "pclfix_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var cands = new List<string>();
            try { cands.Add(Path.GetTempPath()); } catch { }
            try { cands.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")); } catch { }
            try { cands.Add(Path.GetDirectoryName(typeof(PclFixer).Assembly.Location)); } catch { }
            foreach (var c in cands)
            {
                if (string.IsNullOrEmpty(c)) continue;
                try
                {
                    string d = Path.Combine(c, name);
                    Directory.CreateDirectory(d);
                    return d;
                }
                catch { }
            }
            return null;
        }

        void ExtractEmbeddedDeps(ModuleDefinition mod)
        {
            int total = 0;
            foreach (var res in mod.Resources)
            {
                var er = res as EmbeddedResource;
                if (er == null) continue;
                Stream s = null;
                try { s = er.GetResourceStream(); } catch { continue; }
                if (s == null) continue;
                try
                {
                    using (var rr = new ResourceReader(s))
                    {
                        var e = rr.GetEnumerator();
                        while (e.MoveNext())
                        {
                            var v = e.Value as byte[];
                            if (v == null || v.Length < 2 || v[0] != 0x4D || v[1] != 0x5A) continue;
                            string name = Convert.ToString(e.Key);
                            try { File.WriteAllBytes(Path.Combine(tempDir, name + ".dll"), v); total++; } catch { }
                        }
                    }
                }
                catch { }
            }
            if (total > 0) log("已从程序内部提取依赖程序集 " + total + " 个（写盘校验需要）");
        }

        bool Verify(string path)
        {
            try
            {
                var a = AssemblyDefinition.ReadAssembly(path);
                var mod = a.MainModule;
                bool ok = true;
                foreach (var r in Results)
                {
                    if (r.State != PatchState.Applied) continue;
                    if (r.Target.IndexOf("PageDownloadInstall") >= 0)
                        ok &= HasMember(mod, "PCL.PageDownloadInstall", "LoaderInit", 0, OpCodes.Ldsfld, "DlClientListLoader");
                    else if (r.Target.IndexOf("ModpackInstall(String") >= 0)
                        ok &= HasOpcode(mod, "PCL.ModModpack", "ModpackInstall", 3, OpCodes.Switch);
                    else if (r.Target.IndexOf("ModpackInstall()") >= 0)
                        ok &= HasMember(mod, "PCL.ModModpack", "ModpackInstall", 0, OpCodes.Call, "SelectFile");
                }
                if (ok) ok = ValidateStructure(mod);
                a.Dispose();
                return ok;
            }
            catch { return false; }
        }

        // 结构自检：异常表区间必须有序，所有分支目标必须落在同一方法体内
        static bool ValidateStructure(ModuleDefinition mod)
        {
            var stack = new Stack<TypeDefinition>(mod.Types);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                foreach (var n in t.NestedTypes) stack.Push(n);
                foreach (var m in t.Methods)
                {
                    if (!m.HasBody) continue;
                    var body = m.Body;
                    var set = new HashSet<Instruction>(body.Instructions);
                    foreach (var h in body.ExceptionHandlers)
                    {
                        if (h.TryStart == null || h.HandlerStart == null) return false;
                        if (h.TryEnd != null && h.TryEnd.Offset < h.TryStart.Offset) return false;
                        if (h.HandlerEnd != null && h.HandlerEnd.Offset < h.HandlerStart.Offset) return false;
                    }
                    foreach (var i in body.Instructions)
                    {
                        var tgt = i.Operand as Instruction;
                        if (tgt != null && !set.Contains(tgt)) return false;
                        var arr = i.Operand as Instruction[];
                        if (arr != null) foreach (var x in arr) if (!set.Contains(x)) return false;
                    }
                }
            }
            return true;
        }

        static TypeDefinition FindType(ModuleDefinition mod, string fullName)
        {
            var stack = new Stack<TypeDefinition>(mod.Types);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t.FullName == fullName) return t;
                foreach (var n in t.NestedTypes) stack.Push(n);
            }
            return null;
        }

        static MethodDefinition FindMethod(TypeDefinition t, string name, int paramCount)
        {
            if (t == null) return null;
            foreach (var m in t.Methods) if (m.Name == name && m.Parameters.Count == paramCount) return m;
            return null;
        }

        static bool HasMember(ModuleDefinition mod, string type, string method, int pc, OpCode op, string nameLike)
        {
            var t = FindType(mod, type);
            var m = FindMethod(t, method, pc);
            if (m == null || !m.HasBody) return false;
            foreach (var i in m.Body.Instructions)
            {
                if (i.OpCode != op) continue;
                var mr = i.Operand as MemberReference;
                if (mr != null && mr.Name.IndexOf(nameLike, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        static bool HasOpcode(ModuleDefinition mod, string type, string method, int pc, OpCode op)
        {
            var t = FindType(mod, type);
            var m = FindMethod(t, method, pc);
            if (m == null || !m.HasBody) return false;
            foreach (var i in m.Body.Instructions) if (i.OpCode == op) return true;
            return false;
        }

        static bool IsStloc(Instruction i)
        {
            var c = i.OpCode.Code;
            return c == Code.Stloc || c == Code.Stloc_S || c == Code.Stloc_0 || c == Code.Stloc_1 || c == Code.Stloc_2 || c == Code.Stloc_3;
        }

        static VariableDefinition StlocVar(Instruction i, MethodBody body)
        {
            switch (i.OpCode.Code)
            {
                case Code.Stloc_0: return body.Variables.Count > 0 ? body.Variables[0] : null;
                case Code.Stloc_1: return body.Variables.Count > 1 ? body.Variables[1] : null;
                case Code.Stloc_2: return body.Variables.Count > 2 ? body.Variables[2] : null;
                case Code.Stloc_3: return body.Variables.Count > 3 ? body.Variables[3] : null;
                case Code.Stloc:
                case Code.Stloc_S: return i.Operand as VariableDefinition;
            }
            return null;
        }

        static bool IsI4(Instruction i)
        {
            var c = i.OpCode.Code;
            return c == Code.Ldc_I4_M1 || c == Code.Ldc_I4_0 || c == Code.Ldc_I4_1 || c == Code.Ldc_I4_2 || c == Code.Ldc_I4_3
                || c == Code.Ldc_I4_4 || c == Code.Ldc_I4_5 || c == Code.Ldc_I4_6 || c == Code.Ldc_I4_7 || c == Code.Ldc_I4_8
                || c == Code.Ldc_I4_S || c == Code.Ldc_I4;
        }

        static int I4Value(Instruction i)
        {
            switch (i.OpCode.Code)
            {
                case Code.Ldc_I4_M1: return -1;
                case Code.Ldc_I4_0: return 0;
                case Code.Ldc_I4_1: return 1;
                case Code.Ldc_I4_2: return 2;
                case Code.Ldc_I4_3: return 3;
                case Code.Ldc_I4_4: return 4;
                case Code.Ldc_I4_5: return 5;
                case Code.Ldc_I4_6: return 6;
                case Code.Ldc_I4_7: return 7;
                case Code.Ldc_I4_8: return 8;
                default: return Convert.ToInt32(i.Operand);
            }
        }

        // ================= 补丁 1：下载页加载器 =================
        PatchResult PatchDownloadPage(ModuleDefinition mod)
        {
            var r = new PatchResult();
            r.Target = "PCL.PageDownloadInstall.LoaderInit（下载页加载器）";
            var t = FindType(mod, "PCL.PageDownloadInstall");
            var m = FindMethod(t, "LoaderInit", 0);
            if (m == null || !m.HasBody) { r.State = PatchState.NotFound; r.Detail = "找不到该方法"; return r; }

            var dl = FindType(mod, "PCL.ModDownload");
            FieldDefinition loaderField = null;
            if (dl != null) foreach (var f in dl.Fields) if (f.Name == "DlClientListLoader") loaderField = f;
            if (loaderField == null) { r.State = PatchState.NotFound; r.Detail = "找不到 DlClientListLoader 字段"; return r; }

            var il = m.Body.Instructions;
            bool hasRealLoader = false;
            bool hasSabotage = false;
            foreach (var i in il)
            {
                if (i.OpCode == OpCodes.Ldsfld && i.Operand == loaderField) hasRealLoader = true;
                if (i.OpCode == OpCodes.Ldstr && (i.Operand as string) == "关闭下载") hasSabotage = true;
            }
            if (hasRealLoader && !hasSabotage) { r.State = PatchState.AlreadyPatched; r.Detail = "已是真实加载器"; return r; }

            int start = -1, end = -1;
            for (int k = 0; k < il.Count; k++)
            {
                if (il[k].OpCode == OpCodes.Ldstr && (il[k].Operand as string) == "关闭下载") { start = k; break; }
            }
            if (start < 0) { r.State = PatchState.NotFound; r.Detail = "找不到“关闭下载”加载器"; return r; }
            for (int k = start; k < il.Count; k++)
            {
                var mr = il[k].Operand as MethodReference;
                if (il[k].OpCode == OpCodes.Newobj && mr != null && mr.DeclaringType.Name.StartsWith("LoaderTask`2")) { end = k; break; }
            }
            if (end < 0) { r.State = PatchState.NotFound; r.Detail = "找不到 LoaderTask 构造"; return r; }

            int removed = end - start + 1;
            il.Insert(start, Instruction.Create(OpCodes.Ldsfld, loaderField));
            for (int k = 0; k < removed; k++) il.RemoveAt(start + 1);
            if (m.Body.MaxStackSize < 4) m.Body.MaxStackSize = 4;

            r.State = PatchState.Applied;
            r.Detail = "把抛异常的“关闭下载”加载器换回 DlClientListLoader（移除 " + removed + " 条 IL）";
            log("P1 已修复：下载页加载器 -> DlClientListLoader");
            return r;
        }

        // ================= 补丁 2：整合包安装分发 =================
        PatchResult PatchModpackDispatch(ModuleDefinition mod)
        {
            var r = new PatchResult();
            r.Target = "PCL.ModModpack.ModpackInstall(String,String,String)（整合包安装分发）";
            var t = FindType(mod, "PCL.ModModpack");
            var m = FindMethod(t, "ModpackInstall", 3);
            if (m == null || !m.HasBody) { r.State = PatchState.NotFound; r.Detail = "找不到该方法"; return r; }

            var il = m.Body.Instructions;
            foreach (var i in il) if (i.OpCode == OpCodes.Switch) { r.State = PatchState.AlreadyPatched; r.Detail = "分发 switch 已存在"; return r; }

            int throwIdx = -1;
            for (int k = 0; k + 2 < il.Count; k++)
            {
                if (il[k].OpCode == OpCodes.Ldstr && (il[k].Operand as string) == SabotageMsg
                    && il[k + 1].OpCode == OpCodes.Newobj && il[k + 2].OpCode == OpCodes.Throw) { throwIdx = k; break; }
            }
            if (throwIdx < 0) { r.State = PatchState.NotFound; r.Detail = "找不到被替换成 throw 的分发点"; return r; }

            var vars = m.Body.Variables;
            VariableDefinition vArchive = null, vResult = null, vBase = null, vKind = null;
            foreach (var v in vars)
            {
                string fn = v.VariableType.FullName;
                if (vArchive == null && fn == "System.IO.Compression.ZipArchive") vArchive = v;
                if (vResult == null && fn.StartsWith("PCL.ModLoader/LoaderCombo`1")) vResult = v;
            }
            for (int k = 0; k + 3 < il.Count; k++)
            {
                if (il[k].OpCode == OpCodes.Ldnull && IsStloc(il[k + 1])
                    && il[k + 2].OpCode == OpCodes.Ldstr && (il[k + 2].Operand as string) == ""
                    && IsStloc(il[k + 3]))
                { vBase = StlocVar(il[k + 3], m.Body); if (vBase != null) break; }
            }
            var kindScore = new Dictionary<VariableDefinition, int>();
            for (int k = 1; k < il.Count; k++)
            {
                if (!IsStloc(il[k])) continue;
                var v = StlocVar(il[k], m.Body);
                if (v == null || v.VariableType.MetadataType != MetadataType.Int32) continue;
                if (!IsI4(il[k - 1])) continue;
                bool near = false;
                for (int b = Math.Max(0, k - 18); b < k; b++)
                {
                    string s = il[b].Operand as string;
                    if (il[b].OpCode == OpCodes.Ldstr && s != null && Markers.Contains(s)) { near = true; break; }
                }
                if (!near) continue;
                kindScore[v] = (kindScore.ContainsKey(v) ? kindScore[v] : 0) + 1;
            }
            if (kindScore.Count > 0) vKind = kindScore.OrderByDescending(delegate (KeyValuePair<VariableDefinition, int> p) { return p.Value; }).First().Key;

            if (vArchive == null || vResult == null || vBase == null || vKind == null)
            {
                r.State = PatchState.Skipped;
                r.Detail = "局部变量无法识别（" + (vArchive == null ? "Archive " : "") + (vResult == null ? "Result " : "")
                         + (vBase == null ? "Base " : "") + (vKind == null ? "Kind " : "") + "）";
                log("P2 跳过：" + r.Detail);
                return r;
            }

            var kindByMarker = new Dictionary<string, int>();
            for (int k = 1; k < il.Count; k++)
            {
                if (!IsStloc(il[k]) || !object.ReferenceEquals(StlocVar(il[k], m.Body), vKind)) continue;
                if (!IsI4(il[k - 1])) continue;
                int val = I4Value(il[k - 1]);
                for (int b = Math.Max(0, k - 18); b < k; b++)
                {
                    string s = il[b].Operand as string;
                    if (il[b].OpCode == OpCodes.Ldstr && s != null && Markers.Contains(s) && !kindByMarker.ContainsKey(s)) kindByMarker[s] = val;
                }
            }

            int maxKind = 9;
            if (kindByMarker.Count > 0)
                foreach (var v in kindByMarker.Values) if (v > maxKind) maxKind = v;

            var landing = Instruction.Create(OpCodes.Ldloc, vResult);
            var retIns = Instruction.Create(OpCodes.Ret);

            var blocks = new List<DispatchBlock>();
            DispatchBlock compress = BuildBlock(mod, m, "InstallPackCompress", vArchive, vBase, vResult, landing);
            if (compress == null)
            {
                r.State = PatchState.Skipped;
                r.Detail = "找不到 InstallPackCompress，无法生成默认分支";
                log("P2 跳过：" + r.Detail);
                return r;
            }
            blocks.Add(compress);

            var caseTarget = new Dictionary<int, DispatchBlock>();
            for (int i = 0; i < KindMarkers.Length; i++)
            {
                int kind;
                if (!kindByMarker.TryGetValue(KindMarkers[i], out kind)) continue;
                if (caseTarget.ContainsKey(kind)) continue;
                var blk = BuildBlock(mod, m, KindMethods[i], vArchive, vBase, vResult, landing);
                if (blk == null) continue;
                bool dup = false;
                foreach (var b in blocks) if (b.Method == blk.Method) { blk = b; dup = true; break; }
                if (!dup) blocks.Add(blk);
                caseTarget[kind] = blk;
            }

            var switchTargets = new Instruction[maxKind + 1];
            for (int k = 0; k <= maxKind; k++)
            {
                DispatchBlock b;
                switchTargets[k] = (caseTarget.TryGetValue(k, out b) ? b : compress).FirstInstruction;
            }

            var head = new List<Instruction>();
            head.Add(Instruction.Create(OpCodes.Ldloc, vKind));
            head.Add(Instruction.Create(OpCodes.Switch, switchTargets));
            head.Add(Instruction.Create(OpCodes.Br, compress.FirstInstruction));

            var all = new List<Instruction>();
            all.AddRange(head);
            foreach (var b in blocks) all.AddRange(b.Instructions);

            il.RemoveAt(throwIdx);
            il.RemoveAt(throwIdx);
            il.RemoveAt(throwIdx);
            for (int k = all.Count - 1; k >= 0; k--) il.Insert(throwIdx, all[k]);

            // 收尾块必须追加到方法末尾（在 finally 处理块之后），否则异常表区间会倒置
            il.Add(landing);
            il.Add(retIns);

            foreach (var h in m.Body.ExceptionHandlers) if (h.HandlerEnd == null) h.HandlerEnd = landing;

            m.Body.MaxStackSize = Math.Max(m.Body.MaxStackSize, 8);

            r.State = PatchState.Applied;
            r.Detail = "生成 " + all.Count + " 条 IL 的 kind 分发（标记映射 " + kindByMarker.Count + " 项，默认 InstallPackCompress）";
            log("P2 已修复：整合包分发 switch（" + all.Count + " 条 IL，分支 " + blocks.Count + " 个）");
            return r;
        }

        class DispatchBlock
        {
            public MethodDefinition Method;
            public List<Instruction> Instructions = new List<Instruction>();
            public Instruction FirstInstruction { get { return Instructions[0]; } }
        }

        DispatchBlock BuildBlock(ModuleDefinition mod, MethodDefinition host, string methodName, VariableDefinition vArchive, VariableDefinition vBase, VariableDefinition vResult, Instruction landing)
        {
            var target = FindMethod(host.DeclaringType, methodName, -1);
            int argCount = CountArgs(methodName);
            target = FindMethod(host.DeclaringType, methodName, argCount);
            if (target == null) return null;

            var blk = new DispatchBlock();
            blk.Method = target;
            blk.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            blk.Instructions.Add(Instruction.Create(OpCodes.Ldloc, vArchive));
            if (argCount >= 3) blk.Instructions.Add(Instruction.Create(OpCodes.Ldloc, vBase));
            if (argCount >= 4) blk.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
            if (argCount >= 5) blk.Instructions.Add(Instruction.Create(OpCodes.Ldarg_2));
            blk.Instructions.Add(Instruction.Create(OpCodes.Call, target));
            blk.Instructions.Add(Instruction.Create(OpCodes.Stloc, vResult));
            blk.Instructions.Add(Instruction.Create(OpCodes.Leave, landing));
            return blk;
        }

        static int CountArgs(string method)
        {
            if (method == "InstallPackCurseForge" || method == "InstallPackModrinth") return 5;
            if (method == "InstallPackMCBBS") return 4;
            if (method == "InstallPackCompress") return 2;
            return 3;
        }

        // ================= 补丁 3：本地导入整合包入口 =================
        PatchResult PatchModpackPicker(ModuleDefinition mod)
        {
            var r = new PatchResult();
            r.Target = "PCL.ModModpack.ModpackInstall()（本地导入整合包入口）";
            var t = FindType(mod, "PCL.ModModpack");
            var m = FindMethod(t, "ModpackInstall", 0);
            if (m == null || !m.HasBody) { r.State = PatchState.NotFound; r.Detail = "找不到该方法"; return r; }

            foreach (var i in m.Body.Instructions)
            {
                var mr = i.Operand as MemberReference;
                if (i.OpCode == OpCodes.Call && mr != null && mr.Name == "SelectFile")
                { r.State = PatchState.AlreadyPatched; r.Detail = "文件选择框已存在"; return r; }
            }

            MethodReference selFile = null, firstOrDefault = null;
            foreach (var mr in mod.GetMemberReferences())
            {
                var mref = mr as MethodReference;
                if (mref == null) continue;
                if (selFile == null && mref.Name == "SelectFile" && mref.Parameters.Count == 4) selFile = mref;
                if (firstOrDefault == null && mref.Name == "FirstOrDefault" && mref.Parameters.Count == 1
                    && !(mref is GenericInstanceMethod) && mref.DeclaringType.FullName == "System.Linq.Enumerable")
                    firstOrDefault = mref;
            }
            var runInThread = FindMethod(FindType(mod, "PCL.ModBase"), "RunInThread", 1);

            TypeDefinition closureType = null;
            try { if (m.Body.Variables.Count > 0) closureType = m.Body.Variables[0].VariableType.Resolve(); }
            catch { }
            FieldDefinition fileField = null;
            MethodDefinition worker = null;
            MethodDefinition ctor = null;
            if (closureType != null)
            {
                foreach (var f in closureType.Fields) if (f.Name.EndsWith("$VB$Local_File")) fileField = f;
                foreach (var mm in closureType.Methods)
                {
                    if (worker == null && mm.Name.StartsWith("_Lambda$__0") && mm.Parameters.Count == 0) worker = mm;
                    if (ctor == null && mm.IsConstructor && mm.Parameters.Count == 0) ctor = mm;
                }
            }

            if (selFile == null || firstOrDefault == null || runInThread == null || fileField == null || worker == null || ctor == null)
            {
                r.State = PatchState.Skipped;
                r.Detail = "缺少生成条件（" + (selFile == null ? "SelectFile " : "") + (firstOrDefault == null ? "FirstOrDefault " : "")
                         + (runInThread == null ? "RunInThread " : "") + (fileField == null ? "文件字段 " : "") + (worker == null ? "工作方法 " : "") + "）";
                log("P3 跳过：本地导入入口未修复（不影响下载与安装）。" + r.Detail);
                return r;
            }

            var nullableType = selFile.Parameters[3].ParameterType as GenericInstanceType;
            if (nullableType == null)
            {
                r.State = PatchState.Skipped; r.Detail = "SelectFile 过滤器参数类型异常";
                log("P3 跳过：" + r.Detail);
                return r;
            }

            var vNullable = new VariableDefinition(nullableType);
            m.Body.Variables.Add(vNullable);
            var closureLocal = m.Body.Variables[0];

            var fom = new GenericInstanceMethod(firstOrDefault);
            fom.GenericArguments.Add(mod.TypeSystem.String);

            var actionType = new TypeReference("System", "Action", mod, mod.TypeSystem.CoreLibrary);
            var actionCtor = new MethodReference(".ctor", mod.TypeSystem.Void, actionType);
            actionCtor.HasThis = true;
            actionCtor.Parameters.Add(new ParameterDefinition(mod.TypeSystem.Object));
            actionCtor.Parameters.Add(new ParameterDefinition(mod.TypeSystem.IntPtr));

            var isNullOrEmpty = new MethodReference("IsNullOrEmpty", mod.TypeSystem.Boolean, mod.TypeSystem.String);
            isNullOrEmpty.HasThis = false;
            isNullOrEmpty.Parameters.Add(new ParameterDefinition(mod.TypeSystem.String));

            var retIns = Instruction.Create(OpCodes.Ret);

            var code = new List<Instruction>();
            code.Add(Instruction.Create(OpCodes.Newobj, ctor));
            code.Add(Instruction.Create(OpCodes.Stloc, closureLocal));
            code.Add(Instruction.Create(OpCodes.Ldloc, closureLocal));
            code.Add(Instruction.Create(OpCodes.Ldstr, "选择整合包压缩文件"));
            code.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            code.Add(Instruction.Create(OpCodes.Ldnull));
            code.Add(Instruction.Create(OpCodes.Ldloca, vNullable));
            code.Add(Instruction.Create(OpCodes.Initobj, nullableType));
            code.Add(Instruction.Create(OpCodes.Ldloc, vNullable));
            code.Add(Instruction.Create(OpCodes.Call, selFile));
            code.Add(Instruction.Create(OpCodes.Call, fom));
            code.Add(Instruction.Create(OpCodes.Stfld, fileField));
            code.Add(Instruction.Create(OpCodes.Ldloc, closureLocal));
            code.Add(Instruction.Create(OpCodes.Ldfld, fileField));
            code.Add(Instruction.Create(OpCodes.Call, isNullOrEmpty));
            code.Add(Instruction.Create(OpCodes.Brtrue, retIns));
            code.Add(Instruction.Create(OpCodes.Ldloc, closureLocal));
            code.Add(Instruction.Create(OpCodes.Ldftn, worker));
            code.Add(Instruction.Create(OpCodes.Newobj, actionCtor));
            code.Add(Instruction.Create(OpCodes.Call, runInThread));

            m.Body.Instructions.Clear();
            foreach (var i in code) m.Body.Instructions.Add(i);
            m.Body.Instructions.Add(retIns);
            m.Body.InitLocals = true;
            m.Body.MaxStackSize = 8;

            r.State = PatchState.Applied;
            r.Detail = "恢复文件选择框 + 后台线程导入（" + code.Count + " 条 IL）";
            log("P3 已修复：本地导入整合包入口");
            return r;
        }
    }
}
