using SolSignalModel1D_Backtest.Core.Data.Candles;
using SolSignalModel1D_Backtest.Core.Domain;
using SolSignalModel1D_Backtest.Core.Infra;

namespace SolSignalModel1D_Backtest
	{
	/// <summary>
	/// Частичный класс Program: сетевое обновление свечей.
	/// Вынесено отдельно, чтобы из Main только дергать один метод,
	/// а детали по символам/катчапу были изолированы.
	/// </summary>
	public partial class Program
		{
		/// <summary>
		/// Обновляет дневные свечи для SOL/USDT, BTC/USDT и PAXG/USDT.
		/// При необходимости сюда можно добавить другие активы
		/// без изменения Main.
		/// </summary>
		private static async Task UpdateCandlesAsync ( HttpClient http )
			{
			Console.WriteLine ("[update] Updating SOL/USDT, BTC/USDT, PAXG/USDT candles...");

			var solUpdater = new CandleDailyUpdater (
				http,
				TradingSymbols.SolUsdtInternal,
				PathConfig.CandlesDir,
				catchupDays: 3
			);

			var btcUpdater = new CandleDailyUpdater (
				http,
				TradingSymbols.BtcUsdtInternal,
				PathConfig.CandlesDir,
				catchupDays: 3
			);

			var paxgUpdater = new CandleDailyUpdater (
				http,
				TradingSymbols.PaxgUsdtInternal,
				PathConfig.CandlesDir,
				catchupDays: 3
			);

			await Task.WhenAll
			(
				solUpdater.UpdateAllAsync (),
				btcUpdater.UpdateAllAsync (),
				paxgUpdater.UpdateAllAsync ()
			);

			Console.WriteLine ("[update] Candle update done.");
			}
		}
	}
