﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnterGamingRelay;
using NLog;

namespace BroadcastScores
{
    public static class EGSql
    {
        //calling : EGSql.GetEventIDbyGameInfoAsync(new EGSqlQuery(SqlUrl), "Buffalo", "Patriots", "2018-10-29T20:15:00").Wait();

        static Logger logger = LogManager.GetCurrentClassLogger();

        public static async Task<EventDetails> GetEventIDbyGameInfoAsync(this EGSqlQuery query, string home, string away, string gameDate)
        {
            try
            {
                // This is to remove _ and - characters to improve the matching between SportRadar and EG team names
                home = home.Replace("-", " ");
                home = home.Replace("_", " ");
                if (home.Contains(" "))
                    home = home.Substring(0, home.IndexOf(" "));

                away = away.Replace("-", " ");
                away = away.Replace("_", " ");
                if (away.Contains(" "))
                    away = away.Substring(0, away.IndexOf(" "));

                DateTime convertedGameDate = ConverToEasternStandardTime(gameDate);
                //string finalDate = convertedGameDate.ToString("yyyy-MM-ddTHH:mm:ss");
                string finalDate = convertedGameDate.ToString("yyyy-MM-dd");

                var rows = await query
                            .WithTable("bet_events be")
                            .Join("brlive brl", "be.live_id = brl.id")
                            .Join("teams_translation tth", "tth.TEAM_ID = be.TEAM1_ID")
                            .Join("teams_translation tta", "tta.TEAM_ID = be.TEAM2_ID")
                            .ClearSelect()
                            .AndSelect("be.EVENT_ID")
                            .AndSelect("be.TEAM1_ID")
                            .AndSelect("be.TEAM2_ID")
                            .AndWhere($"tth.TEAM_NAME like '{home}%'")
                            .AndWhere($"tta.TEAM_NAME like '{away}%'")
                            //.AndWhere($"tth.TEAM_NAME like '%{home}%' AND tth.TEAM_NAME like '{home}%'")
                            //.AndWhere($"tta.TEAM_NAME like '%{away}%' AND tta.TEAM_NAME like '{away}%'")
                            //.AndWhere($"SCD_DATE = '{finalDate}' ")
                            .AndWhere($"ACTUAL_DATE like '{finalDate}%' ")
                            .ExecAsync<EventDetails[]>();
                
                if (rows is null)
                    return null;

                var result = (from r in rows select new EventDetails { EVENT_ID = r.EVENT_ID, }).FirstOrDefault();
                return result;
            }
            catch(Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} - NCAAFB getting EventId from EG for {away} vs {home} , {gameDate} : {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} -  NCAAFB getting EventId from EG for {away} vs {home} , {gameDate} : {ex.Message + ex.StackTrace}");
            }
            return null;
        }

        public static DateTime ConverToEasternStandardTime(string gameDate)
        {
            var zone = TimeZoneInfo.GetSystemTimeZones()
                       //.Where(x => x.BaseUtcOffset != TimeZoneInfo.Local.BaseUtcOffset)
                       .Where(x => x.Id == "Eastern Standard Time")
                       .First();
            DateTime dateinUTC = Convert.ToDateTime(gameDate).ToUniversalTime();
            return TimeZoneInfo.ConvertTimeFromUtc(dateinUTC, zone);
        }

    }

    public class EventDetails
    {
        public int EVENT_ID { get; set; }
        public int TEAM1_ID { get; set; }
        public int TEAM2_ID { get; set; }
    }
}
