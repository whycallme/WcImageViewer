using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace WcImageViewer.Updater
{
    // .NET Framework 4.8 csc (C# 5) compatible on purpose: this file is compiled
    // by the OS csc.exe in scripts\publish.ps1, not by dotnet build.
    internal static class Program
    {
        private const int ParentWaitMilliseconds = 120000;
        private const int SurvivalMilliseconds = 15000;
        private const string MessageBoxTitle = "WC Image Viewer 업데이트";

        [STAThread]
        private static int Main(string[] args)
        {
            var logPath = GetOption(args, "--log");
            var targetApp = GetOption(args, "--target-app");
            var newApp = GetOption(args, "--new-app");
            var targetUpdater = GetOption(args, "--target-updater");
            var newUpdater = GetOption(args, "--new-updater");
            var staging = GetOption(args, "--staging");
            var parentPid = ParseInt(GetOption(args, "--parent-pid"));

            if (string.IsNullOrEmpty(targetApp) || string.IsNullOrEmpty(newApp) ||
                string.IsNullOrEmpty(targetUpdater) || string.IsNullOrEmpty(newUpdater) ||
                string.IsNullOrEmpty(staging))
            {
                var message = "업데이트 실행 인자가 올바르지 않습니다.";
                Log(logPath, message);
                ShowError(message, logPath);
                return 2;
            }

            Log(logPath, "업데이트 시작. 대상=" + targetApp + " 새 앱=" + newApp);
            WaitForParent(parentPid, logPath);

            try
            {
                ApplyUpdate(targetApp, newApp, targetUpdater, newUpdater, staging, logPath);
            }
            catch (Exception ex)
            {
                Log(logPath, "파일 교체 실패: " + Describe(ex));
                RestorePrevious(targetApp, logPath);
                ShowError("업데이트 파일을 교체할 수 없습니다." + Environment.NewLine +
                          "이전 버전은 그대로 유지됩니다." + Environment.NewLine + Environment.NewLine +
                          Describe(ex), logPath);
                return 1;
            }

            try
            {
                StartAndVerify(targetApp, logPath);
            }
            catch (Exception ex)
            {
                Log(logPath, "새 버전 시작 실패: " + Describe(ex));
                var restored = RestorePrevious(targetApp, logPath);
                if (restored)
                {
                    StartPrevious(targetApp, logPath);
                }

                ShowError("새 버전을 시작하지 못했습니다." + Environment.NewLine +
                          (restored ? "이전 버전으로 되돌렸습니다." : "이전 버전 복원도 실패했습니다.") +
                          Environment.NewLine + Environment.NewLine + Describe(ex), logPath);
                return 1;
            }

            DeleteBackup(targetApp, logPath);
            Log(logPath, "업데이트 완료.");
            return 0;
        }

        private static void ApplyUpdate(
            string targetApp,
            string newApp,
            string targetUpdater,
            string newUpdater,
            string staging,
            string logPath)
        {
            var oldPath = targetApp + ".old";
            if (File.Exists(targetApp))
            {
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }

                File.Copy(targetApp, oldPath, true);
                Log(logPath, "이전 앱 백업: " + oldPath);
            }

            File.Copy(newApp, targetApp, true);
            Log(logPath, "새 앱 복사 완료.");

            if (!SamePath(newUpdater, targetUpdater))
            {
                var updaterDirectory = Path.GetDirectoryName(targetUpdater);
                if (!string.IsNullOrEmpty(updaterDirectory))
                {
                    Directory.CreateDirectory(updaterDirectory);
                }

                File.Copy(newUpdater, targetUpdater, true);
                Log(logPath, "새 updater 복사 완료: " + targetUpdater);
            }

            var targetDirectory = Path.GetDirectoryName(targetApp);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                CopySupportFile(staging, targetDirectory, "LICENSE", logPath);
                CopySupportFile(staging, targetDirectory, "THIRD-PARTY-NOTICES.md", logPath);
            }
        }

        private static void CopySupportFile(string staging, string targetDirectory, string fileName, string logPath)
        {
            var source = FindFile(staging, fileName);
            if (string.IsNullOrEmpty(source))
            {
                return;
            }

            var destination = Path.Combine(targetDirectory, fileName);
            File.Copy(source, destination, true);
            Log(logPath, "문서 복사 완료: " + destination);
        }

        private static string FindFile(string directory, string fileName)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            var direct = Path.Combine(directory, fileName);
            if (File.Exists(direct))
            {
                return direct;
            }

            var matches = Directory.GetFiles(directory, fileName, SearchOption.AllDirectories);
            return matches.Length > 0 ? matches[0] : null;
        }

        private static void WaitForParent(int parentPid, string logPath)
        {
            if (parentPid <= 0)
            {
                return;
            }

            try
            {
                using (var parent = Process.GetProcessById(parentPid))
                {
                    Log(logPath, "이전 앱 종료 대기: PID " + parentPid.ToString(CultureInfo.InvariantCulture));
                    if (!parent.WaitForExit(ParentWaitMilliseconds))
                    {
                        Log(logPath, "이전 앱 대기 시간 초과 (" +
                                     (ParentWaitMilliseconds / 1000).ToString(CultureInfo.InvariantCulture) + "초).");
                    }
                }
            }
            catch (ArgumentException)
            {
                Log(logPath, "이전 앱이 이미 종료되어 있습니다.");
            }
            catch (InvalidOperationException ex)
            {
                Log(logPath, "이전 앱 대기 실패: " + Describe(ex));
            }
        }

        private static void StartAndVerify(string targetApp, string logPath)
        {
            var startInfo = new ProcessStartInfo(targetApp);
            startInfo.UseShellExecute = false;
            startInfo.WorkingDirectory = Path.GetDirectoryName(targetApp) ?? string.Empty;

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("새 앱 프로세스를 만들 수 없습니다.");
                }

                Log(logPath, "새 앱 시작: PID " + process.Id.ToString(CultureInfo.InvariantCulture));

                // Wait until the app reaches an idle state (UI ready) instead of
                // requiring it to stay alive for 15 seconds. The old check treated
                // a user closing the window within 15 seconds as a startup failure.
                if (!process.WaitForInputIdle(SurvivalMilliseconds))
                {
                    throw new InvalidOperationException(
                        "새 앱이 " + (SurvivalMilliseconds / 1000).ToString(CultureInfo.InvariantCulture) +
                        "초 안에 준비되지 않았습니다.");
                }
            }

            Log(logPath, "새 앱이 " + (SurvivalMilliseconds / 1000).ToString(CultureInfo.InvariantCulture) +
                         "초 동안 정상 실행되었습니다.");
        }

        private static void StartPrevious(string targetApp, string logPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo(targetApp);
                startInfo.UseShellExecute = false;
                startInfo.WorkingDirectory = Path.GetDirectoryName(targetApp) ?? string.Empty;
                Process.Start(startInfo);
                Log(logPath, "이전 앱을 다시 시작했습니다.");
            }
            catch (Exception ex)
            {
                Log(logPath, "이전 앱 재시작 실패: " + Describe(ex));
            }
        }

        private static bool RestorePrevious(string targetApp, string logPath)
        {
            var oldPath = targetApp + ".old";
            if (!File.Exists(oldPath))
            {
                Log(logPath, "복원할 백업 파일이 없습니다: " + oldPath);
                return false;
            }

            try
            {
                File.Copy(oldPath, targetApp, true);
                File.Delete(oldPath);
                Log(logPath, "이전 앱 복원 완료.");
                return true;
            }
            catch (Exception ex)
            {
                Log(logPath, "이전 앱 복원 실패: " + Describe(ex));
                return false;
            }
        }

        private static void DeleteBackup(string targetApp, string logPath)
        {
            var oldPath = targetApp + ".old";
            try
            {
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                    Log(logPath, "이전 앱 백업 삭제 완료.");
                }
            }
            catch (Exception ex)
            {
                Log(logPath, "이전 앱 백업 삭제 실패(무시): " + Describe(ex));
            }
        }

        private static string GetOption(string[] args, string name)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static int ParseInt(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0;
        }

        private static bool SamePath(string first, string second)
        {
            return string.Equals(
                Path.GetFullPath(first),
                Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string Describe(Exception exception)
        {
            return exception.GetType().Name + ": " + exception.Message;
        }

        private static void Log(string logPath, string message)
        {
            if (string.IsNullOrEmpty(logPath))
            {
                return;
            }

            try
            {
                var directory = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                           " " + message + Environment.NewLine;
                File.AppendAllText(logPath, line, new UTF8Encoding(true));
            }
            catch
            {
                // Logging must never break the update.
            }
        }

        private static void ShowError(string message, string logPath)
        {
            var text = message;
            if (!string.IsNullOrEmpty(logPath))
            {
                text += Environment.NewLine + Environment.NewLine + "로그: " + logPath;
            }

            MessageBox.Show(text, MessageBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
