using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Cordex.Core
{
    public static class ProcessHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hHandle);

        private const uint TH32CS_SNAPPROCESS = 0x00000002;

        public static HashSet<int> GetChildProcessIds(int parentProcessId)
        {
            var childIds = new HashSet<int>();
            IntPtr hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

            if (hSnapshot == IntPtr.Zero || hSnapshot == new IntPtr(-1))
                return childIds;

            try
            {
                PROCESSENTRY32 pe32 = new PROCESSENTRY32();
                pe32.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));

                if (Process32First(hSnapshot, ref pe32))
                {
                    do
                    {
                        if (pe32.th32ParentProcessID == parentProcessId)
                        {
                            childIds.Add((int)pe32.th32ProcessID);
                        }
                    } while (Process32Next(hSnapshot, ref pe32));
                }
            }
            finally
            {
                CloseHandle(hSnapshot);
            }

            return childIds;
        }
    }
}
