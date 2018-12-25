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
        ProcessSignalR objProcessSignalR;

        static string SqlUrl { get; set; }
        string NBAGamesScheduleAPI { get; set; }
        string NBAScoreAPI { get; set; }
        static List<NBAGame> liveGames = new List<NBAGame>();
        static List<NBAGame> oldLiveGamesList = new List<NBAGame>();
        string APICallingCycleInterval { get; set; }
        string APICallingCycleIntervalIfGameNotLive { get; set; }


        public NBA(string strNBAScoreAPI, ProcessSignalR processSignalR)
        {
            objProcessSignalR = processSignalR;
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];

            NBAScoreAPI = strNBAScoreAPI;
            NBAGamesScheduleAPI = ConfigurationManager.AppSettings["NBAGamesScheduleAPI"];

            APICallingCycleInterval = ConfigurationManager.AppSettings["APICallingCycleInterval"];
            APICallingCycleIntervalIfGameNotLive = ConfigurationManager.AppSettings["APICallingCycleIntervalIfGameNotLive"];


            if (String.IsNullOrWhiteSpace(strNBAScoreAPI))
                throw new ArgumentException("NBA needs Score API URL", nameof(strNBAScoreAPI));

            if (String.IsNullOrWhiteSpace(SqlUrl))
                throw new ArgumentException("NBA needs SqlUrl set to the base URL for the EG SQL service", nameof(SqlUrl));

            if (String.IsNullOrWhiteSpace(NBAGamesScheduleAPI))
                throw new ArgumentException("NBA needs GameSchedule API URL", nameof(NBAGamesScheduleAPI));

            if (String.IsNullOrWhiteSpace(APICallingCycleInterval))
                throw new ArgumentException("Needs APICallingCycleInterval ", nameof(APICallingCycleInterval));


        }

        public async Task BuildNBAScores()
        {
            objProcessSignalR.LogHelpDebug("BuildNBAScores");
            await Task.Factory.StartNew(() => System.Threading.Thread.Sleep(2000));
            while (true)
            {
                try
                {
                    objProcessSignalR.LogHelpDebug("New Iteration BuildNBAScores");
                    
                    GetLiveGames();

                    if (liveGames.Count > 0)
                    {
                        await FetchAndSendScores();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} thrown when fetching and creating NBA Score object: {ex.Message}");
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
            objProcessSignalR.LogHelpDebug("NBA GetLiveGames");
            try
            {
                liveGames.Clear();
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
                        if (gameStatus.ToUpper() == "INPROGRESS")
                        {
                            liveGames.Add(
                                new NBAGame
                                {
                                    GameID = xmlGame.Attributes["id"].Value,
                                    MatchID = xmlGame.Attributes["sr_id"].Value
                                });
                        }
                    }

                // If game is started yesterday but still live today so fetching yesterdays games also
                gameScheduleAPI = NBAGamesScheduleAPI;
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
                                new NBAGame
                                {
                                    GameID = xmlGame.Attributes["id"].Value,
                                    MatchID = xmlGame.Attributes["sr_id"].Value
                                });
                        }
                    }

                //////////////////////////////////////////////////////////////////
                List<NBAGame> tempGameslist = new List<NBAGame>();
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
                    foreach (NBAGame game in tempGameslist)
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
                Console.WriteLine("GetTodaysGames : " + $"{ex.GetType().Name} thrown when getting todays NBA Games : {ex.Message}");
                logger.Error(ex, "GetTodaysGames : " + $"{ex.GetType().Name} thrown when getting todays NBA Games : {ex.Message + ex.StackTrace}");
            }

        }

        public async Task FetchAndSendScores()
        {
            objProcessSignalR.LogHelpDebug("NBA FetchAndSendScores");
            foreach (NBAGame gameDetails in liveGames)
            {
                String currentGameURL = NBAScoreAPI;
                currentGameURL = currentGameURL.Replace("{gameID}", gameDetails.GameID);
                try
                {
                    XmlDocument doc = new XmlDocument();
                    string matchID = gameDetails.MatchID.Replace("sr:match:", "");
                    string[] matchIDs = { matchID };
                    var matchEventsTask = new EGSqlQuery(SqlUrl).MatchIDsToEventAsync(matchIDs);

                    // Got those EventIDs yet?
                    if (!matchEventsTask.IsCompleted)
                        await matchEventsTask;

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
            objProcessSignalR.LogHelpDebug("CreateNBAScoreMessage");
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
                    //if (gameStatus.ToUpper() == "SCHEDULED")
                    //{
                    //    return null;
                    //}
                    //gameStatus = PushGamesSignalRFeeds.CapitalizeFirstLetter(gameStatus.Replace("_", " "));
                    gameStatus = PushGamesSignalRFeeds.ToSRScoreStatus.ContainsKey(gameStatus)
                                            ? PushGamesSignalRFeeds.ToSRScoreStatus[gameStatus]
                                            : PushGamesSignalRFeeds.CapitalizeFirstLetter(gameStatus.Replace("_", " "));

                    string ordinalPeriod = nodeGame.Attributes["quarter"].Value;
                    if (gameStatus.ToUpper() == "INPROGRESS")
                    {
                        if (ordinalPeriod == "1")
                            gameStatus = "1st Quarter";
                        else if (ordinalPeriod == "2")
                            gameStatus = "2nd Quarter";
                        else if (ordinalPeriod == "3")
                            gameStatus = "3rd Quarter";
                        else if (ordinalPeriod == "4")
                            gameStatus = "4th Quarter";
                        else if (ordinalPeriod == "5")
                            gameStatus = "1st OverTime";
                        else if (ordinalPeriod == "6")
                            gameStatus = "2nd OverTime";
                        else if (ordinalPeriod == "7")
                            gameStatus = "3rd OverTime";
                        else if (ordinalPeriod == "8")
                            gameStatus = "4th OverTime";
                        else if (ordinalPeriod == "9")
                            gameStatus = "5th OverTime";
                        else if (ordinalPeriod == "10")
                            gameStatus = "6th OverTime";
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
