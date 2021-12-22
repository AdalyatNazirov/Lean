using System.Linq;
using QuantConnect.Data.Market;
using QuantConnect.Securities;

namespace QuantConnect.Algorithm.CSharp
{
    public class BacktestChartingAlgorithm : QCAlgorithm
    {
        private Security Stock;

        private Security CallOption;
        private Symbol CallOptionSymbol;

        private Security PutOption;
        private Symbol PutOptionSymbol;

        /// <summary>
        /// Called at the start of your algorithm to setup your requirements:
        /// </summary>
        public override void Initialize()
        {
            //Add any stocks you'd like to analyse, and set the resolution:
            // Find more symbols here: http://quantconnect.com/data
            SetStartDate(2018, 12, 24);
            SetEndDate(2019, 6, 28);
            SetCash(100000);
            Stock = AddEquity("MSFT", Resolution.Minute);

            var contracts = OptionChainProvider.GetOptionContractList(Stock.Symbol, UtcTime).ToList();
            var a = TickType.Trade;
            //PutOptionSymbol = contracts
            //    .Where(c => c.ID.OptionRight == OptionRight.Put)
            //    .OrderByDescending(c => c.ID.Date)
            //    .First();
            PutOptionSymbol = SymbolRepresentation.ParseOptionTickerOSI("MSFT  210115P00095000");

            //CallOptionSymbol = contracts
            //    .Where(c => c.ID.OptionRight == OptionRight.Call)
            //    .OrderByDescending(c => c.ID.Date)
            //    .First();
            //CallOptionSymbol = "SPX 190215C03075000";

            PutOption = AddOptionContract(PutOptionSymbol);
            //CallOption = AddOptionContract(CallOptionSymbol);

            //Log("CallOptionSymbol:" + CallOptionSymbol + "," + CallOption.Symbol.ID.OptionStyle);

            Log("PutOptionSymbol:" + PutOptionSymbol + "," + PutOption.Symbol.ID.OptionStyle);

            //Chart - Master Container for the Chart:
            var stockPlot = new Chart("Trade Plot");
            //On the Trade Plotter Chart we want 3 series: trades and price:
            //var callOption = new Series("Call", SeriesType.Scatter, 0);
            var putOption = new Series("Put", SeriesType.Scatter, 0);
            stockPlot.AddSeries(putOption);
            var assetPrice = new Series("Price", SeriesType.Line, 0);
            stockPlot.AddSeries(assetPrice);
            AddChart(stockPlot);
        }


        /// <summary>
        /// OnEndOfDay Event Handler - At the end of each trading day we fire this code.
        /// To avoid flooding, we recommend running your plotting at the end of each day.
        /// </summary>
        public override void OnEndOfDay(Symbol symbol)
        {
            //Log the end of day prices:
            Plot("Trade Plot", "Price", Stock.Price);
            //Plot("Trade Plot", "Call", CallOption.Price);
            Plot("Trade Plot", "Put", PutOption.Price);
        }


        /// <summary>
        /// On receiving new tradebar data it will be passed into this function. The general pattern is:
        /// "public void OnData( CustomType name ) {...s"
        /// </summary>
        /// <param name="data">TradeBars data type synchronized and pushed into this function. The tradebars are grouped in a dictionary.</param>
        public void OnData(TradeBars data)
        {

        }
    }
}
