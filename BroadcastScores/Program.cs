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


namespace BroadcastScores
{
    class Program
    {

        public static string SqlUrl { get; set; }
        public static string SRGamePushURL { get; set; }
        public static string Scorefilepath { get; set; }
        static Logger logger = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            ProcessGameScores().Wait();
        }

        private static async Task ProcessGameScores()
        {
            try
            {
                Console.WriteLine("Scores feeds processing started...");
                logger.Info("Scores feeds processing started...");
                PushGamesSignalRFeeds pushObj = new PushGamesSignalRFeeds();

                string[] ScorePullUrls;
                ScorePullUrls = PushGamesSignalRFeeds.SRScorePullUrlList.Split(',');

                var tasks = new List<Task>();
                foreach (string pullUrl in ScorePullUrls)
                {
                    tasks.Add(pushObj.GenerateScoresFeeds(pullUrl.Trim()));
                    Console.WriteLine("Score feeds started for : " + pullUrl);
                    logger.Info("Score feeds started for : " + pullUrl);
                }
                await Task.WhenAll(tasks);
            }
            catch(Exception ex)
            {
                logger.Error(ex, $"{ex.GetType().Name} thrown when running ProcessGameScores : {ex.Message}");
            }
        }

    }
}
