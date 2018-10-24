using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.IO;
using System.Xml;
using System.Net;

using Miomni.SportsKit;
using Miomni.EventLib.Cache;
using Miomni.Gaming.Relay.Responses;
using Miomni.Gaming.Relay.Events;
using EnterGamingRelay.APIModel;
using EnterGamingRelay.EventModules;
using EnterGamingRelay;


namespace BroadcastScores
{
    class Program
    {

        public static string SqlUrl { get; set; }
        public static string SRGamePushURL { get; set; }
        public static string Scorefilepath { get; set; }

        static void Main(string[] args)
        {
            PushGamesSignalRFeeds pushObj = new PushGamesSignalRFeeds();


            string[] ScorePullUrls;
            ScorePullUrls = PushGamesSignalRFeeds.SRScorePullUrlList.Split(',');
            foreach (string pullUrl in ScorePullUrls)
            {
                pushObj.GenerateCollegeScoresFeeds(pullUrl).Wait();
            }
            Console.Read();
        }

    }
}
