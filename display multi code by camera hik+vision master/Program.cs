using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DisplayMultiCodeApp.UI;

namespace DisplayMultiCodeApp
{
    static class Program
    {
        [DllImport("kernel32.dll")]
        static extern uint SetErrorMode(uint uMode);
        private const uint SEM_FAILCRITICALERRORS = 0x0001;
        private const uint SEM_NOGPFAULTERRORBOX = 0x0002;

        [STAThread]
        static void Main()
        {
            // Vô hiệu hóa hoàn toàn việc tạo tệp .dmp rác từ hệ thống
            SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);

            // Ngăn chặn Windows tạo tệp .dmp bằng cách bắt lỗi toàn cục
            Application.ThreadException += (s, e) => { /* Silently catch */ };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { /* Silently catch */ };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
