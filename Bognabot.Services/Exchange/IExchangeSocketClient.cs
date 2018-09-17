﻿using System;
using System.Threading.Tasks;

namespace Bognabot.Services.Exchange
{
    public interface IExchangeSocketClient
    {
        Task ConnectAsync(string url, Func<string, Task> onReceive);
        Task SubscribeAsync(string request);
        Task SendAsync(string message);
    }
}