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
    class NCAAMB
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        ProcessSignalR objProcessSignalR;

        static string SqlUrl { get; set; }
        string NCAAMBGamesScheduleAPI { get; set; }
        string NCAAMBScoreAPI { get; set; }
        static List<NCAAMBGame> liveGames = new List<NCAAMBGame>();
        static List<NCAAMBGame> oldLiveGamesList = new List<NCAAMBGame>();
        string APICallingCycleInterval { get; set; }
        string APICallingCycleIntervalIfGameNotLive { get; set; }


        public NCAAMB(string strNCAAMBScoreAPI, ProcessSignalR processSignalR)
        {
            objProcessSignalR = processSignalR;
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];

            NCAAMBScoreAPI = strNCAAMBScoreAPI;
            NCAAMBGamesScheduleAPI = ConfigurationManager.AppSettings["NCAAMBGamesScheduleAPI"];

            APICallingCycleInterval = ConfigurationManager.AppSettings["APICallingCycleInterval"];
            APICallingCycleIntervalIfGameNotLive = ConfigurationManager.AppSettings["APICallingCycleIntervalIfGameNotLive"];

            if (String.IsNullOrWhiteSpace(strNCAAMBScoreAPI))
                throw new ArgumentException("NCAAMB needs Score API URL", nameof(strNCAAMBScoreAPI));

            if (String.IsNullOrWhiteSpace(NCAAMBGamesScheduleAPI))
                throw new ArgumentException("NCAAMB needs GameSchedule API URL", nameof(NCAAMBGamesScheduleAPI));

        }

        public async Task BuildNCAAMBScores()
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
                    Console.WriteLine($"{ex.GetType().Name} thrown when fetching and creating NCAAMB Score object: {ex.Message}");
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
                string gameScheduleAPI = NCAAMBGamesScheduleAPI;
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
                                new NCAAMBGame
                                {
                                    GameID = xmlGame.Attributes["id"].Value,
                                    GameSchedule = xmlGame.Attributes["scheduled"].Value,
                                    Home = xmlGame.ChildNodes[1].Attributes["name"].Value,
                                    Away = xmlGame.ChildNodes[2].Attributes["name"].Value
                                });
                        }
                    }


                // If game is started yesterday but still live today so fetching yesterdays games also
                gameScheduleAPI = NCAAMBGamesScheduleAPI;
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
                                new NCAAMBGame
                                {
                                    GameID = xmlGame.Attributes["id"].Value,
                                    GameSchedule = xmlGame.Attributes["scheduled"].Value,
                                    Home = xmlGame.ChildNodes[1].Attributes["name"].Value,
                                    Away = xmlGame.ChildNodes[2].Attributes["name"].Value
                                });
                        }
                    }
                //////////////////////////////////////////////////////////////////
                List<NCAAMBGame> tempGameslist = new List<NCAAMBGame>();
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
                    foreach (NCAAMBGame game in tempGameslist)
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
                Console.WriteLine($"{ex.GetType().Name} thrown when getting todays NCAAMB Games : {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when getting todays NCAAMB Games : {ex.Message + ex.StackTrace}");
            }
        }

        public async Task FetchAndSendScores()
        {
            foreach (NCAAMBGame gameDetails in liveGames)
            {
                String currentGameURL = NCAAMBScoreAPI;
                currentGameURL = currentGameURL.Replace("{gameID}", gameDetails.GameID);
                try
                {
                    XmlDocument doc = new XmlDocument();
                    var eventIDTask = new EGSqlQuery(SqlUrl).GetEventIDbyGameInfoAsync(gameDetails.Home, gameDetails.Away, gameDetails.GameSchedule);

                    // Got those EventIDs yet?
                    if (!eventIDTask.IsCompleted)
                        await eventIDTask;


                    //Some times the home and away teamnames are swapped in EG DB for NCAAMB, so to fix that scenario added below code
                    if (eventIDTask.Result == null)
                    {
                        eventIDTask = new EGSqlQuery(SqlUrl).GetEventIDbyGameInfoAsync(gameDetails.Away, gameDetails.Home, gameDetails.GameSchedule);

                        // Got those EventIDs yet?
                        if (!eventIDTask.IsCompleted)
                            await eventIDTask;
                    }
                    /////////////////////////////////////////////////////////////

                    if (eventIDTask.Result != null)
                    {
                        int eventID = Convert.ToInt32(eventIDTask.Result.EVENT_ID);
                        doc.Load(currentGameURL);
                        EventMessage msg = CreateNCAAMBScoreMessage(doc.InnerXml, eventID.ToString());
                        if (msg != null)
                        {
                            objProcessSignalR.SendSignalRFeedtohub(msg, "NCAAMB");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} - NCAAMB Score feed pulling from API : {ex.Message}");
                    logger.Error(ex, $"{ex.GetType().Name} - NCAAMB Score feed pulling from API : {ex.Message + ex.StackTrace}");
                }
            }

        }

        public EventMessage CreateNCAAMBScoreMessage(string XMLScorefeed, string eventID)
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
                    //gameStatus = PushGamesSignalRFeeds.CapitalizeFirstLetter(gameStatus.Replace("_", " "));
                    gameStatus = PushGamesSignalRFeeds.ToSRScoreStatus.ContainsKey(gameStatus)
                                            ? PushGamesSignalRFeeds.ToSRScoreStatus[gameStatus]
                                            : PushGamesSignalRFeeds.CapitalizeFirstLetter(gameStatus.Replace("_", " "));

                    string ordinalPeriod = nodeGame.Attributes["half"].Value;
                    if (gameStatus.ToUpper() == "INPROGRESS")
                    {
                        if (ordinalPeriod == "1")
                            gameStatus = "1st Half";
                        else if (ordinalPeriod == "2")
                            gameStatus = "2nd Half";
                        else if (ordinalPeriod == "3")
                            gameStatus = "3rd Half";
                        else if (ordinalPeriod == "4")
                            gameStatus = "4th Half";
                        else if (ordinalPeriod == "5")
                            gameStatus = "5th Half";
                        else if (ordinalPeriod == "6")
                            gameStatus = "6th Half";
                        else if (ordinalPeriod == "7")
                            gameStatus = "7th Half";
                        else if (ordinalPeriod == "8")
                            gameStatus = "8th Half";
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
                    //Console.WriteLine("E" + eventID + ", " + gameStatus);
                    return scoreMsg;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when creating NCAAMB Gamefeed object: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating NCAAMB Gamefeed object: {ex.Message + ex.StackTrace}");
            }
            return null;
        }

    }

    class NCAAMBGame
    {
        public string GameID { get; set; }
        public string Home { get; set; }
        public string Away { get; set; }
        public string GameSchedule { get; set; }
    }

}
