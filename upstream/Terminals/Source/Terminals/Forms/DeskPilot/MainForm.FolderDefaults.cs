using System;
using System.Windows.Forms;
using Terminals.Data;
using Terminals.Forms.DeskPilot;

namespace Terminals
{
    internal partial class MainForm
    {
        private FolderInheritance folderDefaults;
        private FolderInheritance FolderDefaults {get {return folderDefaults ?? (folderDefaults=new FolderInheritance(persistence,connectionManager));}}
        internal bool HasFolderDefaults(IFavorite favorite) {return chromeHost!=null && FolderDefaults.HasDefaults(favorite);}
        internal IFavorite ResolveFolderDefaults(IFavorite favorite) {if(chromeHost==null) return favorite.Copy();FolderDefaults.ReloadIfChanged();return FolderDefaults.Resolve(favorite);}
        private void OpenFolderProperties(IGroup group)
        {
            if(group==null) return;
            try {FolderDefaults.ReloadIfChanged();using(var dialog=new FolderPropertiesForm(FolderDefaults,persistence,connectionManager,favoriteIcons,group,null,chrome)) dialog.ShowDialog(this);RefreshVpnAsync();}
            catch(Exception error) {MessageBox.Show(this,error.Message,"Folder Properties",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
        private void OpenEndpointInheritance(IFavorite favorite)
        {
            if(favorite==null) return;
            try {
                while(true) {
                    FolderDefaults.ReloadIfChanged();
                    using(var dialog=new FolderPropertiesForm(FolderDefaults,persistence,connectionManager,favoriteIcons,null,favorite,chrome)) {
                        if(dialog.ShowDialog(this)!=DialogResult.Retry) break;
                    }
                    connectionsUiFactory.EditFavorite(favorite,result=>{});
                    favorite=persistence.Favorites[favorite.Id];if(favorite==null) break;
                }
            } catch(Exception error) {MessageBox.Show(this,error.Message,"Inherited settings",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        }
        internal bool RecordFolderEndpointEdits(IFavorite favorite,IFavorite before)
        {
            if(chromeHost==null) return true;
            try {FolderDefaults.RecordEndpointEdits(favorite,before);return true;}
            catch(Exception error) {MessageBox.Show(this,"Endpoint changes were saved, but inheritance overrides could not be saved. The session was not opened. Reopen Inherited settings before connecting.\n\n"+error.Message,"Inherited settings",MessageBoxButtons.OK,MessageBoxIcon.Information);return false;}
        }
    }
}
