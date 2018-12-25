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
    class NCAAF
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        ProcessSignalR objProcessSignalR;

        static string SqlUrl { get; set; }
        string NCAAFBGamesScheduleAPI { get; set; }
        string NCAAFBDivisions { get; set; }
        string NCAAFBScoreAPI { get; set; }
        string NCAAFBTeamsAPI { get; set; }
        string NCAAFLookupDir { get; set; }
        //static List<NCAAFGame> GamesScheduleList = new List<NCAAFGame>();
        static List<NCAAFGame> liveGames = new List<NCAAFGame>();
        static List<NCAAFGame> oldLiveGamesList = new List<NCAAFGame>();
        static Dictionary<String, string> TeamNameList = new Dictionary<string, string>();
        string APICallingCycleInterval { get; set; }
        string APICallingCycleIntervalIfGameNotLive { get; set; }


        public NCAAF(string strNCAAFBScoreAPI, ProcessSignalR processSignalR)
        {
            objProcessSignalR = processSignalR;
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];

            NCAAFBScoreAPI = strNCAAFBScoreAPI;
            NCAAFBDivisions = ConfigurationManager.AppSettings["NCAAFBDivisions"];
            NCAAFBGamesScheduleAPI = ConfigurationManager.AppSettings["NCAAFBGamesScheduleAPI"];
            NCAAFBTeamsAPI = ConfigurationManager.AppSettings["NCAAFBTeamsAPI"];

            APICallingCycleInterval = ConfigurationManager.AppSettings["APICallingCycleInterval"];
            APICallingCycleIntervalIfGameNotLive = ConfigurationManager.AppSettings["APICallingCycleIntervalIfGameNotLive"];


            if (String.IsNullOrWhiteSpace(strNCAAFBScoreAPI))
                throw new ArgumentException("NCAAFB needs Score API URL", nameof(NCAAFBScoreAPI));

            if (String.IsNullOrWhiteSpace(NCAAFBGamesScheduleAPI))
                throw new ArgumentException("NCAAFB needs GameSchedule API URL", nameof(NCAAFBGamesScheduleAPI));

            if (String.IsNullOrWhiteSpace(NCAAFBDivisions))
                throw new ArgumentException("NCAAFB needs Divisions for getting Game Schedules", nameof(NCAAFBDivisions));

            if (String.IsNullOrWhiteSpace(NCAAFBTeamsAPI))
                throw new ArgumentException("NCAAFB needs Teams API URL ", nameof(NCAAFBTeamsAPI));



            GenerateNCAAFLookUps(null, null);
            System.Timers.Timer aTimer = new System.Timers.Timer();
            aTimer.Elapsed += new System.Timers.ElapsedEventHandler(GenerateNCAAFLookUps);
            aTimer.Interval = (1000 * 60 * 720);  //Every 12 hours call funtion GenerateNCAAFLookUps locally
            aTimer.Enabled = true;
        }

        public async Task BuildNCAAFScores()
        {
            objProcessSignalR.LogHelpDebug("BuildNCAAFScores");
            await Task.Factory.StartNew(() => System.Threading.Thread.Sleep(2000));
            while (true)
            {
                objProcessSignalR.LogHelpDebug("New Iteration BuildNCAAFScores");
                try
                {
                    
                    GetLiveGames();
                    //if (GamesScheduleList.Count > 0)
                    //{
                    //GetTodaysGames();

                    if (liveGames.Count > 0)
                    {
                        await FetchAndSendScores();
                    }
                    //}
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} thrown when fetching and creating NCAAF Score object: {ex.Message + ex.StackTrace}");
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

        /*public void GetTodaysGames()
        {
            try
            {
                todaysGames.Clear();
                if (GamesScheduleList != null)
                    foreach (NCAAFGame game in GamesScheduleList)
                    {
                        // Todays and yesterday as in case if some game started at 11:30 PM and contineus today after 12 AM
                        if ((game.GameDate.Contains(DateTime.UtcNow.ToString("yyyy-MM-dd") + "T")) || (game.GameDate.Contains(DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd") + "T")))
                        {
                            todaysGames.Add(
                                new NCAAFGame
                                {
                                    Home = game.Home,
                                    Away = game.Away,
                                    Week = game.Week,
                                    GameDate = game.GameDate
                                });
                        }

                    }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when getting todays NCAAFB Games : {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when getting todays NCAAFB Games : {ex.Message + ex.StackTrace}");
            }
        }*/

        public async Task FetchAndSendScores()
        {
            objProcessSignalR.LogHelpDebug("NCAAF FetchAndSendScores");
            foreach (NCAAFGame gameDetails in liveGames)
            {
                String currentGameURL = NCAAFBScoreAPI;
                currentGameURL = currentGameURL.Replace("{year}", DateTime.UtcNow.Year.ToString());
                currentGameURL = currentGameURL.Replace("{week}", gameDetails.Week);
                currentGameURL = currentGameURL.Replace("{home}", gameDetails.Away);
                currentGameURL = currentGameURL.Replace("{away}", gameDetails.Home);
                try
                {
                    XmlDocument doc = new XmlDocument();
                    if (TeamNameList.Keys.Contains(gameDetails.Home) && TeamNameList.Keys.Contains(gameDetails.Away))
                    {
                        var eventIDTask = new EGSqlQuery(SqlUrl).GetEventIDbyGameInfoAsync(TeamNameList[gameDetails.Home], TeamNameList[gameDetails.Away], gameDetails.GameDate);

                        if (!eventIDTask.IsCompleted)
                            await eventIDTask;

                        if (eventIDTask.Result != null)
                        {
                            doc.Load(currentGameURL);
                            EventMessage msg = CreateCollegeFootballScoreMessage(doc.InnerXml, Convert.ToString(eventIDTask.Result.EVENT_ID));
                            if (msg != null)
                            {
                                objProcessSignalR.SendSignalRFeedtohub(msg, "NCAAFB");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} - NCAAFB Score feed pulling from API : {ex.Message}");
                    logger.Error(ex, $"{ex.GetType().Name} - NCAAFB Score feed pulling from API : {ex.Message + ex.StackTrace}");
                }
            }

        }

        public EventMessage CreateCollegeFootballScoreMessage(string XMLScorefeed, string eventID)
        {
            objProcessSignalR.LogHelpDebug("CreateCollegeFootballScoreMessage");
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.InnerXml = XMLScorefeed;
                XmlNode nodegame = doc.GetElementsByTagName("game").Item(0);

                if (!String.IsNullOrEmpty(XMLScorefeed))
                {
                    //string schDate = nodegame.Attributes["scheduled"].Value;
                    //if(schDate == null)
                    //{
                    //    return null;
                    //}
                    //else
                    //{
                    //    // Game is today but not yet started ,EG has these games and we get EventID for that but these games are not active yet
                    //    if (Convert.ToDateTime(schDate).ToUniversalTime() > DateTime.UtcNow)
                    //        return null;
                    //}

                    if (nodegame.Attributes["quarter"] == null)
                        return null;

                    string gameStatus = nodegame.Attributes["status"].Value;
                    if (gameStatus == null || gameStatus.ToUpper() == "SCHEDULED")
                    {
                        return null;
                    }
                    //gameStatus = PushGamesSignalRFeeds.CapitalizeFirstLetter(gameStatus.Replace("_", " "));
                    gameStatus = PushGamesSignalRFeeds.ToSRScoreStatus.ContainsKey(gameStatus)
                                                                ? PushGamesSignalRFeeds.ToSRScoreStatus[gameStatus]
                                                                : PushGamesSignalRFeeds.CapitalizeFirstLetter(gameStatus.Replace("_", " "));


                    XmlNode homeScoreXml = doc.GetElementsByTagName("team").Item(0).FirstChild;
                    XmlNode awayScoreXml = doc.GetElementsByTagName("team").Item(1).FirstChild;
                    if (homeScoreXml == null || awayScoreXml == null)
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
                        home_score = Convert.ToInt32(homeScoreXml.Attributes["points"].Value);
                        away_score = Convert.ToInt32(awayScoreXml.Attributes["points"].Value);
                    }


                    string ordinalPeriod = nodegame.Attributes["quarter"].Value;
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
                            gameStatus = "Overtime";
                        else if (ordinalPeriod == "6")
                            gameStatus = "2nd Overtime";
                        else if (ordinalPeriod == "7")
                            gameStatus = "Penalty Shoot Out";
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
                Console.WriteLine($"{ex.GetType().Name} thrown when creating NCAAFB Gamefeed object: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating NCAAFB Gamefeed object:{ex.Message + ex.StackTrace}");
            }
            return null;
        }

        public void GenerateNCAAFLookUps(object source, System.Timers.ElapsedEventArgs e)
        {
            //GenerateGamesScheduleLookup();
            GenerateNCAAFTeamNamesLookup();
        }

        /*public void GenerateGamesScheduleLookup()
        {
            GamesScheduleList.Clear();
            XmlDocument doc = new XmlDocument();
            string gamesScheduleAPI = NCAAFBGamesScheduleAPI;
            gamesScheduleAPI = gamesScheduleAPI.Replace("{year}", DateTime.UtcNow.Year.ToString());
            doc.Load(gamesScheduleAPI);
            XmlNode nodeSeason = doc.GetElementsByTagName("season").Item(0);

            foreach (XmlNode xmlWeek in nodeSeason)
            {
                foreach (XmlNode xmlGame in xmlWeek)
                {
                    string gameStatus = xmlGame.Attributes["status"].Value;
                    if (gameStatus.ToUpper() != "CLOSED" && gameStatus.ToUpper() != "INPROGRESS")
                    {
                        GamesScheduleList.Add(
                            new NCAAFGame
                            {
                                Home = xmlGame.Attributes["home"].Value,
                                Away = xmlGame.Attributes["away"].Value,
                                Week = xmlWeek.Attributes["week"].Value,
                                GameDate = xmlGame.Attributes["scheduled"].Value
                            });
                    }
                }
            }

        }*/

        public void GetLiveGames()
        {
            objProcessSignalR.LogHelpDebug("NCAAF GetLiveGames");
            try
            {
                liveGames.Clear();
                XmlDocument doc = new XmlDocument();
                string gamesScheduleAPI = NCAAFBGamesScheduleAPI;
                gamesScheduleAPI = gamesScheduleAPI.Replace("{year}", DateTime.UtcNow.Year.ToString());
                doc.Load(gamesScheduleAPI);
                XmlNode nodeSeason = doc.GetElementsByTagName("season").Item(0);

                foreach (XmlNode xmlWeek in nodeSeason)
                {
                    foreach (XmlNode xmlGame in xmlWeek)
                    {
                        string gameStatus = xmlGame.Attributes["status"].Value;
                        if (gameStatus.ToUpper() == "INPROGRESS")
                        {
                            liveGames.Add(
                                new NCAAFGame
                                {
                                    Home = xmlGame.Attributes["home"].Value,
                                    Away = xmlGame.Attributes["away"].Value,
                                    Week = xmlWeek.Attributes["week"].Value,
                                    GameDate = xmlGame.Attributes["scheduled"].Value
                                });
                        }
                    }
                }

                //////////////////////////////////////////////////////////////////
                List<NCAAFGame> tempGameslist = new List<NCAAFGame>();
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
                    foreach (NCAAFGame game in tempGameslist)
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
                Console.WriteLine($"{ex.GetType().Name} thrown when fetching live games for NCAAFB : {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when fetching live games for NCAAFB :{ex.Message + ex.StackTrace}");
            }

        }

        public void GenerateNCAAFTeamNamesLookup()
        {
            objProcessSignalR.LogHelpDebug("GenerateNCAAFTeamNamesLookup");
            try
            {
                TeamNameList.Clear();
                string[] divisions = NCAAFBDivisions.Split(',');
                foreach (string division in divisions)
                {
                    XmlDocument doc = new XmlDocument();
                    string teamsAPI = NCAAFBTeamsAPI;
                    doc.Load(teamsAPI.Replace("{division}", division));
                    XmlNode nodeDivision = doc.GetElementsByTagName("division").Item(0);

                    foreach (XmlNode nodeConference in nodeDivision)
                    {
                        foreach (XmlNode xmlSubdivision in nodeConference)
                        {
                            //If Conference node directlky contains Team instead of Subdivision
                            if (xmlSubdivision.Name.ToUpper() == "TEAM")
                            {
                                string teamID = xmlSubdivision.Attributes["id"].Value;
                                string teamName = xmlSubdivision.Attributes["market"].Value;
                                TeamNameList.Add(teamID, teamName);
                            }
                            else
                            {
                                foreach (XmlNode xmlTeam in xmlSubdivision)
                                {
                                    string teamID = xmlTeam.Attributes["id"].Value;
                                    string teamName = xmlTeam.Attributes["market"].Value;
                                    TeamNameList.Add(teamID, teamName);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when generating TeamNames lookup for NCAAFB : {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when generating TeamNames lookup for NCAAFB :{ex.Message + ex.StackTrace}");
            }

        }

    }

    class NCAAFGame
    {
        public string Home { get; set; }
        public string Away { get; set; }
        public string Week { get; set; }
        public string GameDate { get; set; }
    }

}
