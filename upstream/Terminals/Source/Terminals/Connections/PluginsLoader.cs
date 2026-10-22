using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Terminals.Configuration;

namespace Terminals.Connections
{
    internal class PluginsLoader : IPluginsLoader
    {
        private readonly string[] enabledPlugins;
        private static readonly List<string> loadIssues=new List<string>();
        internal static string LoadProblemText { get { return String.Join("\n",loadIssues.ToArray()); } }
        internal static void ClearLoadIssues() {loadIssues.Clear();}
        private static void RecordLoadFailure(string path,Exception error)
        {
            bool blocked=false;
            for(var current=error;current!=null;current=current.InnerException)
                if(current.HResult==unchecked((int)0x80131515)) blocked=true;
            string name=Path.GetFileNameWithoutExtension(path).Replace("Terminals.Plugins.","");
            loadIssues.Add(blocked
                ? "Windows blocked the downloaded " + name + " plugin. Close DeskPilot, open the downloaded ZIP's Properties, select Unblock and Apply, then extract into a new folder and restart."
                : name + " could not load. Extract the complete ZIP and check plugins.log in Tools > Open log folder.");
        }

        public PluginsLoader(IPluginSettings settings)
        {
            this.enabledPlugins = settings.DisabledPlugins;
        }

        public IEnumerable<IConnectionPlugin> Load()
        {
            ClearLoadIssues();
            WritePluginDiagnostic("DeskPilot build " + Program.Info.BuildId + "; executable: " + Assembly.GetExecutingAssembly().Location);
            WritePluginDiagnostic("Plugin root: " + FindBasePluginDirectory());
            List<string> allPluginAssemblies = FindPluginAssemblies()
                .Where(p => !enabledPlugins.Contains(p))
                .ToList();

            WritePluginDiagnostic("Plugin assemblies found: " + allPluginAssemblies.Count);
            foreach (string plugin in allPluginAssemblies)
                WritePluginDiagnostic("Candidate: " + plugin);
            var availablePlugins = allPluginAssemblies.SelectMany(LoadAssemblyPlugins)
                    .ToList();

            if (!availablePlugins.Any())
                throw new ApplicationException("No available protocol plugin was loaded.\n"+LoadProblemText);

            return availablePlugins;
        }

        private static IEnumerable<IConnectionPlugin> LoadAssemblyPlugins(string pluginFile)
        {
            try
            {
                WritePluginDiagnostic("Loading: " + pluginFile);
                Assembly pluginAssembly = Assembly.LoadFrom(pluginFile);
                var types = FindAllPluginTypes(pluginAssembly);
                return types.Select(Activator.CreateInstance).OfType<IConnectionPlugin>().ToArray();
            }
            catch (Exception exception)
            {
                string message = string.Format("Unable to load plugins from '{0}'.", pluginFile);
                WritePluginDiagnostic(message + " " + exception.ToString());
                Logging.Info(message, exception);
                RecordLoadFailure(pluginFile,exception);
                return new IConnectionPlugin[0];
            }
        }

        public IEnumerable<PluginDefinition> FindAvailablePlugins()
        {
            IEnumerable<string> allPluginAssemblies = FindPluginAssemblies();
            return allPluginAssemblies.Select(LoadPlugDefinition)
                .ToList();
        }

        private static PluginDefinition LoadPlugDefinition(string pluginFile)
        {
            string description = GetAssemblyDescription(pluginFile);
            return new PluginDefinition(pluginFile, description);
        }

        private static string GetAssemblyDescription(string pluginFile)
        {
            Assembly assembly = Assembly.ReflectionOnlyLoadFrom(pluginFile);
            CustomAttributeData customData = assembly.GetCustomAttributesData()
                .FirstOrDefault(IsAssemblyDecriptionAttribute);

            if (customData == null)
                return string.Empty;

            return customData.ConstructorArguments[0].Value.ToString();
        }

        private static bool IsAssemblyDecriptionAttribute(CustomAttributeData attribute)
        {
            return attribute.Constructor.DeclaringType == typeof(AssemblyDescriptionAttribute) &&
                   attribute.ConstructorArguments.Count == 1;
        }

        private static IEnumerable<string> FindPluginAssemblies()
        {
            string[] pluginDirectories = FindPluginDirectories();
            return FindAllPluginAssemblies(pluginDirectories);
        }

        private static IEnumerable<Type> FindAllPluginTypes(Assembly pluginAssembly)
        {
            return pluginAssembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetInterface(typeof(IConnectionPlugin).FullName) != null);
        }

        private static string[] FindPluginDirectories()
        {
            try
            {
                string pluginsDirectory = FindBasePluginDirectory();
                return Directory.GetDirectories(pluginsDirectory);
            }
            catch (Exception exception)
            {
                WritePluginDiagnostic("Unable to open plugins directory: " + exception);
                Logging.Info("Unable to open plugins directory.", exception);
                return new string[0];
            }
        }

        internal static string DiagnosticDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPilot", "Logs"); }
        }
        private static void WritePluginDiagnostic(string message)
        {
            try
            {
                string root = DiagnosticDirectory;
                Directory.CreateDirectory(root);
                File.AppendAllText(Path.Combine(root, "plugins.log"), DateTime.UtcNow.ToString("o") + " [UTC] " + message + Environment.NewLine);
            }
            catch { }
        }

        internal static string FindBasePluginDirectory()
        {
            string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string pluginsDirectory = Path.Combine(applicationDirectory, "Plugins");
            return pluginsDirectory;
        }

        private static IEnumerable<string> FindAllPluginAssemblies(string[] pluginDirectories)
        {
            return pluginDirectories.SelectMany(FindPluginAssemblies);
        }

        private static string[] FindPluginAssemblies(string pluginDirectory)
        {
            try
            {
                return Directory.GetFiles(pluginDirectory, "Terminals.Plugins.*.dll");
            }
            catch (Exception exception)
            {
                string message = string.Format("Unable to load plugins list from '{0}'.", pluginDirectory);
                Logging.Info(message, exception);
                return new string[0];
            }
        }
    }
}