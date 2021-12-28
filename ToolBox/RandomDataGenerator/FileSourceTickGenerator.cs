using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Securities;
using System.Collections.Generic;

namespace QuantConnect.ToolBox.RandomDataGenerator
{
    /// <summary>
    /// Pricing model used to determine the fair price or theoretical value for a call or a put option based
    /// </summary>
    public class FileSourceTickGenerator : TickGenerator
    {
        public FileSourceTickGenerator(RandomDataGeneratorSettings settings, TickType[] tickTypes, Security security, IRandomValueGenerator random)
            : base(settings, tickTypes, security, random)
        {
        }

        public override IEnumerable<Tick> GenerateTicks()
        {
            var marketHoursDataBase = MarketHoursDatabase.FromDataFolder();
            var dataTimeZone = marketHoursDataBase.GetDataTimeZone(Symbol.ID.Market, Symbol, Symbol.SecurityType);
            var exchangeTime = marketHoursDataBase.GetExchangeHours(Symbol.ID.Market, Symbol, Symbol.SecurityType);
            var exchangeTimeZone = exchangeTime.TimeZone;

            var current = Settings.Start;
            while (current <= Settings.End)
            {
                LeanDataReader ldr = new LeanDataReader(
                    new SubscriptionDataConfig(typeof(TradeBar), Symbol, Resolution.Minute,
                        dataTimeZone, exchangeTimeZone, tickType: TickType.Trade,
                        fillForward: false, extendedHours: true, isInternalFeed: true),
                    Symbol,
                    Settings.Resolution,
                    current,
                    Globals.DataFolder);

                foreach (var datapoint in ldr.Parse())
                {
                    yield return new Tick
                    {
                        Time = datapoint.Time,
                        Symbol = Symbol,
                        TickType = TickType.Trade,
                        Value = datapoint.Price
                    };

                    // advance to the next time step
                    current = exchangeTime.GetNextMarketOpen(datapoint.Time, true);
                }
            }
        }
    }
}
