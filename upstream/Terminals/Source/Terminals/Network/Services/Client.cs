using System;
using System.Windows.Forms;
namespace Terminals.Network
{
    internal delegate void ServerConnectionHandler(ShareFavoritesEventArgs args);
    internal class Client
    {
        public static event ServerConnectionHandler OnServerConnection;
        public static void Stop() {}
        public static void Start(string server) { MessageBox.Show("Legacy network sharing has been disabled for security. Use File import/export to transfer connections.","DeskPilot"); }
    }
}