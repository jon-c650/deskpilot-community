using System.Collections.Generic;
using System.Windows.Forms;
namespace Terminals.Forms.EditFavorite
{
    // Retains the editor contract without loading the discontinued RAS vendor library.
    internal class RasControl : UserControl
    {
        internal List<string> ConnectionNames { get; private set; }
        internal RasControl() { ConnectionNames = new List<string>(); Enabled = false; }
        internal void OnServerNameChanged(string protocolName, string serverName) { }
        public void SetControls() { }
    }
}
