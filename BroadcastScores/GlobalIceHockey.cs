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
    class GlobalIceHockey
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        ProcessSignalR objProcessSignalR = new ProcessSignalR();

        static string SqlUrl { get; set; }
        string GlobalIceHockeyGamesScheduleAPI { get; set; }
        string GlobalIceHockeyScoreAPI { get; set; }
        static List<GlobalIceHockeyGame> todaysGames = new List<GlobalIceHockeyGame>();


        public GlobalIceHockey(string strGlobalIceHockeyScoreAPI)
        {
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];

            GlobalIceHockeyScoreAPI = strGlobalIceHockeyScoreAPI;
            GlobalIceHockeyGamesScheduleAPI = ConfigurationManager.AppSettings["GlobalIceHockeyGamesScheduleAPI"];


            if (String.IsNullOrWhiteSpace(strGlobalIceHockeyScoreAPI))
                throw new ArgumentException("GlobalIceHockey needs Score API URL", nameof(strGlobalIceHockeyScoreAPI));

            if (String.IsNullOrWhiteSpace(SqlUrl))
                throw new ArgumentException("GlobalIceHockey needs SqlUrl set to the base URL for the EG SQL service", nameof(SqlUrl));

            if (String.IsNullOrWhiteSpace(GlobalIceHockeyGamesScheduleAPI))
                throw new ArgumentException("GlobalIceHockey needs GameSchedule API URL", nameof(GlobalIceHockeyGamesScheduleAPI));


        }

        public async Task BuildGlobalIceHockeyScores()
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
                    Console.WriteLine($"{ex.GetType().Name} thrown when fetching and creating GlobalIceHockey Score object: {ex.Message}");
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
                string gameScheduleAPI = GlobalIceHockeyGamesScheduleAPI;
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
                                new GlobalIceHockeyGame
                                {
                                    MatchID = xmlGame.Attributes["id"].Value
                                });
                        }
                    }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when getting todays GlobalIceHockey Games : {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when getting todays GlobalIceHockey Games : {ex.Message + ex.StackTrace}");
            }
        }

        public void FetchAndSendScores()
        {
            XmlDocument doc = new XmlDocument();

            foreach (GlobalIceHockeyGame gameDetails in todaysGames)
            {
                String currentGameURL = GlobalIceHockeyScoreAPI;
                currentGameURL = currentGameURL.Replace("{matchID}", gameDetails.MatchID);
                try
                {
                    string matchID = gameDetails.MatchID;
                    matchID = matchID.Replace("sr:match:", "");
                    string[] matchIDs = { matchID };
                    var matchEventsTask = new EGSqlQuery(SqlUrl).MatchIDsToEventAsync(matchIDs);

                    // Got those EventIDs yet?
                    if (!matchEventsTask.IsCompleted)
                        matchEventsTask.Wait();

                    if (matchEventsTask.Result != null && matchEventsTask.Result.ContainsKey(Convert.ToInt32(matchID)))
                    {
                        int eventID = matchEventsTask.Result[Convert.ToInt32(matchID)];
                        doc.Load(currentGameURL);
                        EventMessage msg = CreateGlobalIceHockeyScoreMessage(doc.InnerXml, Convert.ToString(eventID));
                        if (msg != null)
                        {
                            objProcessSignalR.SendSignalRFeedtohub(msg, "Global Hockey");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} - GlobalIceHockey Score feed pulling from API : {ex.Message}");
                    logger.Error(ex, $"{ex.GetType().Name} - GlobalIceHockey Score feed pulling from API : {ex.Message + ex.StackTrace}");
                }
            }

        }

        public EventMessage CreateGlobalIceHockeyScoreMessage(string XMLScorefeed, string eventID)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.InnerXml = XMLScorefeed;


                if (!String.IsNullOrEmpty(XMLScorefeed))
                {
                    XmlNode nodesport_event = doc.GetElementsByTagName("sport_event").Item(0);
                    XmlNode nodeSportEventStatus = doc.GetElementsByTagName("sport_event_status").Item(0);
                    XmlNode nodeperiodScores = doc.GetElementsByTagName("period_scores").Item(0);

                    List<Period> periodList = new List<Period>();
                    if(nodeperiodScores != null)
                    if (nodeperiodScores.HasChildNodes)
                        foreach (XmlNode x in nodeperiodScores.ChildNodes)
                        {
                            periodList.Add(new Period
                            {
                                Name = x.Attributes["number"].Value,
                                Home = Convert.ToInt32(x.Attributes["home_score"].Value),
                                Visitor = Convert.ToInt32(x.Attributes["away_score"].Value),
                            });
                        }

                    int home_score = 0;
                    int away_score = 0;

                    if (periodList != null)
                    {
                        home_score = Convert.ToInt32(nodeSportEventStatus.Attributes["home_score"].Value);
                        away_score = Convert.ToInt32(nodeSportEventStatus.Attributes["away_score"].Value);
                    }

                    string gameStatus = nodeSportEventStatus.Attributes["match_status"].Value;
                    if(gameStatus.ToUpper() == "ENDED")
                    {
                        return null;
                    }

                        periodList.Add(new Period
                        {
                            Name = Convert.ToString(periodList.Count + 1),
                            Home = home_score - periodList.Sum(x=>x.Home),
                            Visitor = away_score - periodList.Sum(x => x.Visitor),
                        });

                    string ordinalPeriod = Convert.ToString(periodList.Count());
                     if (ordinalPeriod == "1")
                         gameStatus = "1st Period";
                     else if (ordinalPeriod == "2")
                         gameStatus = "2nd Period";
                     else if (ordinalPeriod == "3")
                         gameStatus = "3rd Period";
                     else if (ordinalPeriod == "4")
                         gameStatus = "4th Period";
                     else if (ordinalPeriod == "5")
                         gameStatus = "5th Period";
                     else if (ordinalPeriod == "6")
                         gameStatus = "6th Period";
                     else if (ordinalPeriod == "7")
                         gameStatus = "7th Period";
                     else if (ordinalPeriod == "8")
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
                Console.WriteLine($"{ex.GetType().Name} thrown when creating Global Ice Hockey Gamefeed object: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating Global Ice Hockey Gamefeed object: {ex.Message + ex.StackTrace}");
            }
            return null;
        }

    }

    class GlobalIceHockeyGame
    {
        public string MatchID { get; set; }
    }

}
