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
    class NHL
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        ProcessSignalR objProcessSignalR = new ProcessSignalR();

        static string SqlUrl { get; set; }
        string NHLGamesScheduleAPI { get; set; }
        string NHLScoreAPI { get; set; }
        static List<NHLGame> todaysGames = new List<NHLGame>();
        string APICallingCycleInterval { get; set; }


        public NHL(string strNHLScoreAPI)
        {
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];

            NHLScoreAPI = strNHLScoreAPI;
            NHLGamesScheduleAPI = ConfigurationManager.AppSettings["NHLGamesScheduleAPI"];

            APICallingCycleInterval = ConfigurationManager.AppSettings["APICallingCycleInterval"];
            if (String.IsNullOrEmpty(APICallingCycleInterval))
                APICallingCycleInterval = "15000";

            if (String.IsNullOrWhiteSpace(strNHLScoreAPI))
                throw new ArgumentException("NHL needs Score API URL", nameof(strNHLScoreAPI));

            if (String.IsNullOrWhiteSpace(SqlUrl))
                throw new ArgumentException("NHL needs SqlUrl set to the base URL for the EG SQL service", nameof(SqlUrl));

            if (String.IsNullOrWhiteSpace(NHLGamesScheduleAPI))
                throw new ArgumentException("NHL needs GameSchedule API URL", nameof(NHLGamesScheduleAPI));


        }

        public async Task BuildNHLScores()
        {
            while (true)
            {
                try
                {
                    await Task.Factory.StartNew(() => System.Threading.Thread.Sleep(100));
                    await GetTodaysGames();

                    if (todaysGames.Count > 0)
                    {
                       await FetchAndSendScores();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} thrown when fetching and creating NHL Score object: {ex.Message}");
                }
                System.Threading.Thread.Sleep(Convert.ToInt32(APICallingCycleInterval));
            }
        }

        public async Task GetTodaysGames()
        {
            try
            {
            todaysGames.Clear();
            XmlDocument doc = new XmlDocument();
            string gameScheduleAPI = NHLGamesScheduleAPI;
            gameScheduleAPI = gameScheduleAPI.Replace("{year}", DateTime.UtcNow.Year.ToString());
            gameScheduleAPI = gameScheduleAPI.Replace("{month}", DateTime.UtcNow.Month.ToString());
            gameScheduleAPI = gameScheduleAPI.Replace("{day}", DateTime.UtcNow.Day.ToString());
            doc.Load(gameScheduleAPI);
            XmlNode nodeGames = doc.GetElementsByTagName("games").Item(0);

            if(nodeGames != null)
            foreach (XmlNode xmlGame in nodeGames)
            {
                string gameStatus = xmlGame.Attributes["status"].Value;
                if (gameStatus.ToUpper() == "INPROGRESS")
                {
                    todaysGames.Add(
                        new NHLGame
                        {
                            GameID = xmlGame.Attributes["id"].Value,
                            MatchID = xmlGame.Attributes["sr_id"].Value
                        });
                }
            }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when getting todays NHL Games : {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when getting todays NHL Games : {ex.Message +  ex.StackTrace}");
            }
        }

        public async Task FetchAndSendScores()
        {
            XmlDocument doc = new XmlDocument();

            foreach (NHLGame gameDetails in todaysGames)
            {
                String currentGameURL = NHLScoreAPI;
                currentGameURL = currentGameURL.Replace("{gameID}", gameDetails.GameID);
                try
                {
                    string matchID = gameDetails.MatchID;
                    matchID = matchID.Replace("sr:match:", "");
                    string[] matchIDs = { matchID };
                    var matchEventsTask = new EGSqlQuery(SqlUrl).MatchIDsToEventAsync(matchIDs);

                    // Got those EventIDs yet?
                    if (!matchEventsTask.IsCompleted)
                        await matchEventsTask;

                    if (matchEventsTask.Result != null && matchEventsTask.Result.ContainsKey(Convert.ToInt32(matchID)))
                    {
                        int eventID = matchEventsTask.Result[Convert.ToInt32(matchID)];
                        doc.Load(currentGameURL);
                        EventMessage msg = CreateNHLScoreMessage(doc.InnerXml, eventID.ToString());
                        if (msg != null)
                        {
                            objProcessSignalR.SendSignalRFeedtohub(msg, "NHL");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} - NHL Score feed pulling from API : {ex.Message}");
                    logger.Error(ex, $"{ex.GetType().Name} - NHL Score feed pulling from API : {ex.Message +  ex.StackTrace}");
                }
            }

        }

        public EventMessage CreateNHLScoreMessage(string XMLScorefeed, string eventID)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.InnerXml = XMLScorefeed;


                if (!String.IsNullOrEmpty(XMLScorefeed))
                {
                    XmlNode nodeGame = doc.GetElementsByTagName("game").Item(0);
                    string gameStatus = nodeGame.Attributes["period"].Value;
                    if (gameStatus.ToUpper() == "SCHEDULED")
                    {
                        return null;
                    }
                    XmlNode homeScoreXml = doc.GetElementsByTagName("team").Item(0).FirstChild;
                    XmlNode awayScoreXml = doc.GetElementsByTagName("team").Item(1).FirstChild;
                    if(homeScoreXml == null || awayScoreXml == null || nodeGame == null)
                    {
                        return null;
                    }

                        List<Period> periodList = new List<Period>();
                        if (homeScoreXml.HasChildNodes && awayScoreXml.HasChildNodes)
                            for (int i = 0; i < homeScoreXml.ChildNodes.Count; i++)
                            {
                                periodList.Add(new Period
                                {
                                    Name = Convert.ToString(i + 1),
                                    Home = Convert.ToInt32(homeScoreXml.ChildNodes[i].Attributes["points"].Value),
                                    Visitor = Convert.ToInt32(awayScoreXml.ChildNodes[i].Attributes["points"].Value),
                                });
                            }

                        int home_score = 0;
                        int away_score = 0;

                        if (periodList != null)
                        {
                            home_score = Convert.ToInt32(doc.GetElementsByTagName("team").Item(0).Attributes["points"].Value);
                            away_score = Convert.ToInt32(doc.GetElementsByTagName("team").Item(1).Attributes["points"].Value);
                        }

                        string ordinalPeriod = gameStatus;
                        if (gameStatus == "1")
                            gameStatus = "1st Period";
                        else if (gameStatus == "2")
                            gameStatus = "2nd Period";
                        else if (gameStatus == "3")
                            gameStatus = "3rd Period";
                        else if (gameStatus == "4")
                            gameStatus = "4th Period";
                        else if (gameStatus == "5")
                            gameStatus = "5th Period";
                        else if (gameStatus == "6")
                            gameStatus = "6th Period";
                        else if (gameStatus == "7")
                            gameStatus = "7th Period";
                        else if (gameStatus == "8")
                            gameStatus = "8th Period";


                        var scoreMsg = new EventMessage
                        {
                            Parent = null,
                            Collected = DateTime.UtcNow,
                            Dirty = true,
                            Watch = System.Diagnostics.Stopwatch.StartNew(),
                            Value = new EventStatusResponse
                            {
                                MiomniEventID = $"E-{eventID}",
                                Status = ResponseStatus.OpSuccess,
                                Score = new Score
                                {
                                    CurrentPeriod = gameStatus,
                                    OrdinalPeriod = Convert.ToInt32(ordinalPeriod),
                                    Time = null,
                                    Home = home_score,
                                    Visitor = away_score,
                                    Periods = periodList,
                                }
                            }
                        };
                        return scoreMsg;
                    }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when creating NHL Gamefeed object: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating NHL Gamefeed object: {ex.Message +  ex.StackTrace}");
            }
            return null;
        }

    }

    class NHLGame
    {
        public string GameID { get; set; }
        public string MatchID { get; set; }
    }

}
