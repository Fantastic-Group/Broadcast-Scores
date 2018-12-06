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
    class MLB
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        ProcessSignalR objProcessSignalR;

        static string SqlUrl { get; set; }
        string MLBGamesScheduleAPI { get; set; }
        string MLBScoreAPI { get; set; }
        static List<MLBGame> liveGames = new List<MLBGame>();
        static List<MLBGame> oldLiveGamesList = new List<MLBGame>();
        string APICallingCycleInterval { get; set; }
        string APICallingCycleIntervalIfGameNotLive { get; set; }


        public MLB(string strMLBScoreAPI, ProcessSignalR processSignalR)
        {
            objProcessSignalR = processSignalR;
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];

            MLBScoreAPI = strMLBScoreAPI;
            MLBGamesScheduleAPI = ConfigurationManager.AppSettings["MLBGamesScheduleAPI"];

            APICallingCycleInterval = ConfigurationManager.AppSettings["APICallingCycleInterval"];
            APICallingCycleIntervalIfGameNotLive = ConfigurationManager.AppSettings["APICallingCycleIntervalIfGameNotLive"];

            if (String.IsNullOrWhiteSpace(strMLBScoreAPI))
                throw new ArgumentException("MLB needs Score API URL", nameof(strMLBScoreAPI));

            if (String.IsNullOrWhiteSpace(MLBGamesScheduleAPI))
                throw new ArgumentException("MLB needs GameSchedule API URL", nameof(MLBGamesScheduleAPI));

        }

        public async Task BuildMLBScores()
        {
            while (true)
            {
                try
                {
                    await Task.Factory.StartNew(() => System.Threading.Thread.Sleep(2000));
                    await GetLiveGames();

                    if (liveGames.Count > 0)
                    {
                        await FetchAndSendScores();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} thrown when fetching and creating MLB Score object: {ex.Message}");
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
        public async Task GetLiveGames()
        {
            try
            {
                liveGames.Clear();
                XmlDocument doc = new XmlDocument();
                string gameScheduleAPI = MLBGamesScheduleAPI;
                gameScheduleAPI = gameScheduleAPI.Replace("{year}", DateTime.UtcNow.Year.ToString());
                gameScheduleAPI = gameScheduleAPI.Replace("{month}", DateTime.UtcNow.Month.ToString());
                gameScheduleAPI = gameScheduleAPI.Replace("{day}", DateTime.UtcNow.Day.ToString());
                doc.Load(gameScheduleAPI);
                XmlNode nodeGames = doc.GetElementsByTagName("games").Item(0);

                if (nodeGames != null)
                    foreach (XmlNode xmlGame in nodeGames)
                    {
                        string gameStatus = xmlGame.Attributes["status"].Value;
                        if (gameStatus.ToUpper() == "INPROGRESS")
                        {
                            liveGames.Add(
                                new MLBGame
                                {
                                    GameID = xmlGame.Attributes["id"].Value,
                                    MatchID = xmlGame.Attributes["sr_id"].Value
                                });
                        }
                    }

                // If game is started yesterday but still live today so fetching yesterdays games also
                gameScheduleAPI = MLBGamesScheduleAPI;
                gameScheduleAPI = gameScheduleAPI.Replace("{year}", DateTime.UtcNow.AddDays(-1).Year.ToString());
                gameScheduleAPI = gameScheduleAPI.Replace("{month}", DateTime.UtcNow.AddDays(-1).Month.ToString());
                gameScheduleAPI = gameScheduleAPI.Replace("{day}", DateTime.UtcNow.AddDays(-1).Day.ToString());
                doc.Load(gameScheduleAPI);
                nodeGames = doc.GetElementsByTagName("games").Item(0);

                if (nodeGames != null)
                    foreach (XmlNode xmlGame in nodeGames)
                    {
                        string gameStatus = xmlGame.Attributes["status"].Value;
                        if (gameStatus.ToUpper() == "INPROGRESS")
                        {
                            liveGames.Add(
                                new MLBGame
                                {
                                    GameID = xmlGame.Attributes["id"].Value,
                                    MatchID = xmlGame.Attributes["sr_id"].Value
                                });
                        }
                    }

                //////////////////////////////////////////////////////////////////
                List<MLBGame> tempGameslist = new List<MLBGame>();
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
                    foreach (MLBGame game in tempGameslist)
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
                Console.WriteLine($"{ex.GetType().Name} thrown when getting todays MLB Games : {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when getting todays MLB Games : {ex.Message + ex.StackTrace}");
            }
        }

        public async Task FetchAndSendScores()
        {
            XmlDocument doc = new XmlDocument();

            foreach (MLBGame gameDetails in liveGames)
            {
                String currentGameURL = MLBScoreAPI;
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
                        EventMessage msg = CreateMLBScoreMessage(doc.InnerXml, eventID.ToString());
                        if (msg != null)
                        {
                            objProcessSignalR.SendSignalRFeedtohub(msg, "MLB");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} - MLB Score feed pulling from API : {ex.Message}");
                    logger.Error(ex, $"{ex.GetType().Name} - MLB Score feed pulling from API : {ex.Message + ex.StackTrace}");
                }
            }

        }

        public EventMessage CreateMLBScoreMessage(string XMLScorefeed, string eventID)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.InnerXml = XMLScorefeed;


                if (!String.IsNullOrEmpty(XMLScorefeed))
                {
                    XmlNode nodeGame = doc.GetElementsByTagName("game").Item(0);

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

                    string gameStatus = nodeGame.Attributes["status"].Value;
                    gameStatus = PushGamesSignalRFeeds.ToSRScoreStatus.ContainsKey(gameStatus)
                                            ? PushGamesSignalRFeeds.ToSRScoreStatus[gameStatus]
                                            : PushGamesSignalRFeeds.CapitalizeFirstLetter(gameStatus.Replace("_", " "));

                    //if (gameStatus.ToUpper() == "SCHEDULED")
                    //{
                    //    return null;
                    //}
                    string ordinalPeriod = nodeGame.Attributes["period"].Value;
                    if (gameStatus.ToUpper() == "INPROGRESS")
                    {
                        if (ordinalPeriod == "1")
                            gameStatus = "1st Period";
                        else if (ordinalPeriod == "2")
                            gameStatus = "2nd Period";
                        else if (ordinalPeriod == "3")
                            gameStatus = "3rd Period";
                        else if (ordinalPeriod == "4")
                            gameStatus = "1st Overtime";
                        else if (ordinalPeriod == "5")
                            gameStatus = "Penalty Shootout";
                    }


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
                Console.WriteLine($"{ex.GetType().Name} thrown when creating MLB Gamefeed object: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating MLB Gamefeed object: {ex.Message + ex.StackTrace}");
            }
            return null;
        }

    }

    class MLBGame
    {
        public string GameID { get; set; }
        public string MatchID { get; set; }
    }

}
