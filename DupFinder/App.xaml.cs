using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32.SafeHandles;

namespace DupFinder
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private const string AppFolderName = "DupFinder";

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            base.OnStartup(e);
        }

        private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            HandleCrash("DispatcherUnhandledException", e.Exception);
            e.Handled = false; // let app crash after logging/dumping
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                HandleCrash("UnhandledException", ex);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            HandleCrash("UnobservedTaskException", e.Exception);
            e.SetObserved();
        }

        private void HandleCrash(string source, Exception exception)
        {
            try
            {
                var logPath = WriteLog(source, exception);
                var dumpPath = WriteMiniDump();
                System.Windows.MessageBox.Show(
                    $"예기치 못한 오류가 발생했습니다.\n로그: {logPath}\n덤프: {dumpPath}",
                    "오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Logging/dump failed; swallow to avoid secondary crash.
            }
        }

        private static string WriteLog(string source, Exception ex)
        {
            var folder = EnsureAppFolder("logs");
            var fileName = $"DupFinder_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{source}.log";
            var path = Path.Combine(folder, fileName);
            var lines = new[]
            {
                $"[{DateTime.Now:O}] Source: {source}",
                ex.ToString(),
                string.Empty
            };
            File.AppendAllLines(path, lines);
            return path;
        }

        private static string WriteMiniDump()
        {
            var folder = EnsureAppFolder("dumps");
            var fileName = $"DupFinder_{DateTime.Now:yyyyMMdd_HHmmss_fff}.dmp";
            var path = Path.Combine(folder, fileName);

            using var process = Process.GetCurrentProcess();
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using SafeFileHandle safeFileHandle = fs.SafeFileHandle;

            MiniDumpWriteDump(
                process.Handle,
                (uint)process.Id,
                safeFileHandle,
                MINIDUMP_TYPE.MiniDumpWithFullMemory,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            return path;
        }

        private static string EnsureAppFolder(string subFolder)
        {
            var baseFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName,
                subFolder);
            Directory.CreateDirectory(baseFolder);
            return baseFolder;
        }

        [DllImport("Dbghelp.dll", SetLastError = true)]
        private static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            uint processId,
            SafeHandle hFile,
            MINIDUMP_TYPE dumpType,
            IntPtr expParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);

        [Flags]
        private enum MINIDUMP_TYPE : uint
        {
            MiniDumpNormal = 0x00000000,
            MiniDumpWithFullMemory = 0x00000002,
            MiniDumpWithHandleData = 0x00000004,
            MiniDumpScanMemory = 0x00000010,
            MiniDumpWithThreadInfo = 0x00001000,
        }
    }
}
