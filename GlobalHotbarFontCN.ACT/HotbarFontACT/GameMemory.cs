using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GlobalHotbarFontCN_ACT;

/// <summary>跨进程读写游戏内存，以及在游戏模块里扫描特征码。</summary>
internal sealed class GameMemory : IDisposable
{
    private const int ProcessVmOperation = 0x0008;
    private const int ProcessVmRead = 0x0010;
    private const int ProcessVmWrite = 0x0020;
    private const int ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr written);

    private readonly IntPtr handle;

    public Process Process { get; }
    public long ModuleBase { get; }
    public int ModuleSize { get; }

    private GameMemory(Process process, IntPtr handle)
    {
        Process = process;
        this.handle = handle;
        ModuleBase = process.MainModule.BaseAddress.ToInt64();
        ModuleSize = process.MainModule.ModuleMemorySize;
    }

    /// <summary>打开游戏进程；没有权限（如游戏以管理员运行而 ACT 没有）时抛出异常。</summary>
    public static GameMemory Open(Process process)
    {
        var handle = OpenProcess(ProcessVmRead | ProcessVmWrite | ProcessVmOperation | ProcessQueryLimitedInformation, false, process.Id);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException($"无法打开游戏进程（错误码 {Marshal.GetLastWin32Error()}），请确认 ACT 与游戏以相同权限运行");

        try
        {
            return new GameMemory(process, handle);
        }
        catch
        {
            CloseHandle(handle);
            throw;
        }
    }

    public bool HasExited
    {
        get
        {
            try { return Process.HasExited; }
            catch { return true; }
        }
    }

    public bool Read(long address, byte[] buffer, int length)
    {
        if (!IsPlausibleAddress(address)) return false;
        return ReadProcessMemory(handle, new IntPtr(address), buffer, new IntPtr(length), out var read) && read.ToInt64() == length;
    }

    public byte[] ReadBytes(long address, int length)
    {
        var buffer = new byte[length];
        return Read(address, buffer, length) ? buffer : null;
    }

    public long ReadPointer(long address)
    {
        var b = ReadBytes(address, 8);
        return b == null ? 0 : BitConverter.ToInt64(b, 0);
    }

    public int ReadUInt16(long address)
    {
        var b = ReadBytes(address, 2);
        return b == null ? -1 : BitConverter.ToUInt16(b, 0);
    }

    public int ReadByte(long address)
    {
        var b = ReadBytes(address, 1);
        return b == null ? -1 : b[0];
    }

    public bool WriteByte(long address, byte value) => WriteBytes(address, new[] { value });

    public bool WriteBytes(long address, byte[] bytes)
    {
        if (!IsPlausibleAddress(address)) return false;
        return WriteProcessMemory(handle, new IntPtr(address), bytes, new IntPtr(bytes.Length), out var written) && written.ToInt64() == bytes.Length;
    }

    /// <summary>用户态 64 位地址的粗略检查，挡掉 0 和明显无效的值</summary>
    public static bool IsPlausibleAddress(long address) => address > 0x10000 && address < 0x7FFFFFFFFFFF;

    /// <summary>
    /// 在游戏模块里找特征码（"??" 为通配），返回所有匹配处的地址。
    /// </summary>
    public List<long> ScanModule(string pattern)
    {
        var bytes = ParsePattern(pattern);
        var results = new List<long>();
        const int chunk = 0x100000;
        var buffer = new byte[chunk + bytes.Length];

        for (long offset = 0; offset < ModuleSize; offset += chunk)
        {
            var length = (int)Math.Min(chunk + bytes.Length, ModuleSize - offset);
            if (!Read(ModuleBase + offset, buffer, length)) continue;

            // 只在本块的前 chunk 字节里找起点，重叠部分留给下一块，避免重复计数
            var last = Math.Min(chunk, length - bytes.Length);
            for (var i = 0; i <= last; i++)
            {
                var match = true;
                for (var j = 0; j < bytes.Length; j++)
                {
                    if (bytes[j] >= 0 && buffer[i + j] != bytes[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    results.Add(ModuleBase + offset + i);
            }
        }
        return results;
    }

    private static int[] ParsePattern(string pattern)
    {
        var parts = pattern.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            bytes[i] = parts[i] == "??" ? -1 : Convert.ToInt32(parts[i], 16);
        return bytes;
    }

    public void Dispose() => CloseHandle(handle);
}
