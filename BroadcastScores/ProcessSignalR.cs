using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Client;
using Newtonsoft.Json;
using NLog;

using Miomni.MiddleKit;
using Miomni.EventLib.Cache;


namespace BroadcastScores
{
    public class ProcessSignalR
    {

        public static string SendSignalR { get; set; }
        public static string hubUrl, salt, hub, method;
        static Logger logger = LogManager.GetCurrentClassLogger();

        public ProcessSignalR()
        {
            SendSignalR = ConfigurationManager.AppSettings["SendSignalR"];

            if (String.IsNullOrWhiteSpace(SendSignalR))
                throw new ArgumentException("EGSportRadarCollegeToEventstatus needs SendSignalR set to fetch feeds", nameof(SendSignalR));

            string[] signalRDetails = SendSignalR.Split(',');

            hubUrl = signalRDetails[0];
            salt = signalRDetails[1];
            hub = signalRDetails[2];
            method = signalRDetails[3];
        }

        public void SendSignalRFeedtohub(EventMessage msg)
        {
            try
            {
                HubConnection connection = new HubConnection(hubUrl);
                connection.Error += Connection_Error;
                connection.ConnectionSlow += Connection_ConnectionSlow;
                connection.Closed += Connection_Closed;
                IHubProxy proxy = connection.CreateHubProxy(hub);

                connection.Start().Wait();

                string serialised = JsonConvert.SerializeObject(msg.Value);
                string authHash = $"{serialised}{salt}".ToSHA256();

                var task = proxy.Invoke(method, authHash, msg.Value);
                task.Wait();
                Console.WriteLine("Message Sent");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when sending SignalR feed to Hub: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when sending SignalR feed to Hub: {ex.Message}");
            }

        }

        private void Connection_Closed()
        {
            Console.WriteLine($"SignalR connection closed");
        }

        private void Connection_ConnectionSlow()
        {
            Console.WriteLine($"SignalR connection is slow.");
        }

        private void Connection_Error(Exception obj)
        {
            Console.WriteLine($"{obj.GetType().Name} thrown on SignalR connection: {obj.Message}");
            logger.Error($"{obj.GetType().Name} thrown on SignalR connection: {obj.Message}");
        }


    }
}
