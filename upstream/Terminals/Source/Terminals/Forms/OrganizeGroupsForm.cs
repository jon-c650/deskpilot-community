using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Terminals.Data;

namespace Terminals
{
    internal partial class OrganizeGroupsForm : Form
    {
        private readonly IPersistence persistence;

        private readonly FavoriteIcons favoriteIcons;

        internal OrganizeGroupsForm(IPersistence persistence, FavoriteIcons favoriteIcons)
        {
            this.persistence = persistence;
            this.favoriteIcons = favoriteIcons;

            InitializeComponent();

            this.gridGroups.AutoGenerateColumns = false;
            this.gridGroupFavorites.AutoGenerateColumns = false;
            LoadGroups();
            AddWireGuardButton();
        }


        // Read-only WireGuard discovery. Association storage is separate from Terminals.
        // Activation uses the official signed client and normal Windows UAC when required.
        private void AddWireGuardButton()
        {
            var button = new Button();
            button.Text = "WireGuard...";
            button.Width = 115;
            button.Height = 25;
            button.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            button.Location = new System.Drawing.Point(8, 7);
            button.Click += (sender, args) => EditWireGuardAssociation();
            this.panel3.Controls.Add(button);
            button.BringToFront();
        }

        private void EditWireGuardAssociation()
        {
            IGroup selected = GetSelectedGroup();
            if (selected == null)
            {
                MessageBox.Show(this, "Select a customer group first.", "DeskPilot WireGuard");
                return;
            }
            string name=selected.Name;
            Terminals.Forms.DeskPilot.FolderVpnBinding binding;
            string[] profiles;
            try {binding=new Terminals.Forms.DeskPilot.FolderVpnBinding(selected,persistence);profiles=binding.Names;}
            catch(Exception error) {MessageBox.Show(this,error.Message,"DeskPilot WireGuard");return;}
            using (var dialog = new Form())
            using (var selector = new ComboBox())
            using (var save = new Button())
            {
                dialog.Icon = Terminals.Forms.DeskPilot.ChromeIcons.ApplicationIcon;
                dialog.Text = "WireGuard — " + name;
                dialog.Width = 430;
                dialog.Height = 305;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MaximizeBox = false;
                selector.Left = 15;
                selector.Top = 18;
                selector.Width = 380;
                selector.DropDownStyle = ComboBoxStyle.DropDownList;
                selector.Items.Add("(No VPN)");
                selector.Items.AddRange(profiles.Cast<object>().ToArray());
                selector.SelectedIndex = 0;
                string existing=binding.Original;
                if(!String.IsNullOrEmpty(existing)) {if(!selector.Items.Contains(existing)) selector.Items.Add(existing);selector.SelectedItem=existing;}
                save.Text = "Save association";
                save.Left = 245;
                save.Top = 211;
                save.Width = 150;
                save.DialogResult = DialogResult.OK;
                var status = new Label();
                status.Left = 15;
                status.Top = 57;
                status.Width = 290;
                status.Height = 36;
                status.Text = "VPN: No tunnel selected";
                var refresh = new Button();
                refresh.Text = "Refresh status";
                refresh.Left = 15;
                refresh.Top = 103;
                refresh.Width = 115;
                Action updateStatus = () =>
                {
                    string tunnel = selector.SelectedItem as string;
                    if (String.IsNullOrEmpty(tunnel) || tunnel == "(No VPN)")
                    {
                        status.Text = "VPN: No tunnel selected";
                        return;
                    }
                    try
                    {
                        string serviceName = "WireGuardTunnel$" + tunnel;
                        using (var service = new System.ServiceProcess.ServiceController(serviceName))
                        {
                            var currentStatus = service.Status;
                            status.Text = currentStatus == System.ServiceProcess.ServiceControllerStatus.Running
                                ? "VPN: Running (service active; connectivity not verified)"
                                : "VPN: " + currentStatus.ToString();
                        }
                    }
                    catch (InvalidOperationException error)
                    {
                        status.Text = Terminals.Forms.DeskPilot.WireGuardControl.NativeError(error)==1060
                            ? "VPN: Inactive (Connect will request activation)"
                            : "VPN: Status unavailable ("+error.Message+")";
                    }
                    catch (Exception error)
                    {
                        status.Text = "VPN: Status unavailable (" + error.GetType().Name + ")";
                    }
                };
                selector.SelectedIndexChanged += (sender, args) => updateStatus();
                refresh.Click += (sender, args) => updateStatus();
                var connect = new Button();
                connect.Text = "Connect VPN";
                connect.Left = 135;
                connect.Top = 103;
                connect.Width = 120;
                var disconnect = new Button();
                disconnect.Text = "Disconnect VPN";
                disconnect.Left = 265;
                disconnect.Top = 103;
                disconnect.Width = 130;
                bool vpnOperationPending=false;
                dialog.FormClosing += (sender,args) => {if(vpnOperationPending) args.Cancel=true;};
                Action<bool> changeVpn = async start =>
                {
                    string tunnel = selector.SelectedItem as string;
                    if (String.IsNullOrEmpty(tunnel) || tunnel == "(No VPN)")
                    {
                        MessageBox.Show(dialog, "Select a WireGuard tunnel first.", "DeskPilot");
                        return;
                    }
                    if (MessageBox.Show(dialog,
                        (start ? "Request connection to " : "Request disconnection of ") + tunnel + "?" + Environment.NewLine +
                        "Windows may request administrator approval. If inactive, Connect activates the installed encrypted profile through official WireGuard. Its tunnel service may remain installed and start with Windows until disabled in WireGuard.",
                        "DeskPilot WireGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                        != DialogResult.Yes)
                        return;
                    vpnOperationPending=true;
                    connect.Enabled=false; disconnect.Enabled=false; save.Enabled=false; selector.Enabled=false; refresh.Enabled=false;
                    try
                    {
                        await Terminals.Forms.DeskPilot.WireGuardControl.ChangeAsync(tunnel,start);
                        updateStatus();
                    }
                    catch (System.ComponentModel.Win32Exception error)
                    {
                        MessageBox.Show(dialog,error.NativeErrorCode==1223
                            ? "The administrator prompt was cancelled. No VPN success is assumed."
                            : error.Message,"DeskPilot WireGuard",MessageBoxButtons.OK,MessageBoxIcon.Information);
                        updateStatus();
                    }
                    catch (Exception error)
                    {
                        MessageBox.Show(dialog,error.Message,"DeskPilot WireGuard",MessageBoxButtons.OK,MessageBoxIcon.Warning);
                        updateStatus();
                    }
                    finally { vpnOperationPending=false; if(!dialog.IsDisposed) { connect.Enabled=true; disconnect.Enabled=true; save.Enabled=true; selector.Enabled=true; refresh.Enabled=true; } }
                };
                connect.Click += (sender, args) => changeVpn(true);
                disconnect.Click += (sender, args) => changeVpn(false);
                dialog.Controls.Add(connect);
                dialog.Controls.Add(disconnect);
                dialog.Controls.Add(selector);
                dialog.Controls.Add(status);
                dialog.Controls.Add(refresh);
                dialog.Controls.Add(save);
                var profilesRow=new FlowLayoutPanel {Left=12,Top=146,Width=395,Height=48};
                foreach(bool import in new[]{false,true}) {
                    bool importing=import;
                    var button=new Button {Text=importing ? "Import .conf…" : "Refresh tunnels…",AutoSize=true,Height=32};
                    button.Click+=async delegate {
                        if(vpnOperationPending) return;vpnOperationPending=true;
                        profilesRow.Enabled=false;connect.Enabled=false;disconnect.Enabled=false;save.Enabled=false;
                        try {
                            var result=await Terminals.Forms.DeskPilot.WireGuardProfiles.UpdateAsync(dialog,importing);
                            if(result==null || dialog.IsDisposed) return;
                            foreach(string item in result.Names) if(!selector.Items.Contains(item)) selector.Items.Add(item);
                            if(result.Imported!=null) {selector.SelectedItem=result.Imported;MessageBox.Show(dialog,"Configuration imported into WireGuard. Save the association when ready. The tunnel has not been connected.","DeskPilot WireGuard");}
                            updateStatus();
                        } catch(System.ComponentModel.Win32Exception error) {if(!dialog.IsDisposed) MessageBox.Show(dialog,error.NativeErrorCode==1223 ? "Windows approval was cancelled." : "Windows could not start the operation.","DeskPilot WireGuard");}
                        catch(Exception error) {if(!dialog.IsDisposed) MessageBox.Show(dialog,error.Message,"DeskPilot WireGuard");}
                        finally {vpnOperationPending=false;if(!dialog.IsDisposed) {profilesRow.Enabled=true;connect.Enabled=true;disconnect.Enabled=true;save.Enabled=true;}}
                    };
                    profilesRow.Controls.Add(button);
                }
                dialog.Controls.Add(profilesRow);
                updateStatus();
                dialog.AcceptButton = save;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    binding.Save((string)selector.SelectedItem=="(No VPN)" ? null : (string)selector.SelectedItem);
                    MessageBox.Show(this, "Association saved.",
                        "DeskPilot WireGuard");
                }
                catch (Exception error)
                {
                    MessageBox.Show(this, "Could not save association: " + error.Message,
                        "DeskPilot WireGuard");
                }
            }
        }

        private void LoadGroups()
        {
            // because the Name property is on the INamedItem level, we have to bound directly the item with the name property
            this.gridGroups.DataSource = this.persistence.Groups.ToList();
        }

        internal void SelectChromeGroup(IGroup group)
        {
            if(group==null) return;
            foreach(DataGridViewRow row in gridGroups.Rows) {
                var candidate=row.DataBoundItem as IGroup;
                if(candidate!=null && candidate.StoreIdEquals(group)) {
                    gridGroups.ClearSelection(); row.Selected=true; gridGroups.CurrentCell=row.Cells[0]; return;
                }
            }
        }
        private IGroup GetSelectedGroup()
        {
            if (gridGroups.SelectedRows.Count > 0)
                return gridGroups.SelectedRows[0].DataBoundItem as IGroup;
            return null;
        }

        private void LoadSelectedGroupFavorites()
        {
            IGroup selectedGroup = GetSelectedGroup();
            if (selectedGroup != null)
            {
                this.gridGroupFavorites.DataSource = selectedGroup.Favorites.ToList();
            }
        }

        private void GridGroups_SelectedRowChanged(object sender, EventArgs e)
        {
            LoadSelectedGroupFavorites();
        }

        private void TsbAddGroup_Click(object sender, EventArgs e)
        {
            string newGroupName = NewGroupForm.AskFroGroupName(this.persistence);
            if (string.IsNullOrEmpty(newGroupName))
                return;
            
            FavoritesFactory.GetOrAddNewGroup(this.persistence, newGroupName);   
            this.LoadGroups();
        }

        private void TsbDeleteGroup_Click(object sender, EventArgs e)
        {
            IGroup group = GetSelectedGroup();
            if (group != null && OrganizeFavoritesForm.AskIfRealyDelete("group"))
            {
                this.persistence.Groups.Delete(group);
                LoadGroups();
            }
        }

        private void TsbAddConnection_Click(object sender, EventArgs e)
        {
            IGroup group = GetSelectedGroup();
            if (group != null)
                this.AddFavoritesToGroup(group);
        }

        private void AddFavoritesToGroup(IGroup group)
        {
            using (var frmAddConnection = new AddConnectionForm(this.persistence, this.favoriteIcons))
            {
                if (frmAddConnection.ShowDialog() == DialogResult.OK)
                {
                    group.AddFavorites(frmAddConnection.SelectedFavorites);
                    this.LoadSelectedGroupFavorites();
                    this.persistence.SaveAndFinishDelayedUpdate();
                }
            }
        }

        private void TsbDeleteConnection_Click(object sender, EventArgs e)
        {
            IGroup group = GetSelectedGroup();
            if (group != null)
            {
                if (this.gridGroupFavorites.SelectedRows.Count > 0)
                {
                    List<IFavorite> selectedFavorites = GetSelectedFavorites();
                    group.RemoveFavorites(selectedFavorites);
                    LoadSelectedGroupFavorites();
                    this.persistence.SaveAndFinishDelayedUpdate();
                }
            }
        }

        private List<IFavorite> GetSelectedFavorites()
        {
            var selectedRows = this.gridGroupFavorites.SelectedRows.Cast<DataGridViewRow>();
            return selectedRows.Select(row => row.DataBoundItem as IFavorite)
                .ToList();
        }
    }
}