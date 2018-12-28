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
        static Logger logger = LogManager.GetCurrentClassLogger();
        string APICallingCycleInterval { get; set; }
        string APICallingCycleIntervalIfGameNotLive { get; set; }
        ProcessSignalR objProcessSignalR = new ProcessSignalR();
        NFLStream objNFL = new NFLStream();


        public PushGamesSignalRFeeds()
        {
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];
            SRScorePullUrlList = ConfigurationManager.AppSettings["SRScorePullUrlList"];

            APICallingCycleInterval = ConfigurationManager.AppSettings["APICallingCycleInterval"];
            APICallingCycleIntervalIfGameNotLive = ConfigurationManager.AppSettings["APICallingCycleIntervalIfGameNotLive"];

            if (String.IsNullOrWhiteSpace(SqlUrl))
                throw new ArgumentException("Needs SqlUrl set to the base URL for the EG SQL service", nameof(SqlUrl));

            if (String.IsNullOrWhiteSpace(SRScorePullUrlList))
                throw new ArgumentException("Needs SRScorePullUrl List to fetch Games Score feeds", nameof(SRScorePullUrlList));

            if (String.IsNullOrWhiteSpace(APICallingCycleInterval))
                throw new ArgumentException("Needs APICallingCycleInterval ", nameof(APICallingCycleInterval));

            if (String.IsNullOrWhiteSpace(APICallingCycleInterval))
                throw new ArgumentException("Needs APICallingCycleInterval ", nameof(APICallingCycleInterval));

        }

        public async Task GenerateScoresFeeds(string urlScorePull)
        {
            try
            {
                //For NCAAF API
                if (urlScorePull.ToUpper().Contains("NCAAF"))
                {
                    NCAAF objNCAAF = new NCAAF(urlScorePull, objProcessSignalR);
                    await objNCAAF.BuildNCAAFScores();
                }
                else if (urlScorePull.ToUpper().Contains("NHL"))
                {
                    NHL objNHL = new NHL(urlScorePull, objProcessSignalR);
                    await objNHL.BuildNHLScores();
                }
                else if (urlScorePull.ToUpper().Contains("NBA"))
                {
                    NBA objNBA = new NBA(urlScorePull, objProcessSignalR);
                    await objNBA.BuildNBAScores();
                }
                else if (urlScorePull.ToUpper().Contains("HOCKEY") && urlScorePull.ToUpper().Contains("ICE"))
                {
                    GlobalIceHockey objGlobalIceHockey = new GlobalIceHockey(urlScorePull, objProcessSignalR);
                    await objGlobalIceHockey.BuildGlobalIceHockeyScores();
                }
                else if (urlScorePull.ToUpper().Contains("BASKETBALL"))
                {
                    GlobalBasketBall objGlobalBasketBall = new GlobalBasketBall(urlScorePull, objProcessSignalR);
                    await objGlobalBasketBall.BuildGlobalBasketBallScores();
                }
                else if (urlScorePull.ToUpper().Contains("NCAAMB"))
                {
                    NCAAMB objNCAAMB = new NCAAMB(urlScorePull, objProcessSignalR);
                    await objNCAAMB.BuildNCAAMBScores();
                }
                else // For all Push feeds
                {
                    while (true)
                    {
                        try
                        {
                            objProcessSignalR.LogHelpDebug("GenerateScoresFeeds : New PushfeedIteration");
                            var client = new WebClient();
                            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;
                            ServicePointManager.Expect100Continue = false;
                            client.OpenReadCompleted += (sender, args) =>
                            {
                                try
                                {
                                    // Only read stream if there is no error in webclient connection to SportRadar, beacause without this condition its directly closing/exiting program without catching exception
                                    if (args.Error == null)
                                        using (var reader = new StreamReader(args.Result))
                                        {
                                            reader.BaseStream.ReadTimeout = 10000;
                                            while (!reader.EndOfStream)
                                            {
                                                string data = reader.ReadLine().Trim();
                                                EventMessage msgScore = new EventMessage();
                                                if (urlScorePull.ToUpper().Contains("NFL"))
                                                {
                                                    if (!String.IsNullOrEmpty(data))
                                                    {
                                                        if (!data.Contains("heartbeat"))
                                                            msgScore = objNFL.CreateNFLScoreMessageByBoxScoreAPI(data);
                                                    }
                                                    if (msgScore != null)
                                                    {
                                                        if (msgScore.Value != null)
                                                        {
                                                            objProcessSignalR.SendSignalRFeedtohub(msgScore, "NFL");
                                                        }
                                                    }
                                                }
                                                else if (data.StartsWith("<root"))
                                                {
                                                    if (!String.IsNullOrEmpty(data))
                                                    {
                                                        if (urlScorePull.ToUpper().Contains("TENNIS"))
                                                        {
                                                            msgScore = CreateTennisScoreMessage(data);
                                                            if (msgScore != null)
                                                                if (msgScore.Value != null)
                                                                    objProcessSignalR.SendSignalRFeedtohub(msgScore, "Tennis");
                                                        }
                                                        else if (urlScorePull.ToUpper().Contains("SOCCER"))
                                                        {
                                                            msgScore = CreateSoccerScoreMessage(data);
                                                            if (msgScore != null)
                                                                if (msgScore.Value != null)
                                                                    objProcessSignalR.SendSignalRFeedtohub(msgScore, "Soccer");
                                                        }
                                                    }
                                                }



                                            }
                                        }
                                }
                                catch (WebException we)
                                {
                                    Console.WriteLine($"{we.GetType().Name} thrown when converting BR scores: {we.Message}");
                                    logger.Error(we, $"{we.GetType().Name} thrown when converting BR scores: {we.Message + we.StackTrace}");
                                }
                                catch (IOException ioe)
                                {
                                    Console.WriteLine($"{ioe.GetType().Name} IO Stream read error thrown when converting BR scores: {ioe.Message}");
                                    logger.Error(ioe, $"{ioe.GetType().Name} IO Stream read error thrown thrown when converting BR scores: {ioe.Message + ioe.StackTrace}");
                                }
                            };
                            await client.OpenReadTaskAsync(urlScorePull);
                        }
                        catch (WebException we)
                        {
                            Console.WriteLine($"{we.GetType().Name} Webclient feeds pulling : {we.Message}");
                            logger.Error(we, $"{we.GetType().Name}  Webclient feeds pulling : {we.Message + we.StackTrace}");
                        }
                        catch (IOException ioe)
                        {
                            Console.WriteLine($"{ioe.GetType().Name} Webclient feeds pulling : {ioe.Message}");
                            logger.Error(ioe, $"{ioe.GetType().Name} Webclient feeds pulling : {ioe.Message + ioe.StackTrace}");
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"{e.GetType().Name}  Webclient feeds pulling: {e.Message}");
                            logger.Error(e, $"{e.GetType().Name}  Webclient feeds pulling: {e.Message}");
                        }
                        System.Threading.Thread.Sleep(10000);
                    }
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when converting BR scores: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when converting BR scores: {ex.Message + ex.StackTrace}");
            }


        }

        public EventMessage CreateSoccerScoreMessage(string XMLScorefeed)
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
                    matchEventsTask.Wait(new TimeSpan(5000));

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

                    string ordinalPeriod = "0";
                    if (nodeEventStatus.Attributes["period"] != null)
                    {
                        ordinalPeriod = nodeEventStatus.Attributes["period"].Value;
                    }
                    else if (doc.GetElementsByTagName("event").Item(0).Attributes["Status"] != null)
                    {
                        ordinalPeriod = doc.GetElementsByTagName("event").Item(0).Attributes["Status"].Value;
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
                Console.WriteLine($"{ex.GetType().Name} thrown when creating Soccer Gamefeed object: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating Soccer Gamefeed object: {ex.Message + ex.StackTrace}");
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
                    matchEventsTask.Wait(new TimeSpan(5000));

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
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating Tennis Gamefeed object: {ex.Message + ex.StackTrace}");
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
            { "1st_half", "1st Half" },
            { "2nd_half", "2nd Half" },
            { "3rd_half", "3rd Half" },
            { "4th_half", "4th Half" },
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
            { "5th_quarter" , "5th Quarter" },
            { "6th_quarter" , "6th Quarter" },
            { "halftime" , "Halftime pause" },
            { "closed" , "Ended" },
            { "Closed" , "Ended" },
            { "complete" , "Ended" },
            { "Complete" , "Ended" },
            { "finished" , "Ended" },
            { "Finished" , "Ended" },
            { "Aet" , "Ended" },
            { "aet" , "Ended" },
            { "ot" , "Overtime" },
            { "Ot" , "Overtime" },
            { "OT" , "Overtime" },
        };

    }
}
