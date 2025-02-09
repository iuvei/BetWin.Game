﻿using BetWin.Game.API.Enums;
using BetWin.Game.API.Models;
using BetWin.Game.API.Requests;
using BetWin.Game.API.Responses;
using BetWin.Game.API.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using SP.StudioCore.Net.Http;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using System.Linq;
using System.Web;

namespace BetWin.Game.API.Providers
{
    [Description("雷火电竞")]
    public sealed class TFESport : GameBase
    {
        [Description("gateway")]
        public string gateway { get; set; } = "";

        [Description("partner_id")]
        public string partner_id { get; set; } = "";

        [Description("public_token")]
        public string public_token { get; set; } = "";

        [Description("private_token")]
        public string private_token { get; set; } = "";

        /// <summary>
        /// 获取游戏开启地址
        /// </summary>
        [Description("游戏地址")]
        public string launch { get; set; }

        public override int CollectDelay => 10 * 1000;

        public TFESport(string jsonString) : base(jsonString)
        {
        }

        public override Dictionary<GameLanguage, string> Languages => new Dictionary<GameLanguage, string>();

        public override Dictionary<GameCurrency, string> Currencies => new Dictionary<GameCurrency, string>() {
            { GameCurrency.CNY,"RMB"}
        };

        public override BalanceResponse Balance(BalanceModel request)
        {
            balance balance = this.Post<balance>(APIMethod.Balance, $"/api/v2/balance/?LoginName={request.PlayerName}", null, out GameResultCode code, out string message);
            return new BalanceResponse(code)
            {
                Balance = balance?.results?.FirstOrDefault()?.balance ?? 0,
            };
        }

        public override CheckTransferResponse CheckTransfer(CheckTransferModel request)
        {
            checkTransfer checkTransfer = this.Post<checkTransfer>(APIMethod.CheckTransfer, $"/api/v2/transfer-status/?reference_no={request.OrderID}", null, out GameResultCode code, out string message);
            checkTransferResult? checkTransferResult = checkTransfer?.results?.FirstOrDefault();

            CheckTransferResponse result = new CheckTransferResponse(code)
            {
                TransferID = checkTransferResult?.partner_reference_no ?? string.Empty,
                Money = checkTransferResult?.amount ?? 0,
                Status = GameAPITransferStatus.Unknow
            };
            if (checkTransferResult?.status == "success")
            {
                result.Status = GameAPITransferStatus.Success;
            }
            else if (code != GameResultCode.Exception)
            {
                result.Status = GameAPITransferStatus.Faild;
            }
            return result;
        }

        public override OrderResult GetOrder(QueryOrderModel request)
        {
            DateTime beginTime = DateTime.Now.AddDays(-7);
            DateTime startTime = WebAgent.GetTimestamps(request.StartTime);
            if (startTime < beginTime) startTime = beginTime;
            DateTime endTime = startTime.AddMinutes(30);
            DateTime now = DateTime.Now.AddMinutes(-2);

            if (endTime > now) endTime = now;
            if (startTime > endTime) startTime = endTime.AddMinutes(-5);

            int page_size = 1000;
            string from_modified_datetime = HttpUtility.UrlEncode(startTime.ToString("yyyy-MM-ddTHH:mm:ss+08:00"));
            string to_modified_datetime = HttpUtility.UrlEncode(endTime.ToString("yyyy-MM-ddTHH:mm:ss+08:00"));

            string path = $"/api/v2/bet-transaction/?page_size={page_size}&from_modified_datetime={from_modified_datetime}&to_modified_datetime={to_modified_datetime}";
            OrderResult orderResult = new OrderResult(GameResultCode.Success)
            {
                data = new List<OrderData>(),
                startTime = WebAgent.GetTimestamps(startTime),
                endTime = WebAgent.GetTimestamps(endTime)
            };
            order order = this.Post<order>(APIMethod.GetOrder, path, null, out GameResultCode code, out string message);
            Dictionary<string, GameCurrency> currency = this.Currencies.ToDictionary(t => t.Value, t => t.Key);
            foreach (var item in order.results)
            {
                GameAPIOrderStatus status = GameAPIOrderStatus.Wait;
                decimal betMoney = item.amount;
                decimal money = 0;
                switch (item.settlement_status)
                {
                    case "confirmed":
                        status = GameAPIOrderStatus.Wait;
                        break;
                    case "cancelled":
                        status = GameAPIOrderStatus.Return;
                        break;
                    case "settled":
                        switch (item.result_status)
                        {
                            case "WIN":
                                status = GameAPIOrderStatus.Win;
                                if (item.earnings != null) money = item.earnings.Value - betMoney;
                                break;
                            case "LOSS":
                                status = GameAPIOrderStatus.Lose;
                                if (item.earnings != null) money = item.earnings.Value;
                                break;
                            case "DRAW":
                            case "CANCELLED":
                                status = GameAPIOrderStatus.Return;
                                break;
                        }
                        break;
                }
                decimal betAmount = Math.Min(betMoney, Math.Abs(money));

                OrderData orderData = new OrderData()
                {
                    orderId = item.id,
                    playerName = item.member_code ?? string.Empty,
                    currency = currency.Get(item.currency),
                    createTime = WebAgent.GetTimestamps(item.date_created, TimeSpan.FromHours(0)),
                    settleTime = item.settlement_datetime == null ? 0 : WebAgent.GetTimestamps(item.settlement_datetime.Value, TimeSpan.FromHours(0)),
                    gameCode = item.game_type_id ?? string.Empty,

                    status = status,
                    betMoney = item.amount,
                    betAmount = betAmount,
                    money = money,

                    rawData = item.ToJson(),
                    hash = string.Concat(item.id, ":", WebAgent.GetTimestamps(item.modified_datetime))
                };

                if (item.is_combo)
                {
                    if (item.tickets != null)
                    {
                        foreach (var result in item.tickets)
                        {
                            //this.GameHandler?.SaveGameCode(this.Type, result.game_type_id, Language.ENG, result.game_type_name);
                        }
                    }
                    // 如果是小游戏
                    if (item.tickets?.Length == 1)
                    {
                        orderData.gameCode = item.tickets?[0].game_type_id ?? string.Empty;
                    }
                    else
                    {
                        orderData.gameCode = $"{item.tickets?.Length}x1";
                    }
                }
                else
                {
                    //this.GameHandler?.SaveGameCode(this.Type, item.game_type_id, Language.ENG, item.game_type_name);
                }

                orderResult.data.Add(orderData);
            }
            return orderResult;
        }

