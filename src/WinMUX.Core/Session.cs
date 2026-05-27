using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMUX.Core;

public class Session : IDisposable
{
    private readonly string _commandLine;
    private readonly short _width;
    private readonly short _height;

    private IntPtr _hPC;
    private IntPtr _inputRead;   // owned by ConPTY (must keep alive)
    private IntPtr _outputWrite; // owned by ConPTY (must keep alive)
    private IntPtr _procHandle;
    private uint _procId;

    private FileStream? _inputStream;  // wraps inputWrite (we write to this)
    private FileStream? _outputStream; // wraps outputRead (we read from this)

    private int _disposed; // 0 = active, 1 = disposed

    public Stream? InputStream => _inputStream;
    public Stream? OutputStream => _outputStream;
    public uint ProcessId => _procId;

    public Session(string commandLine, short width = 120, short height = 30)
    {
        _commandLine = commandLine;
        _width = width;
        _height = height;
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _disposed, -1, -1) != 0)
            throw new ObjectDisposedException(nameof(Session));

        // Pipe security attributes: allow inheritance
        var sa = new NativeMethods.SECURITY_ATTRIBUTES
        {
            nLength = (uint)Marshal.SizeOf<NativeMethods.SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
            lpSecurityDescriptor = IntPtr.Zero
        };

        // Input pipe: we write to hWrite, PTY reads from hRead
        if (!NativeMethods.CreatePipe(out _inputRead, out IntPtr inputWrite, ref sa, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        IntPtr outputRead = IntPtr.Zero;

        try
        {
            if (!NativeMethods.SetHandleInformation(inputWrite, NativeMethods.HANDLE_FLAG_INHERIT, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            // Output pipe: PTY writes to hWrite, we read from hRead
            if (!NativeMethods.CreatePipe(out outputRead, out _outputWrite, ref sa, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (!NativeMethods.SetHandleInformation(outputRead, NativeMethods.HANDLE_FLAG_INHERIT, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch
        {
            // Clean up any pipes created before the exception
            if (_inputRead != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_inputRead);
                _inputRead = IntPtr.Zero;
            }
            if (inputWrite != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(inputWrite);
            }
            if (_outputWrite != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_outputWrite);
                _outputWrite = IntPtr.Zero;
            }
            throw;
        }

        // Create the pseudo console
        int hr = NativeMethods.CreatePseudoConsole(
            new NativeMethods.COORD(_width, _height),
            _inputRead,
            _outputWrite,
            0,
            out _hPC);

        if (hr < 0)
            Marshal.ThrowExceptionForHR(hr);

        // Prepare extended startup info with pseudo console attribute
        IntPtr attrSize = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
        IntPtr attrList = Marshal.AllocHGlobal(attrSize);
        try
        {
            if (!NativeMethods.InitializeProcThreadAttributeList(attrList, 1, 0, ref attrSize))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (!NativeMethods.UpdateProcThreadAttribute(
                attrList,
                0,
                (IntPtr)NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _hPC,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var si = new NativeMethods.STARTUPINFOEX
            {
                StartupInfo = new NativeMethods.STARTUPINFO 
                { 
                    cb = (uint)Marshal.SizeOf<NativeMethods.STARTUPINFOEX>(),
                    hStdInput = new IntPtr(-1),
                    hStdOutput = new IntPtr(-1),
                    hStdError = new IntPtr(-1),
                    dwFlags = NativeMethods.STARTF_USESTDHANDLES
                },
                lpAttributeList = attrList
            };

            var pi = new NativeMethods.PROCESS_INFORMATION();
            bool created = NativeMethods.CreateProcess(
                null,
                _commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                NativeMethods.EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero,
                null,
                ref si,
                out pi);

            NativeMethods.DeleteProcThreadAttributeList(attrList);

            if (!created)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            _procHandle = pi.hProcess;
            _procId = pi.dwProcessId;

            // Close thread handle; keep process handle for monitoring/cleanup
            NativeMethods.CloseHandle(pi.hThread);
        }
        finally
        {
            Marshal.FreeHGlobal(attrList);
        }

        // Wrap our ends of the pipes in FileStreams for easy async I/O
        var safeInputWrite = new SafeFileHandle(inputWrite, true);
        _inputStream = new FileStream(safeInputWrite, FileAccess.Write, 4096, false);

        var safeOutputRead = new SafeFileHandle(outputRead, true);
        _outputStream = new FileStream(safeOutputRead, FileAccess.Read, 4096, false);
    }

    public void Resize(short width, short height)
    {
        if (Interlocked.CompareExchange(ref _disposed, -1, -1) != 0)
            throw new ObjectDisposedException(nameof(Session));
        if (_hPC == IntPtr.Zero) throw new InvalidOperationException("Session not started.");
        NativeMethods.ResizePseudoConsole(_hPC, new NativeMethods.COORD(width, height));
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return; // Already disposed or dispose in progress

        try { _inputStream?.Dispose(); } catch { }
        try { _outputStream?.Dispose(); } catch { }

        // Close the handles we gave to ConPTY (safe after ClosePseudoConsole? docs say keep open until ClosePseudoConsole)
        // We'll close them after closing the pseudo console to be safe.

        if (_hPC != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(_hPC);
            _hPC = IntPtr.Zero;
        }

        if (_inputRead != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_inputRead);
            _inputRead = IntPtr.Zero;
        }

        if (_outputWrite != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_outputWrite);
            _outputWrite = IntPtr.Zero;
        }

        if (_procHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_procHandle);
            _procHandle = IntPtr.Zero;
        }
    }
}
