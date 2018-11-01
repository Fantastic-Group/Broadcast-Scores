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
        ProcessSignalR objProcessSignalR = new ProcessSignalR();

        static string SqlUrl { get; set; }
        string NCAAFBGamesScheduleAPI { get; set; }
        string NCAAFBScoreAPI { get; set; }
        string NCAAFBTeamsAPI { get; set; }
        string NCAAFLookupDir { get; set; }
        static List<NCAAFGame> GamesScheduleList = new List<NCAAFGame>();
        static List<NCAAFGame> todaysGames = new List<NCAAFGame>();
        static Dictionary<String, string> TeamNameList = new Dictionary<string, string>();


        public NCAAF(string strNCAAFBScoreAPI)
        {
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];

            NCAAFBScoreAPI = strNCAAFBScoreAPI;
            NCAAFBGamesScheduleAPI = ConfigurationManager.AppSettings["NCAAFBGamesScheduleAPI"];
            NCAAFBTeamsAPI = ConfigurationManager.AppSettings["NCAAFBTeamsAPI"];


            if (String.IsNullOrWhiteSpace(strNCAAFBScoreAPI))
                throw new ArgumentException("NCAAFB needs Score API URL", nameof(NCAAFBScoreAPI));

            if (String.IsNullOrWhiteSpace(SqlUrl))
                throw new ArgumentException("NCAAFB needs SqlUrl set to the base URL for the EG SQL service", nameof(SqlUrl));

            if (String.IsNullOrWhiteSpace(NCAAFBGamesScheduleAPI))
                throw new ArgumentException("NCAAFB needs GameSchedule API URL", nameof(NCAAFBGamesScheduleAPI));

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
            while (true)
            {
                try
                {
                    if (GamesScheduleList.Count > 0)
                    {
                        GetTodaysGames();

                        if (todaysGames.Count > 0)
                        {
                            FetchAndSendScores();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} thrown when fetching and creating NCAAF Score object: {ex.Message + ex.InnerException.Message + ex.StackTrace}");
                }
                System.Threading.Thread.Sleep(10000);
            }
        }

        public void GetTodaysGames()
        {
            try
            {
                if (GamesScheduleList != null)
                    foreach (NCAAFGame game in GamesScheduleList)
                    {
                        if ((game.GameDate.Contains(DateTime.UtcNow.ToString("yyyy-MM-dd") + "T")))
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
                logger.Error(ex, $"{ex.GetType().Name} thrown when getting todays NCAAFB Games : {ex.Message + ex.InnerException.Message + ex.StackTrace}");
            }
        }

        public void FetchAndSendScores()
        {
            XmlDocument doc = new XmlDocument();

            foreach (NCAAFGame gameDetails in todaysGames)
            {
                String currentGameURL = NCAAFBScoreAPI;
                currentGameURL = currentGameURL.Replace("{year}", DateTime.UtcNow.Year.ToString());
                currentGameURL = currentGameURL.Replace("{week}", gameDetails.Week);
                currentGameURL = currentGameURL.Replace("{home}", gameDetails.Away);
                currentGameURL = currentGameURL.Replace("{away}", gameDetails.Home);
                try
                {
                    doc.Load(currentGameURL);
                    var eventID = EGSql.GetEventIDbyGameInfoAsync(new EGSqlQuery(SqlUrl), TeamNameList[gameDetails.Home], TeamNameList[gameDetails.Away], gameDetails.GameDate);
                    EventMessage msg = CreateCollegeFootballScoreMessage(doc.InnerXml, Convert.ToString(eventID));
                    if (msg != null)
                    {
                        objProcessSignalR.SendSignalRFeedtohub(msg);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} - NCAAFB Score feed pulling from API : {ex.Message}");
                    logger.Error(ex, $"{ex.GetType().Name} - NCAAFB Score feed pulling from API : {ex.Message + ex.InnerException.Message + ex.StackTrace}");
                }
            }

        }

        public EventMessage CreateCollegeFootballScoreMessage(string XMLScorefeed, string eventID)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.InnerXml = XMLScorefeed;


                if (!String.IsNullOrEmpty(XMLScorefeed))
                {
                    string gameStatus = doc.GetElementsByTagName("game").Item(0).Attributes["quarter"].Value;
                    if (gameStatus.ToUpper() == "SCHEDULED")
                    {
                        return null;
                    }
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

                    //if (periodList.Count == 0)
                    //    periodList.Add(new Period
                    //    {
                    //        Name = Convert.ToString(1),
                    //        Home = home_score,
                    //        Visitor = away_score
                    //    });



                    if (gameStatus == "1")
                        gameStatus = "1st Quarter";
                    else if (gameStatus == "2")
                        gameStatus = "2nd Quarter";
                    else if (gameStatus == "3")
                        gameStatus = "3rd Quarter";
                    else if (gameStatus == "4")
                        gameStatus = "4th Quarter";
                    else if (gameStatus == "5")
                        gameStatus = "Overtime";
                    else if (gameStatus == "6")
                        gameStatus = "2nd Overtime";
                    else if (gameStatus == "7")
                        gameStatus = "Penalty Shoot Out";



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
                                OrdinalPeriod = Convert.ToInt32(gameStatus),
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
                Console.WriteLine($"{ex.GetType().Name} thrown when creating Gamefeed object: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating Gamefeed object:{ex.Message + ex.InnerException.Message + ex.StackTrace}");
            }
            return null;
        }

        public void GenerateNCAAFLookUps(object source, System.Timers.ElapsedEventArgs e)
        {
            GenerateGamesScheduleLookup();
            GenerateNCAAFTeamNamesLookup();
        }

        public void GenerateGamesScheduleLookup()
        {
            GamesScheduleList.Clear();
            XmlDocument doc = new XmlDocument();
            string gamesScheduleAPI = NCAAFBGamesScheduleAPI;
            gamesScheduleAPI = gamesScheduleAPI.Replace("{year}", DateTime.UtcNow.Year.ToString());
            doc.Load(NCAAFBGamesScheduleAPI);
            XmlNode nodeSeason = doc.GetElementsByTagName("season").Item(0);

            foreach (XmlNode xmlWeek in nodeSeason)
            {
                foreach (XmlNode xmlGame in xmlWeek)
                {
                    string gameStatus = xmlGame.Attributes["status"].Value;
                    if (gameStatus.ToUpper() != "CLOSED")
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

        }

        public void GenerateNCAAFTeamNamesLookup()
        {
            TeamNameList.Clear();
            XmlDocument doc = new XmlDocument();
            doc.Load(NCAAFBTeamsAPI);
            XmlNode nodeDivision = doc.GetElementsByTagName("division").Item(0);

            foreach (XmlNode nodeConference in nodeDivision)
                foreach (XmlNode xmlSubdivision in nodeConference)
                {
                    foreach (XmlNode xmlTeam in xmlSubdivision)
                    {
                        string teamID = xmlTeam.Attributes["id"].Value;
                        string teamName = xmlTeam.Attributes["name"].Value;
                        TeamNameList.Add(teamID, teamName);
                    }
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
