using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnterGamingRelay;

namespace BroadcastScores
{
    public static class EGSql
    {
        //calling : EGSql.GetEventIDbyGameInfoAsync(new EGSqlQuery(SqlUrl), "Buffalo", "Patriots", "2018-10-29T20:15:00").Wait();

        public static async Task<EventDetails> GetEventIDbyGameInfoAsync(this EGSqlQuery query, string home, string away, string date)
        {
            var rows = await query
                        .WithTable("bet_events be")
                        .Join("brlive brl", "be.live_id = brl.id")
                        .Join("teams_translation tth", "tth.TEAM_ID = be.TEAM1_ID")
                        .Join("teams_translation tta", "tta.TEAM_ID = be.TEAM2_ID")
                        .ClearSelect()
                        .AndSelect("be.EVENT_ID")
                        .AndSelect("be.TEAM1_ID")
                        .AndSelect("be.TEAM2_ID")
                        .AndWhere($"tth.TEAM_NAME = '{home}'")
                        .AndWhere($"tta.TEAM_NAME = '{away}'")
                        .AndWhere($"SCD_DATE = '{date}' ")
                        //.ExecAsync<Dictionary<string, string>[]>();
                        .ExecAsync<EventDetails[]>();

            if (rows is null)
                return null;

            return (from r in rows
                    select new EventDetails { EVENT_ID = r.EVENT_ID,  }).FirstOrDefault();

        }
    }

    public class EventDetails
    {
        public int EVENT_ID { get; set; }
        public int TEAM1_ID { get; set; }
        public int TEAM2_ID { get; set; }
    }
}
