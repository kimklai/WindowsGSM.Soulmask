using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;
using WindowsGSM.GameServer.Query;
using System.Collections.Generic;

namespace WindowsGSM.Plugins
{
    public class Soulmask : SteamCMDAgent
    {
        // - Plugin Details
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.Soulmask", // WindowsGSM.XXXX
            author = "kimklai",
            description = "WindowsGSM plugin for supporting Soulmask Dedicated Server",
            version = "1.3.1",
            url = "https://github.com/kimklai/WindowsGSM.Soulmask", // Github repository link (Best practice)
            color = "#800080" // Color Hex
        };

        // - Standard Constructor and properties
        public Soulmask(ServerConfig serverData) : base(serverData) => base.serverData = _serverData = serverData;
        private readonly ServerConfig _serverData;
        public string Error, Notice;

        // - Settings properties for SteamCMD installer
        public override bool loginAnonymous => true;
        public override string AppId => "3017310"; /* taken via https://steamdb.info/app/3017310/info/ */

        // - Game server Fixed variables
        public override string StartPath => @"WS\Binaries\Win64\WSServer-Win64-Shipping.exe"; // Game server start path
        public string FullName = "Soulmask Dedicated Server"; // Game server FullName
        public bool AllowsEmbedConsole = true;  // Does this server support output redirect?
        public int PortIncrements = 1; // This tells WindowsGSM how many ports should skip after installation
        public object QueryMethod = new A2S(); // Query method should be use on current server type. Accepted value: null or new A2S() or new FIVEM() or new UT3()

        // - Game server default values
        public string ServerName = "Soulmask";
        public string Defaultmap = "Level01_Main"; // Original (MapName)
        public string Maxplayers = "10"; // WGSM reads this as string but originally it is number or int (MaxPlayers)
        public string Port = "7777"; // WGSM reads this as string but originally it is number or int
        public string QueryPort = "27015"; // WGSM reads this as string but originally it is number or int (SteamQueryPort)
        public string Additional = "-UTF8Output -forcepassthrough -server %* -log -EchoPort=18888";


        private Dictionary<string, string> configData = new Dictionary<string, string>();


        // - Create a default cfg for the game server after installation
        public async void CreateServerCFG()
        {

        }

        // - Start server function, return its Process to WindowsGSM
        public async Task<Process> Start()
        {
            string shipExePath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (!File.Exists(shipExePath))
            {
                Error = $"{Path.GetFileName(shipExePath)} not found ({shipExePath})";
                return null;
            }

            string param = $"{_serverData.ServerMap}";


            param += $" -MULTIHOME={_serverData.ServerIP} ";
            param += $" -SteamServerName=\"\"\"{_serverData.ServerName}\"\"\" ";
            param += $" -MaxPlayers={_serverData.ServerMaxPlayer} ";
            param += $" -Port={_serverData.ServerPort} ";
            param += $" -QueryPort={_serverData.ServerQueryPort} ";
            param += $" {_serverData.ServerParam}";

            // Prepare Process
            var p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = ServerPath.GetServersServerFiles(_serverData.ServerID),
                    FileName = shipExePath,
                    Arguments = param.ToString(),
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false

                },
                EnableRaisingEvents = true
            };

            // Set up Redirect Input and Output to WindowsGSM Console if EmbedConsole is on
            if (AllowsEmbedConsole)
            {
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                var serverConsole = new ServerConsole(_serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
            }

            // Start Process
            try
            {
                p.Start();
                if (AllowsEmbedConsole)
                {
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }

                return p;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null; // return null if fail to start
            }
        }

        // - Stop server function
        public async Task Stop(Process p)
        {
            int shutDownTimer = 60; // seconds
            int port = 18888;

            await Task.Run(() =>
            {
                try
                {
                    // dynamically fetch
                    // - echo port
                    // - count down timer
                    // from server parameter
                    string pattern1 = @"-EchoPort=(\d+)";
                    string pattern2 = @"-CountDown=(\d+)";
                    Match match = Regex.Match(p.StartInfo.Arguments, pattern1);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int echoPort))
                    {
                        port = echoPort;
                    }
                    match = Regex.Match(p.StartInfo.Arguments, pattern2);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int timer))
                    {
                        shutDownTimer = timer;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("error while parsing arguments: {0}", e.Message);
                }
                SendQuitCMD(shutDownTimer, port);
            });
            await Task.Delay(shutDownTimer * 1000 + 5000);
        }

        private void SendQuitCMD(int timeToWait, int port)
        {
            string server = "127.0.0.1";
            string command = "close " + timeToWait;

            Console.WriteLine("== [Stop Server]: try to connect local port ==");

            try
            {
                using (TcpClient client = new TcpClient(server, port))
                using (NetworkStream stream = client.GetStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.ASCII))
                using (StreamWriter writer = new StreamWriter(stream, Encoding.ASCII) { AutoFlush = true })
                {
                    // send command to quit
                    Task.Delay(500); // wait a little bit for connection response
                    writer.WriteLine(command);
                    // Console.WriteLine("send: {0}", command);
                    // Console.WriteLine("{0}", reader.ReadLine());
                    // Console.WriteLine("{0}", reader.ReadLine());
                    // Console.WriteLine("{0}", reader.ReadLine());

                    stream.Close();
                    client.Close();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("error while stopping server: {0}", e.Message);
            }
        }

        // - Update server function
        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            var (p, error) = await Installer.SteamCMD.UpdateEx(serverData.ServerID, AppId, validate, custom: custom, loginAnonymous: loginAnonymous);
            Error = error;
            await Task.Run(() => { p.WaitForExit(); });
            return p;
        }

        public bool IsInstallValid()
        {
            return File.Exists(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));
        }

        public bool IsImportValid(string path)
        {
            string exePath = Path.Combine(path, "PackageInfo.bin");
            Error = $"Invalid Path! Fail to find {Path.GetFileName(exePath)}";
            return File.Exists(exePath);
        }

        public string GetLocalBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return steamCMD.GetLocalBuild(_serverData.ServerID, AppId);
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCMD = new Installer.SteamCMD();
            return await steamCMD.GetRemoteBuild(AppId);
        }
    }
}
