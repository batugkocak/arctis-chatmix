using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

static class Hid
{
    [StructLayout(LayoutKind.Sequential)] struct IfaceData { public int cbSize; public Guid g; public int flags; public IntPtr r; }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref IfaceData o);
    [DllImport("setupapi.dll", CharSet = CharSet.Auto)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref IfaceData d, IntPtr o, int sz, out int req, IntPtr di);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);

    public static string Find(string filter)
    {
        Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        try
        {
            var d = new IfaceData { cbSize = Marshal.SizeOf(typeof(IfaceData)) };
            for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
            {
                int req; SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(req);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero))
                    {
                        string p = Marshal.PtrToStringAuto(buf + 4);
                        if (p.ToLower().Contains(filter)) return p;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return null;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }

    public static FileStream Open(string path, int len)
    {
        var h = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid) throw new IOException("open failed " + Marshal.GetLastWin32Error());
        return new FileStream(h, FileAccess.Read, len, false);
    }
}

static class Audio
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator { [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection c); }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint n); [PreserveSig] int Item(uint i, out IMMDevice d); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore s);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore { [PreserveSig] int GetCount(out uint n); [PreserveSig] int GetAt(uint i, out PropKey k); [PreserveSig] int GetValue(ref PropKey k, out PropVariant v); }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr n);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr n);
        [PreserveSig] int GetChannelCount(out uint n);
        [PreserveSig] int SetMasterVolumeLevel(float db, IntPtr ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float v, IntPtr ctx);
    }
    [StructLayout(LayoutKind.Sequential)] struct PropKey { public Guid fmt; public int pid; }
    [StructLayout(LayoutKind.Sequential)] struct PropVariant { public short vt, r1, r2, r3; public IntPtr p, p2; }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant v);

    static PropKey FriendlyName = new PropKey { fmt = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
    static Guid VolumeIid = typeof(IAudioEndpointVolume).GUID;

    public static void Set(string nameContains, float level)
    {
        IMMDeviceCollection col;
        ((IMMDeviceEnumerator)new MMDeviceEnumerator()).EnumAudioEndpoints(0, 1, out col);
        uint n; col.GetCount(out n);
        for (uint i = 0; i < n; i++)
        {
            IMMDevice dev; col.Item(i, out dev);
            IPropertyStore ps; dev.OpenPropertyStore(0, out ps);
            PropVariant v; ps.GetValue(ref FriendlyName, out v);
            string name = v.vt == 31 ? Marshal.PtrToStringUni(v.p) : "";
            PropVariantClear(ref v);
            if (!name.Contains(nameContains)) continue;
            object o; dev.Activate(ref VolumeIid, 1, IntPtr.Zero, out o);
            ((IAudioEndpointVolume)o).SetMasterVolumeLevelScalar(level, IntPtr.Zero);
        }
    }
}

static class ChatMix
{
    const string Device = "vid_1038&pid_2216&mi_05";
    const int ReportLen = 65;

    [MTAThread]
    static void Main()
    {
        bool fresh;
        using (new Mutex(true, "Local\\ArctisChatMix", out fresh))
        {
            if (!fresh) return;
            while (true)
            {
                try { Run(); } catch { }
                Thread.Sleep(2000);
            }
        }
    }

    static void Run()
    {
        string path = Hid.Find(Device);
        if (path == null) return;
        using (var fs = Hid.Open(path, ReportLen))
        {
            var b = new byte[ReportLen];
            float game = -1, chat = -1;
            while (fs.Read(b, 0, ReportLen) > 3)
            {
                if (b[1] != 0x45) continue;
                float g = Curve(b[2]), c = Curve(b[3]);
                if (g != game) Audio.Set("Sonar - Gaming", game = g);
                if (c != chat) Audio.Set("Sonar - Chat", chat = c);
            }
        }
    }

    const float DeadZone = 90f;
    static float Curve(byte v) { return Math.Min(v / DeadZone, 1f); }
}
