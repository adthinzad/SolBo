﻿using SolBo.Shared.Domain.Configs;
using SolBo.Shared.Domain.Enums;
using SolBo.Shared.Domain.Statics;
using SolBo.Shared.Extensions;
using SolBo.Shared.Messages.Rules;
using SolBo.Shared.Services;
using SolBo.Shared.Services.Responses;

namespace SolBo.Shared.Rules.Mode
{
    public class StopLossStepMarketRule : IMarketRule
    {
        public MarketOrderType MarketOrder => MarketOrderType.STOPLOSS;
        private readonly IMarketService _marketService;
        public StopLossStepMarketRule(IMarketService marketService)
        {
            _marketService = marketService;
        }
        public IRuleResult RuleExecuted(Solbot solbot)
        {
            var boughtPrice = solbot.BoughtPrice();

            var result = new MarketResponse();

            if (boughtPrice > 0)
            {
                result = _marketService.IsStopLossReached(
                    solbot.Strategy.AvailableStrategy.CommissionType,
                    solbot.Strategy.AvailableStrategy.StopLossDown,
                    boughtPrice,
                    solbot.Communication.Price.Current);
            }
            else
            {
                result.IsReadyForMarket = false;
                result.Changed = 0;
            }

            solbot.Communication.StopLoss = new ChangeMessage
            {
                Change = result.Changed,
                PriceReached = result.IsReadyForMarket
            };

            var change = solbot.StopLossChange();
            var needed = solbot.NeededStopLossChange();

            var priceDown = (solbot.Strategy.AvailableStrategy.CommissionType == CommissionType.PERCENTAGE && result.Changed > 0)
                || solbot.Strategy.AvailableStrategy.CommissionType == CommissionType.VALUE && result.Changed < 0;

            return new MarketRuleResult()
            {
                Success = result.IsReadyForMarket,
                Message = priceDown
                    ? LogGenerator.StopLossStepSuccess(
                        solbot.Strategy.AvailableStrategy.SellType,
                        solbot.Communication.Price.Current,
                        boughtPrice,
                        change)
                    : LogGenerator.StopLossStepError(
                        solbot.Strategy.AvailableStrategy.SellType,
                        solbot.Communication.Price.Current,
                        boughtPrice,
                        change,
                        needed)
            };
        }
    }
}