﻿#region License
// 
//     CoiniumServ - Crypto Currency Mining Pool Server Software
//     Copyright (C) 2013 - 2014, CoiniumServ Project - http://www.coinium.org
//     http://www.coiniumserv.com - https://github.com/CoiniumServ/CoiniumServ
// 
//     This software is dual-licensed: you can redistribute it and/or modify
//     it under the terms of the GNU General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
// 
//     This program is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU General Public License for more details.
//    
//     For the terms of this license, see licenses/gpl_v3.txt.
// 
//     Alternatively, you can license this software under a commercial
//     license or white-label it as set out in licenses/commercial.txt.
// 
#endregion
using System;
using System.Text;
using AustinHarris.JsonRpc;
using Coinium.Mining.Miners;
using Coinium.Mining.Pools;
using Coinium.Net.Server.Sockets;
using Coinium.Server.Mining.Stratum.Errors;
using Coinium.Server.Mining.Stratum.Notifications;
using Coinium.Server.Mining.Stratum.Service;
using Coinium.Utils.Buffers;
using Coinium.Utils.Extensions;
using Newtonsoft.Json;
using Serilog;

namespace Coinium.Server.Mining.Stratum
{
    public class StratumMiner : IClient, IStratumMiner
    {
        /// <summary>
        /// Miner's connection.
        /// </summary>
        public IConnection Connection { get; private set; }

        /// <summary>
        /// Unique subscription id for identifying the miner.
        /// </summary>
        public int Id { get; private set; }

        /// <summary>
        /// Username of the miner.
        /// </summary>
        public string Username { get; private set; }

        /// <summary>
        /// Is the miner subscribed?
        /// </summary>
        public bool Subscribed { get; private set; }

        /// <summary>
        /// Is the miner authenticated?
        /// </summary>
        public bool Authenticated { get; set; }

        public IPool Pool { get; private set; }

        public float Difficulty { get; set; }

        /// <summary>
        /// Sends a new mining job to the miner.
        /// </summary>
        public bool SupportsJobNotifications { get; private set; }

        /// <summary>
        /// Hex-encoded, per-connection unique string which will be used for coinbase serialization later. (http://mining.bitcoin.cz/stratum-mining)
        /// </summary>
        public uint ExtraNonce { get; private set; }

        public int LastVardiffTimestamp { get; set; }
        public int LastVardiffRetarget { get; set; }
        public IRingBuffer VardiffBuffer { get; set; }

        private readonly IMinerManager _minerManager;

        /// <summary>
        /// Creates a new miner instance.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="extraNonce"></param>
        /// <param name="connection"></param>
        /// <param name="pool"></param>
        /// <param name="minerManager"></param>
        public StratumMiner(int id, UInt32 extraNonce, IConnection connection, IPool pool, IMinerManager minerManager)
        {
            Id = id; // the id of the miner.
            ExtraNonce = extraNonce;
            Connection = connection; // the underlying connection.
            _minerManager = minerManager;
            Pool = pool;
            Difficulty = 16; // set miner difficulty.

            Subscribed = false; // miner has to subscribe.
            Authenticated = false; // miner has to authenticate.
            SupportsJobNotifications = true; // stratum miner'ssupports new mining job notifications.
        }

        /// <summary>
        /// Subscribes the miner to mining service.
        /// </summary>
        public void Subscribe()
        {
            Subscribed = true;
        }

        /// <summary>
        /// Authenticates the miner.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public bool Authenticate(string user, string password)
        {
            Username = user;
            _minerManager.Authenticate(this);

            if(!Authenticated)
                JsonRpcContext.SetException(new AuthenticationError(Username));

            return Authenticated;
        }

        /// <summary>
        /// Parses the incoming data.
        /// </summary>
        /// <param name="e"></param>
        public void Parse(ConnectionDataEventArgs e)
        {
            Log.ForContext<StratumMiner>().Verbose("Recv:\n{0}", e.Data.ToEncodedString().PrettifyJson());

            var rpcResultHandler = new AsyncCallback(
                callback =>
                {
                    var asyncData = ((JsonRpcStateAsync)callback);
                    var result = asyncData.Result + "\n"; // quick hack.
                    var response = Encoding.UTF8.GetBytes(result);

                    var context = (SocketServiceContext) asyncData.AsyncState;

                    var miner = (StratumMiner)context.Miner;
                    miner.Connection.Send(response);

                    Log.ForContext<StratumMiner>().Verbose("Reply:\n{0}", result.PrettifyJson());
                });

            var line = e.Data.ToEncodedString();
            line = line.Replace("\n", ""); // quick hack!

            var rpcRequest = new SocketServiceRequest(line);
            var rpcContext = new SocketServiceContext(this, rpcRequest);

            var async = new JsonRpcStateAsync(rpcResultHandler, rpcContext) { JsonRpc = line };
            JsonRpcProcessor.Process(async, rpcContext);
        }

        /// <summary>
        /// Sends difficulty to the miner.
        /// </summary>
        public void SendDifficulty()
        {
            var notification = new JsonRequest
            {
                Id = null,
                Method = "mining.set_difficulty",
                Params = new Difficulty(Difficulty)
            };

            var json = JsonConvert.SerializeObject(notification) + "\n";

            var data = Encoding.UTF8.GetBytes(json);
            Connection.Send(data);

            Log.ForContext<StratumMiner>().Debug("Difficulty updated to {0} for miner: {1:l}", Difficulty, Username);
            Log.ForContext<StratumMiner>().Verbose("Send:\n{0}", data.ToEncodedString().PrettifyJson());
        }

        /// <summary>
        /// Sends a mining-job to miner.
        /// </summary>
        public void SendJob(IJob job)
        {
            var notification = new JsonRequest
            {
                Id = null,
                Method = "mining.notify",
                Params = job
            };

            var json = JsonConvert.SerializeObject(notification) + "\n";

            var data = Encoding.UTF8.GetBytes(json);
            Connection.Send(data);

            Log.ForContext<StratumMiner>().Verbose("Send:\n{0}", data.ToEncodedString().PrettifyJson());
        }
    }
}
