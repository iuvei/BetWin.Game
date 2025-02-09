﻿using BetWin.Game.Lottery.Base;
using BetWin.Game.Lottery.Models;
using BetWin.Game.Lottery.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace BetWin.Game.Lottery.Collects
{
    /// <summary>
    /// 系统开奖基类
    /// </summary>
    public abstract class SystemProviderBase : CollectProviderBase
    {
        /// <summary>
        /// 杀率，为0是随机开奖，1是包杀
        /// </summary>
        [Description("杀率")]
        public decimal Kill { get; set; }


        protected SystemProviderBase(string setting) : base(setting)
        {
        }

        /// <summary>
        /// 默认赔率
        /// </summary>
        protected abstract Dictionary<string, decimal> DefaultOdds { get; }

        /// <summary>
        /// 根据默认赔率得出随机的号码
        /// </summary>
        /// <returns></returns>
        private string CreateNumber()
        {
            return this.DefaultOdds.ToDictionary(t => t.Key, t => (double)t.Value).GetRandom();
        }

        /// <summary>
        /// 获取号码
        /// </summary>
        /// <param name="orderResult">当前期的下注数据</param>
        /// <returns></returns>
        protected virtual string CreateNumber(BetOrderResult? orderResult, out List<string> logs)
        {
            logs = new List<string>();
            if (this.Kill == 0 || orderResult == null || !orderResult.Value)
            {
                if (this.Kill == 0)
                {
                    logs.Add($"当前配置随机开奖");
                }
                else
                {
                    logs.Add($"当前期没有注单，随机开奖");
                }
                return this.CreateNumber();
            }

            // 开奖号码的存储器
            Dictionary<string, decimal> results = new Dictionary<string, decimal>();

            Dictionary<string, ILotteryPlay> plays = new Dictionary<string, ILotteryPlay>();

            // 试算10次
            for (int i = 0; i < 10; i++)
            {
                string number = this.CreateNumber();
                if (results.ContainsKey(number)) continue;

                decimal profit = decimal.Zero;
                foreach (var order in orderResult.Value.Orders)
                {
                    ILotteryPlay play;
                    if (!plays.ContainsKey(order.Play))
                    {
                        play = LotteryFactory.GetPlay(this.Type, order.Play);
                        plays.Add(order.Play, play);
                    }
                    else
                    {
                        play = plays[order.Play];
                    }

                    profit += (play.GetReward(order.BetContent, number, this.DefaultOdds) - order.BetMoney);
                }

                // 试算本号码的整体杀率
                results.Add(number, (orderResult.Value.Money + profit) / orderResult.Value.BetMoney);
            }

            logs.Add(JsonConvert.SerializeObject(results));

            //#1 找出最接近预设杀率的号码
            string resultNumber = results.OrderBy(t => t.Value + this.Kill).FirstOrDefault().Key;

            logs.Add($"选中号码:{resultNumber}");

            return resultNumber;
        }
    }
}
