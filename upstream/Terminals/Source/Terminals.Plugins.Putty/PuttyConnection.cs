using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Terminals.Connections;
using Terminals.Native;

namespace Terminals.Plugins.Putty
{
    // PuTTY owns SSH, host-key verification, MFA and terminal emulation. DeskPilot
    // owns only this child process and its embedded terminal window.
    internal class PuttyConnection : Connection, IFocusable
    {
        private Process puttyProcess;
        private IntPtr terminal;
        private readonly Timer captureTimer = new Timer { Interval = 100 };
        private readonly Label status = new Label { Dock=DockStyle.Fill, Text="Starting SSH terminal…", ForeColor=Color.White, BackColor=Color.FromArgb(15,23,34), Padding=new Padding(20) };
        private bool closing, notified;
        private DateTime started;
        internal IntPtr EmbeddedWindow { get { return terminal; } }
        internal int ChildProcessId { get { return puttyProcess == null ? 0 : puttyProcess.Id; } }
        // Session-open state, not proof of SSH authentication. This also allows
        // Disconnect and explicit detached-window hosting during login prompts.
        public override bool Connected { get { return !closing && ProcessRunning; } }
        private bool ProcessRunning { get { try { return puttyProcess!=null && !puttyProcess.HasExited; } catch(InvalidOperationException) { return false; } } }
        private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback,IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder name,int size);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);

        public PuttyConnection()
        {
            Controls.Add(status); BackColor=status.BackColor;
            captureTimer.Tick += CaptureTick;
            Resize += delegate { ResizeTerminal(); };
            GotFocus += delegate { SendFocusToPutty(); };
        }
        public override bool Connect()
        {
            if(puttyProcess!=null) return false;
            Dock=DockStyle.Fill;
            try {
                string arguments=new ArgumentsBuilder(ResolveFavoriteCredentials(),Favorite).Build();
                puttyProcess=new Process {StartInfo=new ProcessStartInfo(Executables.GetPuttyBinaryPath(),arguments) {UseShellExecute=false,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory,WindowStyle=ProcessWindowStyle.Minimized},EnableRaisingEvents=true};
                puttyProcess.Exited += ProcessExited;
                if(!puttyProcess.Start()) throw new InvalidOperationException();
                started=DateTime.UtcNow;captureTimer.Start();return true;
            } catch {
                LastError="The SSH terminal could not start. Extract the complete DeskPilot ZIP and check the host, port and SSH options.";
                if(puttyProcess!=null) {puttyProcess.Dispose();puttyProcess=null;}
                return false;
            }
        }
        private void CaptureTick(object sender,EventArgs args)
        {
            if(closing) return;
            if(!ProcessRunning) {NotifyDisconnected();return;}
            if(terminal!=IntPtr.Zero) {captureTimer.Stop();return;}
            IntPtr found=IntPtr.Zero;uint expected=(uint)puttyProcess.Id;
            EnumWindows((window,unused)=> {
                uint pid;GetWindowThreadProcessId(window,out pid);
                if(pid!=expected) return true;
                var name=new StringBuilder(100);GetClassName(window,name,name.Capacity);
                // Never capture a host-key, authentication or security dialog.
                if(name.ToString()!="PuTTY") return true;
                found=window;return false;
            },IntPtr.Zero);
            if(found==IntPtr.Zero) {
                if(DateTime.UtcNow-started>TimeSpan.FromMinutes(2)) {status.Text="Waiting for PuTTY. Check for a host-key or login prompt. Close this tab to cancel.";}
                return;
            }
            uint style=Methods.GetWindowLong(found,(int)WindowLongParam.GWL_STYLE);
            style &= ~(uint)(WindowStyles.WS_CAPTION|WindowStyles.WS_THICKFRAME|WindowStyles.WS_SYSMENU|WindowStyles.WS_MINIMIZEBOX|WindowStyles.WS_MAXIMIZEBOX|WindowStyles.WS_MINIMIZE|WindowStyles.WS_MAXIMIZE|WindowStyles.WS_POPUP);
            style |= (uint)WindowStyles.WS_CHILD;
            Methods.SetWindowLong(found,(int)WindowLongParam.GWL_STYLE,style);
            uint extended=Methods.GetWindowLong(found,(int)WindowLongParam.GWL_EXSTYLE);
            extended &= ~(uint)(WindowExStyles.WS_EX_DLGMODALFRAME|WindowExStyles.WS_EX_CLIENTEDGE|WindowExStyles.WS_EX_STATICEDGE);
            Methods.SetWindowLong(found,(int)WindowLongParam.GWL_EXSTYLE,extended);
            Methods.SetParent(found,Handle);
            if(Methods.GetParent(found)!=Handle) {status.Text="Windows could not embed this SSH terminal. Close the tab and retry.";return;}
            terminal=found;status.Visible=false;ResizeTerminal();captureTimer.Stop();
        }
        private void ResizeTerminal()
        {
            if(terminal==IntPtr.Zero || closing) return;
            var flags=SetWindowPosFlags.FrameChanged|SetWindowPosFlags.DoNotActivate|SetWindowPosFlags.ShowWindow;
            Methods.SetWindowPos(terminal,IntPtr.Zero,0,0,Math.Max(1,ClientSize.Width),Math.Max(1,ClientSize.Height),flags);
            // PuTTY can restore a native caption while processing WM_SIZE. Clip
            // any remaining non-client frame outside our host, as in the original
            // embedding path, so only the terminal and its scroll bar are visible.
            var window=Methods.GetWindowRect(terminal);
            var client=Methods.GetClientRect(terminal);
            var origin=Point.Empty;Methods.ClientToScreen(terminal,ref origin);
            int left=Math.Max(0,origin.X-window.Left),top=Math.Max(0,origin.Y-window.Top);
            int bottom=Math.Max(0,window.Bottom-origin.Y-client.Height);
            if(left!=0 || top!=0 || bottom!=0)
                Methods.SetWindowPos(terminal,IntPtr.Zero,-left,-top,Math.Max(1,ClientSize.Width+left*2),Math.Max(1,ClientSize.Height+top+bottom),flags);
        }
        private void ProcessExited(object sender,EventArgs args)
        {
            if(closing || IsDisposed || !IsHandleCreated) return;
            try {BeginInvoke(new Action(NotifyDisconnected));} catch(InvalidOperationException) {}
        }
        private void NotifyDisconnected()
        {
            if(closing || notified) return;notified=true;captureTimer.Stop();terminal=IntPtr.Zero;FireDisconnected();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing && !closing) {
                closing=true;captureTimer.Stop();captureTimer.Dispose();
                if(puttyProcess!=null) {
                    puttyProcess.Exited-=ProcessExited;
                    try {if(ProcessRunning) {if(terminal!=IntPtr.Zero) PostMessage(terminal,0x10,IntPtr.Zero,IntPtr.Zero);if(!puttyProcess.WaitForExit(250)) puttyProcess.Kill();}}
                    catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {}
                    finally {puttyProcess.Dispose();puttyProcess=null;terminal=IntPtr.Zero;}
                }
            }
            base.Dispose(disposing);
        }
        void IFocusable.Focus() {SendFocusToPutty();}
        private void SendFocusToPutty() {if(Connected && terminal!=IntPtr.Zero) Methods.SetFocus(terminal);}
    }
}
