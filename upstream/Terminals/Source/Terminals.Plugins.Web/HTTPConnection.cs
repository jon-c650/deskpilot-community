using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Terminals.Common.Converters;

namespace Terminals.Connections
{
    internal class HTTPConnection : Connection
    {
        private readonly WebView2 browser=new WebView2 {Dock=DockStyle.Fill,DefaultBackgroundColor=Color.FromArgb(15,23,34)};
        private readonly TextBox address=new TextBox {Width=400};
        private readonly Label status=new Label {Dock=DockStyle.Bottom,Height=28,Padding=new Padding(8,5,0,0),ForeColor=Color.White,BackColor=Color.FromArgb(23,34,49),Text="Starting embedded browser…"};
        private readonly FlowLayoutPanel toolbar=new FlowLayoutPanel {Dock=DockStyle.Top,Height=38,Padding=new Padding(5),BackColor=Color.FromArgb(23,34,49),WrapContents=false};
        private Uri home;
        private bool open,closing,starting;
        internal WebView2 EmbeddedBrowser {get {return browser;}}
        // An open browser tab is not proof of HTTP authentication or reachability.
        public override bool Connected {get {return open && !closing;}}
        internal HTTPConnection()
        {
            Controls.Add(browser);Controls.Add(status);Controls.Add(toolbar);
            AddButton("←",()=> {if(browser.CoreWebView2!=null && browser.CanGoBack) browser.GoBack();});
            AddButton("→",()=> {if(browser.CoreWebView2!=null && browser.CanGoForward) browser.GoForward();});
            AddButton("Home",()=>Navigate(home));AddButton("Reload",()=> {if(browser.CoreWebView2!=null) browser.Reload();else StartBrowser();});
            toolbar.Controls.Add(address);AddButton("Go",NavigateAddress);
            address.KeyDown+=(sender,args)=> {if(args.KeyCode==Keys.Enter) {NavigateAddress();args.Handled=true;args.SuppressKeyPress=true;}};
            toolbar.Resize+=delegate {address.Width=Math.Max(100,toolbar.ClientSize.Width-290);};
        }
        private void AddButton(string text,Action action)
        {
            var button=new Button {Text=text,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(36,26),ForeColor=Color.White,BackColor=Color.FromArgb(38,55,75),FlatStyle=FlatStyle.Flat,UseVisualStyleBackColor=false};
            button.Click+=delegate {action();};toolbar.Controls.Add(button);
        }
        internal static bool AllowedAddress(string text,out Uri uri)
        {
            return Uri.TryCreate(text,UriKind.Absolute,out uri) && (uri.Scheme==Uri.UriSchemeHttp || uri.Scheme==Uri.UriSchemeHttps) && String.IsNullOrEmpty(uri.UserInfo) && !String.IsNullOrEmpty(uri.Host);
        }
        private void NavigateAddress()
        {
            Uri uri;if(!AllowedAddress(address.Text,out uri)) {status.Text="Enter a complete http:// or https:// address without credentials in the URL.";return;}Navigate(uri);
        }
        private void Navigate(Uri uri)
        {
            if(closing || uri==null || browser.CoreWebView2==null) return;
            browser.CoreWebView2.Navigate(uri.AbsoluteUri);
        }
        public override bool Connect()
        {
            if(!AllowedAddress(UrlConverter.ExtractAbsoluteUrl(Favorite),out home)) {LastError="Enter a valid HTTP or HTTPS address. Credentials must not be included in the URL.";return false;}
            Dock=DockStyle.Fill;open=true;address.Text=home.AbsoluteUri;StartBrowser();return true;
        }
        private async void StartBrowser()
        {
            if(starting || closing) return;starting=true;
            try {
                string identity=(OriginFavorite ?? Favorite).Id.ToString("N")+"|"+home.GetLeftPart(UriPartial.Authority);
                string key;using(var hash=SHA256.Create()) key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-","");
                string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPilot","WebProfiles",key);
                Directory.CreateDirectory(folder);
                var environment=await CoreWebView2Environment.CreateAsync(null,folder);
                if(closing || IsDisposed) return;
                await browser.EnsureCoreWebView2Async(environment);
                if(closing || IsDisposed) return;
                var core=browser.CoreWebView2;
                core.Settings.AreHostObjectsAllowed=false;core.Settings.IsWebMessageEnabled=false;
                core.Settings.IsPasswordAutosaveEnabled=false;core.Settings.IsGeneralAutofillEnabled=false;
                core.NavigationStarting+=(sender,args)=> {
                    Uri target;if(!AllowedAddress(args.Uri,out target)) {args.Cancel=true;status.Text="Only HTTP and HTTPS navigation is supported in this session.";} else status.Text="Loading…";
                };
                core.SourceChanged+=delegate {if(!closing) address.Text=core.Source;};
                core.NavigationCompleted+=(sender,args)=> {if(!closing) status.Text=args.IsSuccess ? "Web session — "+new Uri(core.Source).Host : "Page could not load: "+args.WebErrorStatus+". Check the address, VPN and certificate.";};
                core.NewWindowRequested+=(sender,args)=> {args.Handled=true;Uri target;if(args.IsUserInitiated && AllowedAddress(args.Uri,out target)) Navigate(target);else status.Text="Automatic or unsupported pop-up blocked.";};
                core.PermissionRequested+=(sender,args)=> {args.State=CoreWebView2PermissionState.Deny;};
                core.DownloadStarting+=(sender,args)=> {args.Cancel=true;status.Text="Downloads are not supported in embedded sessions.";};
                core.BasicAuthenticationRequested+=(sender,args)=>DeferAuthentication(args);
                core.ServerCertificateErrorDetected+=(sender,args)=>ConfirmCertificate(args);
                core.ProcessFailed+=(sender,args)=> {if(!closing) status.Text="The embedded browser stopped. Close this tab and reconnect.";};
                Navigate(home);
            } catch(WebView2RuntimeNotFoundException) {
                if(!closing) {status.Text="Microsoft Edge WebView2 Runtime is required. Install it, then click Reload.";AddButton("Get WebView2",()=>Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/"){UseShellExecute=true}));}
            } catch {
                if(!closing) status.Text="The embedded browser could not start. Close this tab and retry; check WebView2 Runtime and the Windows profile folder.";
            } finally {starting=false;}
        }
        // Modal UI is posted after the WebView2 callback returns; holding a
        // deferral avoids nested browser callback loops and incomplete decisions.
        private async void DeferAuthentication(CoreWebView2BasicAuthenticationRequestedEventArgs request)
        {
            var deferral=request.GetDeferral();request.Cancel=true;
            try {await Task.Yield();if(!closing && !IsDisposed) PromptAuthentication(request);}
            catch { /* Keep the default denial if the controller closes. */ }
            finally {try {deferral.Complete();} catch(System.Runtime.InteropServices.COMException) {}}
        }
        private bool certificatePrompt;
        private async void ConfirmCertificate(CoreWebView2ServerCertificateErrorDetectedEventArgs request)
        {
            request.Action=CoreWebView2ServerCertificateErrorAction.Cancel;
            if(certificatePrompt) return;
            var deferral=request.GetDeferral();certificatePrompt=true;
            try {
                await Task.Yield();if(closing || IsDisposed) return;
                Uri target;if(!AllowedAddress(request.RequestUri,out target) || target.Scheme!="https") return;
                if(MessageBox.Show(this,"The certificate for "+target.GetLeftPart(UriPartial.Authority)+" could not be verified ("+request.ErrorStatus+").\n\nContinue for this certificate in this browser session only?", "DeskPilot — certificate warning",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes)
                    request.Action=CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
            } catch { /* Keep denial on closure or failed prompt. */ }
            finally {certificatePrompt=false;try {deferral.Complete();} catch(System.Runtime.InteropServices.COMException) {}}
        }
        private void PromptAuthentication(CoreWebView2BasicAuthenticationRequestedEventArgs request)
        {
            request.Cancel=true;Uri target;
            if(!AllowedAddress(request.Uri,out target) || target.Scheme!="https" || target.GetLeftPart(UriPartial.Authority)!=home.GetLeftPart(UriPartial.Authority)) {status.Text="HTTP authentication is permitted only on the saved HTTPS origin. No saved credentials were sent.";return;}
            using(var dialog=new Form {Text="DeskPilot — web sign in",Width=440,Height=240,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false})
            using(var icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath)) {
                dialog.Icon=icon;
                var user=new TextBox {Left=110,Top=60,Width=290};var password=new TextBox {Left=110,Top=95,Width=290,UseSystemPasswordChar=true};
                var ok=new Button {Text="Sign in",Left=230,Top=140,Width=80,DialogResult=DialogResult.OK};var cancel=new Button {Text="Cancel",Left=320,Top=140,Width=80,DialogResult=DialogResult.Cancel};
                dialog.Controls.AddRange(new Control[]{new Label {Text=target.GetLeftPart(UriPartial.Authority),Left=15,Top=15,Width=390},new Label {Text="User name",Left=15,Top=64},new Label {Text="Password",Left=15,Top=99},user,password,ok,cancel});dialog.AcceptButton=ok;dialog.CancelButton=cancel;
                if(dialog.ShowDialog(this)==DialogResult.OK) {request.Response.UserName=user.Text;request.Response.Password=password.Text;request.Cancel=false;}
                password.Clear();
            }
        }
        protected override void Dispose(bool disposing)
        {
            closing=true;open=false;
            // Dispose the controller before the host handle disappears; no orphaned
            // browser window or persisted certificate exception is retained by us.
            if(disposing) browser.Dispose();base.Dispose(disposing);
        }
    }
}
