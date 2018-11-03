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
    class NBA
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        ProcessSignalR objProcessSignalR = new ProcessSignalR();

        static string SqlUrl { get; set; }
        string NBAGamesScheduleAPI { get; set; }
        string NBAScoreAPI { get; set; }
        static List<NBAGame> todaysGames = new List<NBAGame>();


        public NBA(string strNBAScoreAPI)
        {
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];

            NBAScoreAPI = strNBAScoreAPI;
            NBAGamesScheduleAPI = ConfigurationManager.AppSettings["NBAGamesScheduleAPI"];


            if (String.IsNullOrWhiteSpace(strNBAScoreAPI))
                throw new ArgumentException("NBA needs Score API URL", nameof(strNBAScoreAPI));

            if (String.IsNullOrWhiteSpace(SqlUrl))
                throw new ArgumentException("NBA needs SqlUrl set to the base URL for the EG SQL service", nameof(SqlUrl));

            if (String.IsNullOrWhiteSpace(NBAGamesScheduleAPI))
                throw new ArgumentException("NBA needs GameSchedule API URL", nameof(NBAGamesScheduleAPI));


        }

        public async Task BuildNBAScores()
        {
            while (true)
            {
                try
                {
                    GetTodaysGames();

                    if (todaysGames.Count > 0)
                    {
                        FetchAndSendScores();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} thrown when fetching and creating NBA Score object: {ex.Message}");
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
                string gameScheduleAPI = NBAGamesScheduleAPI;
                gameScheduleAPI = gameScheduleAPI.Replace("{year}", DateTime.UtcNow.Year.ToString());
                gameScheduleAPI = gameScheduleAPI.Replace("{month}", DateTime.UtcNow.Month.ToString());
                gameScheduleAPI = gameScheduleAPI.Replace("{day}", DateTime.UtcNow.Day.ToString());
                doc.Load(gameScheduleAPI);
                XmlNode nodeGames = doc.GetElementsByTagName("games").Item(0);

                if (nodeGames != null)
                    foreach (XmlNode xmlGame in nodeGames)
                    {
                        string gameStatus = xmlGame.Attributes["status"].Value;
                        if (gameStatus.ToUpper() != "CLOSED" && gameStatus.ToUpper() != "SCHEDULED")
                        {
                            todaysGames.Add(
                                new NBAGame
                                {
                                    GameID = xmlGame.Attributes["id"].Value,
                                    MatchID = xmlGame.Attributes["sr_id"].Value
                                });
                        }
                    }
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetTodaysGames : " + $"{ex.GetType().Name} thrown when getting todays NBA Games : {ex.Message}");
                logger.Error(ex, "GetTodaysGames : " + $"{ex.GetType().Name} thrown when getting todays NBA Games : {ex.Message + ex.StackTrace}");
            }

        }
        public void FetchAndSendScores()
        {
            XmlDocument doc = new XmlDocument();

            foreach (NBAGame gameDetails in todaysGames)
            {
                String currentGameURL = NBAScoreAPI;
                currentGameURL = currentGameURL.Replace("{gameID}", gameDetails.GameID);
                try
                {
                    string matchID = gameDetails.MatchID.Replace("sr:match:", "");
                    string[] matchIDs = { matchID };
                    var matchEventsTask = new EGSqlQuery(SqlUrl).MatchIDsToEventAsync(matchIDs);

                    // Got those EventIDs yet?
                    if (!matchEventsTask.IsCompleted)
                        matchEventsTask.Wait();

                    if (matchEventsTask.Result != null && matchEventsTask.Result.ContainsKey(Convert.ToInt32(matchID)))
                    {
                        int eventID = matchEventsTask.Result[Convert.ToInt32(matchID)];
                        doc.Load(currentGameURL);
                        EventMessage msg = CreateNBAScoreMessage(doc.InnerXml, Convert.ToString(eventID));

                        if (msg != null)
                        {
                            objProcessSignalR.SendSignalRFeedtohub(msg, "NBA");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("FetchAndSendScores : " +$"{ex.GetType().Name} - NBA Score feed pulling from API : {ex.Message}");
                    logger.Error(ex, "FetchAndSendScores : " + $"{ex.GetType().Name} - NBA Score feed pulling from API : {ex.Message + ex.StackTrace}");
                }
            }

        }

        public EventMessage CreateNBAScoreMessage(string XMLScorefeed, string eventID)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.InnerXml = XMLScorefeed;


                if (!String.IsNullOrEmpty(XMLScorefeed))
                {
                    XmlNode nodeGame = doc.GetElementsByTagName("game").Item(0);

                    string gameStatus = nodeGame.Attributes["quarter"].Value;
                    if (gameStatus.ToUpper() == "SCHEDULED")
                    {
                        return null;
                    }
                    XmlNode homeScoreXml = doc.GetElementsByTagName("team").Item(0).FirstChild;
                    XmlNode awayScoreXml = doc.GetElementsByTagName("team").Item(1).FirstChild;
                    if (homeScoreXml == null || awayScoreXml == null || nodeGame == null)
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
                            gameStatus = "1st Quarter";
                        else if (gameStatus == "2")
                            gameStatus = "2nd Quarter";
                        else if (gameStatus == "3")
                            gameStatus = "3rd Quarter";
                        else if (gameStatus == "4")
                            gameStatus = "4th Quarter";
                        else if (gameStatus == "5")
                            gameStatus = "1st OverTime";
                        else if (gameStatus == "6")
                            gameStatus = "2nd OverTime";
                        else if (gameStatus == "7")
                            gameStatus = "3rd OverTime";
                        else if (gameStatus == "8")
                            gameStatus = "4th OverTime";
                        else if (gameStatus == "9")
                            gameStatus = "5th OverTime";
                        else if (gameStatus == "10")
                            gameStatus = "6th OverTime";


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
                Console.WriteLine($"{ex.GetType().Name} thrown when creating NBA Gamefeed object: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating NBA Gamefeed object: {ex.Message + ex.StackTrace}");
            }
            return null;
        }

    }

    class NBAGame
    {
        //public string Home { get; set; }
        //public string Away { get; set; }
        public string GameID { get; set; }
        public string MatchID { get; set; }
    }
}
