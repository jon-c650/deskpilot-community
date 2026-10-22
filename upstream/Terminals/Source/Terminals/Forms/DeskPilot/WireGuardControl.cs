using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Terminals.Forms.DeskPilot
{
    internal enum TunnelRequest { None, Install, Start, Stop }
    internal static class WireGuardControl
    {
        internal static void ValidateName(string name)
        {
            if(String.IsNullOrEmpty(name) || name=="." || name==".." || !Regex.IsMatch(name,@"\A[A-Za-z0-9_=+.-]{1,32}\z"))
                throw new ArgumentException("The tunnel name is not a valid WireGuard Windows tunnel name.");
        }
        internal static TunnelRequest ChooseRequest(ServiceControllerStatus? state,bool start)
        {
            if(!state.HasValue) return start ? TunnelRequest.Install : TunnelRequest.None;
            if(state==ServiceControllerStatus.Running) return start ? TunnelRequest.None : TunnelRequest.Stop;
            if(state==ServiceControllerStatus.Stopped) return start ? TunnelRequest.Start : TunnelRequest.None;
            throw new InvalidOperationException("The tunnel is changing state ("+state+"). Wait for it to finish, then try again.");
        }
        internal static int NativeError(Exception error)
        {
            while(error != null) {var native=error as Win32Exception; if(native != null) return native.NativeErrorCode; error=error.InnerException;}
            return 0;
        }
        internal static ServiceControllerStatus? ReadState(string tunnel)
        {
            ValidateName(tunnel);
            try { using(var service=new ServiceController("WireGuardTunnel$"+tunnel)) return service.Status; }
            catch(Exception error) { if(NativeError(error)==1060) return null; throw; }
        }
        private static string InstallationDirectory()
        {
            // Read the OS's native Program Files location; never search PATH or user directories.
            using(var hive=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32))
            using(var key=hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion",false)) {
                string programFiles=key == null ? null : key.GetValue("ProgramFilesDir") as string;
                if(String.IsNullOrEmpty(programFiles) || !Path.IsPathRooted(programFiles)) throw new InvalidOperationException("Windows Program Files location is unavailable.");
                return Path.Combine(programFiles,"WireGuard");
            }
        }
        internal static string InstallArguments(string directory,string tunnel)
        {
            ValidateName(tunnel);
            string path=Path.Combine(directory,"Data","Configurations",tunnel+".conf.dpapi");
            return "/installtunnelservice \""+path+"\"";
        }
        internal static Task ChangeAsync(string tunnel,bool start)
        {
            ValidateName(tunnel);
            return Task.Run(() => {
                var request=ChooseRequest(ReadState(tunnel),start);
                if(request==TunnelRequest.None) return;
                if(request==TunnelRequest.Install) {
                    string directory=InstallationDirectory(); string exe=Path.Combine(directory,"wireguard.exe");
                    VerifyOfficialBinary(exe);
                    // Only official WireGuard reads/decrypts its protected config after normal UAC consent.
                    // DeskPilot never enumerates, opens, copies or decrypts that directory's files.
                    RunElevated(exe,InstallArguments(directory,tunnel));
                } else {
                    try {
                        using(var service=new ServiceController("WireGuardTunnel$"+tunnel)) {
                            if(start) service.Start(); else service.Stop();
                        }
                    } catch(Exception error) {
                        if(NativeError(error)!=5) throw;
                        string sc=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"sc.exe");
                        RunElevated(sc,(start ? "start" : "stop")+" \"WireGuardTunnel$"+tunnel+"\"");
                    }
                }
                // WireGuard Manager may remove a stopped tunnel service. Absence is a valid
                // disconnected result, rather than a second misleading 'no service' failure.
                var deadline=DateTime.UtcNow.AddSeconds(20);
                while(DateTime.UtcNow < deadline) {
                    var state=ReadState(tunnel);
                    if(start ? state==ServiceControllerStatus.Running : !state.HasValue || state==ServiceControllerStatus.Stopped) return;
                    System.Threading.Thread.Sleep(200);
                }
                throw new System.TimeoutException("The tunnel did not reach the requested service state. Check the official WireGuard log.");
            });
        }
        private static void RunElevated(string executable,string arguments)
        {
            var info=new ProcessStartInfo(executable,arguments) { UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden };
            using(var process=Process.Start(info)) {
                if(process==null) throw new InvalidOperationException("Windows did not start the requested VPN operation.");
                if(!process.WaitForExit(120000)) throw new InvalidOperationException("The VPN operation is still running. Check WireGuard before retrying.");
                if(process.ExitCode!=0) throw new InvalidOperationException("The VPN operation returned Windows exit code "+process.ExitCode+". Check the official WireGuard log for details; no connection success is assumed.");
            }
        }
        private static void VerifyOfficialBinary(string path)
        {
            if(!File.Exists(path)) throw new FileNotFoundException("Install the official WireGuard for Windows application first.",path);
            VerifyTrust(path);
            using(var signature=X509Certificate.CreateFromSignedFile(path))
            using(var certificate=new X509Certificate2(signature))
                if(!String.Equals(certificate.GetNameInfo(X509NameType.SimpleName,false),"WireGuard LLC",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The installed executable is not signed by WireGuard LLC. No elevated operation was started.");
        }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
        private struct TrustFile {internal uint Size; [MarshalAs(UnmanagedType.LPWStr)] internal string Path; internal IntPtr Handle,Subject;}
        [StructLayout(LayoutKind.Sequential)]
        private struct TrustData {
            internal uint Size; internal IntPtr Policy,Sip; internal uint Ui,Revocation,Union; internal IntPtr File;
            internal uint StateAction; internal IntPtr State,Url; internal uint Flags,Context; internal IntPtr SignatureSettings;
        }
        [DllImport("wintrust.dll",ExactSpelling=true,PreserveSig=true)]
        private static extern int WinVerifyTrust(IntPtr window,ref Guid action,ref TrustData data);
        private static void VerifyTrust(string path)
        {
            var file=new TrustFile { Size=(uint)Marshal.SizeOf(typeof(TrustFile)),Path=path };
            var memory=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
            var action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            var data=new TrustData {Size=(uint)Marshal.SizeOf(typeof(TrustData)),Ui=2,Union=1,File=memory,StateAction=1,Flags=0x1000};
            Marshal.StructureToPtr(file,memory,false);
            try {
                int code=WinVerifyTrust(new IntPtr(-1),ref action,ref data);
                if(code!=0) throw new InvalidOperationException("Windows could not verify the installed WireGuard signature (0x"+code.ToString("X8")+"). No elevated operation was started.");
            } finally {
                data.StateAction=2; WinVerifyTrust(new IntPtr(-1),ref action,ref data);
                Marshal.DestroyStructure(memory,typeof(TrustFile)); Marshal.FreeHGlobal(memory);
            }
        }
    }
}
