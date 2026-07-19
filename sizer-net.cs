/*
  Sizer.Net
  Written by Bernhard Schelling
  https://github.com/schellingb/sizer-net/

  This is free and unencumbered software released into the public domain.

  Anyone is free to copy, modify, publish, use, compile, sell, or
  distribute this software, either in source code form or as a compiled
  binary, for any purpose, commercial or non-commercial, and by any
  means.

  In jurisdictions that recognize copyright laws, the author or authors
  of this software dedicate any and all copyright interest in the
  software to the public domain. We make this dedication for the benefit
  of the public at large and to the detriment of our heirs and
  successors. We intend this dedication to be an overt act of
  relinquishment in perpetuity of all present and future rights to this
  software under copyright law.

  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
  EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
  MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
  IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR
  OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
  ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
  OTHER DEALINGS IN THE SOFTWARE.

  For more information, please refer to <http://unlicense.org/>
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Sizer.Net")]
[assembly: AssemblyProduct("sizer-net")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]

static class SizerNet
{
    //size of a tree node, categorized so bars and tooltips can show metadata vs MSIL code vs resources/data
    class NodeSize
    {
        public const int ILCode = 0, Data = 1, MetaTypes = 2, MetaMethods = 3, MetaFields = 4, MetaProperties = 5, MetaEvents = 6, MetaAttributes = 7, MetaOther = 8, NumKinds = 9;
        public static readonly string[] KindNames = { null, null, "types", "methods", "fields", "properties", "events", "custom attributes", "other" };
        public long[] Sizes = new long[NumKinds];
        public long Code { get { return Sizes[ILCode] + Sizes[Data]; } }
        public long Metadata { get { long m = 0; for (int i = MetaTypes; i != NumKinds; i++) m += Sizes[i]; return m; } }
        public long Total { get { long t = 0; foreach (long s in Sizes) t += s; return t; } }
    }

    static Form f;
    static TreeView tv;
    static int TreeViewScrollX = 0;
    static string AssemblyPath;
    static long AssemblySize;
    static List<string> DependencyDirs = new List<string>(), IgnoredDependencies = new List<string>();

    [STAThread] static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        f = new Form();
        f.KeyPreview = true;
        f.KeyUp += (object sender, KeyEventArgs e) => { if (e.KeyCode == Keys.Escape) ((Form)sender).Close(); };
        f.Text = "Sizer.Net";
        f.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
        f.ClientSize = new Size(800, 700);

        tv = new TreeView();
        tv.Location = new Point(13, 13);
        tv.Size = new Size(f.ClientSize.Width - 13 - 13, f.ClientSize.Height - 13 - 23 - 13 - 13);
        tv.DrawMode = TreeViewDrawMode.OwnerDrawText;
        tv.ShowNodeToolTips = true;
        tv.Anchor = (AnchorStyles)(AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
        tv.Resize += (object sender, EventArgs e) => { tv.Invalidate(); };
        tv.DrawNode += OnTreeDrawNode;
        f.Controls.Add(tv);

        Button btnLoad = new Button();
        btnLoad.Location = new Point(13, f.ClientSize.Height - 13 - 23);
        btnLoad.Size = new Size(500 - 13, 23);
        btnLoad.Anchor = (AnchorStyles)(AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
        btnLoad.Text = "Load Assembly";
        btnLoad.Click += (object sender, EventArgs e) => { BrowseAssembly(); };
        f.Controls.Add(btnLoad);

        Button btnClose = new Button();
        btnClose.Location = new Point(513, f.ClientSize.Height - 13 - 23);
        btnClose.Size = new Size(f.ClientSize.Width - 513 - 13, 23);
        btnClose.Anchor = (AnchorStyles)(AnchorStyles.Bottom | AnchorStyles.Right);
        btnClose.Text = "Close";
        btnClose.Click += (object sender, EventArgs e) => { f.Close(); };
        f.Controls.Add(btnClose);

        if (args.Length > 0)
        {
            if (new FileInfo(args[0]).Exists == false)
            {
                MessageBox.Show("Assembly not found: " + args[0], "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }
            f.Shown += (object sender, EventArgs e) => { LoadAssembly(args[0], true); };
        }
        else f.Shown += (object sender, EventArgs e) => { BrowseAssembly(true); };

        Application.Run(f);
    }

    static void BrowseAssembly(bool InitialLoad = false)
    {
        using (var ofd = new OpenFileDialog())
        {
            ofd.ValidateNames = ofd.CheckFileExists = ofd.CheckPathExists = true;
            ofd.Filter = ".Net Assemblies (*.exe, *.dll)|*.exe;*.dll";
            if (ofd.ShowDialog() != DialogResult.OK) { if (InitialLoad) f.Close(); return; }
            LoadAssembly(ofd.FileName, InitialLoad);
        }
    }

    static void OnTreeDrawNode(object sender, DrawTreeNodeEventArgs e)
    {
        e.DrawDefault = true;
        if (tv.Nodes.Count == 0 || e.Bounds.Height == 0) return;
        NodeSize ns = (NodeSize)e.Node.Tag;
        float pct = (float)ns.Total / AssemblySize;
        int w = tv.ClientSize.Width / 4, x = tv.ClientSize.Width - w - 5, size = (int)(w * pct);
        int sizeMeta = (ns.Total == 0 ? 0 : (int)(size * ((float)ns.Metadata / ns.Total)));
        e.Graphics.FillRectangle(Brushes.White,          x,            e.Bounds.Top + 1, w    + 1,        e.Bounds.Height - 2);
        e.Graphics.FillRectangle(Brushes.LightSteelBlue, x,            e.Bounds.Top + 1, sizeMeta + 1,    e.Bounds.Height - 2);
        e.Graphics.FillRectangle(Brushes.LightGray,      x + sizeMeta, e.Bounds.Top + 1, size - sizeMeta + 1, e.Bounds.Height - 2);
        e.Graphics.DrawRectangle(Pens.DarkGray,          x,            e.Bounds.Top + 1, w    + 1,        e.Bounds.Height - 2);
        e.Graphics.DrawString(FormatKb(ns.Total), tv.Font, Brushes.DarkSlateGray, x, e.Bounds.Top + 1);
        e.Graphics.DrawLine((e.State & TreeNodeStates.Selected) != 0 ? SystemPens.Highlight : SystemPens.ControlLight, e.Bounds.Left + 5, e.Bounds.Top + e.Bounds.Height/2, x - 5, e.Bounds.Top + e.Bounds.Height/2);
        if (tv.Nodes[0].Bounds.X != TreeViewScrollX) { TreeViewScrollX = tv.Nodes[0].Bounds.X; tv.Invalidate(); }
    }

    //estimated numbers for byte size of overhead introduced by various things
    const int Overhead_Type            = 4+8*2;
    const int Overhead_Field           = 2+2*2;
    const int Overhead_Method          = 8+6*2;
    const int Overhead_LocalVariable   = 4+1*2;
    const int Overhead_Parameter       = 4+1*2;
    const int Overhead_InterfaceImpl   = 0+2*2;
    const int Overhead_Event           = 2+2*2;
    const int Overhead_Property        = 2+2*2;
    const int Overhead_CustomAttribute = 0+3*2;

    static void LoadAssembly(string InAssemblyPath, bool InitialLoad = false)
    {
        AssemblyPath = InAssemblyPath;
        try
        {
            tv.Nodes.Clear();
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveExternalAssembly;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveExternalAssembly;
            AssemblyPath = new FileInfo(AssemblyPath).FullName;
            DependencyDirs = new List<string> { Path.GetDirectoryName(AssemblyPath), Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) };
            IgnoredDependencies = new List<string>();
            Assembly assembly = Assembly.LoadFile(AssemblyPath);
            bool IsReflectionOnly = false;
            AssemblySize = new FileInfo(assembly.Location).Length;
            if (AssemblyPath != assembly.Location && !FileContentsMatch(AssemblyPath, assembly.Location))
            {
                MessageBox.Show("Requested assembly:\n" + AssemblyPath + "\n\nAssembly loaded by system:\n" + assembly.Location + "\n\nA different assembly was loaded because an assembly with the same name exists in the global assembly cache.\n\nResorting to loading the assembly in 'reflection only' mode which disables dependency resolving which can make certain type evaluations impossible.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                assembly = Assembly.ReflectionOnlyLoadFrom(AssemblyPath);
                IsReflectionOnly = true;
            }

            BindingFlags all = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            BindingFlags statics = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            TreeNode nAssembly = new TreeNode(assembly.GetName().Name);
            nAssembly.Tag = new NodeSize();

            TreeNode nResources = nAssembly.Nodes.Add("Resources");
            nResources.Tag = new NodeSize();

            //Enumerate Win32 resources
            try
            {
                IntPtr AssemblyHandle = LoadLibraryEx(AssemblyPath, IntPtr.Zero, LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);
                if (AssemblyHandle == IntPtr.Zero) throw new Exception();
                EnumResourceTypes(AssemblyHandle, new EnumResTypeProc((IntPtr hModule, IntPtr lpszType, IntPtr lParam) =>
                {
                    try { lpszType.ToInt32(); } catch (Exception) { return true; }
                    EnumResourceNames(hModule, lpszType.ToInt32(), new EnumResNameProc((IntPtr hModule2, IntPtr lpszType2, IntPtr lpzName, IntPtr lParam2) =>
                    {
                        ResType rt =  unchecked((ResType)(long)lpszType2);
                        IntPtr hResource = FindResource(hModule2, lpzName, lpszType2);
                        long Size = SizeofResource(hModule2, hResource);

                        string name = System.Runtime.InteropServices.Marshal.PtrToStringUni(lpzName);
                        TreeNode nResource = nResources.Nodes.Add("Resource: " + rt.ToString() + " " + (name == null ? "#" + lpzName.ToInt64() : name));
                        SetNodeTag(nResource, NodeSize.Data, Size);

                        return true;
                    }), IntPtr.Zero);
                    return true;
                }), IntPtr.Zero);
                FreeLibrary(AssemblyHandle);
            }
            catch (Exception) { } //ignore Win32 resources

            //Enumerate manifest resources
            foreach (string mr in assembly.GetManifestResourceNames())
            {
                ResourceLocation rl = assembly.GetManifestResourceInfo(mr).ResourceLocation;
                if ((rl & ResourceLocation.Embedded) == 0 || (rl & ResourceLocation.ContainedInAnotherAssembly) != 0) continue;
                TreeNode nResource = nResources.Nodes.Add("Manifest Resource: " + mr);
                Stream mrs = assembly.GetManifestResourceStream(mr);
                SetNodeTag(nResource, NodeSize.Data, mrs.Length);
                mrs.Dispose();
            }

            foreach (Module module in assembly.GetModules())
            {
                foreach (MethodInfo mi in module.GetMethods(all)) AddMethodNode(nAssembly, mi);

                int lenModuleFields = 0;
                foreach (FieldInfo fi in module.GetFields(all)) lenModuleFields += Overhead_Field + fi.Name.Length;
                if (lenModuleFields != 0) { TreeNode nModuleInfo = nAssembly.Nodes.Add(module.GetFields(all).Length.ToString() + " Fields in " + module.Name + " (Overhead)"); SetNodeTag(nModuleInfo, NodeSize.MetaFields, lenModuleFields);     }
            }

            Type[] AssemblyTypes;
            try { AssemblyTypes = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { AssemblyTypes = e.Types; }

            int UnresolvedTypes = 0;
            foreach (Type type in AssemblyTypes)
            {
                if (type == null) { UnresolvedTypes++; continue; }
                TreeNode nType = nAssembly;
                bool IsStaticArrayInitType = type.Name.Contains("StaticArrayInitTypeSize=");
                foreach (string NSPart in type.FullName.Split('.', '+'))
                {
                    if (nType.Nodes.ContainsKey(NSPart)) nType = nType.Nodes[NSPart];
                    else (nType = nType.Nodes.Add(NSPart, NSPart)).Tag = new NodeSize();
                }

                int lenType = Overhead_Type + type.FullName.Length, lenTypeAttrs = 0;
                try { foreach (Type it in type.GetInterfaces()) lenType += Overhead_InterfaceImpl; } catch { }
                #if DOTNET35
                try { foreach (object ca in type.GetCustomAttributes(false)) lenTypeAttrs += Overhead_CustomAttribute; } catch { }
                #else
                try { foreach (CustomAttributeData ad in type.GetCustomAttributesData()) lenTypeAttrs += Overhead_CustomAttribute; } catch { }
                #endif
                SetNodeTag(nType, NodeSize.MetaTypes, lenType);
                if (lenTypeAttrs != 0) SetNodeTag(nType, NodeSize.MetaAttributes, lenTypeAttrs);

                foreach (FieldInfo fi in type.GetFields(statics))
                {
                    try
                    {
                        if (fi.FieldType.ContainsGenericParameters || fi.FieldType.IsGenericType) continue;
                        long fiSize = CalculateSize(IsReflectionOnly, fi.FieldType, fi);
                        if (fiSize > 0) SetNodeTag(nType.Nodes.Add("Static Field: " + fi.Name), NodeSize.Data, fiSize);
                    }
                    catch (Exception) { }
                }

                int numTypeFields = 0, numTypeProperties = 0, numTypeEvents = 0, lenTypeFields = 0, lenTypeProperties = 0, lenTypeEvents = 0;
                foreach (FieldInfo    fi in type.GetFields(all))     { numTypeFields++;     lenTypeFields     += Overhead_Field    + fi.Name.Length;                         }
                foreach (PropertyInfo pi in type.GetProperties(all)) { numTypeProperties++; lenTypeProperties += Overhead_Property + (pi.Name == null ? 0 : pi.Name.Length); }
                foreach (EventInfo    ei in type.GetEvents(all))     { numTypeEvents++;     lenTypeEvents     += Overhead_Event    + ei.Name.Length;                         }
                if (lenTypeFields     != 0) SetNodeTag(nType.Nodes.Add(numTypeFields.ToString()     + " Fields (Overhead)"),     NodeSize.MetaFields,     lenTypeFields);
                if (lenTypeProperties != 0) SetNodeTag(nType.Nodes.Add(numTypeProperties.ToString() + " Properties (Overhead)"), NodeSize.MetaProperties, lenTypeProperties);
                if (lenTypeEvents     != 0) SetNodeTag(nType.Nodes.Add(numTypeEvents.ToString()     + " Events (Overhead)"),     NodeSize.MetaEvents,     lenTypeEvents);

                foreach (ConstructorInfo ci in type.GetConstructors(all)) AddMethodNode(nType, ci);
                foreach (MethodInfo      mi in type.GetMethods(all))      AddMethodNode(nType, mi);
            }

            SetNodeTag(nAssembly.Nodes.Add("Other Overhead"), NodeSize.MetaOther, AssemblySize - ((NodeSize)nAssembly.Tag).Total);
            SortNodesByTag(nAssembly.Nodes);
            //FilterNodeByTag(nAssembly.Nodes, AssemblySize/100);
            nAssembly.Expand();
            tv.Nodes.Add(nAssembly);
            SetNodeTooltips(tv.Nodes);

            if (UnresolvedTypes != 0)
            {
                MessageBox.Show(UnresolvedTypes.ToString() + " types could not be evaluated due to missing dependency errors.\nThese are included in the 'Other Overhead' entry.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception e)
        {
            MessageBox.Show("Assembly loading error:\n\n" + e.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            if (InitialLoad) f.Close();
        }
    }
    
    static void AddMethodNode(TreeNode ParentNode, MethodBase mi)
    {
        TreeNode nMethod = ParentNode.Nodes.Add(mi.Name);
        int lenMeta = Overhead_Method + mi.Name.Length, lenAttrs = 0;
        long lenCode = 0;
        try { var mb = mi.GetMethodBody(); lenCode += mb.GetILAsByteArray().Length; foreach (LocalVariableInfo lvi in mb.LocalVariables) lenMeta += Overhead_LocalVariable; } catch { }
        try { foreach (ParameterInfo pi in mi.GetParameters()) lenMeta += 16 + (pi.Name == null ? 0 : pi.Name.Length); } catch { }
        #if DOTNET35
        try { foreach (object ca in mi.GetCustomAttributes(false)) lenAttrs += Overhead_CustomAttribute; } catch { }
        #else
        try { foreach (CustomAttributeData ad in mi.GetCustomAttributesData()) lenAttrs += Overhead_CustomAttribute; } catch { }
        #endif
        SetNodeTag(nMethod, NodeSize.MetaMethods, lenMeta);
        if (lenAttrs != 0) SetNodeTag(nMethod, NodeSize.MetaAttributes, lenAttrs);
        if (lenCode != 0) SetNodeTag(nMethod, NodeSize.ILCode, lenCode);
    }

    static Assembly ResolveExternalAssembly(object sender, ResolveEventArgs args)
    {
        if (IgnoredDependencies.Contains(args.Name)) return null;

        string DllFileName = new AssemblyName(args.Name).Name + ".dll";
        foreach (string dir in DependencyDirs)
        {
            string TestAssemblyPath = Path.Combine(dir, DllFileName);
            if (File.Exists(TestAssemblyPath)) return Assembly.LoadFile(TestAssemblyPath);
        }

        using (var ofd = new OpenFileDialog())
        {
            ofd.Title = "Find dependency: " + args.Name;
            ofd.ValidateNames = ofd.CheckFileExists = ofd.CheckPathExists = true;
            ofd.FileName = DllFileName;
            ofd.Filter = ".Net Dependencies (*.dll)|*.dll";
            if (ofd.ShowDialog() != DialogResult.OK)
            {
                IgnoredDependencies.Add(args.Name);
                return null;
            }
            DependencyDirs.Add(Path.GetDirectoryName(ofd.FileName));
            return Assembly.LoadFile(ofd.FileName);
        }
    }

    static void SetNodeTag(TreeNode n, int SizeKind, long Amount)
    {
        for (; n != null; n = n.Parent)
        {
            NodeSize ns = n.Tag as NodeSize;
            if (ns == null) n.Tag = ns = new NodeSize();
            ns.Sizes[SizeKind] += Amount;
        }
    }

    static string Percent(long Part, long Total)
    {
        return (100f * Part / Total).ToString("0") + "%";
    }

    static void SetNodeTooltips(TreeNodeCollection nc)
    {
        foreach (TreeNode n in nc)
        {
            NodeSize ns = (NodeSize)n.Tag;
            long total = ns.Total;
            if (total != 0)
            {
                string tip = Percent(ns.Metadata, total) + " metadata, " + Percent(ns.Sizes[NodeSize.ILCode], total) + " MSIL code";
                if (ns.Sizes[NodeSize.Data] != 0) tip += ", " + Percent(ns.Sizes[NodeSize.Data], total) + " resources/data";
                string meta = "";
                for (int i = NodeSize.MetaTypes; i != NodeSize.NumKinds; i++)
                    if (ns.Sizes[i] != 0) meta += (meta.Length == 0 ? "" : ", ") + NodeSize.KindNames[i] + " " + FormatKb(ns.Sizes[i]);
                if (meta.Length != 0) tip += "\r\nMetadata: " + meta;
                n.ToolTipText = tip;
            }
            SetNodeTooltips(n.Nodes);
        }
    }

    static void SortNodesByTag(TreeNodeCollection nc)
    {
        foreach (TreeNode n in nc) SortNodesByTag(n.Nodes);
        TreeNode[] ns = new TreeNode[nc.Count];
        nc.CopyTo(ns, 0);
        Array.Sort<TreeNode>(ns, (TreeNode a, TreeNode b) => ((NodeSize)b.Tag).Total.CompareTo(((NodeSize)a.Tag).Total));
        nc.Clear();
        nc.AddRange(ns);
    }

    static string FormatKb(long bytes)
    {
        return (bytes / 1024f).ToString("0.##") + " kb";
    }

    static void FilterNodeByTag(TreeNodeCollection nc, long Threshold)
    {
        NodeSize Removed = new NodeSize();
        for (int i = 0; i != nc.Count; i++)
        {
            NodeSize ns = (NodeSize)nc[i].Tag;
            if (ns.Total < Threshold) { for (int k = 0; k != NodeSize.NumKinds; k++) Removed.Sizes[k] += ns.Sizes[k]; nc[i].Remove(); i--; continue; }
            FilterNodeByTag(nc[i].Nodes, Threshold);
        }
        if (Removed.Total != 0) { nc.Add("... <Filtered> ...").Tag = Removed; }
    }

    static long CalculateSize(bool IsReflectionOnly, Type t, object FiOrValue = null)
    {
        if (t.IsArray)
        {
            System.Array a = (System.Array)(FiOrValue is FieldInfo ? (IsReflectionOnly ? ((FieldInfo)FiOrValue).GetRawConstantValue() : ((FieldInfo)FiOrValue).GetValue(null)) : FiOrValue);
            if (a == null || a.LongLength == 0) return 0;
            t = t.GetElementType();
            if (t.IsEnum) t = Enum.GetUnderlyingType(t);
            if (!t.ContainsGenericParameters && !t.IsGenericType && (t.IsValueType || t.IsPointer || t.IsLayoutSequential)) return a.LongLength * System.Runtime.InteropServices.Marshal.SizeOf(t);
            if (!t.IsArray && t != typeof(string)) return 0; //can't measure size
            long res = 0;
            foreach (object v in a) res += CalculateSize(IsReflectionOnly, t, v);
            return res;
        }
        if (t == typeof(string))
        {
            string s = (string)(FiOrValue is FieldInfo ? (IsReflectionOnly ? ((FieldInfo)FiOrValue).GetRawConstantValue() : ((FieldInfo)FiOrValue).GetValue(null)) : FiOrValue);
            return (s == null ? 0 : s.Length * 2);
        }
        if (t.IsEnum) t = Enum.GetUnderlyingType(t);
        return (!t.ContainsGenericParameters && !t.IsGenericType && (t.IsValueType || t.IsPointer || t.IsLayoutSequential) ? System.Runtime.InteropServices.Marshal.SizeOf(t) : 0);
    }

    static bool FileContentsMatch(string path1, string path2)
    {
        FileInfo fi1 = new FileInfo(path1), fi2 = new FileInfo(path2);
        if (!fi1.Exists || !fi2.Exists || fi1.Length != fi2.Length) return false;
        using (FileStream stream1 = fi1.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
        using (FileStream stream2 = fi2.Open(FileMode.Open, FileAccess.Read, FileShare.Read))
        for (byte[] buf1 = new byte[4096], buf2 = new byte[4096];;)
        {
            int count = stream1.Read(buf1, 0, 4096); stream2.Read(buf2, 0, 4096);
            if (count == 0) return true;
            for (int i = 0; i < count; i += sizeof(Int64))
                if (BitConverter.ToInt64(buf1, i) != BitConverter.ToInt64(buf2, i))
                    return false;
        }
    }

    //PInvoke definitions for Win32 resource enumeration
    enum LoadLibraryFlags : uint { LOAD_LIBRARY_AS_DATAFILE = 2 };
    enum ResType { Cursor = 1, Bitmap, Icon, Menu, Dialog, String, FontDir, Font, Accelerator, RCData, MessageTable, CursorGroup, IconGroup = 14, VersionInfo = 16, DLGInclude, PlugPlay = 19, VXD, AnimatedCursor, AnimatedIcon, HTML, Manifest };
    delegate bool EnumResTypeProc(IntPtr hModule, IntPtr lpszType, IntPtr lParam);
    delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, LoadLibraryFlags dwFlags);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool EnumResourceTypes(IntPtr hModule, EnumResTypeProc lpEnumFunc, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool EnumResourceNames(IntPtr hModule, int dwID, EnumResNameProc lpEnumFunc, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("Kernel32.dll")] static extern IntPtr FindResource(IntPtr hModule, IntPtr lpszName, IntPtr lpszType);
    [System.Runtime.InteropServices.DllImport("Kernel32.dll")] static extern uint SizeofResource(IntPtr hModule, IntPtr hResource);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr hModule);
}
