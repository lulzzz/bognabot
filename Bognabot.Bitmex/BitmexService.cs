﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoMapper;
using Bognabot.Bitmex.Request;
using Bognabot.Bitmex.Response;
using Bognabot.Data.Config;
using Bognabot.Data.Exchange;
using Bognabot.Data.Exchange.Dtos;
using Bognabot.Data.Exchange.Enums;
using Bognabot.Services.Exchange;
using Bognabot.Services.Exchange.Contracts;
using Bognabot.Storage.Core;
using Newtonsoft.Json;
using NLog;

namespace Bognabot.Bitmex
{
    public class BitmexService : IExchangeService
    {
        public ExchangeConfig ExchangeConfig { get; }
        public DateTime Now => _exchangeApi.Now;

        private readonly ILogger _logger;
        private readonly IExchangeApi _exchangeApi;
        private readonly List<OrderDto> _openOrders;

        public BitmexService(ILogger logger, ExchangeConfig config)
        {
            _logger = logger;
            ExchangeConfig = config;

            _exchangeApi = new BitmexApi(logger, ExchangeConfig);
            _openOrders = new List<OrderDto>();
        }

        public void ConfigureMap(IMapperConfigurationExpression cfg)
        {
            cfg.CreateMap<CandleResponse, CandleDto>()
                .ForMember(d => d.ExchangeName, o => o.MapFrom(s => ExchangeConfig.ExchangeName))
                .ForMember(d => d.Instrument, o => o.MapFrom(s => _exchangeApi.ToInstrumentType(s.Symbol)))
                .ForMember(d => d.Period, o => o.Ignore());

            cfg.CreateMap<TradeResponse, TradeDto>()
                .ForMember(d => d.ExchangeName, o => o.MapFrom(s => ExchangeConfig.ExchangeName))
                .ForMember(d => d.Instrument, o => o.MapFrom(s => _exchangeApi.ToInstrumentType(s.Symbol)))
                .ForMember(d => d.Side, o => o.MapFrom(s => BitmexUtils.ToTradeType(s.Side)))
                .ForMember(d => d.Timestamp, o => o.MapFrom(s => BitmexUtils.Now()));

            cfg.CreateMap<BookResponse, BookDto>()
                .ForMember(d => d.Instrument, o => o.MapFrom(s => _exchangeApi.ToInstrumentType(s.Symbol)))
                .ForMember(d => d.Side, o => o.MapFrom(s => BitmexUtils.ToTradeType(s.Side)));

            cfg.CreateMap<OrderResponse, OrderDto>()
                .ForMember(d => d.ExchangeName, o => o.MapFrom(s => ExchangeConfig.ExchangeName))
                .ForMember(d => d.Instrument, o => o.MapFrom(s => _exchangeApi.ToInstrumentType(s.Symbol)))
                .ForMember(d => d.Side, o => o.MapFrom(s => BitmexUtils.ToTradeType(s.Side)))
                .ForMember(d => d.Timestamp, o => o.MapFrom(s => BitmexUtils.Now()));

            cfg.CreateMap<PositionResponse, PositionDto>()
                .ForMember(d => d.ExchangeName, o => o.MapFrom(s => ExchangeConfig.ExchangeName))
                .ForMember(d => d.Instrument, o => o.MapFrom(s => _exchangeApi.ToInstrumentType(s.Symbol)))
                .ForMember(d => d.Timestamp, o => o.MapFrom(s => BitmexUtils.Now()));
        }

        public async Task StartAsync()
        {
            await _exchangeApi.StartAsync();
        }

        public Task SubscribeToStreamAsync<T>(ExchangeChannel channel, IStreamSubscription subscription, Instrument? instrument = null) where T : ExchangeDto
        {
            return _exchangeApi.SubscribeToSocketAsync<T>(channel, subscription, instrument);
        }

        public async Task<List<CandleDto>> GetCandlesAsync(Instrument instrument, TimePeriod timePeriod, DateTime startTime, DateTime endTime)
        {
            if (!ExchangeConfig.SupportedInstruments.ContainsKey(instrument))
            {
                _logger.Log(LogLevel.Error, $"{ExchangeConfig.ExchangeName} does not support the {instrument} instrument");

                return null;
            }

            var request = new CandleRequest
            {
                Symbol = _exchangeApi.ToSymbol(instrument),
                StartAt = 0,
                Count = 750,
                TimeInterval = _exchangeApi.ToTimePeriod(timePeriod),
                StartTime = startTime.ToUtcTimeString(),
                EndTime = endTime.ToUtcTimeString(),
            };

            var channelPath = ExchangeConfig.SupportedRestChannels[ExchangeChannel.Candle];
            
            var response = await _exchangeApi.GetAllAsync<CandleDto, CandleResponse>(channelPath, request);

            foreach (var candleModel in response)
                candleModel.Period = timePeriod;

            return response.ToList();
        }

        public async Task<OrderDto> PlaceOrderAsync(Instrument instrument, double price, double quantity, TradeSide side, OrderType orderType)
        {
            if (!ExchangeConfig.SupportedInstruments.ContainsKey(instrument))
            {
                _logger.Log(LogLevel.Error, $"{ExchangeConfig.ExchangeName} does not support the {instrument} instrument");

                return null;
            }

            if (!ExchangeConfig.SupportedOrderTypes.ContainsKey(orderType))
            {
                _logger.Log(LogLevel.Error, $"{ExchangeConfig.ExchangeName} does not support orders of type {orderType}");

                return null;
            }

            IRequest request = null;
            var ot = ExchangeConfig.SupportedOrderTypes[orderType];

            switch (orderType)
            {
                case OrderType.Market:
                    request = new PlaceMarketOrderRequest
                    {
                        Symbol = _exchangeApi.ToSymbol(instrument),
                        OrderQty = quantity,
                        Side = side.ToString(),
                        OrderType = ot
                    };
                    break;
                case OrderType.Limit:
                    request = new PlaceLimitOrderRequest
                    {
                        Symbol = _exchangeApi.ToSymbol(instrument),
                        OrderQty = quantity,
                        Side = side.ToString(),
                        Price = price,
                        OrderType = ot
                    };
                    break;
                case OrderType.LimitMarket:
                    break;
                case OrderType.Stop:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(orderType), orderType, null);
            }

            var channelPath = ExchangeConfig.SupportedRestChannels[ExchangeChannel.Order];

            return await _exchangeApi.PostAsync<OrderDto, OrderResponse>(channelPath, request);
        }
    }
}