        public override LoginResponse Login(LoginModel request)
        {
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                {"member_code",request.PlayerName }
            };
            login login = this.Post<login>(APIMethod.Login, "/api/v2/member-login/", data, out GameResultCode code, out string message);
            return new LoginResponse(code)
            {
                Url = login?.launch_url,
                Message = message,
                Method = LoginMethod.Redirect
            };
        }

        public override LogoutResponse Logout(LogoutModel request)
        {
            throw new NotImplementedException();
        }

        public override RegisterResponse Register(RegisterModel request)
        {
            for (int index = 0; index < 5; index++)
            {
                string playerName = this.CreateUserName(request.Prefix, request.UserName, 16, index);
                Dictionary<string, object> data = new Dictionary<string, object>
                {
                    { "member_code",playerName }
                };
                register register = this.Post<register>(APIMethod.Register, "/api/v2/members/", data, out GameResultCode code, out string message);
                if (code == GameResultCode.DuplicatePlayerName) continue;
                return new RegisterResponse(code)
                {
                    Message = message,
                    PlayerName = playerName,
                    Password = string.Empty
                };
            }
            return new RegisterResponse(GameResultCode.DuplicatePlayerName);

        }

        public override TransferResponse Transfer(TransferModel request)
        {
            string transferId = request.OrderID;
            string action = request.Money > 0 ? "deposit" : "withdraw";
            Dictionary<string, object> data = new Dictionary<string, object>()
            {
                {"member",request.PlayerName },
                {"operator_id",this.partner_id },
                {"amount",Math.Abs(request.Money) },
                {"reference_no",transferId }
            };
            transfer transfer = this.Post<transfer>(APIMethod.Transfer, $"/api/v2/{action}/", data, out GameResultCode code, out string message);
            return new TransferResponse(code)
            {
                Money = transfer?.amount ?? 0,
                OrderID = request.OrderID,
                PlayerName = transfer?.member ?? request.PlayerName,
                TransferID = transfer?.reference_no ?? string.Empty,
                Balance = transfer?.balance_amount,
                Status = code switch
                {
                    GameResultCode.Success => GameAPITransferStatus.Success,
                    GameResultCode.Exception => GameAPITransferStatus.Unknow,
                    _ => GameAPITransferStatus.Faild
                }
            };
        }

        protected override GameResultCode GetResultCode(string result, out string message)
        {
            responseBase? response = JsonConvert.DeserializeObject<responseBase>(result);
            if (response == null)
            {
                message = result;
                return GameResultCode.Exception;
            }
            message = result;
            return response.code switch
            {
                1 => GameResultCode.DuplicatePlayerName,
                2 => GameResultCode.SystemBuzy,
                3 => GameResultCode.NoPlayer,
                4 => GameResultCode.MoneyInvalid,
                5 => GameResultCode.NoBalance,
                6 => GameResultCode.ParameterInvalid,
                7 => GameResultCode.ParameterInvalid,
                8 => GameResultCode.CurrencyInvalid,
                0 => GameResultCode.Success,
                _ => GameResultCode.Error
            };
        }

