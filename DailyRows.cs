using SolSignalModel1D_Backtest.Core.Backtest;
using SolSignalModel1D_Backtest.Core.Data;
using SolSignalModel1D_Backtest.Core.Data.Candles.Timeframe;
using SolSignalModel1D_Backtest.Core.Data.DataBuilder;
using SolSignalModel1D_Backtest.Core.Data.Indicators;
using DataRow = SolSignalModel1D_Backtest.Core.Data.DataBuilder.DataRow;

namespace SolSignalModel1D_Backtest
	{
	public partial class Program
		{
		private static async Task<DailyRowsBundle> BuildDailyRowsAsync (
			IndicatorsDailyUpdater indicatorsUpdater,
			DateTime fromUtc, DateTime toUtc,
			List<Candle6h> solAll6h,
			List<Candle6h> btcAll6h,
			List<Candle6h> paxgAll6h,
			List<Candle1m> sol1m )
			{
			// Вместо "fromUtc - 90 дней" берём максимально ранний момент,
			// с которого у ВСЕХ трёх инструментов (SOL/BTC/PAXG) есть 6h-свечи.
			// Это даёт максимум истории, но фактический старт всё равно ограничен
			// требованиями к индикаторам (ret30, RSI, 200SMA и т.п.) внутри RowBuilder.
			var earliestSolUtc = solAll6h.Min (c => c.OpenTimeUtc);
			var earliestBtcUtc = btcAll6h.Min (c => c.OpenTimeUtc);
			var earliestPaxgUtc = paxgAll6h.Min (c => c.OpenTimeUtc);

			// Старт истории — общая точка, где гарантированно есть данные по всем трём.
			var histFrom = new[] { earliestSolUtc, earliestBtcUtc, earliestPaxgUtc }.Max ();

			var solWinTrainRaw = solAll6h
				.Where (c => c.OpenTimeUtc >= histFrom && c.OpenTimeUtc <= toUtc)
				.ToList ();
			var btcWinTrainRaw = btcAll6h
				.Where (c => c.OpenTimeUtc >= histFrom && c.OpenTimeUtc <= toUtc)
				.ToList ();
			var paxgWinTrainRaw = paxgAll6h
				.Where (c => c.OpenTimeUtc >= histFrom && c.OpenTimeUtc <= toUtc)
				.ToList ();

			Console.WriteLine (
				$"[win6h:raw] sol={solWinTrainRaw.Count}, btc={btcWinTrainRaw.Count}, paxg={paxgWinTrainRaw.Count}");

			// Тройное выравнивание: SOL ∩ BTC ∩ PAXG
			var common = solWinTrainRaw.Select (c => c.OpenTimeUtc)
				.Intersect (btcWinTrainRaw.Select (c => c.OpenTimeUtc))
				.Intersect (paxgWinTrainRaw.Select (c => c.OpenTimeUtc))
				.ToHashSet ();

			var solWinTrain = solWinTrainRaw.Where (c => common.Contains (c.OpenTimeUtc)).ToList ();
			var btcWinTrain = btcWinTrainRaw.Where (c => common.Contains (c.OpenTimeUtc)).ToList ();
			var paxgWinTrain = paxgWinTrainRaw.Where (c => common.Contains (c.OpenTimeUtc)).ToList ();

			Console.WriteLine (
				$"[win6h:aligned] sol={solWinTrain.Count}, btc={btcWinTrain.Count}, paxg={paxgWinTrain.Count}, common={common.Count}");

			// Индикаторы по дневному диапазону: покрытие с histFrom до toUtc.
			// Если FNG/DXY есть с 2021 года, то и строки строятся с 2021.
			var fngDict = indicatorsUpdater.LoadFngDict (histFrom.Date, toUtc.Date);
			var dxyDict = indicatorsUpdater.LoadDxyDict (histFrom.Date, toUtc.Date);
			indicatorsUpdater.EnsureCoverageOrFail (histFrom.Date, toUtc.Date);

			// Все 6h-строки (для SL-датасета и path-based labels).
			// Здесь используется общий NyTz, чтобы в одном месте контролировать правила NY-времени.
			var rows = RowBuilder.BuildRowsDaily (
				solWinTrain: solWinTrain,
				btcWinTrain: btcWinTrain,
				paxgWinTrain: paxgWinTrain,
				solAll6h: solAll6h,
				solAll1m: sol1m,
				fngHistory: fngDict,
				dxySeries: dxyDict,
				extraDaily: null,
				nyTz: NyTz
			);

			Console.WriteLine ($"[rows] total built = {rows.Count}");
			DumpNyHourHistogram (rows);

			// ВАЖНО: больше НЕ режем mornings по fromUtc.
			// Здесь собираем ВСЮ историю утренних NY-окон,
			// чтобы стратегия и бэктест могли работать "с 2021".
			var mornings = rows
				.Where (r => r.IsMorning)
				.OrderBy (r => r.Date)
				.ToList ();

			Console.WriteLine ($"[rows] mornings total (all history) = {mornings.Count}");

			var lastSolTime = solWinTrain.Max (c => c.OpenTimeUtc);
			Console.WriteLine ($"[rows] last SOL 6h = {lastSolTime:O}");

			return await Task.FromResult (new DailyRowsBundle
				{
				AllRows = rows,
				Mornings = mornings
				});
			}

		private static void DumpNyHourHistogram ( List<DataRow> rows )
			{
			if (rows.Count == 0) return;

			var hist = new Dictionary<int, int> ();
			foreach (var r in rows)
				{
				// Для расчёта часов по Нью-Йорку используется единая таймзона NyTz.
				// Это избавляет от локальных дубликатов TimeZones.NewYork и снижает риск рассинхронизации.
				var ny = TimeZoneInfo.ConvertTimeFromUtc (r.Date, NyTz);
				if (!hist.TryGetValue (ny.Hour, out var cnt)) cnt = 0;
				hist[ny.Hour] = cnt + 1;
				}

			Console.WriteLine (
				"[rows] NY hour histogram (all 6h rows, до утреннего фильтра): " +
				string.Join (", ", hist
					.OrderBy (kv => kv.Key)
					.Select (kv => $"{kv.Key:D2}:{kv.Value}")));
			}
		}
	}
