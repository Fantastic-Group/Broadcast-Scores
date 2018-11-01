using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.IO;
using System.Xml;
using System.Net;
using System.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using System.Xml.Linq;

using Miomni.SportsKit;
using Miomni.MiddleKit;
using Miomni.EventLib.Cache;
using Miomni.Gaming.Relay.Responses;
using Miomni.Gaming.Relay.Events;
using EnterGamingRelay.APIModel;
using EnterGamingRelay.EventModules;
using EnterGamingRelay;

namespace BroadcastScores
{
    class GlobalBasketBall
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        ProcessSignalR objProcessSignalR = new ProcessSignalR();

        static string SqlUrl { get; set; }
        string GlobalBasketBallGamesScheduleAPI { get; set; }
        string GlobalBasketBallScoreAPI { get; set; }
        static List<GlobalBasketBallGame> todaysGames = new List<GlobalBasketBallGame>();


        public GlobalBasketBall(string strGlobalBasketBallScoreAPI)
        {
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];

            GlobalBasketBallScoreAPI = strGlobalBasketBallScoreAPI;
            GlobalBasketBallGamesScheduleAPI = ConfigurationManager.AppSettings["GlobalBasketBallGamesScheduleAPI"];


            if (String.IsNullOrWhiteSpace(strGlobalBasketBallScoreAPI))
                throw new ArgumentException("GlobalBasketBall needs Score API URL", nameof(strGlobalBasketBallScoreAPI));

            if (String.IsNullOrWhiteSpace(SqlUrl))
                throw new ArgumentException("GlobalBasketBall needs SqlUrl set to the base URL for the EG SQL service", nameof(SqlUrl));

            if (String.IsNullOrWhiteSpace(GlobalBasketBallGamesScheduleAPI))
                throw new ArgumentException("GlobalBasketBall needs GameSchedule API URL", nameof(GlobalBasketBallGamesScheduleAPI));


        }


        public async Task BuildGlobalBasketBallScores()
        {
            while (true)
            {
                try
                {
                    GetTodaysGames();

                    if (todaysGames.Count > 0)
                    {
                        //FetchAndSendScores();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} thrown when fetching and creating GlobalBasketBall Score object: {ex.Message}");
                }
                System.Threading.Thread.Sleep(10000);
            }
        }

        public void GetTodaysGames()
        {
            try
            {
                todaysGames.Clear();
                XmlDocument doc = new XmlDocument();
                string gameScheduleAPI = GlobalBasketBallGamesScheduleAPI;
                gameScheduleAPI = gameScheduleAPI.Replace("{date}", DateTime.UtcNow.ToString("yyyy-MM-dd"));
                doc.Load(gameScheduleAPI);
                XmlNode nodeGames = doc.GetElementsByTagName("schedule").Item(0);

                if (nodeGames != null)
                    foreach (XmlNode xmlGame in nodeGames)
                    {
                        string gameStatus = xmlGame.Attributes["status"].Value;
                        if (gameStatus.ToUpper() == "LIVE")
                        {
                            todaysGames.Add(
                                new GlobalBasketBallGame
                                {
                                    MatchID = xmlGame.Attributes["id"].Value
                                });
                        }
                    }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when getting todays GlobalBasketBall Games : {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when getting todays GlobalBasketBall Games : {ex.Message + ex.StackTrace}");
            }
        }


    }


    class GlobalBasketBallGame
    {
        public string MatchID { get; set; }
    }

}
