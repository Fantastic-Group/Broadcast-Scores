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
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;
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
    public class PushGamesSignalRFeeds
    {
        public static string SqlUrl { get; set; }
        public static string SRScorePullUrlList { get; set; }
        public static string SendSignalR { get; set; }
        public static string hubUrl, salt, hub, method;
        static Logger logger = LogManager.GetCurrentClassLogger();
        ProcessSignalR objProcessSignalR = new ProcessSignalR();
        NFL objNFL = new NFL();


        public PushGamesSignalRFeeds()
        {
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];
            SRScorePullUrlList = ConfigurationManager.AppSettings["SRScorePullUrlList"];
            SendSignalR = ConfigurationManager.AppSettings["SendSignalR"];

            if (String.IsNullOrWhiteSpace(SqlUrl))
                throw new ArgumentException("EGSportRadarCollegeToEventstatus needs SqlUrl set to the base URL for the EG SQL service", nameof(SqlUrl));

            if (String.IsNullOrWhiteSpace(SRScorePullUrlList))
                throw new ArgumentException("EGSportRadarCollegeToEventstatus needs SRScorePullUrlList to fetch Games Score feeds", nameof(SRScorePullUrlList));

            if (String.IsNullOrWhiteSpace(SendSignalR))
                throw new ArgumentException("EGSportRadarCollegeToEventstatus needs SendSignalR set to fetch feeds", nameof(SendSignalR));

            string[] signalRDetails = SendSignalR.Split(',');

            hubUrl = signalRDetails[0];
            salt = signalRDetails[1];
            hub = signalRDetails[2];
            method = signalRDetails[3];

        }

        public async Task GenerateScoresFeeds(string urlScorePull)
        {
            try
            {
                //For NCAAF API
                if (urlScorePull.ToUpper().Contains("NCAAF"))
                {
                    NCAAF objNCAAF = new NCAAF(urlScorePull);
                    objNCAAF.BuildNCAAFScores();
                }
                else // For all Push feeds
                {
                    var client = new WebClient();
                    while (true)
                    {
                        try
                        {
                            client.OpenReadCompleted += (sender, args) =>
                            {
                                using (var reader = new StreamReader(args.Result))
                                {
                                    while (!reader.EndOfStream)
                                    {
                                        string data = reader.ReadLine().Trim();
                                        EventMessage msgScore = new EventMessage();
                                        if (urlScorePull.ToUpper().Contains("NFL"))
                                        {
                                            if (!String.IsNullOrEmpty(data))
                                                msgScore = objNFL.CreateNFLScoreMessage(data);

                                            if (msgScore != null)
                                            {
                                                objProcessSignalR.SendSignalRFeedtohub(msgScore);
                                            }
                                        }
                                        else if (data.StartsWith("<root"))
                                        {
                                            if (!String.IsNullOrEmpty(data))
                                            {
                                                if (urlScorePull.ToUpper().Contains("TENNIS"))
                                                {
                                                    msgScore = CreateTennisScoreMessage(data);
                                                }
                                                else
                                                {
                                                    msgScore = CreateGamesScoreMessage(data);
                                                }
                                            }
                                            if (msgScore != null)
                                            {
                                                objProcessSignalR.SendSignalRFeedtohub(msgScore);
                                            }
                                        }



                                    }
                                }
                            };
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                            logger.Error(e, $"{e.GetType().Name}  Webclient feeds pulling: {e.Message}");
                        }
                        await client.OpenReadTaskAsync(urlScorePull);
                        //client.OpenReadAsync(new Uri(urlScorePull));
                        System.Threading.Thread.Sleep(5000);
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when converting BR scores: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when converting BR scores: {ex.Message}");
            }

            //GenerateCollegeScoresFiles();
        }

        //Other Games than Tennis
        public EventMessage CreateGamesScoreMessage(string XMLScorefeed)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.InnerXml = XMLScorefeed;


                XmlNode nodeFeedMeta = doc.GetElementsByTagName("metadata").Item(0);

                string matchID = nodeFeedMeta.Attributes["sport_event_id"].Value;
                matchID = matchID.Replace("sr:match:", "");

                string sportID = nodeFeedMeta.Attributes["sport_id"].Value;
                sportID = sportID.Replace("sr:sport:", "");

                string[] matchIDs = { matchID };
                var matchEventsTask = new EGSqlQuery(SqlUrl).MatchIDsToEventAsync(matchIDs);

                // Got those EventIDs yet?
                if (!matchEventsTask.IsCompleted)
                    matchEventsTask.Wait();

                if (matchEventsTask.Result != null && matchEventsTask.Result.ContainsKey(Convert.ToInt32(matchID)))
                {
                    int eventID = matchEventsTask.Result[Convert.ToInt32(matchID)];

                    XmlNode nodeEventStatus = doc.GetElementsByTagName("sport_event_status").Item(0);
                    List<Period> periodList = new List<Period>();
                    if (nodeEventStatus.HasChildNodes)
                        foreach (XmlNode x in nodeEventStatus.ChildNodes[0])
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

                    if (nodeEventStatus != null)
                    {
                        home_score = Convert.ToInt32(nodeEventStatus?.Attributes["home_score"]?.Value);
                        away_score = Convert.ToInt32(nodeEventStatus?.Attributes["away_score"]?.Value);
                    }

                    if (periodList.Count == 0)
                        periodList.Add(new Period
                        {
                            Name = Convert.ToString(1),
                            Home = home_score,
                            Visitor = away_score
                        });


                    string gameStatus = nodeEventStatus.Attributes["match_status"].Value;
                    gameStatus = ToSRScoreStatus.ContainsKey(gameStatus)
                                                                ? ToSRScoreStatus[gameStatus]
                                                                : CapitalizeFirstLetter(gameStatus.Replace("_", " "));



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
                                OrdinalPeriod = Convert.ToInt32(nodeEventStatus.Attributes["period"].Value),
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
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating Gamefeed object: {ex.Message}");
            }
            return null;
        }

        public EventMessage CreateTennisScoreMessage(string XMLScorefeed)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.InnerXml = XMLScorefeed;

                XmlNode nodeFeedMeta = doc.GetElementsByTagName("metadata").Item(0);
                string matchID = nodeFeedMeta.Attributes["sport_event_id"].Value;
                matchID = matchID.Replace("sr:match:", "");
                string[] matchIDs = { matchID };
                var matchEventsTask = new EGSqlQuery(SqlUrl).MatchIDsToEventAsync(matchIDs);

                // Got those EventIDs yet?
                if (!matchEventsTask.IsCompleted)
                    matchEventsTask.Wait();

                if (matchEventsTask.Result != null && matchEventsTask.Result.ContainsKey(Convert.ToInt32(matchID)))
                {
                    int eventID = matchEventsTask.Result[Convert.ToInt32(matchID)];

                    XmlNode nodeGameState = doc.GetElementsByTagName("game_state").Item(0);
                    XmlNode nodeEventStatus = doc.GetElementsByTagName("sport_event_status").Item(0);

                    List<Period> periodList = new List<Period>();
                    if (nodeEventStatus.HasChildNodes)
                        foreach (XmlNode x in nodeEventStatus.ChildNodes[0])
                        {
                            periodList.Add(new Period
                            {
                                Name = x.Attributes["number"].Value,
                                Home = Convert.ToInt32(x.Attributes["home_score"].Value),
                                Visitor = Convert.ToInt32(x.Attributes["away_score"].Value),
                            });
                        }



                    string gameStatus = nodeEventStatus.Attributes["match_status"].Value;
                    gameStatus = ToSRScoreStatus.ContainsKey(gameStatus)
                                                                ? ToSRScoreStatus[gameStatus]
                                                                : CapitalizeFirstLetter(gameStatus.Replace("_", " "));



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
                                OrdinalPeriod = Convert.ToInt32(nodeEventStatus.Attributes["period"].Value),
                                Time = null,
                                Home = null,
                                Visitor = null,
                                Periods = periodList,
                                HomeAlt = nodeGameState.Attributes["home_score"].Value, // Tennis - Overall Home Score
                                VisitorAlt = nodeGameState.Attributes["away_score"].Value, // Tennis Overall Away Score
                                DisplayText1 = nodeGameState.Attributes["serving"].Value //Serving Person
                            }
                        }
                    };
                    return scoreMsg;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when creating Tennis Gamefeed object: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating Tennis Gamefeed object: {ex.Message}");
            }
            return null;
        }

        public static string CapitalizeFirstLetter(string s)
        {
            if (String.IsNullOrEmpty(s)) return s;
            if (s.Length == 1) return s.ToUpper();
            return s.Remove(1).ToUpper() + s.Substring(1);
        }

        public static Dictionary<string, string> ToSRScoreStatus = new Dictionary<string, string>
        {
            { "1st_half", "First Half" },
            { "2nd_half", "Second Half" },
            { "1st_set", "First Set" },
            { "2nd_set", "Second Set" },
            { "3rd_set" , "Third Set" },
            { "4th_set" , "Fourth Set" },
            { "5th_set" , "Fifth Set" },
            { "7th_set" , "Seventh Set" },
            { "1st_quarter", "1st Quarter" },
            { "2nd_quarter", "2nd Quarter" },
            { "3rd_quarter" , "3rd Quarter" },
            { "4th_quarter" , "4th Quarter" },
            { "halftime" , "Halftime pause" },
        };

     

    }
}
