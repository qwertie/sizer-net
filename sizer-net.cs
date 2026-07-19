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
    static ToolTip BarToolTip = new ToolTip();
    static TreeNode BarToolTipNode = null;
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
        tv.ShowNodeToolTips = false; //tooltip is shown manually when the mouse is over the size bar (see OnTreeMouseMove)
        tv.Anchor = (AnchorStyles)(AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
        tv.Resize += (object sender, EventArgs e) => { tv.Invalidate(); };
        tv.DrawNode += OnTreeDrawNode;
        BarToolTip.AutoPopDelay = 32767;
        tv.MouseMove += OnTreeMouseMove;
        tv.MouseLeave += (object sender, EventArgs e) => { BarToolTip.Hide(tv); BarToolTipNode = null; };
        var cms = new ContextMenuStrip();
        cms.Items.Add("Copy to clipboard", null, (object sender, EventArgs e) => { if (tv.SelectedNode != null) Clipboard.SetText(GetSubtreeText(tv.SelectedNode)); });
        cms.Opening += (object sender, System.ComponentModel.CancelEventArgs e) =>
        {
            TreeNode n = tv.GetNodeAt(tv.PointToClient(Cursor.Position));
            if (n == null) e.Cancel = true; else tv.SelectedNode = n;
        };
        tv.ContextMenuStrip = cms;
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
        int sizeCode = (ns.Total == 0 ? 0 : (int)(size * ((float)ns.Sizes[NodeSize.ILCode] / ns.Total)));
        e.Graphics.FillRectangle(Brushes.White,          x,                       e.Bounds.Top + 1, w    + 1,                     e.Bounds.Height - 2);
        e.Graphics.FillRectangle(Brushes.LightGray,      x,                       e.Bounds.Top + 1, sizeMeta + 1,                 e.Bounds.Height - 2);
        e.Graphics.FillRectangle(Brushes.LightSteelBlue, x + sizeMeta,            e.Bounds.Top + 1, sizeCode + 1,                 e.Bounds.Height - 2);
        e.Graphics.FillRectangle(Brushes.DarkSeaGreen,   x + sizeMeta + sizeCode, e.Bounds.Top + 1, size - sizeMeta - sizeCode + 1, e.Bounds.Height - 2);
        e.Graphics.DrawRectangle(Pens.DarkGray,          x,            e.Bounds.Top + 1, w    + 1,        e.Bounds.Height - 2);
        e.Graphics.DrawString(FormatKb(ns.Total), tv.Font, Brushes.DarkSlateGray, x, e.Bounds.Top + 1);
        e.Graphics.DrawLine((e.State & TreeNodeStates.Selected) != 0 ? SystemPens.Highlight : SystemPens.ControlLight, e.Bounds.Left + 5, e.Bounds.Top + e.Bounds.Height/2, x - 5, e.Bounds.Top + e.Bounds.Height/2);
        if (tv.Nodes[0].Bounds.X != TreeViewScrollX) { TreeViewScrollX = tv.Nodes[0].Bounds.X; tv.Invalidate(); }
    }

    //shows the size breakdown tooltip only while the mouse is over the owner-drawn size bar (same geometry as OnTreeDrawNode)
    static void OnTreeMouseMove(object sender, MouseEventArgs e)
    {
        int w = tv.ClientSize.Width / 4, x = tv.ClientSize.Width - w - 5;
        TreeNode n = tv.Nodes.Count == 0 ? null : tv.GetNodeAt(e.X, e.Y);
        bool overBar = n != null && !string.IsNullOrEmpty(n.ToolTipText) && e.X >= x && e.X <= x + w;
        if (overBar)
        {
            if (n != BarToolTipNode)
            {
                BarToolTipNode = n;
                BarToolTip.Show(n.ToolTipText, tv, e.X + 16, e.Y + 20);
            }
        }
        else if (BarToolTipNode != null)
        {
            BarToolTip.Hide(tv);
            BarToolTipNode = null;
        }
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
            Exception[] TypeLoadErrors = null;
            try { AssemblyTypes = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { AssemblyTypes = e.Types; TypeLoadErrors = e.LoaderExceptions; }

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

            if (UnresolvedTypes != 0) ShowUnresolvedTypes(assembly, AssemblyTypes, TypeLoadErrors);
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

        AssemblyName WantedName = new AssemblyName(args.Name);
        string DllFileName = WantedName.Name + ".dll";
        foreach (string dir in DependencyDirs)
        {
            string TestAssemblyPath = Path.Combine(dir, DllFileName);
            if (File.Exists(TestAssemblyPath) && !IsCoreRuntimeAssembly(TestAssemblyPath)) return Assembly.LoadFile(TestAssemblyPath);
        }

        foreach (string GuessedPath in GuessDependencyPaths(WantedName))
        {
            try
            {
                Assembly GuessedAssembly = Assembly.LoadFile(GuessedPath);
                DependencyDirs.Add(Path.GetDirectoryName(GuessedPath));
                return GuessedAssembly;
            }
            catch { } //candidate could not be loaded (e.g. a reference-only assembly), try the next one
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

    static uint ReadU16(byte[] b, int i) { return unchecked((uint)(b[i] | (b[i + 1] << 8))); }
    static uint ReadU32(byte[] b, int i) { return unchecked((uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24))); }
    static string ReadUtf8(byte[] b, int i) { int e = i; while (b[e] != 0) e++; return System.Text.Encoding.UTF8.GetString(b, i, e - i); }

    static int RvaToOffset(byte[] b, uint Rva, int FirstSection, int NumSections)
    {
        for (int s = 0; s != NumSections; s++)
        {
            int Section = FirstSection + s * 40;
            uint VirtualAddress = ReadU32(b, Section + 12), RawSize = ReadU32(b, Section + 16), RawPointer = ReadU32(b, Section + 20);
            if (Rva >= VirtualAddress && Rva < VirtualAddress + RawSize) return (int)(Rva - VirtualAddress + RawPointer);
        }
        throw new BadImageFormatException("RVA not in any section");
    }

    //reads the names of all types straight out of the .NET metadata TypeDef table (ECMA-335 II.22/II.24),
    //without loading the assembly - reflection returns nameless nulls for types that failed to load, but
    //their names are right there in the file; returns them in TypeDef order (the order GetTypes() uses),
    //excluding the <Module> pseudo-type, or null if the file cannot be parsed
    static string[] TryReadTypeNamesFromMetadata(string FilePath)
    {
        try
        {
            byte[] b = File.ReadAllBytes(FilePath);
            int pe = (int)ReadU32(b, 0x3C);
            if (ReadU32(b, pe) != 0x00004550) return null; //"PE\0\0"
            int NumSections = (int)ReadU16(b, pe + 6), FirstSection = pe + 24 + (int)ReadU16(b, pe + 20);
            int DataDirs = pe + 24 + (ReadU16(b, pe + 24) == 0x20B ? 112 : 96); //PE32+ vs PE32
            int Cli = RvaToOffset(b, ReadU32(b, DataDirs + 14 * 8), FirstSection, NumSections);
            int Md = RvaToOffset(b, ReadU32(b, Cli + 8), FirstSection, NumSections);
            if (ReadU32(b, Md) != 0x424A5342) return null; //'BSJB'

            int NumStreams = (int)ReadU16(b, Md + 16 + (int)ReadU32(b, Md + 12) + 2);
            int StreamHeader = Md + 16 + (int)ReadU32(b, Md + 12) + 4;
            int Tables = 0, Strings = 0;
            for (int s = 0; s != NumStreams; s++)
            {
                int NameStart = StreamHeader + 8, NameEnd = NameStart;
                while (b[NameEnd] != 0) NameEnd++;
                string StreamName = System.Text.Encoding.ASCII.GetString(b, NameStart, NameEnd - NameStart);
                if (StreamName == "#~" || StreamName == "#-") Tables = Md + (int)ReadU32(b, StreamHeader);
                if (StreamName == "#Strings") Strings = Md + (int)ReadU32(b, StreamHeader);
                StreamHeader = NameStart + ((NameEnd - NameStart) / 4 + 1) * 4;
            }
            if (Tables == 0 || Strings == 0) return null;

            ulong Valid = (ulong)ReadU32(b, Tables + 8) | ((ulong)ReadU32(b, Tables + 12) << 32);
            int[] Rows = new int[64];
            int p = Tables + 24;
            for (int t = 0; t != 64; t++)
                if ((Valid >> t & 1) != 0) { Rows[t] = (int)ReadU32(b, p); p += 4; }

            int HeapSizes = b[Tables + 6];
            int Str = ((HeapSizes & 1) != 0 ? 4 : 2), Guid = ((HeapSizes & 2) != 0 ? 4 : 2), Blob = ((HeapSizes & 4) != 0 ? 4 : 2);
            Func<int, int> Idx = (int Table) => (Rows[Table] > 0xFFFF ? 4 : 2);
            Func<int, int[], int> Coded = (int TagBits, int[] Tbls) => { int Max = 0; foreach (int tb in Tbls) if (Rows[tb] > Max) Max = Rows[tb]; return (Max < (1 << (16 - TagBits)) ? 2 : 4); };
            int[] TypeDefOrRef = { 0x02, 0x01, 0x1B };
            int[] HasCustomAttribute = { 0x06, 0x04, 0x01, 0x02, 0x08, 0x09, 0x0A, 0x00, 0x0E, 0x17, 0x14, 0x11, 0x1A, 0x1B, 0x20, 0x23, 0x26, 0x27, 0x28, 0x2A, 0x2B, 0x2C };
            int[] Implementation = { 0x26, 0x23, 0x27 };

            //byte size of one row of every table up to NestedClass (0x29), ECMA-335 II.22
            int[] RowSize = new int[0x2A];
            RowSize[0x00] = 2 + Str + 3 * Guid;                                             //Module
            RowSize[0x01] = Coded(2, new[] { 0x00, 0x1A, 0x23, 0x01 }) + 2 * Str;           //TypeRef
            RowSize[0x02] = 4 + 2 * Str + Coded(2, TypeDefOrRef) + Idx(0x04) + Idx(0x06);   //TypeDef
            RowSize[0x03] = Idx(0x04);                                                      //FieldPtr
            RowSize[0x04] = 2 + Str + Blob;                                                 //Field
            RowSize[0x05] = Idx(0x06);                                                      //MethodPtr
            RowSize[0x06] = 8 + Str + Blob + Idx(0x08);                                     //MethodDef
            RowSize[0x07] = Idx(0x08);                                                      //ParamPtr
            RowSize[0x08] = 4 + Str;                                                        //Param
            RowSize[0x09] = Idx(0x02) + Coded(2, TypeDefOrRef);                             //InterfaceImpl
            RowSize[0x0A] = Coded(3, new[] { 0x02, 0x01, 0x1A, 0x06, 0x1B }) + Str + Blob;  //MemberRef
            RowSize[0x0B] = 2 + Coded(2, new[] { 0x04, 0x08, 0x17 }) + Blob;                //Constant
            RowSize[0x0C] = Coded(5, HasCustomAttribute) + Coded(3, new[] { 0x06, 0x0A }) + Blob; //CustomAttribute
            RowSize[0x0D] = Coded(1, new[] { 0x04, 0x08 }) + Blob;                          //FieldMarshal
            RowSize[0x0E] = 2 + Coded(2, new[] { 0x02, 0x06, 0x20 }) + Blob;                //DeclSecurity
            RowSize[0x0F] = 6 + Idx(0x02);                                                  //ClassLayout
            RowSize[0x10] = 4 + Idx(0x04);                                                  //FieldLayout
            RowSize[0x11] = Blob;                                                           //StandAloneSig
            RowSize[0x12] = Idx(0x02) + Idx(0x14);                                          //EventMap
            RowSize[0x13] = Idx(0x14);                                                      //EventPtr
            RowSize[0x14] = 2 + Str + Coded(2, TypeDefOrRef);                               //Event
            RowSize[0x15] = Idx(0x02) + Idx(0x17);                                          //PropertyMap
            RowSize[0x16] = Idx(0x17);                                                      //PropertyPtr
            RowSize[0x17] = 2 + Str + Blob;                                                 //Property
            RowSize[0x18] = 2 + Idx(0x06) + Coded(1, new[] { 0x14, 0x17 });                 //MethodSemantics
            RowSize[0x19] = Idx(0x02) + 2 * Coded(1, new[] { 0x06, 0x0A });                 //MethodImpl
            RowSize[0x1A] = Str;                                                            //ModuleRef
            RowSize[0x1B] = Blob;                                                           //TypeSpec
            RowSize[0x1C] = 2 + Coded(1, new[] { 0x04, 0x06 }) + Str + Idx(0x1A);           //ImplMap
            RowSize[0x1D] = 4 + Idx(0x04);                                                  //FieldRVA
            RowSize[0x1E] = 8;                                                              //EnCLog
            RowSize[0x1F] = 4;                                                              //EnCMap
            RowSize[0x20] = 16 + Blob + 2 * Str;                                            //Assembly
            RowSize[0x21] = 4;                                                              //AssemblyProcessor
            RowSize[0x22] = 12;                                                             //AssemblyOS
            RowSize[0x23] = 12 + 2 * Blob + 2 * Str;                                        //AssemblyRef
            RowSize[0x24] = 4 + Idx(0x23);                                                  //AssemblyRefProcessor
            RowSize[0x25] = 12 + Idx(0x23);                                                 //AssemblyRefOS
            RowSize[0x26] = 4 + Str + Blob;                                                 //File
            RowSize[0x27] = 8 + 2 * Str + Coded(2, Implementation);                         //ExportedType
            RowSize[0x28] = 8 + Str + Coded(2, Implementation);                             //ManifestResource
            RowSize[0x29] = 2 * Idx(0x02);                                                  //NestedClass

            int TypeDefPos = 0, NestedClassPos = 0;
            for (int t = 0; t != 0x2A; t++)
            {
                if ((Valid >> t & 1) == 0) continue;
                if (t == 0x02) TypeDefPos = p;
                if (t == 0x29) NestedClassPos = p;
                p += RowSize[t] * Rows[t];
            }
            if (TypeDefPos == 0 || Rows[0x02] < 1) return null;

            int NumTypes = Rows[0x02];
            string[] Names = new string[NumTypes + 1], Namespaces = new string[NumTypes + 1];
            for (int r = 1; r <= NumTypes; r++)
            {
                int Row = TypeDefPos + (r - 1) * RowSize[0x02];
                Names[r]      = ReadUtf8(b, Strings + (int)(Str == 4 ? ReadU32(b, Row + 4)       : ReadU16(b, Row + 4)));
                Namespaces[r] = ReadUtf8(b, Strings + (int)(Str == 4 ? ReadU32(b, Row + 4 + Str) : ReadU16(b, Row + 4 + Str)));
            }
            int[] Enclosing = new int[NumTypes + 1];
            for (int r = 0; NestedClassPos != 0 && r != Rows[0x29]; r++)
            {
                int Row = NestedClassPos + r * RowSize[0x29];
                int Nested    = (int)(Idx(0x02) == 4 ? ReadU32(b, Row)             : ReadU16(b, Row));
                int Enclosing_ = (int)(Idx(0x02) == 4 ? ReadU32(b, Row + Idx(0x02)) : ReadU16(b, Row + Idx(0x02)));
                if (Nested >= 1 && Nested <= NumTypes) Enclosing[Nested] = Enclosing_;
            }
            string[] Result = new string[NumTypes - 1]; //row 1 is the <Module> pseudo-type, which GetTypes() omits
            for (int r = 2; r <= NumTypes; r++) Result[r - 2] = BuildFullTypeName(r, Names, Namespaces, Enclosing, 0);
            return Result;
        }
        catch { return null; }
    }

    static string BuildFullTypeName(int r, string[] Names, string[] Namespaces, int[] Enclosing, int Depth)
    {
        string Name = (Namespaces[r].Length != 0 ? Namespaces[r] + "." + Names[r] : Names[r]);
        if (Enclosing[r] == 0 || Depth > 100) return Name;
        return BuildFullTypeName(Enclosing[r], Names, Namespaces, Enclosing, Depth + 1) + "+" + Name;
    }

    //shows which types could not be evaluated, why the loader rejected them, and what the user can do about it
    static void ShowUnresolvedTypes(Assembly assembly, Type[] AssemblyTypes, Exception[] LoaderExceptions)
    {
        int NumUnresolved = 0;
        foreach (Type t in AssemblyTypes) if (t == null) NumUnresolved++;

        //GetTypes() returns nameless nulls for failed types, in an order that does not reliably match the
        //metadata, so identify them by metadata token instead: TypeDef row r has the token 0x02000000+r, its
        //name can be read straight from the file, and ResolveType(token) reproduces the per-type load error
        string[] MetaNames = TryReadTypeNamesFromMetadata(AssemblyPath);
        var Lines = new List<string>();
        var Errors = new List<Exception>();
        if (LoaderExceptions != null) foreach (Exception le in LoaderExceptions) if (le != null) Errors.Add(le);
        try
        {
            if (MetaNames != null)
            {
                Module Manifest = assembly.ManifestModule;
                var ResolvedTokens = new HashSet<int>();
                foreach (Type t in AssemblyTypes) if (t != null && t.Module == Manifest) ResolvedTokens.Add(t.MetadataToken);
                for (int r = 2; r <= MetaNames.Length + 1; r++) //row 1 is <Module>, which GetTypes() omits
                {
                    if (ResolvedTokens.Contains(0x02000000 + r)) continue;
                    try { Manifest.ResolveType(0x02000000 + r); } //if this succeeds the type loaded after all
                    catch (Exception e) { Lines.Add(MetaNames[r - 2] + " - " + e.Message); Errors.Add(e); }
                }
            }
        }
        catch { }

        bool HasMissingFile = false, HasCoreOnlyType = false;
        foreach (Exception e in Errors)
        {
            if (e is FileNotFoundException) HasMissingFile = true;
            if (e is TypeLoadException && (e.Message.Contains("from assembly 'System.") || e.Message.Contains("does not have an implementation"))) HasCoreOnlyType = true;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(NumUnresolved.ToString() + " of " + AssemblyTypes.Length.ToString() + " types could not be evaluated. Their bytes are not lost, but they are counted in 'Other Overhead' without a detailed breakdown.");
        sb.AppendLine();
        sb.AppendLine("Background: this tool measures sizes via .NET reflection, and the runtime cannot load a type without also resolving its base class, interfaces and member signatures, which may live in other assemblies.");
        sb.AppendLine();
        sb.AppendLine("What you can do about it:");
        if (HasMissingFile)
            sb.AppendLine("- A dependency could not be found. Copy it into the folder of the analyzed assembly and load again, or select it in the dialog that asks for its location instead of canceling.");
        if (HasCoreOnlyType)
            sb.AppendLine("- Some required types (e.g. Span<T>, Memory<T>) only exist on the .NET Core/.NET 5+ runtime and cannot be loaded by this tool, which runs on the .NET Framework. If a .NET Framework or .NET Standard build of this assembly exists, analyze that build to evaluate these types.");
        if (!HasMissingFile && !HasCoreOnlyType)
            sb.AppendLine("- Check the reasons below; usually a dependency is missing or was built for an incompatible runtime.");
        sb.AppendLine();

        sb.AppendLine("Unresolved types:");
        foreach (string Line in Lines) sb.AppendLine(Line);
        if (Lines.Count == 0)
        {
            sb.AppendLine("(the type names could not be determined)");
            var Counts = new Dictionary<string, int>();
            var Order = new List<string>();
            foreach (Exception e in Errors)
            {
                if (!Counts.ContainsKey(e.Message)) { Counts[e.Message] = 0; Order.Add(e.Message); }
                Counts[e.Message]++;
            }
            if (Order.Count != 0)
            {
                sb.AppendLine();
                sb.AppendLine("Reasons reported by the assembly loader:");
                foreach (string m in Order) sb.AppendLine((Counts[m] > 1 ? "(" + Counts[m] + "x) " : "") + m);
            }
        }

        var dlg = new Form();
        dlg.Text = "Sizer.Net - Unresolved Types";
        dlg.StartPosition = FormStartPosition.CenterParent;
        dlg.ClientSize = new Size(780, 480);
        dlg.MinimizeBox = false;
        dlg.ShowIcon = false;

        var btnOK = new Button(); //added first so the text box is not focused with all of its text selected
        btnOK.Text = "OK";
        btnOK.Size = new Size(100, 23);
        btnOK.Location = new Point(dlg.ClientSize.Width - 13 - 100, dlg.ClientSize.Height - 13 - 23);
        btnOK.Anchor = (AnchorStyles)(AnchorStyles.Bottom | AnchorStyles.Right);
        btnOK.DialogResult = DialogResult.OK;
        dlg.Controls.Add(btnOK);

        var txt = new TextBox();
        txt.Multiline = true;
        txt.ReadOnly = true;
        txt.WordWrap = false;
        txt.ScrollBars = ScrollBars.Both;
        txt.Location = new Point(13, 13);
        txt.Size = new Size(dlg.ClientSize.Width - 13 - 13, dlg.ClientSize.Height - 13 - 23 - 13 - 13);
        txt.Anchor = (AnchorStyles)(AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
        txt.Text = sb.ToString();
        dlg.Controls.Add(txt);

        dlg.AcceptButton = dlg.CancelButton = btnOK;
        dlg.ShowDialog(f);
        dlg.Dispose();
    }

    //guess the location of a dependency from its metadata (name, version, public key token) by probing standard
    //.NET locations: the GAC, .NET Framework install and reference assembly dirs and the NuGet package cache -
    //candidates whose version and public key token match best are returned first
    static List<string> GuessDependencyPaths(AssemblyName Wanted)
    {
        string DllFileName = Wanted.Name + ".dll";
        string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string[] ProgramFilesDirs = { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)") };
        var Candidates = new List<string>();

        //global assembly cache (v4 style and legacy), e.g. C:\Windows\Microsoft.NET\assembly\GAC_MSIL\<name>\v4.0_<version>__<token>\<name>.dll
        foreach (string GacRoot in new[] { Path.Combine(WinDir, "Microsoft.NET\\assembly"), Path.Combine(WinDir, "assembly") })
            foreach (string Gac in new[] { "GAC_MSIL", "GAC_64", "GAC_32", "GAC" })
                foreach (string VersionDir in SafeGetDirectories(Path.Combine(Path.Combine(GacRoot, Gac), Wanted.Name)))
                    AddCandidate(Candidates, Path.Combine(VersionDir, DllFileName));

        //.NET Framework install directories
        foreach (string FrameworkDir in new[] { "Framework64\\v4.0.30319", "Framework\\v4.0.30319", "Framework64\\v4.0.30319\\WPF", "Framework\\v4.0.30319\\WPF", "Framework64\\v2.0.50727", "Framework\\v2.0.50727" })
            AddCandidate(Candidates, Path.Combine(Path.Combine(Path.Combine(WinDir, "Microsoft.NET"), FrameworkDir), DllFileName));

        //.NET Framework reference assemblies (targeting packs), newest first, including facades (System.Runtime etc.)
        foreach (string ProgramFilesDir in ProgramFilesDirs)
        {
            if (string.IsNullOrEmpty(ProgramFilesDir)) continue;
            foreach (string VersionDir in SortByVersionDesc(SafeGetDirectories(Path.Combine(ProgramFilesDir, "Reference Assemblies\\Microsoft\\Framework\\.NETFramework")), Wanted.Version))
            {
                AddCandidate(Candidates, Path.Combine(VersionDir, DllFileName));
                AddCandidate(Candidates, Path.Combine(Path.Combine(VersionDir, "Facades"), DllFileName));
            }

        }

        //note: the .NET Core/5+ shared framework dirs under dotnet\shared are deliberately NOT probed - their
        //assemblies reference System.Private.CoreLib and can never resolve types on the .NET Framework runtime
        //hosting this tool (System.Object would come from the wrong core library, failing every type load)

        //NuGet package cache
        string PackageDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget\\packages\\" + Wanted.Name.ToLowerInvariant());
        foreach (string VersionDir in SortByVersionDesc(SafeGetDirectories(PackageDir), Wanted.Version))
            foreach (string LibOrRef in new[] { "lib", "ref" })
                foreach (string TfmDir in SafeGetDirectories(Path.Combine(VersionDir, LibOrRef)))
                    AddCandidate(Candidates, Path.Combine(TfmDir, DllFileName));

        //verify candidates by reading their metadata; best matches first, non-matching names dropped
        byte[] WantedToken = Wanted.GetPublicKeyToken();
        var Scored = new List<KeyValuePair<int, string>>();
        foreach (string Candidate in Candidates)
        {
            try
            {
                AssemblyName Found = AssemblyName.GetAssemblyName(Candidate);
                if (!string.Equals(Found.Name, Wanted.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsCoreRuntimeAssembly(Candidate)) continue;
                int Score = 0;
                if (Wanted.Version != null && Wanted.Version.Equals(Found.Version)) Score += 2;
                if (WantedToken != null && WantedToken.Length != 0 && TokensEqual(WantedToken, Found.GetPublicKeyToken())) Score += 1;
                Scored.Add(new KeyValuePair<int, string>(Score, Candidate));
            }
            catch { } //not a readable .NET assembly
        }
        var Result = new List<string>();
        for (int Score = 3; Score >= 0; Score--)
            foreach (KeyValuePair<int, string> kv in Scored)
                if (kv.Key == Score) Result.Add(kv.Value);
        return Result;
    }

    //assemblies built for the .NET Core runtime (System.Private.CoreLib and anything referencing it) cannot be
    //loaded for execution on the .NET Framework runtime hosting this tool - returning one from AssemblyResolve
    //poisons the whole load, making every type fail with "the parent does not exist" instead of asking the user
    static bool IsCoreRuntimeAssembly(string CandidatePath)
    {
        if (string.Equals(Path.GetFileNameWithoutExtension(CandidatePath), "System.Private.CoreLib", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            foreach (AssemblyName Reference in Assembly.ReflectionOnlyLoadFrom(CandidatePath).GetReferencedAssemblies())
                if (Reference.Name == "System.Private.CoreLib") return true;
            return false;
        }
        catch { return true; } //if it cannot even be inspected, do not risk loading it
    }

    static void AddCandidate(List<string> Candidates, string CandidatePath)
    {
        if (File.Exists(CandidatePath) && !Candidates.Contains(CandidatePath)) Candidates.Add(CandidatePath);
    }

    static string[] SafeGetDirectories(string Dir)
    {
        try { return Directory.GetDirectories(Dir); } catch { return new string[0]; }
    }

    //sort version-named directories descending, listing versions with the wanted major version first
    static string[] SortByVersionDesc(string[] Dirs, Version Wanted)
    {
        Array.Sort(Dirs, (string a, string b) =>
        {
            Version va = ParseDirVersion(a), vb = ParseDirVersion(b);
            if (Wanted != null && (va.Major == Wanted.Major) != (vb.Major == Wanted.Major)) return (vb.Major == Wanted.Major ? 1 : -1);
            return vb.CompareTo(va);
        });
        return Dirs;
    }

    static Version ParseDirVersion(string Dir)
    {
        string s = Path.GetFileName(Dir).TrimStart('v', 'V');
        int Dash = s.IndexOf('-'); //strip prerelease suffix, e.g. "9.0.0-preview.1"
        if (Dash >= 0) s = s.Substring(0, Dash);
        try { return new Version(s.IndexOf('.') < 0 ? s + ".0" : s); } catch { return new Version(0, 0); }
    }

    static bool TokensEqual(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i != a.Length; i++) if (a[i] != b[i]) return false;
        return true;
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

    //renders a subtree as indented text, e.g.:
    //SelectedItem: 2.88 kb (30% metadata)
    //	SubItem1: 1.4 kb
    //	SubItem2: 1.1 kb
    static string GetSubtreeText(TreeNode n)
    {
        var sb = new System.Text.StringBuilder();
        AppendSubtreeText(sb, n, 0);
        return sb.ToString();
    }

    static void AppendSubtreeText(System.Text.StringBuilder sb, TreeNode n, int Depth)
    {
        NodeSize ns = (NodeSize)n.Tag;
        sb.Append('\t', Depth).Append(n.Text).Append(": ").Append(FormatKb(ns.Total));
        if (n.Nodes.Count != 0 && ns.Total != 0) sb.Append(" (").Append(Percent(ns.Metadata, ns.Total)).Append(" metadata)");
        sb.AppendLine();
        foreach (TreeNode c in n.Nodes) AppendSubtreeText(sb, c, Depth + 1);
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
