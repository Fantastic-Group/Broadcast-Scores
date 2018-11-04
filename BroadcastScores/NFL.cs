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
    // This is for NFL Clock Feeds
    public class NFL
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        public static string SqlUrl { get; set; }
        public List<NFLGameScoreHistory> listNFlGameScoreHistory = new List<NFLGameScoreHistory>();

        public NFL()
        {
            SqlUrl = ConfigurationManager.AppSettings["SqlUrl"];

            if (String.IsNullOrWhiteSpace(SqlUrl))
                throw new ArgumentException("NFL needs SqlUrl set to the base URL for the EG SQL service", nameof(SqlUrl));

        }


        public EventMessage CreateNFLScoreMessage(string JsonScorefeed)
        {
            try
            {

                NFLScore objNFLScore = JsonConvert.DeserializeObject<NFLScore>(JsonScorefeed);

                string matchID = objNFLScore.metadata.match;
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

                    int home_score = 0;
                    int away_score = 0;

                    home_score = Convert.ToInt32(objNFLScore.payload.score.home);
                    away_score = Convert.ToInt32(objNFLScore.payload.score.away);

                    int ordinalPeriod;
                    if (objNFLScore.payload.phase.Any(c => char.IsDigit(c)))
                    {
                        ordinalPeriod = Convert.ToInt32(new string(objNFLScore.payload.phase.Where(Char.IsDigit).ToArray()));
                    }
                    else
                    {
                        ordinalPeriod = 0;
                    }


                    string gameStatus = objNFLScore.payload.phase;
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
                        }
                        else
                        {
                            listNFlGameScoreHistory.Add(
                                new NFLGameScoreHistory
                                {
                                    eventID = eventID,
                                    q1home = home_score,
                                    q1away = away_score,
                                    createdDate = DateTime.UtcNow
                                }
                            );
                        }
                    }
                    // End : NFL Period Score History
                    var objScore = listNFlGameScoreHistory.FirstOrDefault(x => x.eventID == eventID);
                    if(ordinalPeriod == 1)
                    {
                        periodList.Add(new Period
                        {
                            Name = Convert.ToString(1), Home = home_score, Visitor = away_score });
                    }
                    else if (ordinalPeriod == 2)
                    {
                        periodList.Add(new Period { Name = Convert.ToString(1),Home = objScore.q1home,Visitor = objScore.q1away });
                        periodList.Add(new Period { Name = Convert.ToString(2), Home = objScore.q2home, Visitor = objScore.q2away });
                    }
                    else if (ordinalPeriod == 3)
                    {
                        periodList.Add(new Period { Name = Convert.ToString(1), Home = objScore.q1home, Visitor = objScore.q1away });
                        periodList.Add(new Period { Name = Convert.ToString(2), Home = objScore.q2home, Visitor = objScore.q2away });
                        periodList.Add(new Period { Name = Convert.ToString(3), Home = objScore.q3home, Visitor = objScore.q3away });
                    }
                    else if (ordinalPeriod == 4)
                    {
                        periodList.Add(new Period { Name = Convert.ToString(1), Home = objScore.q1home, Visitor = objScore.q1away });
                        periodList.Add(new Period { Name = Convert.ToString(2), Home = objScore.q2home, Visitor = objScore.q2away });
                        periodList.Add(new Period { Name = Convert.ToString(3), Home = objScore.q3home, Visitor = objScore.q3away });
                        periodList.Add(new Period { Name = Convert.ToString(4), Home = objScore.q4home, Visitor = objScore.q4away });
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
                logger.Error(ex, $"{ex.GetType().Name} thrown when creating NFL Gamefeed object: {ex.Message +  ex.StackTrace}");
            }
            return null;
        }

    }


    class NFLScore
    {
        public payload payload { get; set; }
        public string locale { get; set; }
        public Metadata metadata { get; set; }
    }

    class payload
    {
        public string clock { get; set; }
        public string possession { get; set; }
        public string phase { get; set; }
        public string down { get; set; }
        public string yfd { get; set; }
        public string location { get; set; }
        public string source { get; set; }
        public string play_review { get; set; }
        public string play_clock { get; set; }
        public string id { get; set; }
        public string sr_id { get; set; }
        public Team home { get; set; }
        public Team away { get; set; }
        public GameScore score { get; set; }
    }

    public class Team
    {
        public string alias { get; set; }
        public string id { get; set; }
        public string name { get; set; }
        public string market { get; set; }
        public string sr_id { get; set; }
    }

    public class Metadata
    {
        public string league { get; set; }
        public string match { get; set; }
        public string locale { get; set; }
        public string operation { get; set; }
        public string version { get; set; }
    }

    public class GameScore
    {
        public string home { get; set; }
        public string away { get; set; }
    }

    public class NFLGameScoreHistory
    {
        public int eventID { get; set; }
        public int q1home { get; set; }
        public int q1away { get; set; }
        public int q2home { get; set; }
        public int q2away { get; set; }
        public int q3home { get; set; }
        public int q3away { get; set; }
        public int q4home { get; set; }
        public int q4away { get; set; }
        public DateTime createdDate { get; set; }
    }

    


}

