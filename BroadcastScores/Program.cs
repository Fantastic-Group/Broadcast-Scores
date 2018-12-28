using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.IO;
using System.Xml;
using System.Net;
using NLog;

using Miomni.SportsKit;
using Miomni.EventLib.Cache;
using Miomni.Gaming.Relay.Responses;
using Miomni.Gaming.Relay.Events;
using EnterGamingRelay.APIModel;
using EnterGamingRelay.EventModules;
using EnterGamingRelay;

using System.ServiceProcess;
using System.Configuration.Install;
using System.Reflection;


namespace BroadcastScores
{
    class Program : ServiceBase
    {

        public static string SqlUrl { get; set; }
        public static string SRGamePushURL { get; set; }
        public static string Scorefilepath { get; set; }
        static Logger logger = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            try
            {
                Program service = new Program();

                if (Environment.UserInteractive)
                {
                    string parameter = string.Concat(args);
                    switch (parameter)
                    {
                        // Install/Uninstall myself as a Windows Service
                        // Make sure you run EventService.exe as Administrator from the command line
                        case "--install":
                            ManagedInstallerClass.InstallHelper(new[] { Assembly.GetExecutingAssembly().Location });
                            break;
                        case "--uninstall":
                            ManagedInstallerClass.InstallHelper(new[] { "/u", Assembly.GetExecutingAssembly().Location });
                            break;
                        default:
                            service.OnStart(args);

                            Console.WriteLine("Press any key to stop.");
                            Console.Read();

                            service.OnStop();
                            break;
                    }
                }
                else
                    ServiceBase.Run(service);
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Exception in Main : { ex.Message + ex.StackTrace}");
                logger.Error(ex, $"{ex.GetType().Name}  Exception in Main : {ex.Message + ex.StackTrace}");
            }
        }

        private void ProcessGameScores()
        {
            try
            {
                Console.WriteLine("Scores feeds processing started...");
                logger.Info("Scores feeds processing started...");
                PushGamesSignalRFeeds pushObj = new PushGamesSignalRFeeds();

                string[] ScorePullUrls;
                ScorePullUrls = PushGamesSignalRFeeds.SRScorePullUrlList.Split(',');

                var tasks = new List<Task>();
                int i = 1;
                foreach (string pullUrl in ScorePullUrls)
                {
                    Console.WriteLine(i + "): Score feeds started for : " + pullUrl);
                    logger.Info(i + "): Score feeds started for : " + pullUrl);
                    tasks.Add(pushObj.GenerateScoresFeeds(pullUrl.Trim()));
                    i++;
                }
                Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"thrown when running ProcessGameScores: { ex.Message + ex.StackTrace}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when running ProcessGameScores : {ex.Message + ex.StackTrace}");
            }
        }

        public Program()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            Console.WriteLine("Service starting");
            logger.Info("Service starting");
            try
            {
                ProcessGameScores();
            }
            catch (Exception ex)
            {
                logger.Fatal($"{ex.GetType().Name} thrown when loading profile: {ex.Message}");
                throw;
            }
        }

        protected override void OnStop()
        {
            Console.WriteLine("Service stopped");
            logger.Info("Service stopped");
        }

        private void InitializeComponent()
        {
            // 
            // EventServiceHost
            // 
            this.ServiceName = "BroadcastScoresService";

        }




    }
}
