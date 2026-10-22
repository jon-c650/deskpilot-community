using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Terminals.Connections;
using Terminals.Data;

namespace Terminals.Forms.DeskPilot
{
    internal sealed class FolderPropertiesForm : Form
    {
        private readonly FolderInheritance store;
        private readonly IPersistence persistence;
        private readonly ConnectionManager manager;
        private readonly FavoriteIcons icons;
        private readonly IGroup group;
        private readonly IFavorite endpoint;
        private readonly ChromePalette palette;
        private IFavorite model;
        private readonly HashSet<string> defined=new HashSet<string>();
        private readonly CheckedListBox settings=new CheckedListBox {Dock=DockStyle.Fill,CheckOnClick=true,HorizontalScrollbar=true};
        private readonly ComboBox source=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=450};
        private readonly TextBox filter=new TextBox {Dock=DockStyle.Top};
        private readonly Label summary=new Label {Dock=DockStyle.Top,Height=64,Padding=new Padding(10,6,10,0)};
        private readonly ComboBox category=new ComboBox {Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList};
        private readonly Label settingDetail=new Label {Dock=DockStyle.Bottom,Height=100,Padding=new Padding(8),AutoEllipsis=true};
        private FolderVpnBinding vpnBinding;
        private ComboBox folderVpn;
        private bool filling;
        private NewTerminalForm embeddedEditor;
        private Dictionary<string,FolderSettingValue> editorSnapshot;
        private sealed class SettingItem
        {
            internal string Path,Text;
            public override string ToString() {return Text;}
        }
        private sealed class FolderChoice
        {
            internal string Key,Text;
            public override string ToString() {return Text;}
        }
        internal FolderPropertiesForm(FolderInheritance store,IPersistence persistence,ConnectionManager manager,FavoriteIcons icons,IGroup group,IFavorite endpoint=null,ChromePalette palette=null)
        {
            Icon=ChromeIcons.ApplicationIcon;
            this.store=store;this.persistence=persistence;this.manager=manager;this.icons=icons;this.group=group;this.endpoint=endpoint;this.palette=palette;
            Text=endpoint==null ? "Folder Properties — "+group.Name : "Inherited settings — "+endpoint.Name;
            Font=new Font("Segoe UI",10F);StartPosition=FormStartPosition.CenterParent;MinimumSize=new Size(1050,660);Size=new Size(1220,760);
            var record=endpoint==null ? store.FolderRecord(group) : store.EndpointRecord(endpoint);
            foreach(string key in endpoint==null ? record.Values.Select(value=>value.Path) : record.LocalOverrides) defined.Add(key);
            if(endpoint==null) model=store.FolderModel(group);
            else {
                source.Items.Add(new FolderChoice {Key="",Text="Automatic (one folder branch with defaults)"});
                source.Items.Add(new FolderChoice {Key="none",Text="Do not inherit folder defaults"});
                foreach(var member in endpoint.Groups.OrderBy(item=>FolderInheritance.GroupPath(item))) source.Items.Add(new FolderChoice {Key=FolderInheritance.GroupKey(member),Text=FolderInheritance.GroupPath(member)});
                source.SelectedIndex=0;
                for(int i=0;i<source.Items.Count;i++) if(((FolderChoice)source.Items[i]).Key==(record.SourceFolder ?? "")) source.SelectedIndex=i;
                source.SelectedIndexChanged+=delegate {RefreshSettings();};
            }
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=52,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(7)};
            var cancel=new Button {Text="Cancel",DialogResult=DialogResult.Cancel,AutoSize=true};buttons.Controls.Add(cancel);CancelButton=cancel;
            var save=new Button {Text="Save",AutoSize=true};save.Click+=delegate {Save(false);};buttons.Controls.Add(save);AcceptButton=save;
            var edit=new Button {Text=endpoint==null ? "Edit values…" : "Save and edit endpoint…",AutoSize=true};
            edit.Click+=delegate {Save(true);};if(endpoint!=null) buttons.Controls.Add(edit);
            if(endpoint==null) {
                var credentials=new Button {Text="Saved credentials…",AutoSize=true};credentials.Click+=delegate {ChooseCredentials();};buttons.Controls.Add(credentials);
            }
            var inheritAll=new Button {Text=endpoint==null ? "Reset folder overrides" : "Inherit all",AutoSize=true};inheritAll.Click+=delegate {ReadEditor(false);defined.Clear();ReloadInheritedEditor();RefreshSettings();};buttons.Controls.Add(inheritAll);
            var defineAll=new Button {Text=endpoint==null ? "Override all" : "Keep all local",AutoSize=true};defineAll.Click+=delegate {
                ReadEditor(false);foreach(var item in FolderInheritance.Capture(endpoint ?? model).Keys) defined.Add(item);RefreshSettings();
            };if(endpoint!=null) buttons.Controls.Add(defineAll);
            var top=new Panel {Dock=DockStyle.Top,Height=endpoint==null ? 146 : 136};
            top.Controls.Add(summary);
            if(endpoint==null) {
                vpnBinding=new FolderVpnBinding(group,persistence);
                var vpnRow=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=70,Padding=new Padding(10,0,10,0),WrapContents=true};
                vpnRow.Controls.Add(new Label {Text="VPN tunnel",Width=105,Margin=new Padding(0,7,0,0)});
                folderVpn=new ComboBox {Name="folderVpn",Width=330,DropDownStyle=ComboBoxStyle.DropDown,AccessibleName="VPN tunnel for this customer folder"};
                folderVpn.Items.Add("(Inherit parent VPN / no association)");folderVpn.Items.AddRange(vpnBinding.Names.Cast<object>().ToArray());
                folderVpn.SelectedIndex=0;if(!String.IsNullOrEmpty(vpnBinding.Original)) folderVpn.SelectedItem=vpnBinding.Original;
                vpnRow.Controls.Add(folderVpn);
                foreach(bool import in new[]{false,true}) {
                    bool importing=import;
                    var button=new Button {Name=importing ? "importWireGuard" : "refreshWireGuard",Text=importing ? "Import .conf…" : "Refresh tunnels…",AutoSize=true,Height=32};
                    button.Click+=async delegate {
                        vpnRow.Enabled=false;
                        try {
                            var result=await WireGuardProfiles.UpdateAsync(this,importing);if(result==null || IsDisposed) return;
                            foreach(string name in result.Names) if(!folderVpn.Items.Contains(name)) folderVpn.Items.Add(name);
                            if(result.Imported!=null) {folderVpn.SelectedItem=result.Imported;MessageBox.Show(this,"Imported "+result.Imported+" into WireGuard. Click Save to associate it with this folder. The tunnel has not been connected.","DeskPilot WireGuard",MessageBoxButtons.OK,MessageBoxIcon.Information);}
                        } catch(System.ComponentModel.Win32Exception error) {if(!IsDisposed) MessageBox.Show(this,error.NativeErrorCode==1223 ? "Windows approval was cancelled. No import or discovery success is assumed." : "Windows could not start the WireGuard operation.","DeskPilot WireGuard",MessageBoxButtons.OK,MessageBoxIcon.Information);}
                        catch(Exception error) {if(!IsDisposed) MessageBox.Show(this,error.Message,"DeskPilot WireGuard",MessageBoxButtons.OK,MessageBoxIcon.Information);}
                        finally {if(!IsDisposed) vpnRow.Enabled=true;}
                    };
                    vpnRow.Controls.Add(button);
                }

