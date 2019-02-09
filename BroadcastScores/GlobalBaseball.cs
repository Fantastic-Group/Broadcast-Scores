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
    class GlobalBaseball
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        ProcessSignalR objProcessSignalR;

        static string SqlUrl { get; set; }
        string GlobalBaseballGamesScheduleAPI { get; set; }
        string GlobalBaseballScoreAPI { get; set; }
        static List<GlobalBaseballGame> liveGames = new List<GlobalBaseballGame>();
        static List<GlobalBaseballGame> oldLiveGamesList = new List<GlobalBaseballGame>();
        string APICallingCycleInterval { get; set; }
        string APICallingCycleIntervalIfGameNotLive { get; set; }


        public GlobalBaseball(string strGlobalBaseballScoreAPI, ProcessSignalR processSignalR)
        {
            objProcessSignalR = processSignalR;
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];

            GlobalBaseballScoreAPI = strGlobalBaseballScoreAPI;
            GlobalBaseballGamesScheduleAPI = ConfigurationManager.AppSettings["GlobalBaseballGamesScheduleAPI"];

            APICallingCycleInterval = ConfigurationManager.AppSettings["APICallingCycleInterval"];
            APICallingCycleIntervalIfGameNotLive = ConfigurationManager.AppSettings["APICallingCycleIntervalIfGameNotLive"];


            if (String.IsNullOrWhiteSpace(strGlobalBaseballScoreAPI))
                throw new ArgumentException("GlobalBaseball needs Score API URL", nameof(strGlobalBaseballScoreAPI));

            if (String.IsNullOrWhiteSpace(GlobalBaseballGamesScheduleAPI))
                throw new ArgumentException("GlobalBaseball needs GameSchedule API URL", nameof(GlobalBaseballGamesScheduleAPI));

        }

        public async Task BuildGlobalBaseballScores()
        {
            await Task.Factory.StartNew(() => System.Threading.Thread.Sleep(2000));
            while (true)
            {
                objProcessSignalR.LogHelpDebug("New Iteration BuildGlobalBaseballScores");
                try
                {
                    
                    GetLiveGames();

                    if (liveGames.Count > 0)
                    {
                        await FetchAndSendScores();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} thrown when fetching and creating GlobalBaseball Score object: {ex.Message}");
                }

                if (liveGames.Count > 0) //if any game is live Api calling cycle interval will be less otherwise more to avoid frequent polling
                {
                    System.Threading.Thread.Sleep(Convert.ToInt32(APICallingCycleInterval));
                }
                else
                {
                    System.Threading.Thread.Sleep(Convert.ToInt32(APICallingCycleIntervalIfGameNotLive));
                }
            }
        }

        // Get live games
        public void GetLiveGames()
        {
            try
            {
                liveGames.Clear();
                XmlDocument doc = new XmlDocument();
                string gameScheduleAPI = GlobalBaseballGamesScheduleAPI;
                gameScheduleAPI = gameScheduleAPI.Replace("{date}", DateTime.UtcNow.ToString("yyyy-MM-dd"));
                doc.Load(gameScheduleAPI);
                XmlNode nodeGames = doc.GetElementsByTagName("schedule").Item(0);

                if (nodeGames != null)
                    foreach (XmlNode xmlGame in nodeGames)
                    {
                        string gameStatus = xmlGame.Attributes["status"].Value;
                        if (gameStatus.ToUpper() == "LIVE")
                        {
                            liveGames.Add(
                                new GlobalBaseballGame
                                {
                                    MatchID = xmlGame.Attributes["id"].Value
                                });
                        }
                    }

                gameScheduleAPI = GlobalBaseballGamesScheduleAPI;
                gameScheduleAPI = gameScheduleAPI.Replace("{date}", DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"));
                doc.Load(gameScheduleAPI);
                nodeGames = doc.GetElementsByTagName("schedule").Item(0);

                if (nodeGames != null)
                    foreach (XmlNode xmlGame in nodeGames)
                    {
                        string gameStatus = xmlGame.Attributes["status"].Value;
                        if (gameStatus.ToUpper() == "LIVE")
                        {
                            liveGames.Add(
                                new GlobalBaseballGame
                                {
                                    MatchID = xmlGame.Attributes["id"].Value
                                });
                        }
                    }

                //////////////////////////////////////////////////////////////////
                List<GlobalBaseballGame> tempGameslist = new List<GlobalBaseballGame>();
                if (oldLiveGamesList.Count > 0)
                {
                    tempGameslist = oldLiveGamesList;
                }
                else
                {
                    tempGameslist = liveGames;
                }
                oldLiveGamesList = liveGames;
                if (tempGameslist.Count > 0)
                {
                    foreach (GlobalBaseballGame game in tempGameslist)
                    {
                        if (!liveGames.Contains(game))
                        {
                            // for adding previous live games those are Finshed now, for fetching the final scores of those games
                            // otherwise once game status is not live(finished), that game was not coming in live game list
                            // and game status was not changing to Finished and its score were not updating finally
                            liveGames.Add(game);
                        }
                    }
                }
                //////////////////////////////////////////////////////////////////

            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when getting todays GlobalBaseball Games : {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when getting todays GlobalBaseball Games : {ex.Message + ex.StackTrace}");
            }
        }

        public async Task FetchAndSendScores()
        {
            foreach (GlobalBaseballGame gameDetails in liveGames)
            {
                String currentGameURL = GlobalBaseballScoreAPI;
                currentGameURL = currentGameURL.Replace("{matchID}", gameDetails.MatchID);
                try
                {
                    XmlDocument doc = new XmlDocument();
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
                        EventMessage msg = CreateGlobalBaseballScoreMessage(doc.InnerXml, Convert.ToString(eventID));
                        if (msg != null)
                        {
                            objProcessSignalR.SendSignalRFeedtohub(msg, "Global Baseball");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} - GlobalBaseball Score feed pulling from API : {ex.Message}");
                    logger.Error(ex, $"{ex.GetType().Name} - GlobalBaseball Score feed pulling from API : {ex.Message + ex.StackTrace}");
                }
            }

        }

        public EventMessage CreateGlobalBaseballScoreMessage(string XMLScorefeed, string eventID)
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
                            if(x.Attributes["home_score"] != null && x.Attributes["away_score"] != null && x.Attributes["number"] != null)
                            periodList.Add(new Period
                            {
                                Name = x.Attributes["number"].Value,
                                Home = Convert.ToInt32(x.Attributes["home_score"].Value),
                                Visitor = Convert.ToInt32(x.Attributes["away_score"].Value),
                            });
                        }

                    int home_score = 0;
                    int away_score = 0;

                    if (periodList != null && nodeSportEventStatus.Attributes["home_score"] != null && nodeSportEventStatus.Attributes["away_score"] != null)
                    {
                        home_score = Convert.ToInt32(nodeSportEventStatus.Attributes["home_score"].Value);
                        away_score = Convert.ToInt32(nodeSportEventStatus.Attributes["away_score"].Value);
                    }

                    string gameStatus = nodeSportEventStatus.Attributes["match_status"].Value;
                    gameStatus = PushGamesSignalRFeeds.ToSRScoreStatus.ContainsKey(gameStatus)
                                            ? PushGamesSignalRFeeds.ToSRScoreStatus[gameStatus]
                                            : PushGamesSignalRFeeds.CapitalizeFirstLetter(gameStatus.Replace("_", " "));


                    if (gameStatus.ToUpper() != "ENDED")
                    {
                        periodList.Add(new Period
                        {
                            Name = Convert.ToString(periodList.Count + 1),
                            Home = home_score - periodList.Sum(x => x.Home),
                            Visitor = away_score - periodList.Sum(x => x.Visitor),
                        });
                    }

                    string ordinalPeriod = Convert.ToString(periodList.Count());

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

    class GlobalBaseballGame
    {
        public string MatchID { get; set; }
    }

}
