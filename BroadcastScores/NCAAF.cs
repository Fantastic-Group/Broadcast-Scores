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
        //static string NCAAFBGamesScheduleAPI = "http://api.sportradar.us/ncaafb-t1/2018/REG/schedule.xml?api_key=n36p42dqxknd6ejqbn7aap4y";
        //static string NCAAFBScoreAPI = "https://api.sportradar.us/ncaafb-t1/{year}/REG/{week}/{home}/{away}/boxscore.xml?api_key=n36p42dqxknd6ejqbn7aap4y";
        //static string  NCAAFBTeams = "https://api.sportradar.us/ncaafb-t1/teams/FBS/hierarchy.xml?api_key=n36p42dqxknd6ejqbn7aap4y";
        //static string NCAAFBScoreAPI = "https://api.sportradar.us/ncaafb-t1/2015/REG/12/TOL/BGN/boxscore.xml?api_key=n36p42dqxknd6ejqbn7aap4y";
        
        static Logger logger = LogManager.GetCurrentClassLogger();
        static string SqlUrl { get; set; }
        string NCAAFBGamesScheduleAPI { get; set; }
        string NCAAFBScoreAPI { get; set; }
        string NCAAFBTeams { get; set; }
        static List<Game> todaysGames = new List<Game>();


        public NCAAF()
        {
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];

            NCAAFBGamesScheduleAPI = ConfigurationManager.AppSettings["NCAAFBGamesScheduleAPI"];
            NCAAFBScoreAPI = ConfigurationManager.AppSettings["NCAAFBScoreAPI"];
            NCAAFBTeams = ConfigurationManager.AppSettings["NCAAFBTeams"];


            if (String.IsNullOrWhiteSpace(SqlUrl))
                throw new ArgumentException("NCAAFB needs SqlUrl set to the base URL for the EG SQL service", nameof(SqlUrl));

            if (String.IsNullOrWhiteSpace(NCAAFBGamesScheduleAPI))
                throw new ArgumentException("NCAAFB needs GameSchedule API URL", nameof(NCAAFBGamesScheduleAPI));

            if (String.IsNullOrWhiteSpace(NCAAFBScoreAPI))
                throw new ArgumentException("NCAAFB needs Score API URL", nameof(NCAAFBScoreAPI));

            if (String.IsNullOrWhiteSpace(NCAAFBTeams))
                throw new ArgumentException("NCAAFB needs Teams API URL ", nameof(NCAAFBTeams));
        }

        public async Task BuildNCAAFScores()
        {
            while (true)
            {
                GetTodaysGames();
                FetchScores();
            }
        }


        public void GetTodaysGames()
        {
            XmlDocument doc = new XmlDocument();
            NCAAFBGamesScheduleAPI = NCAAFBGamesScheduleAPI.Replace("{year}",DateTime.UtcNow.Year.ToString());
            doc.Load(NCAAFBGamesScheduleAPI);
            XmlNode nodeSeason = doc.GetElementsByTagName("season").Item(0);

            foreach (XmlNode xmlWeek in nodeSeason)
            {
                foreach (XmlNode xmlGame in xmlWeek)
                {
                    string gameSchedule = xmlGame.Attributes["scheduled"].Value;
                    string gameStatus = xmlGame.Attributes["status"].Value;
                    if ((gameSchedule.Contains(DateTime.UtcNow.ToString("yyyy-MM-dd") + "T")) && gameStatus.ToUpper() != "CLOSED")
                    {
                        todaysGames.Add(
                            new Game
                            {
                                Home = xmlGame.Attributes["home"].Value,
                                Away = xmlGame.Attributes["away"].Value,
                                Week = xmlWeek.Attributes["week"].Value
                            });
                    }
                }
            }


            Console.WriteLine(todaysGames.Count);
        }

        public void FetchScores()
        {
            XmlDocument doc = new XmlDocument();

            foreach (Game gameDetails in todaysGames)
            {
                NCAAFBScoreAPI = NCAAFBScoreAPI.Replace("{year}", DateTime.UtcNow.Year.ToString());
                NCAAFBScoreAPI = NCAAFBScoreAPI.Replace("{week}", gameDetails.Week);
                NCAAFBScoreAPI = NCAAFBScoreAPI.Replace("{home}", gameDetails.Home);
                NCAAFBScoreAPI = NCAAFBScoreAPI.Replace("{away}", gameDetails.Away);
                //NCAAFBScoreAPI = "https://api.sportradar.us/ncaafb-t1/2015/REG/12/TOL/BGN/boxscore.xml?api_key=n36p42dqxknd6ejqbn7aap4y";
                NCAAFBScoreAPI = "https://api.sportradar.us/ncaafb-t1/2018/REG/9/BCU/NEB/boxscore.xml?api_key=n36p42dqxknd6ejqbn7aap4y";
                doc.Load(NCAAFBScoreAPI);
                EventMessage msg = CreateCollegeFootballScoreMessage(doc.InnerXml, "50c06d6e-f2ea-4850-8b06-140aeb82e00c");
                //SendSignalRFeedtohub(msg);
            }

        }

        public EventMessage CreateCollegeFootballScoreMessage(string XMLScorefeed, string matchID)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.InnerXml = XMLScorefeed;



                matchID = matchID.Replace("sr:match:", "");

                string[] matchIDs = { matchID };
                var matchEventsTask = new EGSqlQuery(SqlUrl).MatchIDsToEventAsync(matchIDs);

                // Got those EventIDs yet?
                if (!matchEventsTask.IsCompleted)
                    matchEventsTask.Wait();

                if (matchEventsTask.Result != null && matchEventsTask.Result.ContainsKey(Convert.ToInt32(matchID)))
                {
                    int eventID = matchEventsTask.Result[Convert.ToInt32(matchID)];

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

        public string getTeamNamebyAbb(string teamName)
        {
            return "";
        }

    }

    class Game
    {
        public string Home { get; set; }
        public string Away { get; set; }
        public string Week { get; set; }
    }

}
