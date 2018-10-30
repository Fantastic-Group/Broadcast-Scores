using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.IO;
using NLog;
using Newtonsoft.Json;
using Miomni.MiddleKit;
using Miomni.EventLib.Cache;
using Miomni.Gaming.Relay.Responses;
using System.Configuration;


namespace BroadcastScores
{
    public class ScoreFeedsToDisk 
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        public static string filePathScorestoDisk { get; set; }
        public static string flagScoresToDisk { get; set; }

        public ScoreFeedsToDisk() 
        {
            filePathScorestoDisk = ConfigurationManager.AppSettings["FilePathScorestoDisk"];
            flagScoresToDisk = ConfigurationManager.AppSettings["FlagScoresToDisk"];

            if (flagScoresToDisk.ToUpper() == "TRUE")
            {
                if (String.IsNullOrWhiteSpace(filePathScorestoDisk))
                    throw new ArgumentException("Broadcast Scores needs filePathScorestoDisk to write score feeds to disk", nameof(filePathScorestoDisk));
            }

        }

        public void WritefeedToDisk(EventMessage msg)
        {
            try
            {
                if (msg.Value != null & !String.IsNullOrEmpty(filePathScorestoDisk))
                {
                    EventStatusResponse obj = (EventStatusResponse)msg.Value;
                    if (obj.Score != null)
                    {
                        if (!Directory.Exists(filePathScorestoDisk))
                            Directory.CreateDirectory(filePathScorestoDisk);

                        string jsonString = JsonConvert.SerializeObject(obj.Score);
                        File.WriteAllText(Path.Combine(filePathScorestoDisk, $"{obj.MiomniEventID}.json"), jsonString);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error($"{ex.GetType().Name} thrown when serialising object and writing ScoreFeeds to file : {ex.Message}");
            }
        }



    }
}
