using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Terminals.Connections;
using Terminals.Data;
using Terminals.Integration.Import;
namespace Terminals.Wizard
{
    // Import known RDP entries only. The removed Metro raw-packet scanner is never loaded.
    internal partial class AddExistingRDPConnections : UserControl
    {
        public delegate void DiscoveryCompleted();
        public event DiscoveryCompleted OnDiscoveryCompleted;
        public List<FavoriteConfigurationElement> DiscoveredConnections { get; private set; }
        public AddExistingRDPConnections()
        {
            DiscoveredConnections = new List<FavoriteConfigurationElement>();
            InitializeComponent();
            dataGridView1.Visible = false;
            label1.Text = "Importing known Remote Desktop entries from this Windows user profile.";
            label2.Text = "This beta does not scan the local network.";
            label4.Text = "Imported connections:";
        }
        public void StartImport(ConnectionManager connectionManager)
        {
            DiscoveredConnections.Clear();
            DiscoveredConnections.AddRange(ImportRdpRegistry.Import(connectionManager));
            ConnectionsCountLabel.Text = DiscoveredConnections.Count.ToString();
            PendingRequestsLabel.Text = "0";
            if (OnDiscoveryCompleted != null) OnDiscoveryCompleted();
        }
        internal void CancelDiscovery() { }
        private void ConnectionsCountLabel_Click(object sender, EventArgs e)
        {
            dataGridView1.DataSource = DiscoveredConnections;
            dataGridView1.Visible = true;
        }
    }
}
