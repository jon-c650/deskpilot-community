using System.Windows.Forms;
using Terminals.Connections;
using Terminals.Data;
namespace Terminals.Network
{
    internal class Server
    {
        internal const int SERVER_PORT=1216;
        public bool ServerOnline {get {return false;}}
        public Server(IPersistence persistence,ConnectionManager manager) {}
        public void Stop() {}
        public void Start() { MessageBox.Show("Legacy network sharing has been disabled for security. Use File import/export to transfer connections.","DeskPilot"); }
    }
}