        private T Post<T>(APIMethod method, string url, Dictionary<string, object>? data, out GameResultCode gameCode, out string message)
        {
            url = url.StartsWith("http") ? url : $"{this.gateway}{url}";
            GameResponse response = this.Request(new GameRequest()
            {
                Method = method,
                Data = data == null ? string.Empty : data.ToJson(),
                Url = url
            });
            gameCode = response.Code;
            message = response.Message;
            return JsonConvert.DeserializeObject<T>(response);
        }

        internal override HttpResult RequestAPI(GameRequest request)
        {
            using (HttpClient client = new HttpClient())
            {
                if (string.IsNullOrEmpty(request.Data))
                {
                    return client.Get(request.Url, new Dictionary<string, string>()
                    {
                        {"Authorization",$"Token {this.private_token}" }
                    });
                }
                else
                {
                    return client.Post(request.Url, request.Data, new Dictionary<string, string>()
                    {
                        {"Authorization",$"Token {this.private_token}" },
                        {"Content-Type","application/json" }
                    });
                }
            }
        }


        #region ========  实体类  ========

        class responseBase
        {
            public int code { get; set; }
        }

        /// <summary>
        /// 登录地址
        /// </summary>
        class login : responseBase
        {
            public string launch_url { get; set; }
        }

        class register : responseBase
        {
            /// <summary>
            /// 用户名
            /// </summary>
            public string member_code { get; set; }
        }

        class balance : responseBase
        {
            public int count { get; set; }

            public balanceResult[] results { get; set; }
        }

        class balanceResult : responseBase
        {
            public int id { get; set; }

            public string member_id { get; set; }

            public string currency { get; set; }

            public decimal balance { get; set; }
        }

        class transfer : responseBase
        {
            public string member { get; set; }

            public string operator_id { get; set; }

            public decimal amount { get; set; }

            public string reference_no { get; set; }

            public string currency { get; set; }

            public string transaction_type { get; set; }

            public decimal balance_amount { get; set; }
        }

        class checkTransfer
        {
            public int count { get; set; }

            public checkTransferResult[] results { get; set; }
        }

        class checkTransferResult
        {
            public int id { get; set; }

            public string reference_no { get; set; }

            public string partner_reference_no { get; set; }

            public string operator_id { get; set; }

            public string member_code { get; set; }

            /// <summary>
            ///  IN / OUT
            /// </summary>
            public string transaction_type { get; set; }

            public DateTime transaction_date { get; set; }

            public decimal amount { get; set; }

            public string status { get; set; }

            public decimal balance { get; set; }
        }

        class callback
        {
            public string token { get; set; }
        }

        class order
        {
            public int count { get; set; }

            public orderResult[] results { get; set; }
        }

        class orderResult
        {

            //"id": "CAUAG14392E4357NJX8303",
            //"bet_selection": "home",
            //"odds": 0.51,
            //"currency": "RMB",
            //"amount": 10,
            //"game_type_name": "CS:GO",
            //"game_market_name": "WINNER",
            //"market_option": "match",
            //"map_num": null,
            //"bet_type_name": "WIN",
            //"competition_name": "CS:GO_ProHouse Wuxi Villa Cup",
            //"event_id": 30204,
            //"event_name": "TyLoo vs Let's Quit",
            //"event_datetime": "2020-08-06T09:00:00Z",
            //"date_created": "2020-08-06T04:43:57.011527Z",
            //"settlement_datetime": null,
            //"modified_datetime": "2020-08-06T04:43:57.147317Z",
            //"settlement_status": "confirmed",
            //"result": null,
            //"result_status": null,
            //"earnings": null,
            //"handicap": null,
            //"is_combo": false,
            //"member_code": "test22",
            //"is_unsettled": false,
            //"ticket_type": "db",
            //"malay_odds": 0.51,
            //"euro_odds": 1.51,
            //"member_odds": 1.51,
            //"member_odds_style": "euro",
            //"game_type_id": 1,
            //"request_source": "desktop-browser"

            /// <summary>
            /// 订单号
            /// </summary>
            public string id { get; set; }

            /// <summary>
            /// 下注选项
            /// </summary>
            public string bet_selection { get; set; }

            /// <summary>
            /// 赔率
            /// </summary>
            public decimal odds { get; set; }

            /// <summary>
            /// 货币
            /// </summary>
            public string currency { get; set; }

            /// <summary>
            /// 下注金额	
            /// </summary>
            public decimal amount { get; set; }

