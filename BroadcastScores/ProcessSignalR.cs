﻿using System;
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
using Miomni.Gaming.Relay.Events;
using Miomni.Gaming.Relay.Responses;
using System.Diagnostics;


namespace BroadcastScores
{
    public class ProcessSignalR
    {

        public static string SignalUrl { get; set; }
        public static string hubUrl, salt, hub, method;
        static Logger logger = LogManager.GetCurrentClassLogger();
        static ScoreFeedsToDisk objFeedsToDisk = new ScoreFeedsToDisk();
        //HubConnection connection;
        //IHubProxy proxy;
        List<HubNProxy> connectionList = new List<HubNProxy>();
        //int counterMessageToSignalR = 0;

        public ProcessSignalR()
        {
            SignalUrl = ConfigurationManager.AppSettings["SignalUrl"];
            salt = ConfigurationManager.AppSettings["SignalRSalt"];
            hub = ConfigurationManager.AppSettings["SignalRHub"];
            method = ConfigurationManager.AppSettings["SignalRMethod"];

            if (String.IsNullOrWhiteSpace(SignalUrl))
                throw new ArgumentException("Needs SendSignalR urls to send feeds", nameof(SendSignalR));

            //CreateSignalRConnectionandConnect(SignalUrl);
            string[] signalRurl = SignalUrl.Split(',');
            foreach (string hubUrl in signalRurl)
            {
                if(!String.IsNullOrEmpty(hubUrl))
                    CreateSignalRConnectionandConnect(hubUrl.Trim());
            }
        }

