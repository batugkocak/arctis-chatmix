using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

static class HidListen
{
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr r; }
    [StructLayout(LayoutKind.Sequential)] struct HIDD_ATTRIBUTES { public int Size; public ushort Vid, Pid, Ver; }
    [StructLayout(LayoutKind.Sequential)] struct HIDP_CAPS { public ushort Usage, UsagePage, InLen, OutLen, FeatLen; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] r; public ushort a, b, c, d, e, f, g, h, i, j; }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
    [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int f);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA o);
    [DllImport("setupapi.dll", CharSet = CharSet.Auto)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA d, IntPtr o, int sz, out int req, IntPtr di);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);

    public static IEnumerable<string> Paths(string filter)
    {
        Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
        for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
        {
            int req; SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
            IntPtr buf = Marshal.AllocHGlobal(req);
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
            if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero))
            {
                string p = Marshal.PtrToStringAuto(buf + 4);
                if (p.ToLower().Contains(filter)) yield return p;
            }
            Marshal.FreeHGlobal(buf);
        }
    }

    static void Main(string[] args)
    {
        int seconds = args.Length > 0 ? int.Parse(args[0]) : 60;
        foreach (var p in Paths("vid_1038&pid_2216"))
        {
            var h = CreateFile(p, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) h = CreateFile(p, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) { Console.WriteLine("OPEN FAIL " + Marshal.GetLastWin32Error() + " " + p); continue; }
            IntPtr pp; HIDP_CAPS c; HidD_GetPreparsedData(h, out pp); HidP_GetCaps(pp, out c); HidD_FreePreparsedData(pp);
            string tag = string.Format("UP={0:X4} U={1:X4} in={2}", c.UsagePage, c.Usage, c.InLen);
            Console.WriteLine("IFACE " + tag + " " + p);
            if (c.InLen == 0) continue;
            var fs = new FileStream(h, FileAccess.Read, c.InLen, false);
            int len = c.InLen;
            new Thread(() =>
            {
                var b = new byte[len];
                try { while (true) { int n = fs.Read(b, 0, len); Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [" + tag + "] " + BitConverter.ToString(b, 0, n)); } }
                catch (Exception e) { Console.WriteLine("READ END [" + tag + "] " + e.Message); }
            }) { IsBackground = true }.Start();
        }
        Console.Out.Flush();
        Thread.Sleep(seconds * 1000);
        Console.WriteLine("DONE");
    }
}
