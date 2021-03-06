﻿using Binance.Net.Interfaces;
using NLog;
using Quartz;
using SolBo.Shared.Domain.Statics;
using SolBo.Shared.Rules;
using SolBo.Shared.Rules.Mode;
using SolBo.Shared.Rules.Sequence;
using SolBo.Shared.Rules.Validation;
using SolBo.Shared.Rules.Validation.Generated;
using SolBo.Shared.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SolBo.Agent.Jobs
{
    [DisallowConcurrentExecution]
    public class BuyDeepSellHighJob : IJob
    {
        private static readonly Logger Logger = LogManager.GetLogger("SOLBO");

        private readonly IBinanceClient _binanceClient;

        private readonly IStorageService _storageService;
        private readonly IMarketService _marketService;
        private readonly IConfigurationService _schedulerService;
        private readonly IPushOverNotificationService _pushOverNotificationService;
        private readonly ITickerPriceService _tickerPriceService;

        private readonly ICollection<IRule> _rules = new HashSet<IRule>();

        public BuyDeepSellHighJob(
            IBinanceClient binanceClient,
            IStorageService storageService,
            IMarketService marketService,
            IConfigurationService schedulerService,
            IPushOverNotificationService pushOverNotificationService,
            ITickerPriceService tickerPriceService)
        {
            _binanceClient = binanceClient;
            _storageService = storageService;
            _marketService = marketService;
            _schedulerService = schedulerService;
            _pushOverNotificationService = pushOverNotificationService;
            _tickerPriceService = tickerPriceService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var botVersion = context.JobDetail.JobDataMap["Version"] as string;
            try
            {
                var configFileName = context.JobDetail.JobDataMap["FileName"] as string;

                var readConfig = await _schedulerService.GetConfigAsync(configFileName);

                if (readConfig.ReadSucces)
                {
                    var solbot = readConfig.SolBotConfig;

                    _rules.Add(new SendNotificationRule(_pushOverNotificationService, context.PreviousFireTimeUtc));

                    _rules.Add(new StrategyValidationRule());
                    _rules.Add(new ModeTypeValidationRule());
                    _rules.Add(new AverageValidationRule());
                    _rules.Add(new AverageTypeValidationRule());
                    _rules.Add(new SellTypeValidationRule());
                    _rules.Add(new BuyStepValidationRule());
                    _rules.Add(new SellStepValidationRule());
                    _rules.Add(new CommissionTypeValidationRule());
                    _rules.Add(new StopLossStepValidationRule());
                    _rules.Add(new StopLossPauseCyclesValidationRules());
                    _rules.Add(new FundStepValidationRule());
                    _rules.Add(new BoughtValidationRule());
                    _rules.Add(new StopLossCurrentCycleValidationRule());

                    _rules.Add(new SetStorageSequenceRule(_storageService));

                    _rules.Add(new ClearOnStartupSequenceRule(_storageService, context.PreviousFireTimeUtc));
                    _rules.Add(new SymbolSequenceRule(_binanceClient));
                    _rules.Add(new GetPriceSequenceRule(_tickerPriceService));
                    _rules.Add(new SavePriceSequenceRule(_storageService));
                    _rules.Add(new AverageTypeSequenceRule());
                    _rules.Add(new CalculateAverageSequenceRule(_storageService, _marketService));

                    _rules.Add(new ModeTypeSequenceRule());
                    _rules.Add(new PumpStopLossCycleSequenceRule());

                    if (solbot.Exchange.IsInTestMode)
                        _rules.Add(new ModeTestRule(_marketService, _pushOverNotificationService));
                    else
                    {
                        _rules.Add(new ApiCredentialsValidationRule());
                        _rules.Add(new ModeProductionRule(_marketService, _pushOverNotificationService));
                    }

                    foreach (var item in _rules)
                    {
                        var result = item.RuleExecuted(solbot);

                        if (result.Success)
                            Logger.Trace($"{result.Message}");
                        else
                        {
                            Logger.Error($"{result.Message}");

                            break;
                        }
                    }

                    var saveConfig = await _schedulerService.SetConfigAsync(configFileName, solbot);

                    if (saveConfig.WriteSuccess)
                        Logger.Info(LogGenerator.SaveSuccess(botVersion));
                    else
                        Logger.Error(LogGenerator.SaveError(botVersion));
                }
            }
            catch (Exception e)
            {
                Logger.Fatal($"{Environment.NewLine}[{botVersion}] Message => {e.Message}{Environment.NewLine}StackTrace => {e.StackTrace}");
            }
        }
    }
}