﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Libplanet.Crypto;
using Libplanet.Net;
using Libplanet.Net.Protocols;
using Libplanet.Net.Transports;
using Libplanet.Seed.Executable.Exceptions;
using Libplanet.Seed.Interfaces;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using Serilog.Events;

namespace Libplanet.Seed.Executable
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Options options = Options.Parse(args, Console.Error);

            var loggerConfig = new LoggerConfiguration();
            switch (options.LogLevel)
            {
                case "error":
                    loggerConfig = loggerConfig.MinimumLevel.Error();
                    break;

                case "warning":
                    loggerConfig = loggerConfig.MinimumLevel.Warning();
                    break;

                case "information":
                    loggerConfig = loggerConfig.MinimumLevel.Information();
                    break;

                case "debug":
                    loggerConfig = loggerConfig.MinimumLevel.Debug();
                    break;

                case "verbose":
                    loggerConfig = loggerConfig.MinimumLevel.Verbose();
                    break;

                default:
                    loggerConfig = loggerConfig.MinimumLevel.Information();
                    break;
            }

            loggerConfig = loggerConfig
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console();
            Log.Logger = loggerConfig.CreateLogger();

            if (options.IceServer is null && options.Host is null)
            {
                Log.Error(
                    "-h/--host is required if -I/--ice-server is not given."
                );
                Environment.Exit(1);
                return;
            }

            if (!(options.IceServer is null || options.Host is null))
            {
                Log.Warning("-I/--ice-server will not work because -h/--host is given.");
            }

            try
            {
                var privateKey = options.PrivateKey ?? new PrivateKey();
                var seed = new Net.Seed(
                    privateKey,
                    options.Host,
                    options.Port,
                    options.Workers,
                    options.IceServer is null ? new IceServer[] { } : new[] { options.IceServer },
                    AppProtocolVersion.FromToken(options.AppProtocolVersionToken),
                    options.TransportType);
                Startup.TableSingleton = seed.Table;

                IWebHost webHost = WebHost.CreateDefaultBuilder()
                    .UseStartup<SeedStartup<Startup>>()
                    .UseSerilog()
                    .UseUrls($"http://{options.GraphQLHost}:{options.GraphQLPort}/")
                    .Build();

                using (var cts = new CancellationTokenSource())
                {
                    Console.CancelKeyPress += (sender, eventArgs) =>
                    {
                        eventArgs.Cancel = true;
                        cts.Cancel();
                    };

                    try
                    {
                        await Task.WhenAll(
                            webHost.RunAsync(cts.Token),
                            seed.StartAsync(new HashSet<BoundPeer>(options.Peers), cts.Token));
                    }
                    catch (OperationCanceledException)
                    {
                        await seed.StopAsync(TimeSpan.FromSeconds(1));
                    }
                }
            }
            catch (InvalidOptionValueException e)
            {
                string expectedValues = string.Join(", ", e.ExpectedValues);
                Console.Error.WriteLine($"Unexpected value given through '{e.OptionName}'\n"
                                        + $"  given value: {e.OptionValue}\n"
                                        + $"  expected values: {expectedValues}");
            }
        }

        private class Startup : IContext
        {
            public RoutingTable Table => TableSingleton;

            internal static RoutingTable TableSingleton { get; set; }
        }
    }
}
