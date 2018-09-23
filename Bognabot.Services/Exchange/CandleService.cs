﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bognabot.Data.Exchange.Dtos;
using Bognabot.Data.Exchange.Enums;
using Bognabot.Services.Repository;
using NLog;

namespace Bognabot.Services.Exchange
{
    public class CandleService
    {
        private readonly ILogger _logger;
        private readonly RepositoryService _repoService;
        private readonly List<IExchangeService> _exchanges;
        private readonly Dictionary<Instrument, IStreamSubscription> _candleSubscriptions;
        private readonly Dictionary<Instrument, IStreamSubscription> _tradeSubscriptions;
        private readonly Dictionary<string, ExchangeCandles> _candleData;

        public CandleService(ILogger logger, RepositoryService repoService, IEnumerable<IExchangeService> exchanges, IndicatorFactory indicatorFactory)
        {
            _repoService = repoService;
            _exchanges = exchanges.ToList();
            _logger = logger;
            
            var instruments = Enum.GetValues(typeof(Instrument)).Cast<Instrument>().ToArray();

            _candleSubscriptions = new Dictionary<Instrument, IStreamSubscription>();
            _tradeSubscriptions = new Dictionary<Instrument, IStreamSubscription>();
            _candleData = new Dictionary<string, ExchangeCandles>();

            foreach (var instrument in instruments)
            {
                _candleSubscriptions.Add(instrument, new StreamSubscription<CandleDto>(OnNewCandle));
                _tradeSubscriptions.Add(instrument, new StreamSubscription<TradeDto>(OnNewTrade));

                foreach (var exchange in _exchanges)
                {
                    foreach (var timePeriod in exchange.ExchangeConfig.SupportedTimePeriods)
                    {
                        var exchangeData = new ExchangeCandles(logger, repoService, indicatorFactory, exchange, timePeriod.Key, instrument);

                        _candleData.Add(exchangeData.Key, exchangeData);
                    }
                }
            }
        }

        public async Task StartAsync()
        {
            foreach (var candleData in _candleData.Values)
                await candleData.LoadAsync();

            foreach (var exchange in _exchanges)
            {
                var supportedInstruments = exchange.ExchangeConfig.SupportedInstruments;

                foreach (var instrument in supportedInstruments.Keys)
                {
                    await exchange.SubscribeToStreamAsync<CandleDto>(ExchangeChannel.Candle, instrument, _candleSubscriptions[instrument]);
                    await exchange.SubscribeToStreamAsync<TradeDto>(ExchangeChannel.Trade, instrument, _tradeSubscriptions[instrument]);
                }
            }
        }

        public CandleDto GetLatestCandle(string exchangeName, Instrument instrument, TimePeriod timePeriod)
        {
            var candleData = GetData(exchangeName, instrument, timePeriod);

            if (candleData != null)
                return candleData.CurrentCandle;

            _logger.Log(LogLevel.Error, $"{exchangeName} {instrument} {timePeriod} candle data not found");
            return null;
        }

        public Task<List<CandleDto>> GetCandlesAsync(string exchangeName, Instrument instrument, TimePeriod timePeriod, int dataPoints)
        {
            var candleData = GetData(exchangeName, instrument, timePeriod);

            if (candleData != null)
                return Task.FromResult(candleData.GetCandles());

            _logger.Log(LogLevel.Error, $"{exchangeName} {instrument} {timePeriod} candle data not found");

            return null;
        }

        public Task<ExchangeCandles> GetExchangeCandleDataAsync(string exchangeName, Instrument instrument, TimePeriod timePeriod)
        {
            var candleData = GetData(exchangeName, instrument, timePeriod);

            if (candleData != null)
                return Task.FromResult(candleData);

            _logger.Log(LogLevel.Error, $"{exchangeName} {instrument} {timePeriod} candle data not found");

            return null;
        }

        private async Task OnNewCandle(CandleDto[] arg)
        {
            if (arg == null || !arg.Any())
                return;

            var last = arg.Last();

            var key = ExchangeUtils.GetCandleDataKey(last.ExchangeName, last.Instrument, last.Period);

            if (!_candleData.ContainsKey(key))
                return;

            await _candleData[key].InsertCandlesAsync(arg);
        }

        private Task OnNewTrade(TradeDto[] arg)
        {
            if (arg == null || !arg.Any())
                return Task.CompletedTask;

            var last = arg.Last();

            var timePeriods = Enum.GetValues(typeof(TimePeriod)).Cast<TimePeriod>().ToArray();

            foreach (var period in timePeriods)
            {
                var key = ExchangeUtils.GetCandleDataKey(last.ExchangeName, last.Instrument, period);

                if (!_candleData.ContainsKey(key))
                    continue;

                _candleData[key].UpdateCurrentCandle(last.Price, arg.Length, arg.Sum(x => x.Size));
            }

            return Task.CompletedTask;
        }

        private ExchangeCandles GetData(string exchangeName, Instrument instrument, TimePeriod period)
        {
            var key = ExchangeUtils.GetCandleDataKey(exchangeName, instrument, period);

            return _candleData.ContainsKey(key) 
                ? _candleData[key] 
                : null;
        }
    }
}
