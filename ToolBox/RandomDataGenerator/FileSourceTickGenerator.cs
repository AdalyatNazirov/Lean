using System.Collections.Generic;
using System.Linq;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Securities;

namespace QuantConnect.ToolBox.RandomDataGenerator
{
    /// <summary>
    /// Pricing model used to determine the fair price or theoretical value for a call or a put option based
    /// </summary>
    public class FileSourceTickGenerator : TickGenerator
    {
        public FileSourceTickGenerator(RandomDataGeneratorSettings settings, TickType[] tickTypes, Symbol symbol)
            : base(settings, tickTypes, symbol)
        {
        }

        public FileSourceTickGenerator(RandomDataGeneratorSettings settings, TickType[] tickTypes, IRandomValueGenerator random, Symbol symbol)
            : base(settings, tickTypes, random, symbol)
        {
        }

        public override IEnumerable<IEnumerable<Tick>> GenerateTicks()
        {
            List<Tick> ticks = new List<Tick>();

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
                    ticks.Clear();

                    ticks.Add(new Tick
                    {
                        Time = datapoint.Time,
                        Symbol = Symbol,
                        TickType = TickType.Trade,
                        Value = datapoint.Price
                    });
                    
                    yield return ticks;
                }

                // advance to the next time step
                current = exchangeTime.GetNextMarketOpen(ticks.FirstOrDefault()?.Time ?? current, true);
            }
        }
    }
}
