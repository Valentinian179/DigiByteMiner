﻿using DigibyteMiner.Core;
using DigibyteMiner.Core.Interfaces;
using DigibyteMiner.Model;
using DigibyteMiner.Model.Config;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DigibyteMiner.Coins.Skein
{
    /// <summary>
    /// Resources::
    /// https://www.cryptocurrencyfreak.com/2017/08/13/monero-gpu-mining-ccminer-2-2/
    /// http://cryptomining-blog.com/3987-new-ccminer-1-5-fork-by-tpruvot-with-websocket-api-example-available/
    /// </summary>
    class CCMiner : MinerProgramBase
    {

        public const string STATSLINK2 = "127.0.0.1";
        public const string STATSLINK3 = "";
        public override string MINERURL
        {
            get
            {
                return "https://github.com/arunsatyarth/MinerRepos/releases/download/v0.2/ccminer-alexis.zip";
            }
        }
        public override string EXENAME
        {
            get
            {
                return "ccminer.exe";
            }
        }
        public override string PROCESSNAME
        {
            get
            {
                return "ccminer";
            }
        }
        public override string STATS_LINK
        {
            get
            {
                return  STATSLINK2 ;
            }
        }
        public override string STATS_LINK_HTML
        {
            get
            {
                return "http://" + STATSLINK2;
            }
        }
        public override string STATS_LINK_PORT
        {
            get
            {
                return "4068";
            }
        }


        public override string Script { get; set; }


        public override string Type { get; set; }//claymore ccminer etc

        public override IOutputReader OutputReader { get; set; }


        public CCMiner(ICoin mainCoin, bool dualMining, ICoin dualCoin, string minerName, IMiner miner) :
            base(mainCoin, dualMining, dualCoin, minerName, miner)
        {
            Type = "Nvidia";
            GPUType = CardMake.Nvidia;
            OutputReader = new CCReader(STATS_LINK, STATS_LINK_PORT);
        }

        public override string GenerateScript()
        {
            try
            {

                string command = EXENAME + " --algo=skein -o " + MainCoinConfigurer.Pool;
                command += " -u " + MainCoinConfigurer.Wallet;
                string pwd = MainCoinConfigurer.Password.Trim();
                if (pwd.Length == 0)
                    pwd = " x ";
                command += " -p " + pwd;

                command += " --api-bind " + STATS_LINK+":"+STATS_LINK_PORT;


                Script = SCRIPT1 + command;
                AutomaticScriptGeneration = true;
                SaveScriptToDB();
                return Script;
            }
            catch (Exception e)
            {
                return "";
            }
        }


        private const string SCRIPT1 = "";


        /// <summary>
        /// reads data for claymore miner
        /// </summary>
        public class CCReader : OutputReaderBase
        {
            public CCReader(string link, string port)
                : base(link,port)
            {
            }
            CCMinerData GetResultsSection(string innerText)
            {
                try
                {
                    CCMinerData minerResult = (CCMinerData)new JavaScriptSerializer().Deserialize(innerText, typeof(CCMinerData));
                    return minerResult;
                }
                catch (Exception e)
                {
                }
                return null;
            }
           
            public override  void Read()
            {
                try
                {
                    CCMinerCommandOutputs output = new CCMinerCommandOutputs();
                    string result = "";
                    TcpReaderUtil util = new TcpReaderUtil(StatsLink, StatsPort);
                    result = util.GetData("summary");
                    output.Summary = result;

                    result = util.GetData("threads");
                    output.Threads = result;

                    string str = new JavaScriptSerializer().Serialize(output);


                    NextLog = str;
                }
                catch (Exception e)
                {
                    ReadWithBrowser();
                    throw;
                }

            }
            public override void Parse()
            {
                CCMinerData ewbfData = GetResultsSection(LastLog);
                if (ewbfData.Parse(new CcMinerResultParser(LastLog, ReReadGpuNames)))
                {
                    MinerResult = ewbfData.MinerDataResult;
                }
                ReReadGpuNames = false;
            }

            public class CcMinerResultParser : IMinerResultParser
            {
                MinerDataResult m_MinerResult = null;
                CCMinerData m_EwbfData = null;
                public bool Succeeded { get; set; }//if parsing succeeded without errors
                static Hashtable m_Gpus = new Hashtable();// we only need t read gpu info once as it dosent change with more logs comining in

                string m_fullLog = "";
                public CcMinerResultParser(string fullLog, bool reReadGpunames)
                {
                    m_fullLog = fullLog;
                }

                public bool Parse(object obj)
                {
                    Succeeded = false;

                    m_EwbfData = obj as CCMinerData;
                    try
                    {
                        if (m_EwbfData == null)
                            return false;

                        ComputeGPUData();
                        return true;
                    }
                    catch (Exception e)
                    {
                        Succeeded = false;
                    }
                    return false;

                }

                public void ComputeGPUData()
                {
                    try
                    {
                        m_MinerResult = new MinerDataResult();
                        m_MinerResult.GPUs = new List<GpuData>();

                        int totalHashrate = 0, totalShares = 0, rejected = 0;
                        foreach (Result item in m_EwbfData.result)
                        {
                            GpuData gpu = new GpuData(item.name);
                            gpu.IdentifyMake();

                            gpu.Hashrate = item.speed_sps.ToString();
                            gpu.Temperature = item.temperature + "C";
                            m_MinerResult.GPUs.Add(gpu);
                            totalHashrate += item.speed_sps;
                            totalShares += item.accepted_shares;
                            rejected += item.rejected_shares;
                        }

                        m_MinerResult.TotalHashrate = totalHashrate;
                        m_MinerResult.TotalShares = totalShares;
                        m_MinerResult.Rejected = rejected;

                        m_EwbfData.MinerDataResult = m_MinerResult;

                    }
                    catch (Exception)
                    {
                        Succeeded = false;
                        throw;
                    }
                }
            }
        }
        public class Result
        {
            public int gpuid { get; set; }
            public int cudaid { get; set; }
            public string busid { get; set; }
            public string name { get; set; }
            public int gpu_status { get; set; }
            public int solver { get; set; }
            public int temperature { get; set; }
            public int gpu_power_usage { get; set; }
            public int speed_sps { get; set; }
            public int accepted_shares { get; set; }
            public int rejected_shares { get; set; }
            public int start_time { get; set; }
        }

        public class CCMinerData
        {
            public MinerDataResult MinerDataResult { get; set; }
            public string method { get; set; }
            public object error { get; set; }
            public int start_time { get; set; }
            public string current_server { get; set; }
            public int available_servers { get; set; }
            public int server_status { get; set; }
            public List<Result> result { get; set; }
            public bool Parse(IMinerResultParser parser)
            {
                return parser.Parse(this);
            }
        }
        public class CCMinerCommandOutputs
        {
            public string Summary { get; set; }
            public string Threads { get; set; }


        }

    }

}