        public void CreateSignalRConnectionandConnect(string hubConnectionURL)
        {
            try
            {
                HubConnection connection;
                IHubProxy proxy;
                connection = new HubConnection(hubConnectionURL);
                connection.Error += Connection_Error;
                connection.ConnectionSlow += Connection_ConnectionSlow;
                connection.Closed += Connection_Closed;
                proxy = connection.CreateHubProxy(hub);
                connection.Start().Wait(new TimeSpan(5000));

                connectionList.Add(new HubNProxy { proxy = proxy, connection = connection });
                Console.WriteLine("SignalR Connection created for " +hubConnectionURL);
            }
            catch(Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when Creating SignalR connection to Hub : {hubConnectionURL + " , " + ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when Creating SignalR connection to Hub: {hubConnectionURL + " , " + ex.Message + ex.StackTrace}");
            }
        }

        public void SendSignalRFeedtohub(EventMessage msg, string Sport)
        {
            try
            {
                string serialised = JsonConvert.SerializeObject(msg.Value);
                string authHash = $"{serialised}{salt}".ToSHA256();
                foreach (HubNProxy hubNProxy in connectionList)
                {

                    //if (counterMessageToSignalR > 20)
                    //{
                    //    Console.WriteLine("Stopping SignalR Connection to " + connection.Url);
                    //    logger.Info("Stopping SignalR Connection to " + connection.Url);
                    //    counterMessageToSignalR = 0;
                    //    connection.Stop(new TimeSpan(1000));
                    //    Console.WriteLine("SignalR Connecting to " + connection.Url);
                    //    logger.Info("SignalR Connecting to " + connection.Url);
                    //    connection.Start().Wait(new TimeSpan(5000));

                    //}
                    if (hubNProxy.connection.State.ToString().ToUpper() == "DISCONNECTED")
                    {
                        Console.WriteLine("Reconnecting to " + hubNProxy.connection.Url);
                        logger.Info("Reconnecting to " + hubNProxy.connection.Url);
                        hubNProxy.connection.Start().Wait(new TimeSpan(5000));
                        //Some times proxy is not getting enabled so it throws error : connection was disconnected before invocation result was received
                        // To avoid error added below wait
                        System.Threading.Thread.Sleep(3000);
                    }

                    string eventID = ((Miomni.Gaming.Relay.Responses.EventStatusResponse)msg.Value).MiomniEventID;
                    string currentPeriod = ((Miomni.Gaming.Relay.Responses.EventStatusResponse)msg.Value).Score.CurrentPeriod;
                    if (currentPeriod.ToUpper() == "ENDED")
                    {
                        // To show the last period score black as it was coming White/Active
                        ((Miomni.Gaming.Relay.Responses.EventStatusResponse)msg.Value).Score.OrdinalPeriod = 0;
                    }

                    if (hubNProxy.connection.State.ToString().ToUpper() == "CONNECTED")
                    {
                        var task = hubNProxy.proxy.Invoke(method, authHash, msg.Value);
                        task.Wait(new TimeSpan(5000));

                        Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " : Message Sent for " + eventID + " with Status:'" + currentPeriod + "' for " + Sport + " to " + hubNProxy.connection.Url);
                        logger.Info("Message Sent for " + eventID + " with Status:'" + currentPeriod + "' for " + Sport + " to " + hubNProxy.connection.Url);
                    }

                    //This logic is only for blocking markets as sometimes EG games are active even after the game finished 
                    if (currentPeriod.ToUpper() == "ENDED")
                    {
                        BlockMarketsAfterGameEnds(eventID, Sport).Wait(new TimeSpan(10000));
                    }

                }
                objFeedsToDisk.WritefeedToDisk(msg);
                //counterMessageToSignalR++;
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when sending SignalR feed to Hub: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when sending SignalR feed to Hub: {ex.Message +  ex.StackTrace}");
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

        private async Task BlockMarketsAfterGameEnds(string eventID, string sport)
        {
            try
            {
                //its just to initiate parallel task for initaiaing async await
                await Task.Factory.StartNew(() => System.Threading.Thread.Sleep(1));

                EventMessage eventMessage = new EventMessage();

                BettableOutcome bettableOutcome = new BettableOutcome
                {
                    MiomniID = "E-" + eventID,
                    Code = null,
                    Odds = new Price
                    {
                        European = 0,
                        Moneyline = 0
                    },
                    Direction = PriceDirections.Unch,
                    Description = "blocked" //GameStatus
                };

                List<BettableOutcome> listBettableOutcome = new List<BettableOutcome>();
                listBettableOutcome.Add(bettableOutcome);
                Proposition proposition = new Proposition
                {
                    Title = null,
                    PropCode = null,
                    MiomniEventID = "E-" + eventID,
                    Outcomes = listBettableOutcome,
                };

                List<Proposition> propositionList = new List<Proposition>();
                propositionList.Add(proposition);
                eventMessage.Value = new EventStatusResponse
                {
                    MiomniEventID = "E-" + eventID,
                    Code = null,
                    Status = ResponseStatus.OpSuccess,
                    Propositions = propositionList,
                    Score = null
                };
                eventMessage.Collected = DateTime.Now;
                eventMessage.Dirty = true;
                eventMessage.Watch = Stopwatch.StartNew();

                string serialised = JsonConvert.SerializeObject(eventMessage.Value);
                string authHash = $"{serialised}{salt}".ToSHA256();

                foreach (HubNProxy hubNProxy in connectionList)
                {
                    if (hubNProxy.connection.State.ToString().ToUpper() == "DISCONNECTED")
                    {
                        Console.WriteLine("Reconnecting to " + hubNProxy.connection.Url);
                        hubNProxy.connection.Start().Wait(new TimeSpan(5000));
                        //Some times proxy is not getting enabled so it throws error : connection was disconnected before invocation result was received
                        // To avoid error added below wait
                        System.Threading.Thread.Sleep(3000);
                    }

                    var task = hubNProxy.proxy.Invoke(method, authHash, eventMessage.Value);
                    task.Wait(new TimeSpan(5000));
                    Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " Message Sent for " + eventID + " for blocking Markets as Game Ended for " + sport + " to " + hubNProxy.connection.Url);
                    logger.Info("Message Sent for " + eventID + " for blocking Markets as Game Ended for " + sport + " to " + hubNProxy.connection.Url);
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"{ex.GetType().Name} thrown when sending SignalR Event Block feed to Hub: {ex.Message}");
                logger.Error(ex, $"{ex.GetType().Name} thrown when sending SignalR Event Block feed to Hub: {ex.Message + ex.StackTrace}");
            }
        }

        public void LogHelpDebug(string funtionName)
        {
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " : Started - " + funtionName);
            logger.Info("Started - " + funtionName);
        }
    }

    class HubNProxy
    {
        public HubConnection connection;
        public IHubProxy proxy;
    }


}