            /// <summary>
            /// 游戏名称
            /// </summary>
            public string game_type_name { get; set; }

            /// <summary>
            /// 盘口名称
            /// </summary>
            public string game_market_name { get; set; }

            /// <summary>
            /// 盘口局分
            /// match = 总局
            /// map = 局
            /// </summary>
            public string market_option { get; set; }

            /// <summary>
            /// 第几局
            /// MAP 1 = 第一局
            /// Q1 = 第一节
            /// FIRST HALF - 上半场
            /// SECOND HALF - 下半场
            /// </summary>
            public string map_num { get; set; }

            /// <summary>
            /// 盘口类型
            /// WIN = 主盘口独赢 (下注选项: home/away)
            /// /// 1X2 = 独赢(下注选项: home/draw/away)
            /// /// AH = 让分局(下注选项: home/away)
            /// OU = 大小(下注选项: over/under)
            /// OE = 单双(下注选项: odd/even)
            /// SPWINMAP = 局独赢(下注选项: home/away)
            /// WINMAP = 局独赢比分(下注选项: home/away)
            /// SPHA = 特别主客(下注选项: home/away)
            /// SPYN = 特别是否(下注选项: yes/no)
            /// SPOE = 特别单双(下注选项: odd/even)
            /// SPOU = 特别大小(下注选项: over/under)
            /// SP1X2 = 特别1X2(下注选项: home/draw/away)
            /// OR = 冠军盘(下注选项: 队伍名字)
            /// SPOR = 特别多项(下注选项: 自定)
            /// SPXX = 特别双项(下注选项: 自定)
            /// </summary>
            public string bet_type_name { get; set; }

            /// <summary>
            /// competition_name
            /// </summary>
            public string competition_name { get; set; }

            /// <summary>
            /// 赛事ID
            /// </summary>
            public string event_id { get; set; }

            /// <summary>
            /// 赛事名称	
            /// </summary>
            public string event_name { get; set; }

            /// <summary>
            /// 赛事开始时间	
            /// </summary>
            public string event_datetime { get; set; }

            /// <summary>
            /// 下注时间
            /// </summary>
            public DateTime date_created { get; set; }

            /// <summary>
            /// 结算时间	
            /// </summary>
            public DateTime? settlement_datetime { get; set; }

            /// <summary>
            /// 更改时间	
            /// </summary>
            public DateTime modified_datetime { get; set; }

            /// <summary>
            /// 注单状况
            /// confirmed = 确定
            /// settled = 结算
            /// cancelled = 取消
            /// </summary>
            public string settlement_status { get; set; }

            /// <summary>
            /// 盘口结果
            /// </summary>
            public string result { get; set; }

            /// <summary>
            /// 注单结果
            /// WIN = 赢
            /// LOSS = 输
            /// DRAW = 和
            /// CANCELLED = 取消
            /// </summary>
            public string? result_status { get; set; }

            /// <summary>
            /// 输赢额
            /// </summary>
            public decimal? earnings { get; set; }

            /// <summary>
            /// 让分数
            /// </summary>
            public string? handicap { get; set; }

            /// <summary>
            /// 是否连串
            /// </summary>
            public bool is_combo { get; set; }

            /// <summary>
            /// 会员号
            /// </summary>
            public string? member_code { get; set; }

            /// <summary>
            /// 已重新结算
            /// </summary>
            public bool is_unsettled { get; set; }

            /// <summary>
            /// 注单下注状况
            /// db = 早盘
            /// live = 滚球
            /// </summary>
            public string? ticket_type { get; set; }

            /// <summary>
            /// 马来赔率
            /// 备注：如果是连串，赔率会是null
            /// </summary>
            public decimal? malay_odds { get; set; }

            /// <summary>
            /// 欧盘赔率
            /// </summary>
            public decimal euro_odds { get; set; }

            /// <summary>
            /// 会员下的赔率
            /// </summary>
            public decimal member_odds { get; set; }

            /// <summary>
            /// 会员下的盘
            /// euro    hongkong    indo    malay
            /// </summary>
            public string? member_odds_style { get; set; }

            /// <summary>
            /// 游戏ID
            /// </summary>
            public string? game_type_id { get; set; }

            /// <summary>
            /// 下注的管道
            /// desktop-browser = 电脑浏览器
            /// mobile-browser = 手机浏览器(包括嵌入在APP里)
            /// mobile-app = 手机APP
            /// unknown = 未知
            /// null = 旧数据没有
            /// </summary>
            public string? request_source { get; set; }

            /// <summary>
            /// 只有在is_combo=true的情况才会有。里面的格式是跟以上一样
            /// </summary>
            public orderResult[]? tickets { get; set; }
        }


        #endregion
    }
}