                vpnRow.Controls.Add(new Label {Text="Select, refresh installed tunnels, or import a .conf file. Save keeps the association; import does not connect the VPN.",AutoSize=true,Margin=new Padding(0,6,0,0)});
                top.Controls.Add(vpnRow);
            }
            if(endpoint!=null) {var sourceRow=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=38,Padding=new Padding(8,0,0,0)};sourceRow.Controls.Add(new Label {Text="Inherit from",AutoSize=true,Margin=new Padding(3,6,9,0)});sourceRow.Controls.Add(source);top.Controls.Add(sourceRow);}
            var find=new Panel {Dock=DockStyle.Top,Height=34,Padding=new Padding(10,2,10,2)};filter.Dock=DockStyle.Fill;find.Controls.Add(filter);find.Controls.Add(new Label {Text="Find a setting",Dock=DockStyle.Left,Width=105,TextAlign=ContentAlignment.MiddleLeft});filter.AccessibleName="Find an inherited setting";filter.TextChanged+=delegate {RefreshSettings();};
            if(endpoint==null) {
                var inheritance=new Panel {Dock=DockStyle.Right,Width=390,Padding=new Padding(12)};
                inheritance.Controls.Add(settings);inheritance.Controls.Add(find);
                inheritance.Controls.Add(category);inheritance.Controls.Add(settingDetail);
                category.Items.AddRange(new object[]{"General","Display","Redirection","Gateway","Other protocol settings","Before connecting","All settings"});category.SelectedIndex=0;
                category.SelectedIndexChanged+=delegate {ReadEditor(false);RefreshSettings();};
                settings.SelectedIndexChanged+=delegate {settingDetail.Text=settings.SelectedItem==null ? "Select a setting to see its value and source." : settings.SelectedItem.ToString();};
                inheritance.Controls.Add(new Label {Text="Folder overrides",Dock=DockStyle.Top,Height=36,Font=new Font("Segoe UI",11F,FontStyle.Bold)});
                var editorHost=new Panel {Dock=DockStyle.Fill,Padding=new Padding(8)};
                Controls.Add(editorHost);Controls.Add(inheritance);Controls.Add(top);Controls.Add(buttons);
                var template=model.Copy();template.Name="Folder defaults "+Guid.NewGuid().ToString("N");template.ServerName="folder-defaults.invalid";
                embeddedEditor=NewTerminalForm.CreateSettingsEditor(persistence,manager,icons,template);
                embeddedEditor.EmbedFolderEditor(editorHost);editorSnapshot=FolderInheritance.Capture(embeddedEditor.ReadFolderEditor(false));
                embeddedEditor.Leave+=delegate {ReadEditor(false);RefreshSettings();};
            } else {Controls.Add(settings);Controls.Add(find);Controls.Add(top);Controls.Add(buttons);}

            settings.ItemCheck+=delegate(object sender,ItemCheckEventArgs e) {
                if(filling) return;ReadEditor(false);string key=((SettingItem)settings.Items[e.Index]).Path;
                if(e.NewValue==CheckState.Checked) defined.Add(key);else defined.Remove(key);
                if(endpoint==null) ReloadInheritedEditor();
                if(IsHandleCreated) BeginInvoke(new Action(RefreshSettings));
            };
            RefreshSettings();ApplyReadableColors(this);
            save.BackColor=Color.FromArgb(0,91,164);save.ForeColor=Color.White;save.FlatStyle=FlatStyle.Flat;
            settings.Font=new Font("Segoe UI",10.5F);settings.IntegralHeight=false;
            settingDetail.Text="Check a setting to override it here. Clear its check to inherit. Select a row to see the full value and source.";
        }
        // The legacy settings controls contain fixed blue text and native inputs. Use one
        // high-contrast dialog palette instead of mixing those with the shell's dark theme.
        private static void ApplyReadableColors(Control root)
        {
            root.ForeColor=Color.FromArgb(24,30,38);
            root.BackColor=root is TextBoxBase || root is ListBox || root is ComboBox || root is TreeView ? Color.White : Color.FromArgb(245,246,248);
            var button=root as Button;
            if(button!=null) {button.UseVisualStyleBackColor=false;button.BackColor=Color.White;button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Color.FromArgb(146,155,166);button.MinimumSize=new Size(90,32);}
            foreach(Control child in root.Controls) ApplyReadableColors(child);
        }
        private bool InCategory(string path)
        {
            if(endpoint!=null || category.SelectedIndex==6) return true;
            if(category.SelectedIndex==0) return !path.StartsWith("ProtocolProperties.") && !path.StartsWith("Display.") && !path.StartsWith("ExecuteBeforeConnect.");
            if(category.SelectedIndex==1) return path.StartsWith("Display.") || path.Contains(".UserInterface.");
            if(category.SelectedIndex==2) return path.Contains(".Redirect.");
            if(category.SelectedIndex==3) return path.Contains(".TsGateway.");
            if(category.SelectedIndex==5) return path.StartsWith("ExecuteBeforeConnect.");
            return path.StartsWith("ProtocolProperties.") && !path.Contains(".Redirect.") && !path.Contains(".TsGateway.") && !path.Contains(".UserInterface.");
        }
        private string SourceKey {get {return endpoint==null ? null : ((FolderChoice)source.SelectedItem).Key;}}
        private static string Friendly(string path) {return Regex.Replace(path.Replace("ProtocolProperties.","").Replace(".Credential",".Saved credential"),"([a-z])([A-Z])","$1 $2").Replace("."," / ");}
        private string DisplayValue(FolderSettingValue value)
        {
            if(value==null) return "not available for this protocol";
            if(value.Path.EndsWith(".Credential")) {
                Guid id;if(!Guid.TryParse(value.Value,out id) || id==Guid.Empty) return "No saved credential";
                var credential=persistence.Credentials[id];return credential==null ? "Missing saved credential" : credential.Name;
            }
            return value.IsNull || String.IsNullOrEmpty(value.Value) ? "(empty)" : value.Value.Replace("\r"," ").Replace("\n"," ");
        }
        private void RefreshSettings()
        {
            if(filling || IsDisposed) return;int scroll=settings.TopIndex;string selected=settings.SelectedItem==null ? null : ((SettingItem)settings.SelectedItem).Path;filling=true;settings.BeginUpdate();
            try {
                Dictionary<string,FolderSettingValue> inherited;Dictionary<string,string> sources;
                if(endpoint==null) {
                    inherited=FolderInheritance.Capture(store.FolderModel(group,false));sources=store.FolderSources(group,false);
                    summary.Text="Edit a value to override it for this folder. Clear its check under Folder overrides to inherit again.\r\nChanges apply on the next connection after you Save.";
                } else {
                    try {inherited=FolderInheritance.Capture(store.Resolve(endpoint,SourceKey,false));sources=store.FolderSources(store.Source(endpoint,SourceKey));summary.Text="Checked settings keep this endpoint's saved value; unchecked settings inherit from the chosen folder. Save and edit endpoint opens its existing properties editor. Clearing an override resumes inheritance. Active sessions are unchanged.";}
                    catch(InvalidOperationException error) {inherited=FolderInheritance.Capture(endpoint);sources=new Dictionary<string,string>();summary.Text=error.Message;}
                }
                var local=FolderInheritance.Capture(endpoint ?? model);
                settings.Items.Clear();
                foreach(string key in local.Keys.Union(inherited.Keys).OrderBy(item=>item)) {
                    string parent=sources.ContainsKey(key) ? DisplayValue(inherited.ContainsKey(key) ? inherited[key] : null)+" [from "+sources[key]+"]" : endpoint==null ? "application default" : "endpoint saved value (no folder default)";
                    if(!InCategory(key) && String.IsNullOrEmpty(filter.Text)) continue;
                    string text=Friendly(key)+"  —  "+(endpoint==null ? defined.Contains(key) ? DisplayValue(local.ContainsKey(key) ? local[key] : null)+" [override here]" : parent : "local: "+DisplayValue(local.ContainsKey(key) ? local[key] : null)+"; folder: "+parent);
                    if(!String.IsNullOrEmpty(filter.Text) && text.IndexOf(filter.Text,StringComparison.OrdinalIgnoreCase)<0) continue;
                    settings.Items.Add(new SettingItem {Path=key,Text=text},defined.Contains(key));
                    if(key==selected) settings.SelectedIndex=settings.Items.Count-1;
                }
            } finally {settings.EndUpdate();if(settings.Items.Count>0) settings.TopIndex=Math.Max(0,Math.Min(scroll,settings.Items.Count-1));filling=false;}
        }
        private bool ReadEditor(bool validate)
        {
            if(embeddedEditor==null) return true;
            var value=embeddedEditor.ReadFolderEditor(validate);if(value==null) return false;
            var current=FolderInheritance.Capture(value);
            foreach(var item in current.Values) if(!editorSnapshot.ContainsKey(item.Path) || !FolderInheritance.Equal(item,editorSnapshot[item.Path])) defined.Add(item.Path);
            model=value;editorSnapshot=current;return true;
        }
        private void ReloadInheritedEditor()
        {
            if(embeddedEditor==null) return;
            var values=FolderInheritance.Capture(model);
            var effective=store.FolderModel(group,false);
            store.Apply(effective,new FolderSettingsRecord {Protocol=model.Protocol,Values=values.Values.Where(item=>defined.Contains(item.Path)).ToList()},null,false);
            effective.Name=model.Name;effective.ServerName=model.ServerName;model=effective;
            embeddedEditor.ReloadFolderEditor(model);editorSnapshot=FolderInheritance.Capture(embeddedEditor.ReadFolderEditor(false));
        }
        private sealed class CredentialChoice
        {
            internal Guid Id;internal string Name;
            public override string ToString() {return Name;}
        }
        private void ChooseCredentials()
        {
            ReadEditor(false);
            using(var dialog=new Form {Icon=ChromeIcons.ApplicationIcon,Text="Saved folder credentials",StartPosition=FormStartPosition.CenterParent,Size=new Size(540,230),Font=Font,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false}) {
                var panel=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,Padding=new Padding(12),WrapContents=false};dialog.Controls.Add(panel);
                var combos=new List<ComboBox>();var targets=new List<ISecurityOptions> {model.Security};var paths=new List<string> {"Security.Credential"};
                var gateway=model.ProtocolProperties as IContainsCredentials;
                if(gateway!=null) {targets.Add(gateway.GetSecurity());paths.AddRange(FolderInheritance.Capture(model).Keys.Where(key=>key.StartsWith("ProtocolProperties.") && key.EndsWith(".Credential")).Take(1));}
                for(int i=0;i<targets.Count && i<paths.Count;i++) {
                    panel.Controls.Add(new Label {Text=i==0 ? "Endpoint authentication" : "Gateway authentication",AutoSize=true});
                    var combo=new ComboBox {Width=470,DropDownStyle=ComboBoxStyle.DropDownList};combo.Items.Add(new CredentialChoice {Id=Guid.Empty,Name="No saved credential (application defaults)"});
                    foreach(var credential in persistence.Credentials.OrderBy(item=>item.Name)) combo.Items.Add(new CredentialChoice {Id=credential.Id,Name=credential.Name});
                    combo.SelectedIndex=0;for(int j=0;j<combo.Items.Count;j++) if(((CredentialChoice)combo.Items[j]).Id==targets[i].Credential) combo.SelectedIndex=j;
                    combos.Add(combo);panel.Controls.Add(combo);
                }
                var save=new Button {Text="Use selected credentials",AutoSize=true,DialogResult=DialogResult.OK};panel.Controls.Add(save);dialog.AcceptButton=save;ApplyReadableColors(dialog);
                if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                for(int i=0;i<combos.Count;i++) {
                    var id=((CredentialChoice)combos[i].SelectedItem).Id;
                    if(targets[i].Credential!=id) {targets[i].Credential=id;targets[i].EncryptedDomain=null;targets[i].EncryptedUserName=null;targets[i].EncryptedPassword=null;defined.Add(paths[i]);}
                }
                if(embeddedEditor!=null) {embeddedEditor.ReloadFolderEditor(model);editorSnapshot=FolderInheritance.Capture(embeddedEditor.ReadFolderEditor(false));}
                RefreshSettings();
            }
        }
        private void Save(bool editEndpoint)
        {
            try {
                if(endpoint==null) {if(!ReadEditor(true)) return;
                    string tunnel=folderVpn.SelectedIndex==0 ? null : folderVpn.Text.Trim();
                    vpnBinding.Validate(tunnel);vpnBinding.CheckCurrent();
                    store.SaveFolderWithAssociation(group,model,defined,()=>vpnBinding.Save(tunnel));}
                else {store.Source(endpoint,SourceKey);store.SaveEndpoint(endpoint,defined,SourceKey);}
                DialogResult=editEndpoint ? DialogResult.Retry : DialogResult.OK;Close();
            } catch(Exception error) {MessageBox.Show(this,error.Message,"Folder settings could not be saved",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
    }
    // Names-only association shared with the existing VPN/groups UI. No config files or keys.
    internal sealed class FolderVpnBinding
    {
        private readonly string path,group;
        private byte[] snapshot;
        internal string Original {get;private set;}
        internal string[] Names {get;private set;}
        private static string Storage(IPersistence persistence)
        {
            string scope=persistence.TypeId+"|"+Terminals.Configuration.Settings.Instance.FileLocations.Favorites+"|"+(persistence.TypeId==0 ? "" : Terminals.Configuration.Settings.Instance.ConnectionString);
            string suffix;using(var sha=System.Security.Cryptography.SHA256.Create()) suffix=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(scope))).Replace("-","");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPilot","WireGuardPreview","Associations",suffix);
        }
        internal FolderVpnBinding(IGroup folder,IPersistence persistence,string storage=null)
        {
            group=FolderInheritance.GroupKey(folder);bool custom=storage!=null;storage=storage ?? Storage(persistence);
            path=Path.Combine(storage,"groups-v2.txt");
            EnsureMigrated(storage,persistence,custom ? storage : null);
            snapshot=File.ReadAllBytes(path);
            var values=Read();string existing;Original=values.TryGetValue(group,out existing) ? existing : null;
            string catalog=Path.Combine(custom ? storage : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPilot","WireGuardPreview"),"profiles.txt");
            Names=(File.Exists(catalog) ? File.ReadAllLines(catalog) : new string[0]).Concat(values.Values).Select(value=>value.Trim()).Where(value=>value.Length>0 && !value.StartsWith("#")).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value=>value).ToArray();
        }
        internal static Dictionary<string,string> Associations(IPersistence persistence)
        {
            var first=persistence.Groups.FirstOrDefault();
            if(first==null) return new Dictionary<string,string>();
            return new FolderVpnBinding(first,persistence).Read();
        }
        private static void EnsureMigrated(string storage,IPersistence persistence,string legacyStorage)
        {
            Directory.CreateDirectory(storage);string target=Path.Combine(storage,"groups-v2.txt");
            using(var gate=new FileStream(target+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                if(File.Exists(target)) return;
                string legacy=Path.Combine(legacyStorage ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPilot","WireGuardPreview"),"groups.txt");
                var names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                if(File.Exists(legacy)) foreach(string line in File.ReadAllLines(legacy)) {int separator=line.IndexOf('\t');if(separator>0) names[line.Substring(0,separator)]=line.Substring(separator+1);}
                var migrated=new List<string>();
                foreach(var matches in persistence.Groups.GroupBy(item=>item.Name,StringComparer.OrdinalIgnoreCase)) {
                    string tunnel;if(matches.Count()==1 && names.TryGetValue(matches.Key,out tunnel)) {WireGuardControl.ValidateName(tunnel);migrated.Add(FolderInheritance.GroupKey(matches.Single())+"\t"+tunnel);}
                }
                string temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
                try {File.WriteAllLines(temporary,migrated.ToArray());File.Move(temporary,target);} finally {if(File.Exists(temporary)) File.Delete(temporary);}
                // Do not read names again after this migration; an old name may belong
                // to a different customer later. The legacy file is retained untouched.
            }
        }
        private Dictionary<string,string> Read()
        {
            var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            if(snapshot!=null) foreach(string line in System.Text.Encoding.UTF8.GetString(snapshot).TrimStart('\uFEFF').Split('\n')) {
                int separator=line.IndexOf('\t');if(separator>0) values[line.Substring(0,separator)]=line.Substring(separator+1).TrimEnd('\r');
            }
            return values;
        }
        internal void Validate(string tunnel)
        {
            if(group.IndexOfAny(new[]{'\r','\n','\t'})>=0) throw new InvalidOperationException("This folder name cannot be stored as a VPN association.");
            if(!String.IsNullOrEmpty(tunnel)) WireGuardControl.ValidateName(tunnel);
        }
        internal void CheckCurrent()
        {
            var current=File.Exists(path) ? File.ReadAllBytes(path) : null;
            if((snapshot==null)!=(current==null) || snapshot!=null && !snapshot.SequenceEqual(current)) throw new IOException("VPN associations changed in another window. Reopen Properties before saving.");
        }
        internal void Save(string tunnel)
        {
            Validate(tunnel);if(String.Equals(tunnel ?? "",Original ?? "",StringComparison.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using(var gate=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                var current=File.Exists(path) ? File.ReadAllBytes(path) : null;
                if((snapshot==null)!=(current==null) || snapshot!=null && !snapshot.SequenceEqual(current)) throw new IOException("VPN associations changed in another window. Reopen Properties before saving the VPN selection.");
                var values=Read();if(String.IsNullOrEmpty(tunnel)) values.Remove(group);else values[group]=tunnel;
                string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try {
                    File.WriteAllLines(temporary,values.Select(item=>item.Key+"\t"+item.Value).ToArray());
                    byte[] written=File.ReadAllBytes(temporary);
                    if(File.Exists(path)) File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
                    snapshot=written;Original=tunnel;
                } finally {if(File.Exists(temporary)) File.Delete(temporary);}
            }
        }
    }

}
