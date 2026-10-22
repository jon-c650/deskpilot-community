using System;
using System.Text;
using Terminals.Data;

namespace Terminals.Plugins.Putty
{
    internal class ArgumentsBuilder
    {
        private readonly IGuardedSecurity credentials;
        private readonly IFavorite favorite;
        private PuttyOptions puttyOptions;

        public ArgumentsBuilder(IGuardedSecurity credentials, IFavorite favorite)
        {
            this.credentials = credentials;
            this.favorite = favorite;
        }
        private void ValidateGeneral()
        {
            if (!(favorite.Protocol == SshConnectionPlugin.SSH || favorite.Protocol == TelnetConnectionPlugin.TELNET))
                throw new ArgumentException(string.Format("Protocol {0} is not supported", favorite.Protocol));
        }

        public string Build()
        {
            var args = default(string);
            ValidateGeneral();

            puttyOptions = favorite.ProtocolProperties as PuttyOptions;

            if (favorite.Protocol == SshConnectionPlugin.SSH)
                args = BuildSsh();
            else if (favorite.Protocol == TelnetConnectionPlugin.TELNET)
                args = BuildTelnet();


            return args;
        }

        internal static void ValidateHost(string host)
        {
            if (String.IsNullOrWhiteSpace(host) || host.StartsWith("-") || Uri.CheckHostName(host.Trim('[',']')) == UriHostNameType.Unknown)
                throw new ArgumentException("Enter a host name or IP address without a scheme, path or command options.");
        }
        internal static string Quote(string value)
        {
            if(value == null || value.IndexOfAny(new[]{'\0','\r','\n'}) >= 0) throw new ArgumentException("Invalid SSH argument.");
            var result = new StringBuilder("\""); int slashes=0;
            foreach(char c in value) {
                if(c=='\\') {slashes++;continue;}
                if(c=='\"') {result.Append('\\',slashes*2+1);result.Append(c);slashes=0;continue;}
                result.Append('\\',slashes);slashes=0;result.Append(c);
            }
            return result.Append('\\',slashes*2).Append('\"').ToString();
        }

        internal string BuildTelnet()
        {
            var args = new StringBuilder();
            var telnetOptions = puttyOptions as TelnetOptions;

            // 3.8.3.1 -load: load a saved session
            if (!string.IsNullOrEmpty(telnetOptions.SessionName))
                args.Append(" -load " + Quote(telnetOptions.SessionName));

            // 3.8.3.2 Selecting a protocol: -ssh, -telnet, -rlogin, -raw -serial
            args.Append(" -telnet");

            // 3.8.3.3 -v: increase verbosity
            if (telnetOptions.Verbose)
                args.Append(" -v");

            // 3.8.3.7 -P: specify a port number
            if (favorite.Port > 0)
                args.AppendFormat(" -P {0}", favorite.Port);

            ValidateHost(favorite.ServerName);
            args.Append(" " + Quote(favorite.ServerName));

            return args.ToString();
        }

        internal string BuildSsh()
        {
            var userName = this.credentials.UserName;
            // Passwords are entered in the terminal, never in process arguments.

            var args = new StringBuilder();
            var sshOptions = puttyOptions as SshOptions;

            // 3.8.3.1 -load: load a saved session
            if (!string.IsNullOrEmpty(sshOptions.SessionName))
                args.Append(" -load " + Quote(sshOptions.SessionName));

            // 3.8.3.2 Selecting a protocol: -ssh, -telnet, -rlogin, -raw -serial
            args.Append(" -ssh");

            // 3.8.3.3 -v: increase verbosity
            if (sshOptions.Verbose)
                args.Append(" -v");

            // 3.8.3.4 -l: specify a login name
            if (!string.IsNullOrEmpty(userName))
                args.Append(" -l " + Quote(userName));

            // 3.8.3.5 -L, -R and -D: set up port forwardings
            // not right now, supported via session config

            // 3.8.3.6 -m: read a remote command or script from a file
            // not right now

            // 3.8.3.7 -P: specify a port number
            if (favorite.Port > 0)
                args.AppendFormat(" -P {0}", favorite.Port);

            // Interactive password/MFA and Pageant keep secrets out of command lines.

            // 3.8.3.9 -agent and -noagent: control use of Pageant for authentication
            if (sshOptions.EnablePagentAuthentication)
                args.Append(" -agent");
            else
                args.Append(" -noagent");

            // 3.8.3.10 -A and -a: control agent forwarding
            if (sshOptions.EnablePagentForwarding)
                args.Append(" -A");
            else
                args.Append(" -a");

            // 3.8.3.11 -X and -x: control X11 forwarding
            if (sshOptions.X11Forwarding)
                args.Append(" -X");
            else
                args.Append(" -x");

            // 3.8.3.15 -C: enable compression
            if (sshOptions.EnableCompression)
                args.Append(" -C");

            // 3.8.3.16 -1 and -2: specify an SSH protocol version
            if (sshOptions.SshVersion == SshVersion.SshVersion1)
                throw new InvalidOperationException("SSH-1 is no longer supported. Select SSH-2 in connection properties.");
            else
                args.Append(" -2");

            ValidateHost(favorite.ServerName);
            args.Append(" " + Quote(favorite.ServerName));

            return args.ToString();
        }
    }
}
