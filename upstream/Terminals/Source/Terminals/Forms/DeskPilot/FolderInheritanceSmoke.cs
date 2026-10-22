using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Terminals.Data;
using Terminals.Data.Credentials;
using Terminals.Forms.Controls;
using Terminals.Forms.DeskPilot;

namespace Terminals
{
    internal partial class MainForm
    {
        private static void FolderAssert(bool value,string message) {if(!value) throw new InvalidOperationException(message);}
        private void RunFolderInheritanceSmoke(IFavorite favorite,string report)
        {
            string path=Path.Combine(Path.GetTempPath(),"DeskPilot-folder-smoke-"+Guid.NewGuid().ToString("N")+".dpapi");
            var previous=folderDefaults;Guid identity=favorite.Id;string memberships=favorite.GroupNames;var original=favorite.Copy();int favoriteCount=persistence.Favorites.Count();
            var servers=favorite.Groups.Single();var customer=servers.Parent;string originalName=customer.Name;
            var nested=persistence.Factory.CreateGroup("Nested defaults (CI sample)");nested.Parent=servers;persistence.Groups.Add(nested);
            var other=persistence.Factory.CreateGroup("Other defaults (CI sample)");persistence.Groups.Add(other);
            var endpoint=persistence.Factory.CreateFavorite();endpoint.Name="Inheritance endpoint (CI sample)";endpoint.ServerName="192.0.2.11";persistence.Favorites.Add(endpoint);persistence.Favorites.UpdateFavorite(endpoint,new List<IGroup>{nested});
            var credential=persistence.Factory.CreateCredentialSet();credential.Name="Folder credential (CI sample)";
            var guard=new GuardedCredentialFactory(persistence).CreateCredential(credential);guard.UserName="CI-only-user";guard.Password="CI-only-secret-not-a-real-password";persistence.Credentials.Add(credential);
            try {
                var store=new FolderInheritance(persistence,connectionManager,path);folderDefaults=store;
                var unchanged=FolderInheritance.Capture(favorite);
                var model=store.FolderModel(customer);((ISecurityOptions)((IContainsCredentials)model.ProtocolProperties).GetSecurity()).Credential=credential.Id;model.Port=3395;model.NewWindow=!original.NewWindow;model.Security.Credential=credential.Id;
                var options=model.ProtocolProperties;var redirect=options.GetType().GetProperty("Redirect").GetValue(options,null);
                redirect.GetType().GetProperty("Clipboard").SetValue(redirect,false,null);
                store.SaveFolder(customer,model,new[]{"Port","NewWindow","Security.Credential","ProtocolProperties.Redirect.Clipboard","ProtocolProperties.TsGateway.Security.Credential"});
                var child=store.FolderModel(servers);child.Port=3401;child.Display.Width=1440;
                store.SaveFolder(servers,child,new[]{"Port","Display.Width"});
                var resolved=ResolveFolderDefaults(favorite);
                FolderAssert(resolved.Port==3401 && resolved.Display.Width==1440 && resolved.Security.Credential==credential.Id,"Existing endpoint did not inherit parent/child defaults through connection-copy route");
                FolderAssert(favorite.Port==original.Port && favorite.Name==original.Name && favorite.ServerName==original.ServerName && favorite.GroupNames==memberships && favorite.Security.Credential==original.Security.Credential,"Folder save rewrote endpoint data");
                FolderAssert(FolderInheritance.Capture(favorite).All(item=>FolderInheritance.Equal(item.Value,unchanged[item.Key])),"Folder defaults changed saved endpoint settings");
                FolderAssert(store.Resolve(endpoint).Port==3401,"Nested descendant did not inherit");
                var resolvedCredentials=new GuardedCredentialFactory(persistence).CreateSecurityOptoins(resolved.Security).GetResolvedCredentials();
                FolderAssert(resolvedCredentials.UserName=="CI-only-user" && resolvedCredentials.Password=="CI-only-secret-not-a-real-password","Inherited credential reference did not resolve through existing guarded credentials");
                FolderAssert(new GuardedCredentialFactory(persistence).CreateCredential(((IContainsCredentials)resolved.ProtocolProperties).GetSecurity()).Password=="CI-only-secret-not-a-real-password","Inherited gateway reference was not hydrated for the existing RDP transport path");
                FolderAssert(!FolderInheritance.Capture(model).Keys.Any(key=>key.Contains("Encrypted")),"Credential contents entered inheritance snapshot");
                store.SaveEndpoint(favorite,new[]{"Port","Security.Credential"},null);
                FolderAssert(store.Resolve(favorite).Port==original.Port && store.Resolve(favorite).Security.Credential==original.Security.Credential,"Per-setting local overrides failed");
                child.Port=3409;store.SaveFolder(servers,child,new[]{"Port","Display.Width"});
                FolderAssert(store.Resolve(favorite).Port==original.Port && store.Resolve(endpoint).Port==3409,"Parent edits did not preserve endpoint override or update descendant");
                store.SaveEndpoint(favorite,new string[0],null);
                FolderAssert(store.Resolve(favorite).Port==3409,"Clearing endpoint override did not resume inheritance");
                var before=endpoint.Copy();endpoint.Port=3477;persistence.Favorites.UpdateFavorite(endpoint,endpoint.Groups);
                store.RecordEndpointEdits(endpoint,before);
                FolderAssert(store.Resolve(endpoint).Port==3477,"Existing endpoint editor changes were not explicit overrides");
                store.SaveEndpoint(endpoint,new string[0],null);
                var otherModel=store.FolderModel(other);otherModel.Port=4455;store.SaveFolder(other,otherModel,new[]{"Port"});
                persistence.Favorites.UpdateFavorite(endpoint,new List<IGroup>{nested,other});
                bool ambiguous=false;try {store.Resolve(endpoint);} catch(InvalidOperationException) {ambiguous=true;}
                FolderAssert(ambiguous,"Multiple unrelated folder defaults silently chose a source");
                store.SaveEndpoint(endpoint,new string[0],FolderInheritance.GroupKey(nested));
                FolderAssert(store.Resolve(endpoint).Port==3409,"Explicit inheritance source failed");
                customer.Name=originalName+" renamed";persistence.Groups.Update(customer);
                FolderAssert(store.Resolve(endpoint).Port==3409,"Folder rename lost stable defaults identity");
                customer.Name=originalName;persistence.Groups.Update(customer);
                other.Parent=other;bool cycle=false;try {store.FolderModel(other);} catch(InvalidOperationException) {cycle=true;}finally {other.Parent=null;}
                FolderAssert(cycle,"Folder cycle was not rejected");
                var reload=new FolderInheritance(persistence,connectionManager,path);
                FolderAssert(reload.Resolve(favorite).Port==3409,"Protected defaults did not survive reload");
                FolderAssert(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("FolderSettingsFile"),"Folder defaults were saved as plaintext");
                model.Port=3398;store.SaveFolder(customer,model,new[]{"Port","NewWindow","Security.Credential","ProtocolProperties.Redirect.Clipboard","ProtocolProperties.TsGateway.Security.Credential"});
                bool stale=false;try {reload.SaveFolder(customer,model,new[]{"Port"});} catch(IOException) {stale=true;}
                FolderAssert(stale,"Stale defaults writer overwrote newer settings");
                reload.ReloadIfChanged();FolderAssert(reload.Resolve(favorite).Port==store.Resolve(favorite).Port,"Reopened defaults did not refresh a stale cache");
                var failedEdit=store.FolderModel(servers);failedEdit.Port=4999;bool rollback=false;
                try {store.SaveFolderWithAssociation(servers,failedEdit,new[]{"Port"},()=>{throw new IOException("CI simulated VPN save failure");});} catch(InvalidOperationException) {rollback=true;}
                FolderAssert(rollback && store.Resolve(favorite).Port==3409 && store.FolderRecord(servers).Values.Any(value=>value.Path=="Display.Width"),"VPN save failure left partial folder settings");
                File.AppendAllText(report,"PASS reopened folder defaults refresh external edits; failed VPN save restores prior folder policy and individual settings\n");
                persistence.Credentials.Remove(credential);bool missingCredential=false;try {store.Resolve(favorite);} catch(InvalidOperationException) {missingCredential=true;}finally {persistence.Credentials.Add(credential);}
                FolderAssert(missingCredential,"Missing inherited credential fell back to unrelated authentication");
                byte[] good=File.ReadAllBytes(path);File.WriteAllBytes(path,new byte[]{1,2,3});bool corrupt=false;
                try {new FolderInheritance(persistence,connectionManager,path);}catch(InvalidOperationException) {corrupt=true;}finally {File.WriteAllBytes(path,good);}
                FolderAssert(corrupt,"Unreadable defaults were silently ignored");
                File.AppendAllText(report,"PASS folder defaults: parent/child/descendant propagation, connection copies, credential references, endpoint overrides/reset, source conflicts, rename/cycle, protected persistence/reload, stale-write and corruption safeguards\n");
                string vpnTest=Path.Combine(Path.GetTempPath(),"DeskPilot-vpn-picker-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(vpnTest);
                try {
                    File.WriteAllLines(Path.Combine(vpnTest,"groups.txt"),new[]{other.Name+"\tExistingTunnel"});
                    File.WriteAllLines(Path.Combine(vpnTest,"profiles.txt"),new[]{"CustomerTunnel"});
                    var binding=new FolderVpnBinding(customer,persistence,vpnTest);var staleBinding=new FolderVpnBinding(customer,persistence,vpnTest);
                    FolderAssert(binding.Names.Contains("CustomerTunnel") && binding.Names.Contains("ExistingTunnel"),"VPN picker omitted catalog or existing tunnel names");
                    binding.Save("CustomerTunnel");
                    FolderAssert(new FolderVpnBinding(customer,persistence,vpnTest).Original=="CustomerTunnel" && new FolderVpnBinding(other,persistence,vpnTest).Original=="ExistingTunnel","Folder VPN association did not round-trip or overwrote another customer");
                    bool vpnStale=false;try {staleBinding.Save("OtherTunnel");} catch(IOException) {vpnStale=true;}
                    FolderAssert(vpnStale,"Stale VPN picker overwrote another change");
                    bool invalidVpn=false;try {binding.Validate("bad tunnel\nname");} catch(ArgumentException) {invalidVpn=true;}
                    FolderAssert(invalidVpn,"VPN picker accepted an invalid tunnel name");
                    customer.Name=originalName+" VPN rename";
                    FolderAssert(new FolderVpnBinding(customer,persistence,vpnTest).Original=="CustomerTunnel","VPN association lost on folder rename");
                    var replacement=persistence.Factory.CreateGroup(originalName);
                    persistence.Groups.Add(replacement);
                    try {FolderAssert(new FolderVpnBinding(replacement,persistence,vpnTest).Original==null,"Reusing a folder name inherited a previous customer's VPN");}
                    finally {persistence.Groups.Delete(replacement);customer.Name=originalName;}
                    binding.Save(null);FolderAssert(new FolderVpnBinding(customer,persistence,vpnTest).Original==null,"Clearing a folder VPN did not restore inheritance");
                    File.AppendAllText(report,"PASS folder VPN picker catalog, names-only association round-trip, other customers preserved, inherit reset, stable identity/rename/name reuse, invalid names and stale-write protection; no tunnel activation attempted\n");
                } finally {Directory.Delete(vpnTest,true);}
                RunFolderPropertiesUiSmoke(favorite,servers,store,report);
                FolderAssert(favorite.Id==identity && favorite.Name==original.Name && favorite.ServerName==original.ServerName && favorite.Notes==original.Notes && favorite.GroupNames==memberships && favorite.Security.Credential==original.Security.Credential,"Inheritance UI changed endpoint identity, notes, membership or credential reference");
                FolderAssert(persistence.Favorites.Count()==favoriteCount+1,"Detached folder editor created a saved endpoint");
            } finally {
                folderDefaults=previous;favorite.Port=original.Port;persistence.Favorites.UpdateFavorite(favorite,favorite.Groups);
                customer.Name=originalName;persistence.Groups.Update(customer);
                persistence.Favorites.Delete(endpoint);persistence.Groups.Delete(nested);persistence.Groups.Delete(other);persistence.Credentials.Remove(credential);
                foreach(string file in new[]{path,path+".bak",path+".lock"}) if(File.Exists(file)) File.Delete(file);
            }
        }
        private void RunFolderPropertiesUiSmoke(IFavorite favorite,IGroup group,FolderInheritance store,string report)
        {
            favsList1.SelectChromeFixtureGroup(group);
            FolderAssert(favsList1.FolderPropertiesMenu!=null,"Folder Properties context command missing");
            Exception failure=null;int stage=0,ticks=0;bool saved=false;
            using(var timer=new Timer {Interval=100}) {
                timer.Tick+=delegate {
                    try {
                        if(++ticks>100) throw new InvalidOperationException("Folder Properties editor test timed out at stage "+stage);
                        var dialog=Application.OpenForms.OfType<FolderPropertiesForm>().FirstOrDefault();
                        var editor=Application.OpenForms.OfType<NewTerminalForm>().FirstOrDefault();
                        if(stage==0 && dialog!=null) {
                            stage=1;
                            FolderAssert(dialog.Controls.Find("importWireGuard",true).Any() && dialog.Controls.Find("refreshWireGuard",true).Any(),"Native WireGuard import/discovery buttons missing");
                            FolderAssert(dialog.Controls.Find("folderVpn",true).OfType<ComboBox>().Any(),"Folder Properties VPN picker missing");
                            FolderAssert(FindFolderChildren(dialog).OfType<NewTerminalForm>().Any(),"Endpoint settings layout was not embedded in folder Properties");
                            ((TextBox)dialog.Controls.Find("txtPort",true).Single()).Text="3412";
                            SaveChromeScreenshot("ui-folder-properties.png");FindFolderButton(dialog,"Save").PerformClick();saved=true;
                        }
                    } catch(Exception error) {failure=error;CloseFolderTestDialogs();}
                };
                timer.Start();favsList1.FolderPropertiesMenu.PerformClick();timer.Stop();
            }
            if(failure!=null) throw failure;
            FolderAssert(saved && store.Resolve(favorite).Port==3412,"Actual folder Properties/edit/save route did not update inherited settings");
            byte[] snapshot=File.ReadAllBytes(store.FilePath);
            using(var close=new Timer {Interval=100}) {
                close.Tick+=delegate {
                    try {
                        foreach(var dialog in Application.OpenForms.OfType<FolderPropertiesForm>().ToArray()) {
                            var list=FindFolderChildren(dialog).OfType<CheckedListBox>().Single();
                            for(int i=0;i<list.Items.Count;i++) if(list.Items[i].ToString().StartsWith("Port  —",StringComparison.Ordinal)) list.SetItemChecked(i,false);
                            FolderAssert(((TextBox)dialog.Controls.Find("txtPort",true).Single()).Text=="3398","Clearing a folder override did not restore the parent value in the embedded editor");
                            dialog.DialogResult=DialogResult.Cancel;dialog.Close();
                        }
                    } catch(Exception error) {failure=error;CloseFolderTestDialogs();}
                };
                close.Start();favsList1.FolderPropertiesMenu.PerformClick();close.Stop();
            }
            if(failure!=null) throw failure;
            FolderAssert(snapshot.SequenceEqual(File.ReadAllBytes(store.FilePath)),"Cancel changed folder defaults");
            favsList1.SelectChromeFixtureFavorite(favorite);
            using(var timer=new Timer {Interval=100}) {
                timer.Tick+=delegate {
                    try {
                        var dialog=Application.OpenForms.OfType<FolderPropertiesForm>().FirstOrDefault();if(dialog==null) return;
                        var list=FindFolderChildren(dialog).OfType<CheckedListBox>().Single();
                        for(int i=0;i<list.Items.Count;i++) if(list.Items[i].ToString().StartsWith("Port  —",StringComparison.Ordinal)) list.SetItemChecked(i,true);
                        FindFolderButton(dialog,"Save").PerformClick();
                    } catch(Exception error) {failure=error;CloseFolderTestDialogs();}
                };
                timer.Start();favsList1.EndpointInheritanceMenu.PerformClick();timer.Stop();
            }
            if(failure!=null) throw failure;
            FolderAssert(store.Resolve(favorite).Port==favorite.Port,"Actual endpoint override context menu did not keep local port");
            File.AppendAllText(report,"PASS actual folder context Properties, embedded endpoint settings editor/save/cancel and endpoint inherited-settings override UI; no endpoint copies persisted\n");
        }
        private static IEnumerable<Control> FindFolderChildren(Control parent) {foreach(Control child in parent.Controls) {yield return child;foreach(var descendant in FindFolderChildren(child)) yield return descendant;}}
        private static Button FindFolderButton(Control form,string text) {return FindFolderChildren(form).OfType<Button>().Single(button=>button.Text==text);}
        private static void CloseFolderTestDialogs() {foreach(Form dialog in Application.OpenForms.Cast<Form>().ToArray()) if(dialog is FolderPropertiesForm || dialog is NewTerminalForm) {dialog.DialogResult=DialogResult.Cancel;dialog.Close();}}
    }
}
