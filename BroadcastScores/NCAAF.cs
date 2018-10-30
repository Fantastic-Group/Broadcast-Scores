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
        static List<Game> GamesScheduleList = new List<Game>();
        static List<Game> todaysGames = new List<Game>();
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

            GenerateNCAAFLookUps(null,null);
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
                catch(Exception ex)
                {
                    Console.WriteLine($"{ex.GetType().Name} thrown when fetching and creating NCAAF Score object: {ex.Message}");
                }
                System.Threading.Thread.Sleep(10000);
            }
        }

        public void GetTodaysGames()
        {
            foreach (Game game in GamesScheduleList)
            {
                   if ((game.GameDate.Contains(DateTime.UtcNow.ToString("yyyy-MM-dd") + "T")))
                    {
                        todaysGames.Add(
                            new Game
                            {
                                Home = game.Home,
                                Away = game.Away,
                                Week = game.Week,
                                GameDate = game.GameDate
                            });
                    }
            }

            Console.WriteLine(todaysGames.Count);
        }

        public void FetchAndSendScores()
        {
            XmlDocument doc = new XmlDocument();

            foreach (Game gameDetails in todaysGames)
            {
                NCAAFBScoreAPI = NCAAFBScoreAPI.Replace("{year}", DateTime.UtcNow.Year.ToString());
                NCAAFBScoreAPI = NCAAFBScoreAPI.Replace("{week}", gameDetails.Week);
                NCAAFBScoreAPI = NCAAFBScoreAPI.Replace("{home}", gameDetails.Home);
                NCAAFBScoreAPI = NCAAFBScoreAPI.Replace("{away}", gameDetails.Away);
                doc.Load(NCAAFBScoreAPI);
                var eventID = EGSql.GetEventIDbyGameInfoAsync(new EGSqlQuery(SqlUrl), TeamNameList[gameDetails.Home], TeamNameList[gameDetails.Away], gameDetails.GameDate);
                EventMessage msg = CreateCollegeFootballScoreMessage(doc.InnerXml, Convert.ToString(eventID));
                objProcessSignalR.SendSignalRFeedtohub(msg);
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
                    XmlNode homeScoreXml = doc.GetElementsByTagName("team").Item(0).FirstChild;
                    XmlNode awayScoreXml = doc.GetElementsByTagName("team").Item(1).FirstChild;

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

                    if (periodList.Count == 0)
                        periodList.Add(new Period
                        {
                            Name = Convert.ToString(1),
                            Home = home_score,
                            Visitor = away_score
                        });


                    string gameStatus = doc.GetElementsByTagName("game").Item(0).Attributes["quarter"].Value;



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
                                CurrentPeriod = "Quarter" + gameStatus,
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
                //logger.Error(ex, $"{ex.GetType().Name} thrown when creating Gamefeed object: {ex.Message}");
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
            NCAAFBGamesScheduleAPI = NCAAFBGamesScheduleAPI.Replace("{year}", DateTime.UtcNow.Year.ToString());
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
                            new Game
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
            XmlNode nodeConference = doc.GetElementsByTagName("conference").Item(0);

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

    class Game
    {
        public string Home { get; set; }
        public string Away { get; set; }
        public string Week { get; set; }
        public string GameDate { get; set; }
    }

}
