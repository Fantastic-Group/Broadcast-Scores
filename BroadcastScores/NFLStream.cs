﻿using System;
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
    // This is for NFL Stream Feeds
    public class NFLStream
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        public static string SqlUrl { get; set; }
        //public List<NFLStreamGameScoreHistory> listNFlGameScoreHistory = new List<NFLStreamGameScoreHistory>();
        string NFLBoxScoreAPI { get; set; }

        public NFLStream()
        {
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];
            NFLBoxScoreAPI = ConfigurationManager.AppSettings["NFLBoxScoreAPI"];


            if (String.IsNullOrWhiteSpace(NFLBoxScoreAPI))
                throw new ArgumentException("NFL needs NFLBoxScoreAPI url to fetch box scores", nameof(NFLBoxScoreAPI));

        }

        /*
        public EventMessage CreateNFLScoreMessage(string JsonScorefeed)
        {
            try
            {

                NFLRootObject objNFLRoot = JsonConvert.DeserializeObject<NFLRootObject>(JsonScorefeed);

                string matchID = objNFLRoot.metadata.match;
                matchID = matchID.Substring(matchID.IndexOf("sr:match:")).Replace("sr:match:", "");

                string[] matchIDs = { matchID };
                var matchEventsTask = new EGSqlQuery(SqlUrl).MatchIDsToEventAsync(matchIDs);

                // Got those EventIDs yet?
                if (!matchEventsTask.IsCompleted)
                    matchEventsTask.Wait();

                if (matchEventsTask.Result != null && matchEventsTask.Result.ContainsKey(Convert.ToInt32(matchID)))
                {
                    int eventID = matchEventsTask.Result[Convert.ToInt32(matchID)];

                    List<Period> periodList = new List<Period>();

                    int ordinalPeriod;
                    if (objNFLRoot.payload.@event.score != null)
                    {
                        ordinalPeriod = Convert.ToInt32(objNFLRoot.payload.@event.score.sequence);
                    }
                    else
                    {
                        return null;
                    }

                    int home_score = 0;
                    int away_score = 0;

                    home_score = Convert.ToInt32(objNFLRoot.payload.@event.score.home_points);
                    away_score = Convert.ToInt32(objNFLRoot.payload.@event.score.away_points);


                    string gameStatus = "Quarter "+ ordinalPeriod;
                    gameStatus = PushGamesSignalRFeeds.ToSRScoreStatus.ContainsKey(gameStatus)
                                                                ? PushGamesSignalRFeeds.ToSRScoreStatus[gameStatus]
                                                                : PushGamesSignalRFeeds.CapitalizeFirstLetter(gameStatus.Replace("_", " "));

                    // Start : NFL Period Score History
                    if (ordinalPeriod > 0)
                    {
                        if (listNFlGameScoreHistory.Any(x => x.eventID == eventID))
                        {
                            var objExistingScore = listNFlGameScoreHistory.FirstOrDefault(x => x.eventID == eventID);
                            if (ordinalPeriod == 1)
                            {
                                objExistingScore.q1home = home_score;
                                objExistingScore.q1away = away_score;
                            }
                            else if (ordinalPeriod == 2)
                            {
                                objExistingScore.q2home = home_score - objExistingScore.q1home;
                                objExistingScore.q2away = away_score - objExistingScore.q1away;
                            }
                            else if (ordinalPeriod == 3)
                            {
                                objExistingScore.q3home = home_score - (objExistingScore.q1home + objExistingScore.q2home);
                                objExistingScore.q3away = away_score - (objExistingScore.q1away + objExistingScore.q2away);
                            }
                            else if (ordinalPeriod == 4)
                            {
                                objExistingScore.q4home = home_score - (objExistingScore.q1home + objExistingScore.q2home + objExistingScore.q3home);
                                objExistingScore.q4away = away_score - (objExistingScore.q1away + objExistingScore.q2away + objExistingScore.q3away);
                            }
                            else if (ordinalPeriod == 5)
                            {
                                objExistingScore.q5home = home_score - (objExistingScore.q1home + objExistingScore.q2home + objExistingScore.q3home + objExistingScore.q4home);
                                objExistingScore.q5away = away_score - (objExistingScore.q1away + objExistingScore.q2away + objExistingScore.q3away + objExistingScore.q4away);
                            }
                            else if (ordinalPeriod == 6)
                            {
                                objExistingScore.q5home = home_score - (objExistingScore.q1home + objExistingScore.q2home + objExistingScore.q3home + objExistingScore.q4home + objExistingScore.q5home);
                                objExistingScore.q5away = away_score - (objExistingScore.q1away + objExistingScore.q2away + objExistingScore.q3away + objExistingScore.q4away + objExistingScore.q5away);
                            }
                        }
                        else
                        {
                            listNFlGameScoreHistory.Add(
                                new NFLStreamGameScoreHistory
                                {
                                    eventID = eventID,
                                    q1home = (ordinalPeriod == 1) ? home_score : 0,
                                    q1away = (ordinalPeriod == 1) ? away_score : 0,
                                    q2home = (ordinalPeriod == 2) ? home_score : 0,
                                    q2away = (ordinalPeriod == 2) ? away_score : 0,
                                    q3home = (ordinalPeriod == 3) ? home_score : 0,
                                    q3away = (ordinalPeriod == 3) ? away_score : 0,
                                    q4home = (ordinalPeriod == 4) ? home_score : 0,
                                    q4away = (ordinalPeriod == 4) ? away_score : 0,
                                    q5home = (ordinalPeriod == 5) ? home_score : 0,
                                    q5away = (ordinalPeriod == 5) ? away_score : 0,
                                    q6home = (ordinalPeriod == 6) ? home_score : 0,
                                    q6away = (ordinalPeriod == 6) ? away_score : 0,
                                    createdDate = DateTime.UtcNow
                                }
                            );
                        }
                    }
                    
                    var objScore = listNFlGameScoreHistory.FirstOrDefault(x => x.eventID == eventID);
                    if(ordinalPeriod == 1)
                    {
                        periodList.Add(new Period
                        {
                            Name = Convert.ToString(1), Home = home_score, Visitor = away_score });
                    }
                    else if (ordinalPeriod == 2)
                    {
                        periodList.Add(new Period { Name = "1",Home = objScore.q1home,Visitor = objScore.q1away });
                        periodList.Add(new Period { Name = "2", Home = objScore.q2home, Visitor = objScore.q2away });
                    }
                    else if (ordinalPeriod == 3)
                    {
                        periodList.Add(new Period { Name = "1", Home = objScore.q1home, Visitor = objScore.q1away });
                        periodList.Add(new Period { Name = "2", Home = objScore.q2home, Visitor = objScore.q2away });
                        periodList.Add(new Period { Name = "3", Home = objScore.q3home, Visitor = objScore.q3away });
                    }
                    else if (ordinalPeriod == 4)
                    {
                        periodList.Add(new Period { Name = "1", Home = objScore.q1home, Visitor = objScore.q1away });
                        periodList.Add(new Period { Name = "2", Home = objScore.q2home, Visitor = objScore.q2away });
                        periodList.Add(new Period { Name = "3", Home = objScore.q3home, Visitor = objScore.q3away });
                        periodList.Add(new Period { Name = "4", Home = objScore.q4home, Visitor = objScore.q4away });
                    }
                    else if (ordinalPeriod == 5)
                    {
                        periodList.Add(new Period { Name = "1", Home = objScore.q1home, Visitor = objScore.q1away });
                        periodList.Add(new Period { Name = "2", Home = objScore.q2home, Visitor = objScore.q2away });
                        periodList.Add(new Period { Name = "3", Home = objScore.q3home, Visitor = objScore.q3away });
                        periodList.Add(new Period { Name = "4", Home = objScore.q4home, Visitor = objScore.q4away });
                        periodList.Add(new Period { Name = "5", Home = objScore.q5home, Visitor = objScore.q5away });
                    }
                    else if (ordinalPeriod == 6)
                    {
                        periodList.Add(new Period { Name = "1", Home = objScore.q1home, Visitor = objScore.q1away });
                        periodList.Add(new Period { Name = "2", Home = objScore.q2home, Visitor = objScore.q2away });
                        periodList.Add(new Period { Name = "3", Home = objScore.q3home, Visitor = objScore.q3away });
                        periodList.Add(new Period { Name = "4", Home = objScore.q4home, Visitor = objScore.q4away });
                        periodList.Add(new Period { Name = "5", Home = objScore.q5home, Visitor = objScore.q5away });
                        periodList.Add(new Period { Name = "6", Home = objScore.q6home, Visitor = objScore.q6away });
                    }
                    else if (ordinalPeriod == 7)
                    {
                        periodList.Add(new Period { Name = "1", Home = objScore.q1home, Visitor = objScore.q1away });
                        periodList.Add(new Period { Name = "2", Home = objScore.q2home, Visitor = objScore.q2away });
                        periodList.Add(new Period { Name = "3", Home = objScore.q3home, Visitor = objScore.q3away });
                        periodList.Add(new Period { Name = "4", Home = objScore.q4home, Visitor = objScore.q4away });
                        periodList.Add(new Period { Name = "5", Home = objScore.q5home, Visitor = objScore.q5away });
                        periodList.Add(new Period { Name = "6", Home = objScore.q6home, Visitor = objScore.q6away });
                        periodList.Add(new Period { Name = "7", Home = objScore.q7home, Visitor = objScore.q7away });
                    }
                    // End : NFL Period Score History

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

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when creating Gamefeed object: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating Gamefeed object: {ex.Message +  ex.StackTrace}");
            }
            return null;
        }
        */


        public EventMessage CreateNFLScoreMessageByBoxScoreAPI(string JsonScorefeed)
        {
            try
            {
                NFLRootObject objNFLRoot = JsonConvert.DeserializeObject<NFLRootObject>(JsonScorefeed);

                string matchID = objNFLRoot.metadata.match;
                matchID = matchID.Substring(matchID.IndexOf("sr:match:")).Replace("sr:match:", "");

                string[] matchIDs = { matchID };
                var matchEventsTask = new EGSqlQuery(SqlUrl).MatchIDsToEventAsync(matchIDs);

                // Got those EventIDs yet?
                if (!matchEventsTask.IsCompleted)
                    matchEventsTask.Wait(new TimeSpan(5000));

                if (matchEventsTask.Result != null && matchEventsTask.Result.ContainsKey(Convert.ToInt32(matchID)))
                {
                    int eventID = matchEventsTask.Result[Convert.ToInt32(matchID)];

                    List<Period> periodList = new List<Period>();


                    XmlDocument doc = new XmlDocument();
                    string boxScoreAPI = NFLBoxScoreAPI;
                    boxScoreAPI = boxScoreAPI.Replace("{gameID}", objNFLRoot.payload.game.id);
                    doc.Load(boxScoreAPI);
                    


                    int home_score = 0;
                    int away_score = 0;
                    //home_score = Convert.ToInt32(objNFLRoot.payload.@event.home_points);
                    //away_score = Convert.ToInt32(objNFLRoot.payload.@event.away_points);


                    XmlNode nodeScoring = doc.GetElementsByTagName("scoring").Item(0);
                    if (nodeScoring.HasChildNodes)
                        foreach (XmlNode x in nodeScoring)
                        {
                            periodList.Add(new Period
                            {
                                Name = x.Attributes["number"].Value,
                                Home = Convert.ToInt32(x.Attributes["home_points"].Value),
                                Visitor = Convert.ToInt32(x.Attributes["away_points"].Value),
                            });
                        }


                    home_score = periodList.Sum(x => x.Home).Value;
                    away_score = periodList.Sum(x => x.Visitor).Value;


                    int ordinalPeriod = objNFLRoot.payload.game.quarter;
                    string gameStatus = objNFLRoot.payload.game.status;
                    gameStatus = PushGamesSignalRFeeds.ToSRScoreStatus.ContainsKey(gameStatus)
                                            ? PushGamesSignalRFeeds.ToSRScoreStatus[gameStatus]
                                            : PushGamesSignalRFeeds.CapitalizeFirstLetter(gameStatus.Replace("_", " "));

                    if (gameStatus.ToUpper() == "INPROGRESS")
                    {
                        if (ordinalPeriod == 1)
                        {
                            gameStatus = "1st Quarter ";
                        }
                        else if (ordinalPeriod == 2)
                        {
                            gameStatus = "2nd Quarter ";
                        }
                        else if (ordinalPeriod == 3)
                        {
                            gameStatus = "3rd Quarter ";
                        }
                        else if (ordinalPeriod == 4)
                        {
                            gameStatus = "4th Quarter ";
                        }
                        else if (ordinalPeriod == 5)
                        {
                            gameStatus = "Overtime";
                        }
                        else if (ordinalPeriod == 6)
                        {
                            gameStatus = "2nd Overtime";
                        }
                        else if (ordinalPeriod == 7)
                        {
                            gameStatus = "3rd Overtime ";
                        }
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

                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when creating NFL Gamefeed object: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating NFL Gamefeed object: {ex.Message + ex.StackTrace}");
            }
            return null;
        }


    }

 
    public class NFLGame
    {
        public string id { get; set; }
        public string status { get; set; }
        public string reference { get; set; }
        public int number { get; set; }
        public DateTime scheduled { get; set; }
        public int attendance { get; set; }
        public int utc_offset { get; set; }
        public string entry_mode { get; set; }
        public string weather { get; set; }
        public int quarter { get; set; }
        public string clock { get; set; }
        public string sr_id { get; set; }
    }

    public class NFLPossession
    {
        public string name { get; set; }
        public string market { get; set; }
        public string alias { get; set; }
        public string reference { get; set; }
        public string id { get; set; }
        public string sr_id { get; set; }
    }

    public class NFLLocation
    {
        public string name { get; set; }
        public string market { get; set; }
        public string alias { get; set; }
        public string reference { get; set; }
        public string id { get; set; }
        public int yardline { get; set; }
        public string sr_id { get; set; }
    }

    public class NFLStartSituation
    {
        public string clock { get; set; }
        public int down { get; set; }
        public int yfd { get; set; }
        public NFLPossession possession { get; set; }
        public NFLLocation location { get; set; }
    }

    public class NFLPossession2
    {
        public string name { get; set; }
        public string market { get; set; }
        public string alias { get; set; }
        public string reference { get; set; }
        public string id { get; set; }
        public string sr_id { get; set; }
    }

    public class NFLLocation2
    {
        public string name { get; set; }
        public string market { get; set; }
        public string alias { get; set; }
        public string reference { get; set; }
        public string id { get; set; }
        public int yardline { get; set; }
        public string sr_id { get; set; }
    }

    public class NFLEndSituation
    {
        public string clock { get; set; }
        public int down { get; set; }
        public int yfd { get; set; }
        public NFLPossession2 possession { get; set; }
        public NFLLocation2 location { get; set; }
    }

    public class NFLScoring
    {
        public int sequence { get; set; }
        public string clock { get; set; }
        public int points { get; set; }
        public int home_points { get; set; }
        public int away_points { get; set; }
    }

    public class NFLTeam
    {
        public string name { get; set; }
        public string market { get; set; }
        public string alias { get; set; }
        public string reference { get; set; }
        public string id { get; set; }
        public string sr_id { get; set; }
    }

    public class NFLPlayer
    {
        public string name { get; set; }
        public string jersey { get; set; }
        public string reference { get; set; }
        public string id { get; set; }
        public string position { get; set; }
        public string sr_id { get; set; }
    }

    public class NFLStatistic
    {
        public string stat_type { get; set; }
        public int attempt { get; set; }
        public int att_yards { get; set; }
        public int yards { get; set; }
        public int missed { get; set; }
        public NFLTeam team { get; set; }
        public NFLPlayer player { get; set; }
    }

    public class NFLEvent
    {
        public string type { get; set; }
        public string id { get; set; }
        public double sequence { get; set; }
        public string reference { get; set; }
        public string clock { get; set; }
        public int home_points { get; set; }
        public int away_points { get; set; }
        public string play_type { get; set; }
        public bool scoring_play { get; set; }
        public int play_clock { get; set; }
        public DateTime wall_clock { get; set; }
        public bool fake_punt { get; set; }
        public bool fake_field_goal { get; set; }
        public bool screen_pass { get; set; }
        public string description { get; set; }
        public string alt_description { get; set; }
        public NFLStartSituation start_situation { get; set; }
        public NFLEndSituation end_situation { get; set; }
        public NFLScoring score { get; set; }
        public List<NFLStatistic> statistics { get; set; }
    }

    public class NFLPayload
    {
        public NFLGame game { get; set; }
        public NFLEvent @event { get; set; }
    }

    public class NFLMetadata
    {
        public string league { get; set; }
        public string match { get; set; }
        public string status { get; set; }
        public string event_type { get; set; }
        public string event_category { get; set; }
        public string locale { get; set; }
        public string operation { get; set; }
        public string version { get; set; }
        public string team { get; set; }
    }

    public class NFLRootObject
    {
        public NFLPayload payload { get; set; }
        public string locale { get; set; }
        public NFLMetadata metadata { get; set; }
    }



}

