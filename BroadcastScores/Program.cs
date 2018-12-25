﻿using System;
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
            try
            {
                ProcessGameScores().Wait();
            }
            catch(Exception ex)
            {
                Console.WriteLine($" Exception in Main : { ex.Message + ex.StackTrace}");
                logger.Error(ex, $"{ex.GetType().Name}  Exception in Main : {ex.Message + ex.StackTrace}");
            }
            Console.WriteLine("Main finished");
            logger.Info("Main finished");
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
                int i = 1;
                foreach (string pullUrl in ScorePullUrls)
                {
                    Console.WriteLine(i + "): Score feeds started for : " + pullUrl);
                    logger.Info(i + "): Score feeds started for : " + pullUrl);
                    tasks.Add(pushObj.GenerateScoresFeeds(pullUrl.Trim()));
                    i++;
                }
                await Task.WhenAll(tasks);
                Console.WriteLine("All tasks finished");
                logger.Info("All tasks finished");
            }
            catch(Exception ex)
            {
                Console.WriteLine($"thrown when running ProcessGameScores: { ex.Message + ex.StackTrace}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when running ProcessGameScores : {ex.Message +  ex.StackTrace}");
            }
            Console.WriteLine("ProcessGameScores finished");
            logger.Info("ProcessGameScores finished");
        }

    }
}
