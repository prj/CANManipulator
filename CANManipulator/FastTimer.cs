using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace VehiCAL {
    public class FastTimer {
        private delegate void TimerCompleteDelegate();

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetTimerResolution(int DesiredResolution, bool SetResolution, out int CurrentResolution);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryTimerResolution(out int MinimumResolution, out int MaximumResolution, out int CurrentResolution);

        [DllImport("kernel32.dll")]
        static extern IntPtr CreateWaitableTimer(IntPtr lpTimerAttributes, bool bManualReset, string lpTimerName);

        [DllImport("kernel32.dll")]
        private static extern bool SetWaitableTimer(IntPtr hTimer, [In] ref long pDueTime, int lPeriod, TimerCompleteDelegate pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, bool fResume);

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static ManualResetEvent waitHandle = new ManualResetEvent(true);
        private static bool isRunning = false;
        private static IntPtr handle;
        private static long period;
        private static int savedResolution = -1;
        private static object globalLocker = new object();
        private static TimerCompleteDelegate TimerCallback = new TimerCompleteDelegate(TimerTrigger);

        public static void Start(long periodUs) {
            lock (globalLocker) {
                if (isRunning) {
                    Stop();
                }

                period = -(periodUs * 10 - 1000);

                NtQueryTimerResolution(out int MinimumResolution, out int MaximumResolution, out int CurrentResolution);
                if (CurrentResolution > 5000 && CurrentResolution > MaximumResolution && (periodUs * 10) < CurrentResolution) {
                    NtSetTimerResolution(Math.Max(MaximumResolution, 5000), true, out int CurrentResolution2);
                    savedResolution = CurrentResolution2;
                }

                handle = CreateWaitableTimer(IntPtr.Zero, true, null);
                isRunning = true;
                FireOnce();
            }
        }

        public static void ResetHandle() {
            lock (globalLocker) {
                if (isRunning) {
                    waitHandle.Reset();
                }
            }
        }

        public static void AwaitTimer() {
            waitHandle.WaitOne();
        }

        public static void Stop() {
            lock (globalLocker) {
                if (isRunning) {
                    isRunning = false;
                    CloseHandle(handle);
                    if (savedResolution != -1) {
                        NtSetTimerResolution(savedResolution, false, out int CurrentResolution3);
                        savedResolution = -1;
                    }
                }
            }
        }

        private static void TimerTrigger() {
            lock (globalLocker) {
                if (isRunning) {
                    FireOnce();
                }
                waitHandle.Set();
            }
        }

        private static void FireOnce() {
            SetWaitableTimer(handle, ref period, 0, TimerCallback, IntPtr.Zero, false);
        }
    }
}
